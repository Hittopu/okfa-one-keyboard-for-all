using KeyboardBridge.Windows.Diagnostics;
using KeyboardBridge.Windows.Input;
using KeyboardBridge.Windows.Protocol;
using KeyboardBridge.Windows.Trust;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace KeyboardBridge.Windows.Bluetooth;

public sealed record PendingClientApproval(
    ulong ClientId,
    ushort ClientVersion,
    ushort CapabilityFlags,
    DateTimeOffset RequestedAt
)
{
    public string DisplayText =>
        $"Pending receiver PC 0x{ClientId:X16} version {ClientVersion} flags 0x{CapabilityFlags:X4}";
}

public sealed class WindowsGattSenderService : IAsyncDisposable
{
    private readonly TrustedClientStore _trustedClientStore;
    private readonly HashSet<ulong> _approvedClientIds = new();
    private readonly ulong _serverId;
    private GattServiceProvider? _serviceProvider;
    private GattLocalCharacteristic? _controlCharacteristic;
    private GattLocalCharacteristic? _inputEventCharacteristic;
    private GattLocalCharacteristic? _inputSnapshotCharacteristic;
    private PendingClientApproval? _pendingClient;
    private ushort _controlSequence;
    private ushort _inputEventSequence;
    private ushort _snapshotSequence;
    private ulong? _lastInputTimestampMilliseconds;
    private byte _currentModifiers;
    private readonly List<byte> _currentPressedUsages = [];

    public WindowsGattSenderService(TrustedClientStore trustedClientStore)
    {
        _trustedClientStore = trustedClientStore;
        _serverId = LoadOrCreateServerId();
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<string>? LogEmitted;
    public event EventHandler<int>? TrustedClientCountChanged;
    public event EventHandler<PendingClientApproval?>? PendingClientChanged;
    public event EventHandler<bool>? RemoteInputStateChanged;

    public bool IsRunning { get; private set; }

    public bool IsRemoteInputActive { get; private set; }

    public bool HasApprovedConnectedReceiver =>
        _approvedClientIds.Count > 0
        && (_inputEventCharacteristic?.SubscribedClients.Count ?? 0) > 0;

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            StatusChanged?.Invoke(this, "Advertising as okfa");
            return;
        }

        TrustedClientCountChanged?.Invoke(this, _trustedClientStore.TrustedCount);
        StatusChanged?.Invoke(this, "Checking Bluetooth adapter...");

        var adapter = await BluetoothAdapter.GetDefaultAsync();
        if (adapter is null)
        {
            StatusChanged?.Invoke(this, "Bluetooth adapter not found.");
            Log("BluetoothAdapter.GetDefaultAsync returned null.");
            return;
        }

        if (!adapter.IsPeripheralRoleSupported)
        {
            StatusChanged?.Invoke(this, "This Bluetooth adapter cannot publish a BLE peripheral service.");
            Log("Bluetooth adapter does not support the peripheral role.");
            return;
        }

        var providerResult = await GattServiceProvider.CreateAsync(BridgeUuids.Service);
        if (providerResult.Error != BluetoothError.Success)
        {
            StatusChanged?.Invoke(this, $"GATT service creation failed: {providerResult.Error}");
            Log($"GattServiceProvider.CreateAsync failed with {providerResult.Error}.");
            return;
        }

        _serviceProvider = providerResult.ServiceProvider;
        _controlCharacteristic = await CreateCharacteristicAsync(
            BridgeUuids.Control,
            GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Notify,
            read: false,
            write: true,
            "okfa control"
        );
        _inputEventCharacteristic = await CreateCharacteristicAsync(
            BridgeUuids.InputEvent,
            GattCharacteristicProperties.Notify,
            read: false,
            write: false,
            "okfa input events"
        );
        _inputSnapshotCharacteristic = await CreateCharacteristicAsync(
            BridgeUuids.InputSnapshot,
            GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
            read: true,
            write: false,
            "okfa input snapshot"
        );

        _controlCharacteristic.WriteRequested += OnControlWriteRequested;
        _controlCharacteristic.SubscribedClientsChanged += OnSubscribedClientsChanged;
        _inputEventCharacteristic.SubscribedClientsChanged += OnSubscribedClientsChanged;
        _inputSnapshotCharacteristic.SubscribedClientsChanged += OnSubscribedClientsChanged;
        _inputSnapshotCharacteristic.ReadRequested += OnSnapshotReadRequested;

