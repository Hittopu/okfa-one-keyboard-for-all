using KeyboardBridge.Windows.Protocol;
using KeyboardBridge.Windows.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace KeyboardBridge.Windows.Bluetooth;

public sealed class BridgeSession : IAsyncDisposable
{
    private static readonly TimeSpan BluetoothOperationTimeout = TimeSpan.FromSeconds(15);
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _controlCharacteristic;
    private GattCharacteristic? _inputEventCharacteristic;
    private GattCharacteristic? _inputSnapshotCharacteristic;
    private ushort _controlSequence;

    public event EventHandler<string>? LogEmitted;
    public event EventHandler<TrustStatusMessage>? TrustStatusReceived;
    public event EventHandler<ModeChangeMessage>? ModeChangeReceived;
    public event EventHandler<ReleaseAllMessage>? ReleaseAllReceived;
    public event EventHandler<InputEventMessage>? InputEventReceived;
    public event EventHandler<KeyboardSnapshotMessage>? SnapshotReceived;

    public async Task ConnectAsync(ulong bluetoothAddress)
    {
        Log($"Opening BluetoothLEDevice address=0x{bluetoothAddress:X}...");
        _device = await WithTimeout(
            async () => await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress),
            "open Bluetooth device"
        );
        if (_device is null)
        {
            throw new InvalidOperationException("BluetoothLEDevice.FromBluetoothAddressAsync returned null.");
        }

        Log("Discovering okfa GATT service...");
        var serviceResult = await WithTimeout(
            async () => await _device.GetGattServicesForUuidAsync(BridgeUuids.Service, BluetoothCacheMode.Uncached),
            "discover okfa service"
        );
        if (serviceResult.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Service discovery failed with status {serviceResult.Status}.");
        }

        _service = serviceResult.Services.FirstOrDefault()
            ?? throw new InvalidOperationException("okfa service was not found.");

        Log("Resolving Control characteristic...");
        _controlCharacteristic = await ResolveCharacteristicAsync(BridgeUuids.Control);
        Log("Resolving InputEvent characteristic...");
        _inputEventCharacteristic = await ResolveCharacteristicAsync(BridgeUuids.InputEvent);
        Log("Resolving InputSnapshot characteristic...");
        _inputSnapshotCharacteristic = await ResolveCharacteristicAsync(BridgeUuids.InputSnapshot);

        _controlCharacteristic.ValueChanged += OnControlValueChanged;
        _inputEventCharacteristic.ValueChanged += OnInputEventValueChanged;
        _inputSnapshotCharacteristic.ValueChanged += OnSnapshotValueChanged;

