using KeyboardBridge.Windows.Protocol;
using Windows.Devices.Bluetooth.Advertisement;

namespace KeyboardBridge.Windows.Bluetooth;

public sealed record BridgeAdvertisement(
    ulong BluetoothAddress,
    string LocalName,
    short Rssi,
    bool HasTargetService
);

public sealed class BridgeScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly Dictionary<ulong, AggregatedAdvertisement> _advertisements = new();

    public event EventHandler<BridgeAdvertisement>? DeviceFound;

    public BridgeScanner()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        _watcher.Received += OnReceived;
    }

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        _watcher.Stop();
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var hasTargetService = args.Advertisement.ServiceUuids.Contains(BridgeUuids.Service);
        var localName = string.IsNullOrWhiteSpace(args.Advertisement.LocalName) ? null : args.Advertisement.LocalName;

        if (!_advertisements.TryGetValue(args.BluetoothAddress, out var aggregate))
        {
            aggregate = new AggregatedAdvertisement();
        }

        aggregate.HasTargetService |= hasTargetService;
        if (!string.IsNullOrWhiteSpace(localName))
        {
            aggregate.LocalName = localName;
        }
        aggregate.Rssi = args.RawSignalStrengthInDBm;
        _advertisements[args.BluetoothAddress] = aggregate;

        var looksLikeKeyboardBridge = aggregate.HasTargetService
            || (!string.IsNullOrWhiteSpace(aggregate.LocalName) && aggregate.LocalName.Contains("okfa", StringComparison.OrdinalIgnoreCase));

        if (!looksLikeKeyboardBridge)
        {
            return;
        }

        DeviceFound?.Invoke(
            this,
            new BridgeAdvertisement(
                BluetoothAddress: args.BluetoothAddress,
                LocalName: aggregate.LocalName ?? "okfa Mac",
                Rssi: aggregate.Rssi,
                HasTargetService: aggregate.HasTargetService
            )
        );
    }

    private sealed class AggregatedAdvertisement
    {
        public string? LocalName { get; set; }

        public short Rssi { get; set; }

        public bool HasTargetService { get; set; }
    }
}
