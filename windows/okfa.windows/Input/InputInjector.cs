using System.Runtime.InteropServices;
using KeyboardBridge.Windows.Diagnostics;
using KeyboardBridge.Windows.Protocol;

namespace KeyboardBridge.Windows.Input;

public sealed class InputInjector
{
    private readonly HashSet<ushort> _pressedVirtualKeys = new();
    private byte _currentModifiers;
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public bool IsEnabled { get; set; } = true;

    public void HandleInputEvent(InputEventMessage inputEvent)
    {
        if (!IsEnabled)
        {
            return;
        }

        SynchronizeModifiers(inputEvent.Modifiers);

        if (inputEvent.Action == InputEventAction.ModifiersOnly)
        {
            return;
        }

        if (!TryMapHidUsage(inputEvent.HidUsage, out var key))
        {
            BridgeLog.Write("InputInjector", $"Unsupported HID usage=0x{inputEvent.HidUsage:X2}");
            return;
        }

        switch (inputEvent.Action)
        {
        case InputEventAction.KeyDown:
        case InputEventAction.KeyRepeat:
            SendVirtualKey(key.VirtualKey, isKeyUp: false, key.IsExtended);
            _pressedVirtualKeys.Add(key.VirtualKey);
            break;

        case InputEventAction.KeyUp:
            SendVirtualKey(key.VirtualKey, isKeyUp: true, key.IsExtended);
            _pressedVirtualKeys.Remove(key.VirtualKey);
            break;
        }
    }

    public void HandleSnapshot(KeyboardSnapshotMessage snapshot)
    {
        if (!IsEnabled)
        {
            return;
        }

        var desiredOrdinaryKeys = new HashSet<ushort>();
        foreach (var usage in snapshot.Usages)
        {
            if (!TryMapHidUsage(usage, out var key))
            {
                continue;
            }

            if (IsModifierVirtualKey(key.VirtualKey))
            {
                continue;
            }

            desiredOrdinaryKeys.Add(key.VirtualKey);
        }

        var currentlyPressedOrdinaryKeys = _pressedVirtualKeys
            .Where(static vk => !IsModifierVirtualKey(vk))
            .ToArray();

        foreach (var virtualKey in currentlyPressedOrdinaryKeys)
        {
            if (desiredOrdinaryKeys.Contains(virtualKey))
            {
                continue;
            }

            if (TryGetDescriptorForVirtualKey(virtualKey, out var key))
            {
                SendVirtualKey(key.VirtualKey, isKeyUp: true, key.IsExtended);
            }

            _pressedVirtualKeys.Remove(virtualKey);
        }

        SynchronizeModifiers(snapshot.Modifiers);

        foreach (var virtualKey in desiredOrdinaryKeys)
        {
            if (_pressedVirtualKeys.Contains(virtualKey))
            {
                continue;
            }

            if (!TryGetDescriptorForVirtualKey(virtualKey, out var key))
            {
                continue;
            }

            SendVirtualKey(key.VirtualKey, isKeyUp: false, key.IsExtended);
            _pressedVirtualKeys.Add(virtualKey);
        }
    }

    public void ReleaseAllInjectedKeys()
    {
        var ordinaryKeys = _pressedVirtualKeys
            .Where(static vk => !IsModifierVirtualKey(vk))
            .ToArray();

        foreach (var virtualKey in ordinaryKeys)
        {
            if (!TryGetDescriptorForVirtualKey(virtualKey, out var key))
            {
                continue;
            }

            SendVirtualKey(key.VirtualKey, isKeyUp: true, key.IsExtended);
            _pressedVirtualKeys.Remove(virtualKey);
        }

        SynchronizeModifiers(0);
    }

    private void SynchronizeModifiers(byte desiredModifiers)
    {
        foreach (var modifier in ModifierDescriptors)
        {
            var isPressed = (desiredModifiers & modifier.BitMask) != 0;
            var wasPressed = (_currentModifiers & modifier.BitMask) != 0;
            if (isPressed == wasPressed)
            {
                continue;
            }

            SendVirtualKey(modifier.VirtualKey, isKeyUp: !isPressed, modifier.IsExtended);
            if (isPressed)
            {
                _pressedVirtualKeys.Add(modifier.VirtualKey);
            }
            else
            {
                _pressedVirtualKeys.Remove(modifier.VirtualKey);
            }
        }

        _currentModifiers = desiredModifiers;
    }

    private static bool TryMapHidUsage(byte hidUsage, out InjectedKeyDescriptor key)
    {
        if (KeyMap.TryGetValue(hidUsage, out key))
        {
            return true;
        }

        if (ModifierKeyMap.TryGetValue(hidUsage, out key))
        {
            return true;
        }

        key = default;
        return false;
    }