        Log("Subscribing to Control notifications...");
        var controlNotifyStatus = await WithTimeout(
            async () => await _controlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify
            ),
            "subscribe Control notifications"
        );
        Log("Subscribing to InputEvent notifications...");
        var inputNotifyStatus = await WithTimeout(
            async () => await _inputEventCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify
            ),
            "subscribe InputEvent notifications"
        );
        Log("Subscribing to InputSnapshot notifications...");
        var snapshotNotifyStatus = await WithTimeout(
            async () => await _inputSnapshotCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify
            ),
            "subscribe InputSnapshot notifications"
        );

        Log(
            $"Connected. Control notify={controlNotifyStatus} Input notify={inputNotifyStatus} Snapshot notify={snapshotNotifyStatus}"
        );
    }

    public async Task SendClientHelloAsync(ulong clientId, ushort clientVersion, ushort capabilityFlags)
    {
        if (_controlCharacteristic is null)
        {
            throw new InvalidOperationException("Control characteristic is not ready.");
        }

        var payload = ControlMessageCodec.EncodeClientHello(++_controlSequence, clientId, clientVersion, capabilityFlags);
        var writer = new DataWriter();
        writer.WriteBytes(payload);

        Log($"Sending ClientHello clientId=0x{clientId:X16}...");
        var status = await WithTimeout(
            async () => await _controlCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse),
            "send ClientHello"
        );
        Log($"Sent ClientHello clientId=0x{clientId:X16} status={status}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_controlCharacteristic is not null)
        {
            _controlCharacteristic.ValueChanged -= OnControlValueChanged;
            await DisableNotifyAsync(_controlCharacteristic);
        }

        if (_inputEventCharacteristic is not null)
        {
            _inputEventCharacteristic.ValueChanged -= OnInputEventValueChanged;
            await DisableNotifyAsync(_inputEventCharacteristic);
        }

        if (_inputSnapshotCharacteristic is not null)
        {
            _inputSnapshotCharacteristic.ValueChanged -= OnSnapshotValueChanged;
            await DisableNotifyAsync(_inputSnapshotCharacteristic);
        }

        _inputSnapshotCharacteristic = null;
        _inputEventCharacteristic = null;
        _controlCharacteristic = null;
        _service?.Dispose();
        _service = null;
        _device?.Dispose();
        _device = null;
    }

    private async Task<GattCharacteristic> ResolveCharacteristicAsync(Guid uuid)
    {
        if (_service is null)
        {
            throw new InvalidOperationException("Service is not ready.");
        }

        var result = await WithTimeout(
            async () => await _service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached),
            $"discover characteristic {uuid}"
        );
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Characteristic discovery for {uuid} failed with status {result.Status}.");
        }

        return result.Characteristics.FirstOrDefault()
            ?? throw new InvalidOperationException($"Characteristic {uuid} was not found.");
    }

    private void OnControlValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var payload = ReadPayload(args.CharacteristicValue);

        if (ControlMessageCodec.TryDecodeTrustStatus(payload, out var trustStatus))
        {
            Log($"Received TrustStatus status={trustStatus.Status} reason={trustStatus.Reason} serverId=0x{trustStatus.ServerId:X16}");
            TrustStatusReceived?.Invoke(this, trustStatus);
            return;
        }

        if (ControlMessageCodec.TryDecodeModeChange(payload, out var modeChange))
        {
            Log($"Received ModeChange mode={modeChange.Mode} source={modeChange.Source}");
            ModeChangeReceived?.Invoke(this, modeChange);
            return;
        }

        if (ControlMessageCodec.TryDecodeReleaseAll(payload, out var releaseAll))
        {
            Log($"Received ReleaseAll sequence={releaseAll.Sequence}");
            ReleaseAllReceived?.Invoke(this, releaseAll);
            return;
        }

        Log($"Received unknown control payload bytes={payload.Length} hex={Convert.ToHexString(payload)}");
    }

    private void OnInputEventValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var payload = ReadPayload(args.CharacteristicValue);

        if (InputMessageCodec.TryDecodeInputEvent(payload, out var inputEvent))
        {
            Log($"Received InputEvent action={inputEvent.Action} usage=0x{inputEvent.HidUsage:X2} modifiers=0x{inputEvent.Modifiers:X2} delta={inputEvent.DeltaMilliseconds}ms");
            InputEventReceived?.Invoke(this, inputEvent);
            return;
        }

        Log($"Received unknown InputEvent payload bytes={payload.Length} hex={Convert.ToHexString(payload)}");
    }

    private void OnSnapshotValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var payload = ReadPayload(args.CharacteristicValue);

        if (InputMessageCodec.TryDecodeSnapshot(payload, out var snapshot))
        {
            var usages = snapshot.Usages.Count == 0 ? "<none>" : string.Join(", ", snapshot.Usages.Select(static usage => $"0x{usage:X2}"));
            Log($"Received Snapshot reason={snapshot.Reason} modifiers=0x{snapshot.Modifiers:X2} usages={usages}");
            SnapshotReceived?.Invoke(this, snapshot);
            return;
        }

        Log($"Received unknown Snapshot payload bytes={payload.Length} hex={Convert.ToHexString(payload)}");
    }

    private static byte[] ReadPayload(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var payload = new byte[(int)reader.UnconsumedBufferLength];
        reader.ReadBytes(payload);
        return payload;
    }

    private async Task DisableNotifyAsync(GattCharacteristic characteristic)
    {
        try
        {
            await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None
            );
        }
        catch
        {
        }
    }

    private void Log(string message)
    {
        BridgeLog.Write("BridgeSession", message);
        LogEmitted?.Invoke(this, message);
    }

    private static async Task<T> WithTimeout<T>(Func<Task<T>> operation, string label)
    {
        var task = operation();
        var completed = await Task.WhenAny(task, Task.Delay(BluetoothOperationTimeout));
        if (completed != task)
        {
            throw new TimeoutException($"{label} timed out after {BluetoothOperationTimeout.TotalSeconds:0}s.");
        }

        return await task;
    }
}
