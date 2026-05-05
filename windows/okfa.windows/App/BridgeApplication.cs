using KeyboardBridge.Windows.Bluetooth;
using KeyboardBridge.Windows.Input;
using KeyboardBridge.Windows.Protocol;
using KeyboardBridge.Windows.Trust;

namespace KeyboardBridge.Windows.App;

public sealed class BridgeApplication
{
    private readonly BridgeScanner _scanner = new();
    private readonly TrustedSenderStore _trustedSenderStore = new();
    private readonly InputInjector _inputInjector = new();
    private readonly Dictionary<ulong, DateTimeOffset> _lastLoggedAtByAddress = new();
    private BridgeSession? _session;

    public async Task RunAsync()
    {
        Console.WriteLine("okfa Windows Phase 2 skeleton");
        Console.WriteLine("This scaffold wires up scanning, GATT session setup, ClientHello, and InputEvent logging.");

        _scanner.DeviceFound += OnDeviceFound;
        _scanner.Start();

        Console.WriteLine("Scanning for okfa sender PCs. Press Enter to stop.");
        Console.ReadLine();

        _scanner.Stop();
        await DisconnectAsync();
    }

    private async void OnDeviceFound(object? sender, BridgeAdvertisement advertisement)
    {
        if (_session is not null)
        {
            return;
        }

        if (ShouldLogDiscovery(advertisement.BluetoothAddress))
        {
            Console.WriteLine(
                $"Found {advertisement.LocalName} RSSI={advertisement.Rssi} Address=0x{advertisement.BluetoothAddress:X} ServiceMatch={advertisement.HasTargetService}"
            );
        }

        _session = new BridgeSession();
        _session.LogEmitted += (_, message) => Console.WriteLine(message);
        _session.TrustStatusReceived += (_, status) =>
        {
            Console.WriteLine($"Trust status={status.Status} reason={status.Reason} serverId=0x{status.ServerId:X}");
        };
        _session.ModeChangeReceived += (_, modeChange) =>
        {
            Console.WriteLine($"Mode change={modeChange.Mode} source={modeChange.Source}");
            if (modeChange.Mode == RemoteInputMode.Idle)
            {
                _inputInjector.ReleaseAllInjectedKeys();
            }
        };
        _session.ReleaseAllReceived += (_, releaseAll) =>
        {
            Console.WriteLine($"ReleaseAll sequence={releaseAll.Sequence}");
            _inputInjector.ReleaseAllInjectedKeys();
        };
        _session.SnapshotReceived += (_, snapshot) =>
        {
            var usages = snapshot.Usages.Count == 0 ? "<none>" : string.Join(", ", snapshot.Usages.Select(static usage => $"0x{usage:X2}"));
            Console.WriteLine($"Snapshot reason={snapshot.Reason} modifiers=0x{snapshot.Modifiers:X2} usages={usages}");
            if (snapshot.Reason == SnapshotReason.ModeStop)
            {
                _inputInjector.ReleaseAllInjectedKeys();
                return;
            }

            _inputInjector.HandleSnapshot(snapshot);
        };
        _session.InputEventReceived += (_, inputEvent) =>
        {
            _inputInjector.HandleInputEvent(inputEvent);
        };

        try
        {
            await _session.ConnectAsync(advertisement.BluetoothAddress);
            await _session.SendClientHelloAsync(_trustedSenderStore.ClientId, clientVersion: 1, capabilityFlags: 0);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Connection failed: {exception.Message}");
            await DisconnectAsync();
        }
    }

    private bool ShouldLogDiscovery(ulong bluetoothAddress)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastLoggedAtByAddress.TryGetValue(bluetoothAddress, out var previous) && now - previous < TimeSpan.FromSeconds(3))
        {
            return false;
        }

        _lastLoggedAtByAddress[bluetoothAddress] = now;
        return true;
    }

    private async Task DisconnectAsync()
    {
        if (_session is null)
        {
            return;
        }

        await _session.DisposeAsync();
        _session = null;
        _inputInjector.ReleaseAllInjectedKeys();
    }
}