    private static bool TryGetDescriptorForVirtualKey(ushort virtualKey, out InjectedKeyDescriptor key)
    {
        if (VirtualKeyMap.TryGetValue(virtualKey, out key))
        {
            return true;
        }

        key = default;
        return false;
    }

    private static bool IsModifierVirtualKey(ushort virtualKey) => ModifierVirtualKeys.Contains(virtualKey);

    private static void SendVirtualKey(ushort virtualKey, bool isKeyUp, bool isExtended)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = (isKeyUp ? KeyeventfKeyUp : 0u) | (isExtended ? KeyeventfExtendedKey : 0u),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

        var sent = SendInput(1, [input], InputSize);
        if (sent == 1)
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        BridgeLog.Write(
            "InputInjector",
            $"SendInput failed vk=0x{virtualKey:X2} keyUp={isKeyUp} error={error} cbSize={InputSize}"
        );
    }

    private readonly record struct InjectedKeyDescriptor(ushort VirtualKey, bool IsExtended);
    private readonly record struct ModifierDescriptor(byte BitMask, ushort VirtualKey, bool IsExtended, byte HidUsage);

    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyUp = 0x0002;

    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkPause = 0x13;
    private const ushort VkCapital = 0x14;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkPrior = 0x21;
    private const ushort VkNext = 0x22;
    private const ushort VkEnd = 0x23;
    private const ushort VkHome = 0x24;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkSnapshot = 0x2C;
    private const ushort VkInsert = 0x2D;
    private const ushort VkDelete = 0x2E;
    private const ushort Vk0 = 0x30;
    private const ushort Vk1 = 0x31;
    private const ushort Vk2 = 0x32;
    private const ushort Vk3 = 0x33;
    private const ushort Vk4 = 0x34;
    private const ushort Vk5 = 0x35;
    private const ushort Vk6 = 0x36;
    private const ushort Vk7 = 0x37;
    private const ushort Vk8 = 0x38;
    private const ushort Vk9 = 0x39;
    private const ushort VkA = 0x41;
    private const ushort VkB = 0x42;
    private const ushort VkC = 0x43;
    private const ushort VkD = 0x44;
    private const ushort VkE = 0x45;
    private const ushort VkF = 0x46;
    private const ushort VkG = 0x47;
    private const ushort VkH = 0x48;
    private const ushort VkI = 0x49;
    private const ushort VkJ = 0x4A;
    private const ushort VkK = 0x4B;
    private const ushort VkL = 0x4C;
    private const ushort VkM = 0x4D;
    private const ushort VkN = 0x4E;
    private const ushort VkO = 0x4F;
    private const ushort VkP = 0x50;
    private const ushort VkQ = 0x51;
    private const ushort VkR = 0x52;
    private const ushort VkS = 0x53;
    private const ushort VkT = 0x54;
    private const ushort VkU = 0x55;
    private const ushort VkV = 0x56;
    private const ushort VkW = 0x57;
    private const ushort VkX = 0x58;
    private const ushort VkY = 0x59;
    private const ushort VkZ = 0x5A;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkNumlock = 0x90;
    private const ushort VkScroll = 0x91;
    private const ushort VkLShift = 0xA0;
    private const ushort VkRShift = 0xA1;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkLMenu = 0xA4;
    private const ushort VkRMenu = 0xA5;
    private const ushort VkOem1 = 0xBA;
    private const ushort VkOemPlus = 0xBB;
    private const ushort VkOemComma = 0xBC;
    private const ushort VkOemMinus = 0xBD;
    private const ushort VkOemPeriod = 0xBE;
    private const ushort VkOem2 = 0xBF;
    private const ushort VkOem3 = 0xC0;
    private const ushort VkOem4 = 0xDB;
    private const ushort VkOem5 = 0xDC;
    private const ushort VkOem6 = 0xDD;
    private const ushort VkOem7 = 0xDE;
    private const ushort VkF1 = 0x70;
    private const ushort VkF2 = 0x71;
    private const ushort VkF3 = 0x72;
    private const ushort VkF4 = 0x73;
    private const ushort VkF5 = 0x74;
    private const ushort VkF6 = 0x75;
    private const ushort VkF7 = 0x76;
    private const ushort VkF8 = 0x77;
    private const ushort VkF9 = 0x78;
    private const ushort VkF10 = 0x79;
    private const ushort VkF11 = 0x7A;
    private const ushort VkF12 = 0x7B;
    private static readonly ModifierDescriptor[] ModifierDescriptors =
    [
        new(1 << 0, VkLControl, false, 0xE0),
        new(1 << 1, VkLShift, false, 0xE1),
        new(1 << 2, VkLMenu, false, 0xE2),
        new(1 << 3, VkLWin, true, 0xE3),
        new(1 << 4, VkRControl, true, 0xE4),
        new(1 << 5, VkRShift, false, 0xE5),
        new(1 << 6, VkRMenu, true, 0xE6),
        new(1 << 7, VkRWin, true, 0xE7),
    ];

    private static readonly Dictionary<byte, InjectedKeyDescriptor> ModifierKeyMap = ModifierDescriptors
        .ToDictionary(
            static descriptor => descriptor.HidUsage,
            static descriptor => new InjectedKeyDescriptor(descriptor.VirtualKey, descriptor.IsExtended)
        );

    private static readonly Dictionary<byte, InjectedKeyDescriptor> KeyMap = new()
    {
        [0x04] = new(VkA, false),
        [0x05] = new(VkB, false),
        [0x06] = new(VkC, false),
        [0x07] = new(VkD, false),
        [0x08] = new(VkE, false),
        [0x09] = new(VkF, false),
        [0x0A] = new(VkG, false),
        [0x0B] = new(VkH, false),
        [0x0C] = new(VkI, false),
        [0x0D] = new(VkJ, false),
        [0x0E] = new(VkK, false),
        [0x0F] = new(VkL, false),
        [0x10] = new(VkM, false),
        [0x11] = new(VkN, false),
        [0x12] = new(VkO, false),
        [0x13] = new(VkP, false),
        [0x14] = new(VkQ, false),
        [0x15] = new(VkR, false),
        [0x16] = new(VkS, false),
        [0x17] = new(VkT, false),
        [0x18] = new(VkU, false),
        [0x19] = new(VkV, false),
        [0x1A] = new(VkW, false),
        [0x1B] = new(VkX, false),
        [0x1C] = new(VkY, false),
        [0x1D] = new(VkZ, false),
        [0x1E] = new(Vk1, false),
        [0x1F] = new(Vk2, false),
        [0x20] = new(Vk3, false),
        [0x21] = new(Vk4, false),
        [0x22] = new(Vk5, false),
        [0x23] = new(Vk6, false),
        [0x24] = new(Vk7, false),
        [0x25] = new(Vk8, false),
        [0x26] = new(Vk9, false),
        [0x27] = new(Vk0, false),
        [0x28] = new(VkReturn, false),
        [0x29] = new(VkEscape, false),
        [0x2A] = new(VkBack, false),
        [0x2B] = new(VkTab, false),
        [0x2C] = new(VkSpace, false),
        [0x2D] = new(VkOemMinus, false),
        [0x2E] = new(VkOemPlus, false),
        [0x2F] = new(VkOem4, false),
        [0x30] = new(VkOem6, false),
        [0x31] = new(VkOem5, false),
        [0x33] = new(VkOem1, false),
        [0x34] = new(VkOem7, false),
        [0x35] = new(VkOem3, false),
        [0x36] = new(VkOemComma, false),
        [0x37] = new(VkOemPeriod, false),
        [0x38] = new(VkOem2, false),
        [0x39] = new(VkCapital, false),
        [0x3A] = new(VkF1, false),
        [0x3B] = new(VkF2, false),
        [0x3C] = new(VkF3, false),
        [0x3D] = new(VkF4, false),
        [0x3E] = new(VkF5, false),
        [0x3F] = new(VkF6, false),
        [0x40] = new(VkF7, false),
        [0x41] = new(VkF8, false),
        [0x42] = new(VkF9, false),
        [0x43] = new(VkF10, false),
        [0x44] = new(VkF11, false),
        [0x45] = new(VkF12, false),
        [0x46] = new(VkSnapshot, true),
        [0x47] = new(VkScroll, false),
        [0x48] = new(VkPause, false),
        [0x49] = new(VkInsert, true),
        [0x4A] = new(VkHome, true),
        [0x4B] = new(VkPrior, true),
        [0x4C] = new(VkDelete, true),
        [0x4D] = new(VkEnd, true),
        [0x4E] = new(VkNext, true),
        [0x4F] = new(VkRight, true),
        [0x50] = new(VkLeft, true),
        [0x51] = new(VkDown, true),
        [0x52] = new(VkUp, true),
        [0x53] = new(VkNumlock, true),
    };

    private static readonly Dictionary<ushort, InjectedKeyDescriptor> VirtualKeyMap = KeyMap.Values
        .Concat(ModifierKeyMap.Values)
        .Distinct()
        .ToDictionary(static descriptor => descriptor.VirtualKey);

    private static readonly HashSet<ushort> ModifierVirtualKeys =
    [
        VkLControl,
        VkLShift,
        VkLMenu,
        VkLWin,
        VkRControl,
        VkRShift,
        VkRMenu,
        VkRWin,
    ];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