        _serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
        {
            IsConnectable = true,
            IsDiscoverable = true,
        });

        IsRunning = true;
        StatusChanged?.Invoke(this, "Advertising as okfa");
        NotifyConnectionStatus();
        Log("Started Windows GATT sender service.");
    }

    public async Task StopAsync()
    {
        if (!IsRunning && _serviceProvider is null)
        {
            return;
        }

        if (IsRemoteInputActive)
        {
            await DeactivateRemoteInputAsync(ModeChangeSource.System);
        }

        IsRunning = false;
        PendingClient = null;
        _approvedClientIds.Clear();
        ResetInputState();

        if (_controlCharacteristic is not null)
        {
            _controlCharacteristic.WriteRequested -= OnControlWriteRequested;
            _controlCharacteristic.SubscribedClientsChanged -= OnSubscribedClientsChanged;
        }

        if (_inputEventCharacteristic is not null)
        {
            _inputEventCharacteristic.SubscribedClientsChanged -= OnSubscribedClientsChanged;
        }

        if (_inputSnapshotCharacteristic is not null)
        {
            _inputSnapshotCharacteristic.SubscribedClientsChanged -= OnSubscribedClientsChanged;
            _inputSnapshotCharacteristic.ReadRequested -= OnSnapshotReadRequested;
        }

        _serviceProvider?.StopAdvertising();
        _serviceProvider = null;
        _controlCharacteristic = null;
        _inputEventCharacteristic = null;
        _inputSnapshotCharacteristic = null;

        StatusChanged?.Invoke(this, "Bridge stopped");
        NotifyConnectionStatus();
        Log("Stopped Windows GATT sender service.");
    }

    public async Task ApprovePendingClientAsync()
    {
        if (PendingClient is null)
        {
            Log("Approve requested, but there is no pending client.");
            return;
        }

        _approvedClientIds.Add(PendingClient.ClientId);
        _trustedClientStore.MarkApproved(PendingClient.ClientId, $"Windows-{PendingClient.ClientId:X6}");
        TrustedClientCountChanged?.Invoke(this, _trustedClientStore.TrustedCount);
        await SendTrustStatusAsync(TrustStatusCode.Approved, TrustReason.UserApproved);
        Log($"Approved receiver 0x{PendingClient.ClientId:X16}.");
        PendingClient = null;
        NotifyConnectionStatus();
    }

    public async Task DenyPendingClientAsync()
    {
        if (PendingClient is null)
        {
            Log("Deny requested, but there is no pending client.");
            return;
        }

        _approvedClientIds.Remove(PendingClient.ClientId);
        await SendTrustStatusAsync(TrustStatusCode.Denied, TrustReason.UserDenied);
        Log($"Denied receiver 0x{PendingClient.ClientId:X16}.");
        PendingClient = null;
        NotifyConnectionStatus();
    }

    public async Task<bool> ToggleRemoteInputModeAsync(ModeChangeSource source = ModeChangeSource.WindowsUser)
    {
        if (IsRemoteInputActive)
        {
            await DeactivateRemoteInputAsync(source);
            return false;
        }

        if (!HasApprovedConnectedReceiver)
        {
            StatusChanged?.Invoke(this, "Waiting for approved receiver PC");
            Log("Cannot activate remote input because no approved receiver has subscribed to input events.");
            return false;
        }

        IsRemoteInputActive = true;
        ResetInputState();
        await SendModeChangeAsync(RemoteInputMode.RemoteInputActive, source);
        await SendCurrentSnapshotAsync(SnapshotReason.ModeStart);
        StatusChanged?.Invoke(this, "Remote input active");
        RemoteInputStateChanged?.Invoke(this, true);
        Log("Remote input activated.");
        return true;
    }

    public async Task DeactivateRemoteInputAsync(ModeChangeSource source = ModeChangeSource.WindowsUser)
    {
        if (!IsRemoteInputActive)
        {
            return;
        }

        await SendReleaseAllAsync();
        ResetInputState();
        await SendCurrentSnapshotAsync(SnapshotReason.ModeStop);
        IsRemoteInputActive = false;
        await SendModeChangeAsync(RemoteInputMode.Idle, source);
        StatusChanged?.Invoke(this, "Advertising as okfa");
        RemoteInputStateChanged?.Invoke(this, false);
        Log("Remote input deactivated.");
    }

    public async Task<bool> SendCapturedKeyEventAsync(CapturedInputEvent capturedEvent)
    {
        if (!IsRemoteInputActive)
        {
            return false;
        }

        if (_inputEventCharacteristic is null)
        {
            Log("Cannot send InputEvent because the characteristic is not ready.");
            return false;
        }

        if (!HasApprovedConnectedReceiver)
        {
            Log("Cannot send InputEvent because no approved receiver is connected.");
            return false;
        }

        SynchronizePressedState(capturedEvent);
        var message = new InputEventMessage(
            Sequence: NextInputEventSequence(),
            Action: capturedEvent.Action,
            HidUsage: capturedEvent.HidUsage,
            Modifiers: capturedEvent.Modifiers,
            EventFlags: capturedEvent.EventFlags,
            DeltaMilliseconds: NextInputDelta(capturedEvent.TimestampMilliseconds)
        );

        var results = await _inputEventCharacteristic.NotifyValueAsync(ToBuffer(InputMessageCodec.EncodeInputEvent(message)));
        if (NotificationSucceeded(results))
        {
            return true;
        }

        Log($"InputEvent notify failed results={DescribeNotificationResults(results)} action={capturedEvent.Action} usage=0x{capturedEvent.HidUsage:X2}");
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private PendingClientApproval? PendingClient
    {
        get => _pendingClient;
        set
        {
            _pendingClient = value;
            PendingClientChanged?.Invoke(this, value);
        }
    }

    private async Task<GattLocalCharacteristic> CreateCharacteristicAsync(
        Guid uuid,
        GattCharacteristicProperties properties,
        bool read,
        bool write,
        string description
    )
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException("GATT service provider is not ready.");
        }

        var parameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = properties,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = description,
        };

        var result = await _serviceProvider.Service.CreateCharacteristicAsync(uuid, parameters);
        if (result.Error != BluetoothError.Success)
        {
            throw new InvalidOperationException($"Characteristic {uuid} creation failed with {result.Error}.");
        }

        return result.Characteristic;
    }

    private async void OnControlWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
            {
                return;
            }

            var payload = ReadPayload(request.Value);
            Log($"Received Control write bytes={payload.Length} hex={Convert.ToHexString(payload)}");
            await HandleControlPayloadAsync(payload);

            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }
        }
        catch (Exception exception)
        {
            Log($"Control write failed: {exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task HandleControlPayloadAsync(byte[] payload)
    {
        if (!ControlMessageCodec.TryDecodeClientHello(payload, out var hello))
        {
            Log($"Ignored unsupported Control payload bytes={payload.Length}.");
            return;
        }

        Log($"Decoded ClientHello clientId=0x{hello.ClientId:X16} version={hello.ClientVersion} flags=0x{hello.CapabilityFlags:X4}");

        if (_trustedClientStore.IsTrusted(hello.ClientId))
        {
            _trustedClientStore.MarkSeen(hello.ClientId);
            _approvedClientIds.Add(hello.ClientId);
            TrustedClientCountChanged?.Invoke(this, _trustedClientStore.TrustedCount);
            await SendTrustStatusAsync(TrustStatusCode.Approved, TrustReason.AutoTrusted);
            PendingClient = null;
            NotifyConnectionStatus();
            return;
        }

        PendingClient = new PendingClientApproval(
            ClientId: hello.ClientId,
            ClientVersion: hello.ClientVersion,
            CapabilityFlags: hello.CapabilityFlags,
            RequestedAt: DateTimeOffset.UtcNow
        );
        await SendTrustStatusAsync(TrustStatusCode.Pending, TrustReason.UnknownClient);
        NotifyConnectionStatus();
    }

    private async void OnSnapshotReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            request?.RespondWithValue(ToBuffer(InputMessageCodec.EncodeSnapshot(MakeSnapshot(SnapshotReason.Resync))));
            Log("Served InputSnapshot read.");
        }
        catch (Exception exception)
        {
            Log($"Snapshot read failed: {exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnSubscribedClientsChanged(GattLocalCharacteristic sender, object args)
    {
        NotifyConnectionStatus();

        if (IsRemoteInputActive && !HasApprovedConnectedReceiver)
        {
            Log("Approved input subscribers are gone; deactivating remote input.");
            await DeactivateRemoteInputAsync(ModeChangeSource.System);
        }
    }

    private async Task SendTrustStatusAsync(TrustStatusCode status, TrustReason reason)
    {
        var payload = ControlMessageCodec.EncodeTrustStatus(
            NextControlSequence(),
            status,
            (byte)reason,
            serverFlags: 0,
            _serverId
        );
        await SendControlPayloadAsync(payload, $"TrustStatus {status}");
    }

    private async Task SendModeChangeAsync(RemoteInputMode mode, ModeChangeSource source)
    {
        var payload = ControlMessageCodec.EncodeModeChange(NextControlSequence(), mode, source, flags: 0);
        await SendControlPayloadAsync(payload, $"ModeChange {mode}");
    }

    private async Task SendReleaseAllAsync()
    {
        var payload = ControlMessageCodec.EncodeReleaseAll(NextControlSequence());
        await SendControlPayloadAsync(payload, "ReleaseAll");
    }

    private async Task SendControlPayloadAsync(byte[] payload, string label)
    {
        if (_controlCharacteristic is null)
        {
            Log($"Cannot send {label} because Control characteristic is not ready.");
            return;
        }

        var results = await _controlCharacteristic.NotifyValueAsync(ToBuffer(payload));
        Log($"Sent {label} results={DescribeNotificationResults(results)}");
    }

    private async Task SendCurrentSnapshotAsync(SnapshotReason reason)
    {
        if (_inputSnapshotCharacteristic is null)
        {
            return;
        }

        var results = await _inputSnapshotCharacteristic.NotifyValueAsync(
            ToBuffer(InputMessageCodec.EncodeSnapshot(MakeSnapshot(reason)))
        );
        Log($"Sent InputSnapshot reason={reason} results={DescribeNotificationResults(results)} pressed={_currentPressedUsages.Count} modifiers=0x{_currentModifiers:X2}");
    }

    private KeyboardSnapshotMessage MakeSnapshot(SnapshotReason reason) =>
        new(
            Sequence: NextSnapshotSequence(),
            Reason: reason,
            Modifiers: _currentModifiers,
            PressedCount: (byte)Math.Min(_currentPressedUsages.Count, 6),
            Usages: _currentPressedUsages.Take(6).ToArray()
        );

    private void SynchronizePressedState(CapturedInputEvent capturedEvent)
    {
        _currentModifiers = capturedEvent.Modifiers;

        if (capturedEvent.HidUsage == 0)
        {
            return;
        }

        switch (capturedEvent.Action)
        {
            case InputEventAction.KeyDown:
                if (!_currentPressedUsages.Contains(capturedEvent.HidUsage))
                {
                    _currentPressedUsages.Add(capturedEvent.HidUsage);
                }
                break;
            case InputEventAction.KeyUp:
                _currentPressedUsages.RemoveAll(usage => usage == capturedEvent.HidUsage);
                break;
            case InputEventAction.KeyRepeat:
            case InputEventAction.ModifiersOnly:
                break;
        }

        if (_currentPressedUsages.Count > 6)
        {
            _currentPressedUsages.RemoveRange(6, _currentPressedUsages.Count - 6);
        }
    }

    private void ResetInputState()
    {
        _currentModifiers = 0;
        _currentPressedUsages.Clear();
        _lastInputTimestampMilliseconds = null;
    }

    private ushort NextControlSequence()
    {
        _controlSequence += 1;
        return _controlSequence;
    }

    private ushort NextInputEventSequence()
    {
        _inputEventSequence += 1;
        return _inputEventSequence;
    }

    private ushort NextSnapshotSequence()
    {
        _snapshotSequence += 1;
        return _snapshotSequence;
    }

    private ushort NextInputDelta(ulong nowMilliseconds)
    {
        if (_lastInputTimestampMilliseconds is not { } previous)
        {
            _lastInputTimestampMilliseconds = nowMilliseconds;
            return 0;
        }

        _lastInputTimestampMilliseconds = nowMilliseconds;
        return (ushort)Math.Min(nowMilliseconds - previous, ushort.MaxValue);
    }

    private void NotifyConnectionStatus()
    {
        var status =
            $"Control subs: {_controlCharacteristic?.SubscribedClients.Count ?? 0} | Input subs: {_inputEventCharacteristic?.SubscribedClients.Count ?? 0} | Snapshot subs: {_inputSnapshotCharacteristic?.SubscribedClients.Count ?? 0} | Approved: {_approvedClientIds.Count}";
        ConnectionStatusChanged?.Invoke(this, status);
    }

    private static IBuffer ToBuffer(byte[] payload)
    {
        var writer = new DataWriter();
        writer.WriteBytes(payload);
        return writer.DetachBuffer();
    }

    private static byte[] ReadPayload(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var payload = new byte[(int)reader.UnconsumedBufferLength];
        reader.ReadBytes(payload);
        return payload;
    }

    private static bool NotificationSucceeded(IReadOnlyList<GattClientNotificationResult> results) =>
        results.Count > 0 && results.All(static result => result.Status == GattCommunicationStatus.Success);

    private static string DescribeNotificationResults(IReadOnlyList<GattClientNotificationResult> results)
    {
        if (results.Count == 0)
        {
            return "<no subscribers>";
        }

        return string.Join(", ", results.Select(static result => result.Status.ToString()));
    }

    private static ulong LoadOrCreateServerId()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "okfa");
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "sender-id.txt");

        if (File.Exists(filePath)
            && ulong.TryParse(File.ReadAllText(filePath), System.Globalization.NumberStyles.HexNumber, null, out var existing))
        {
            return existing;
        }

        var generated = (ulong)Random.Shared.NextInt64();
        File.WriteAllText(filePath, generated.ToString("X16"));
        return generated;
    }

    private void Log(string message)
    {
        BridgeLog.Write("WindowsGattSender", message);
        LogEmitted?.Invoke(this, message);
    }
}
