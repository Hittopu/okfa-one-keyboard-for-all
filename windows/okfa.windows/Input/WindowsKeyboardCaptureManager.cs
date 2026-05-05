using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyboardBridge.Windows.Diagnostics;
using KeyboardBridge.Windows.Protocol;

namespace KeyboardBridge.Windows.Input;

public enum CaptureCommand
{
    ToggleRemoteInput,
    EmergencyStop,
}

public readonly record struct CapturedInputEvent(
    InputEventAction Action,
    byte HidUsage,
    byte Modifiers,
    byte EventFlags,
    ulong TimestampMilliseconds
);

public sealed class WindowsKeyboardCaptureManager : IDisposable
{
    private readonly HookProc _hookProc;
    private readonly HashSet<ushort> _pressedVirtualKeys = new();
    private IntPtr _hookHandle;
    private byte _currentModifierBitmap;
    private bool _disposed;
    private bool _isRemoteInputActive;

    public WindowsKeyboardCaptureManager()
    {
        _hookProc = HookCallback;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? LogEmitted;
    public event EventHandler<CaptureCommand>? CommandReceived;
    public event EventHandler<CapturedInputEvent>? InputCaptured;

    public bool IsRemoteInputActive
    {
        get => _isRemoteInputActive;
        set
        {
            if (_isRemoteInputActive == value)
            {
                return;
            }

            _isRemoteInputActive = value;
            if (!value)
            {
                _pressedVirtualKeys.Clear();
                _currentModifierBitmap = 0;
            }

            var status = value
                ? "Exclusive keyboard mode enabled."
                : "Keyboard returned to this PC.";
            StatusChanged?.Invoke(this, status);
            Log(status);
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hookHandle != IntPtr.Zero)
        {
            StatusChanged?.Invoke(this, "Keyboard capture already running.");
            return;
        }

        _activeInstance = this;
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = GetModuleHandle(currentModule?.ModuleName);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _activeInstance = null;
            StatusChanged?.Invoke(this, "Keyboard capture unavailable. Try running okfa again.");
            Log($"Failed to install WH_KEYBOARD_LL hook. error={error}");
            return;
        }

        StatusChanged?.Invoke(this, "Quick Switch: Ctrl+Alt+F9. Emergency: Ctrl+Alt+F10.");
        Log("Installed WH_KEYBOARD_LL hook for Windows sender capture.");
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        if (ReferenceEquals(_activeInstance, this))
        {
            _activeInstance = null;
        }

        _pressedVirtualKeys.Clear();
        _currentModifierBitmap = 0;
        IsRemoteInputActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (!IsKeyboardMessage(message))
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var hookInfo = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((hookInfo.Flags & LlkHfInjected) != 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var virtualKey = NormalizeVirtualKey((ushort)hookInfo.VirtualKeyCode, hookInfo.ScanCode, hookInfo.Flags);
        var isKeyUp = message is WmKeyUp or WmSysKeyUp || (hookInfo.Flags & LlkHfUp) != 0;
        var isKeyDown = !isKeyUp;

        if (isKeyDown && ShouldTriggerToggleCommand(virtualKey))
        {
            CommandReceived?.Invoke(this, CaptureCommand.ToggleRemoteInput);
            return Suppress();
        }

        if (isKeyDown && ShouldTriggerEmergencyStop(virtualKey))
        {
            CommandReceived?.Invoke(this, CaptureCommand.EmergencyStop);
            return Suppress();
        }

        if (TryHandleModifierTransition(virtualKey, isKeyDown))
        {
            return _isRemoteInputActive ? Suppress() : CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (!_isRemoteInputActive)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (!KeyboardUsageMap.TryGetHidUsageForVirtualKey(virtualKey, out var hidUsage))
        {
            Log($"No HID usage mapping for vk=0x{virtualKey:X2}; remote event ignored.");
            return Suppress();
        }

        var action = ResolveAction(virtualKey, isKeyDown);
        var capturedEvent = new CapturedInputEvent(
            Action: action,
            HidUsage: hidUsage,
            Modifiers: _currentModifierBitmap,
            EventFlags: action == InputEventAction.KeyRepeat ? (byte)0x01 : (byte)0x00,
            TimestampMilliseconds: CurrentTimestampMilliseconds()
        );

        InputCaptured?.Invoke(this, capturedEvent);
        return Suppress();
    }

    private bool TryHandleModifierTransition(ushort virtualKey, bool isKeyDown)
    {
        var modifier = KeyboardUsageMap.ModifierDescriptors.FirstOrDefault(descriptor => descriptor.VirtualKey == virtualKey);
        if (modifier == default)
        {
            return false;
        }

        if (isKeyDown)
        {
            _currentModifierBitmap |= modifier.BitMask;
            _pressedVirtualKeys.Add(virtualKey);
        }
        else
        {
            _currentModifierBitmap &= (byte)~modifier.BitMask;
            _pressedVirtualKeys.Remove(virtualKey);
        }

        if (_isRemoteInputActive)
        {
            InputCaptured?.Invoke(
                this,
                new CapturedInputEvent(
                    Action: InputEventAction.ModifiersOnly,
                    HidUsage: modifier.HidUsage,
                    Modifiers: _currentModifierBitmap,
                    EventFlags: 0,
                    TimestampMilliseconds: CurrentTimestampMilliseconds()
                )
            );
        }

        return true;
    }

    private InputEventAction ResolveAction(ushort virtualKey, bool isKeyDown)
    {
        if (isKeyDown)
        {
            if (!_pressedVirtualKeys.Add(virtualKey))
            {
                return InputEventAction.KeyRepeat;
            }

            return InputEventAction.KeyDown;
        }

        _pressedVirtualKeys.Remove(virtualKey);
        return InputEventAction.KeyUp;
    }

    private bool ShouldTriggerToggleCommand(ushort virtualKey) =>
        virtualKey == VkF9
        && HasControlAlt();

    private bool ShouldTriggerEmergencyStop(ushort virtualKey) =>
        virtualKey == VkF10
        && HasControlAlt();

    private bool HasControlAlt() =>
        (_currentModifierBitmap & ControlMask) != 0
        && (_currentModifierBitmap & AltMask) != 0;

    private static ushort NormalizeVirtualKey(ushort virtualKey, uint scanCode, uint flags)
    {
        return virtualKey switch
        {
            VkShift => scanCode == RightShiftScanCode ? VkRShift : VkLShift,
            VkControl => (flags & LlkHfExtended) != 0 ? VkRControl : VkLControl,
            VkMenu => (flags & LlkHfExtended) != 0 ? VkRMenu : VkLMenu,
            _ => virtualKey,
        };
    }

    private static bool IsKeyboardMessage(int message) =>
        message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp;

    private static IntPtr Suppress() => new(1);

    private static ulong CurrentTimestampMilliseconds() =>
        (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Log(string message)
    {
        BridgeLog.Write("KeyboardCapture", message);
        LogEmitted?.Invoke(this, message);
    }

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkHfExtended = 0x01;
    private const uint LlkHfInjected = 0x10;
    private const uint LlkHfUp = 0x80;
    private const uint RightShiftScanCode = 0x36;
    private const byte ControlMask = (1 << 0) | (1 << 4);
    private const byte AltMask = (1 << 2) | (1 << 6);

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkF9 = 0x78;
    private const ushort VkF10 = 0x79;
    private const ushort VkLShift = 0xA0;
    private const ushort VkRShift = 0xA1;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkLMenu = 0xA4;
    private const ushort VkRMenu = 0xA5;

    private static WindowsKeyboardCaptureManager? _activeInstance;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
