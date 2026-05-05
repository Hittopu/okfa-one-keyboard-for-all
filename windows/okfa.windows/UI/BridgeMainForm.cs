using KeyboardBridge.Windows.Bluetooth;
using KeyboardBridge.Windows.Diagnostics;
using KeyboardBridge.Windows.Input;
using KeyboardBridge.Windows.Protocol;
using KeyboardBridge.Windows.Trust;

namespace KeyboardBridge.Windows.UI;

public sealed class BridgeMainForm : Form
{
    private readonly BridgeScanner _scanner = new();
    private readonly TrustedSenderStore _trustedSenderStore = new();
    private readonly InputInjector _inputInjector = new();
    private readonly Dictionary<ulong, BridgeAdvertisement> _discoveredDevices = new();

    private BridgeSession? _session;
    private BridgeAdvertisement? _connectedAdvertisement;
    private ulong? _selectedBluetoothAddress;
    private bool _isScanning;
    private bool _isConnecting;
    private bool _isRemoteInputLive;
    private TrustStatusCode? _trustStatus;
    private string? _connectionProgress;
    private string? _lastConnectionError;
    private PrimaryAction _primaryAction = PrimaryAction.None;
    private SecondaryAction _secondaryAction = SecondaryAction.None;

    private readonly Panel _surfacePanel = new();
    private readonly BridgeStatusBadge _statusBadge = new();
    private readonly Label _brandLabel = new();
    private readonly Label _headlineLabel = new();
    private readonly Label _messageLabel = new();
    private readonly Label _deviceCaptionLabel = new();
    private readonly Label _deviceLabel = new();
    private readonly BridgeButton _primaryButton = new();
    private readonly BridgeButton _secondaryButton = new();
    private readonly FlowLayoutPanel _buttonRow = new();

    public BridgeMainForm()
    {
        Text = "okfa Receiver";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 600);
        ClientSize = new Size(820, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(243, 246, 251);
        Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        _inputInjector.IsEnabled = true;
        _scanner.DeviceFound += OnDeviceFound;

        BuildLayout();
        RefreshUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BridgeTheme.TryApplyWindowBackdrop(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BridgeLog.Write("BridgeMainForm", "Window shown. Starting BLE scan.");
        StartScanning();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        StopScanning();
        await DisconnectAsync(restartScanning: false);
        _scanner.Dispose();
    }

    private void BuildLayout()
    {
        Padding = new Padding(32);

        _surfacePanel.Dock = DockStyle.Fill;
        _surfacePanel.BackColor = Color.FromArgb(249, 250, 252);
        _surfacePanel.Paint += (_, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(226, 231, 238), 1f);
            var rect = new Rectangle(0, 0, _surfacePanel.Width - 1, _surfacePanel.Height - 1);
            e.Graphics.DrawRectangle(borderPen, rect);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Anchor = AnchorStyles.None,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _brandLabel.AutoSize = true;
        _brandLabel.Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        _brandLabel.ForeColor = Color.FromArgb(102, 110, 124);
        _brandLabel.TextAlign = ContentAlignment.MiddleCenter;
        _brandLabel.Anchor = AnchorStyles.None;
        _brandLabel.Margin = new Padding(0, 0, 0, 12);
        _brandLabel.Text = "okfa";

        _statusBadge.Size = new Size(52, 52);
        _statusBadge.Anchor = AnchorStyles.None;
        _statusBadge.Margin = new Padding(0, 0, 0, 18);
        _statusBadge.Apply(BridgeBadgeKind.Waves, BridgeTheme.AccentBlue);

        _headlineLabel.AutoSize = true;
        _headlineLabel.Font = new Font("Segoe UI Variable Display", 32f, FontStyle.Bold, GraphicsUnit.Point);
        _headlineLabel.ForeColor = Color.FromArgb(24, 28, 34);
        _headlineLabel.MaximumSize = new Size(560, 0);
        _headlineLabel.TextAlign = ContentAlignment.MiddleCenter;
        _headlineLabel.Anchor = AnchorStyles.None;
        _headlineLabel.Margin = new Padding(0, 0, 0, 10);

        _messageLabel.AutoSize = true;
        _messageLabel.Font = new Font("Segoe UI Variable Text", 15f, FontStyle.Regular, GraphicsUnit.Point);
        _messageLabel.ForeColor = Color.FromArgb(100, 108, 124);
        _messageLabel.MaximumSize = new Size(520, 0);
        _messageLabel.TextAlign = ContentAlignment.MiddleCenter;
        _messageLabel.Anchor = AnchorStyles.None;
        _messageLabel.Margin = new Padding(0, 0, 0, 26);

        _deviceCaptionLabel.AutoSize = true;
        _deviceCaptionLabel.Font = new Font("Segoe UI Variable Text", 10f, FontStyle.Bold, GraphicsUnit.Point);
        _deviceCaptionLabel.ForeColor = Color.FromArgb(108, 116, 132);
        _deviceCaptionLabel.TextAlign = ContentAlignment.MiddleCenter;
        _deviceCaptionLabel.Anchor = AnchorStyles.None;
        _deviceCaptionLabel.Margin = new Padding(0, 0, 0, 8);
        _deviceCaptionLabel.Text = "Sender PC";

        _deviceLabel.AutoSize = false;
        _deviceLabel.Width = 420;
        _deviceLabel.Height = 42;
        _deviceLabel.Font = new Font("Segoe UI Variable Text", 11f, FontStyle.Regular, GraphicsUnit.Point);
        _deviceLabel.ForeColor = Color.FromArgb(34, 39, 48);
        _deviceLabel.TextAlign = ContentAlignment.MiddleCenter;
        _deviceLabel.Anchor = AnchorStyles.None;
        _deviceLabel.BackColor = Color.FromArgb(244, 247, 251);
        _deviceLabel.BorderStyle = BorderStyle.FixedSingle;
        _deviceLabel.AutoEllipsis = true;
        _deviceLabel.Margin = new Padding(0, 0, 0, 28);

        ConfigureButton(_primaryButton, BridgeButtonKind.Primary, 190);
        _primaryButton.Margin = new Padding(0, 0, 12, 0);
        _primaryButton.Click += async (_, _) => await HandlePrimaryActionAsync();

        ConfigureButton(_secondaryButton, BridgeButtonKind.Secondary, 190);
        _secondaryButton.Margin = Padding.Empty;
        _secondaryButton.Click += (_, _) => HandleSecondaryAction();

        _buttonRow.AutoSize = true;
        _buttonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonRow.FlowDirection = FlowDirection.LeftToRight;
        _buttonRow.WrapContents = false;
        _buttonRow.BackColor = Color.Transparent;
        _buttonRow.Anchor = AnchorStyles.None;
        _buttonRow.Margin = Padding.Empty;
        _buttonRow.Padding = Padding.Empty;
        _buttonRow.Controls.Add(_primaryButton);
        _buttonRow.Controls.Add(_secondaryButton);

        content.Controls.Add(_brandLabel, 0, 0);
        content.Controls.Add(_statusBadge, 0, 1);
        content.Controls.Add(_headlineLabel, 0, 2);
        content.Controls.Add(_messageLabel, 0, 3);
        content.Controls.Add(_deviceCaptionLabel, 0, 4);
        content.Controls.Add(_deviceLabel, 0, 5);
        content.Controls.Add(_buttonRow, 0, 6);

        root.Controls.Add(content, 1, 1);
        _surfacePanel.Controls.Add(root);
        Controls.Add(_surfacePanel);
        AcceptButton = _primaryButton;
    }

    private static void ConfigureButton(BridgeButton button, BridgeButtonKind kind, int width)
    {
        button.AutoSize = false;
        button.Width = width;
        button.Height = 46;
        button.BridgeKind = kind;
        button.Font = new Font("Segoe UI Variable Text", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
        button.Cursor = Cursors.Hand;
    }

    private async Task HandlePrimaryActionAsync()
    {
        switch (_primaryAction)
        {
            case PrimaryAction.Connect:
                await ConnectSelectedAsync();
                break;
            case PrimaryAction.Disconnect:
                await DisconnectAsync(restartScanning: true);
                break;
        }
    }

    private void HandleSecondaryAction()
    {
        switch (_secondaryAction)
        {
            case SecondaryAction.ChooseDevice:
                OpenDevicePicker();
                break;
            case SecondaryAction.Rescan:
                RestartScanning();
                break;
        }
    }

    private void StartScanning()
    {
        if (_isScanning || _session is not null)
        {
            return;
        }

        _scanner.Start();
        _isScanning = true;
        RefreshUi();
    }

    private void StopScanning()
    {
        if (!_isScanning)
        {
            return;
        }

        _scanner.Stop();
        _isScanning = false;
        RefreshUi();
    }

    private void RestartScanning()
    {
        if (_isConnecting)
        {
            return;
        }

        _discoveredDevices.Clear();
        _lastConnectionError = null;
        _connectionProgress = null;
        if (_session is null)
        {
            _selectedBluetoothAddress = null;
        }

        StopScanning();
        StartScanning();
    }

    private void OpenDevicePicker()
    {
        var devices = SortedDevices().ToArray();
        if (devices.Length == 0)
        {
            RestartScanning();
            return;
        }

        using var dialog = new AllDevicesForm(devices, _selectedBluetoothAddress, _connectedAdvertisement?.BluetoothAddress);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedAdvertisement is null)
        {
            return;
        }

        _selectedBluetoothAddress = dialog.SelectedAdvertisement.BluetoothAddress;
        RefreshUi();
    }

    private async Task ConnectSelectedAsync()
    {
        if (_isConnecting || _session is not null)
        {
            return;
        }

        var advertisement = SelectedAdvertisement();
        if (advertisement is null)
        {
            return;
        }

        _isConnecting = true;
        _trustStatus = null;
        _lastConnectionError = null;
        _connectionProgress = "Opening Bluetooth connection...";
        _isRemoteInputLive = false;
        StopScanning();
        RefreshUi();

        var session = new BridgeSession();
        _session = session;

        session.LogEmitted += (_, message) => RunOnUi(() =>
        {
            _connectionProgress = message;
            if (_isConnecting)
            {
                RefreshUi();
            }
        });

        session.TrustStatusReceived += (_, status) => RunOnUi(() =>
        {
            _trustStatus = status.Status;
            RefreshUi();
        });

        session.ModeChangeReceived += (_, modeChange) => RunOnUi(() =>
        {
            _isRemoteInputLive = modeChange.Mode == RemoteInputMode.RemoteInputActive;
            RefreshUi();

            if (!_isRemoteInputLive)
            {
                _inputInjector.ReleaseAllInjectedKeys();
            }
        });
        session.ReleaseAllReceived += (_, _) => _inputInjector.ReleaseAllInjectedKeys();

        session.SnapshotReceived += (_, snapshot) =>
        {
            if (snapshot.Reason == SnapshotReason.ModeStop)
            {
                _inputInjector.ReleaseAllInjectedKeys();
                return;
            }

            _inputInjector.HandleSnapshot(snapshot);
        };

        session.InputEventReceived += (_, inputEvent) => _inputInjector.HandleInputEvent(inputEvent);

        try
        {
            await session.ConnectAsync(advertisement.BluetoothAddress);
            await session.SendClientHelloAsync(_trustedSenderStore.ClientId, clientVersion: 1, capabilityFlags: 0);

            RunOnUi(() =>
            {
                _connectedAdvertisement = advertisement;
                _selectedBluetoothAddress = advertisement.BluetoothAddress;
                RefreshUi();
            });
        }
        catch (Exception exception)
        {
            BridgeLog.WriteException("BridgeMainForm", exception);
            await DisconnectAsync(restartScanning: true);
            RunOnUi(() =>
            {
                _lastConnectionError = exception.Message;
                _connectionProgress = null;
                RefreshUi();
            });
        }
        finally
        {
            _isConnecting = false;
            RunOnUi(RefreshUi);
        }
    }

    private async Task DisconnectAsync(bool restartScanning)
    {
        if (_session is not null)
        {
            var session = _session;
            _session = null;
            await session.DisposeAsync();
        }

        _inputInjector.ReleaseAllInjectedKeys();
        if (_connectedAdvertisement is not null)
        {
            _selectedBluetoothAddress = _connectedAdvertisement.BluetoothAddress;
        }

        _connectedAdvertisement = null;
        _isRemoteInputLive = false;
        _trustStatus = null;
        _connectionProgress = null;

        if (restartScanning)
        {
            StartScanning();
        }

        RefreshUi();
    }

    private void OnDeviceFound(object? sender, BridgeAdvertisement advertisement)
    {
        RunOnUi(() =>
        {
            _discoveredDevices[advertisement.BluetoothAddress] = advertisement;
            EnsureSelection();
            RefreshUi();
        });
    }

    private IEnumerable<BridgeAdvertisement> SortedDevices()
    {
        return _discoveredDevices.Values
            .OrderByDescending(static advertisement => advertisement.HasTargetService)
            .ThenByDescending(static advertisement => advertisement.Rssi)
            .ThenBy(static advertisement => advertisement.LocalName, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureSelection()
    {
        if (_connectedAdvertisement is not null)
        {
            _selectedBluetoothAddress = _connectedAdvertisement.BluetoothAddress;
            return;
        }

        if (_selectedBluetoothAddress is not null && _discoveredDevices.ContainsKey(_selectedBluetoothAddress.Value))
        {
            return;
        }

        _selectedBluetoothAddress = SortedDevices().FirstOrDefault()?.BluetoothAddress;
    }

    private BridgeAdvertisement? SelectedAdvertisement()
    {
        if (_connectedAdvertisement is not null)
        {
            return _connectedAdvertisement;
        }

        if (_selectedBluetoothAddress is not null && _discoveredDevices.TryGetValue(_selectedBluetoothAddress.Value, out var advertisement))
        {
            return advertisement;
        }

        return null;
    }

    private void RefreshUi()
    {
        EnsureSelection();
        var state = ComputeState();

        _headlineLabel.Text = state.Headline;
        _messageLabel.Text = state.Message;
        _statusBadge.Apply(state.BadgeKind, state.BadgeColor);

        _deviceCaptionLabel.Visible = state.DeviceText is not null;
        _deviceLabel.Visible = state.DeviceText is not null;
        _deviceLabel.Text = state.DeviceText ?? string.Empty;

        _primaryAction = state.PrimaryAction;
        _primaryButton.Text = state.PrimaryTitle;
        _primaryButton.Visible = state.ShowPrimary;
        _primaryButton.Enabled = state.PrimaryEnabled;

        _secondaryAction = state.SecondaryAction;
        _secondaryButton.Text = state.SecondaryTitle;
        _secondaryButton.Visible = state.ShowSecondary;
        _secondaryButton.Enabled = state.SecondaryEnabled;

        _buttonRow.Visible = state.ShowPrimary || state.ShowSecondary;
    }

    private VisualState ComputeState()
    {
        var selectedAdvertisement = SelectedAdvertisement();
        var deviceCount = _discoveredDevices.Count;

        if (_session is not null && _connectedAdvertisement is not null)
        {
            if (_trustStatus == TrustStatusCode.Denied)
            {
                return new VisualState(
                    "Access denied",
                    "Approve this receiver PC on the sender, then reconnect.",
                    _connectedAdvertisement.LocalName,
                    BridgeBadgeKind.Warning,
                    BridgeTheme.WarningOrange,
                    true,
                    "Disconnect",
                    !_isConnecting,
                    PrimaryAction.Disconnect,
                    false,
                    string.Empty,
                    false,
                    SecondaryAction.None
                );
            }

            if (_trustStatus == TrustStatusCode.Pending)
            {
                return new VisualState(
                    "Approve on sender PC",
                    "Approve this receiver on the sender PC before input can be shared.",
                    _connectedAdvertisement.LocalName,
                    BridgeBadgeKind.Question,
                    BridgeTheme.WarningOrange,
                    true,
                    "Disconnect",
                    true,
                    PrimaryAction.Disconnect,
                    false,
                    string.Empty,
                    false,
                    SecondaryAction.None
                );
            }

            if (_isRemoteInputLive)
            {
                return new VisualState(
                    "Connected",
                    "Keyboard input is currently routed to this receiver PC.",
                    _connectedAdvertisement.LocalName,
                    BridgeBadgeKind.Check,
                    BridgeTheme.SuccessGreen,
                    true,
                    "Disconnect",
                    true,
                    PrimaryAction.Disconnect,
                    false,
                    string.Empty,
                    false,
                    SecondaryAction.None
                );
            }

            return new VisualState(
                "Ready",
                    "Use the sender PC to start sharing the keyboard.",
                _connectedAdvertisement.LocalName,
                BridgeBadgeKind.Link,
                BridgeTheme.AccentBlue,
                true,
                "Disconnect",
                true,
                PrimaryAction.Disconnect,
                false,
                string.Empty,
                false,
                SecondaryAction.None
            );
        }

        if (_isConnecting)
        {
            return new VisualState(
                "Connecting",
                _connectionProgress
                    ?? (selectedAdvertisement is null
                        ? "Starting a secure link."
                        : $"Starting a secure link to {selectedAdvertisement.LocalName}."),
                selectedAdvertisement?.LocalName,
                BridgeBadgeKind.Link,
                BridgeTheme.AccentBlue,
                false,
                string.Empty,
                false,
                PrimaryAction.None,
                false,
                string.Empty,
                false,
                SecondaryAction.None
            );
        }

        if (_lastConnectionError is not null)
        {
            return new VisualState(
                "Connection failed",
                _lastConnectionError,
                selectedAdvertisement?.LocalName,
                BridgeBadgeKind.Warning,
                BridgeTheme.WarningOrange,
                true,
                "Connect",
                selectedAdvertisement is not null,
                PrimaryAction.Connect,
                true,
                "Refresh",
                true,
                SecondaryAction.Rescan
            );
        }

        if (selectedAdvertisement is not null)
        {
            return new VisualState(
                "Ready to connect",
                "Use the selected sender PC below, then press Connect.",
                selectedAdvertisement.LocalName,
                selectedAdvertisement.HasTargetService ? BridgeBadgeKind.Link : BridgeBadgeKind.Waves,
                BridgeTheme.AccentBlue,
                true,
                "Connect",
                true,
                PrimaryAction.Connect,
                true,
                deviceCount > 1 ? "Choose Sender" : "Refresh",
                true,
                deviceCount > 1 ? SecondaryAction.ChooseDevice : SecondaryAction.Rescan
            );
        }

        if (_isScanning)
        {
            return new VisualState(
                "Looking for sender PC",
                "Keep the okfa sender app open and nearby.",
                null,
                BridgeBadgeKind.Waves,
                BridgeTheme.AccentBlue,
                false,
                string.Empty,
                false,
                PrimaryAction.None,
                true,
                "Refresh",
                true,
                SecondaryAction.Rescan
            );
        }

        return new VisualState(
            "No sender PCs nearby",
            "Open the sender app, then try again.",
            null,
            BridgeBadgeKind.Off,
            BridgeTheme.TextSecondary,
            false,
            string.Empty,
            false,
            PrimaryAction.None,
            true,
            "Refresh",
            true,
            SecondaryAction.Rescan
        );
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private enum PrimaryAction
    {
        None,
        Connect,
        Disconnect,
    }

    private enum SecondaryAction
    {
        None,
        ChooseDevice,
        Rescan,
    }

    private readonly record struct VisualState(
        string Headline,
        string Message,
        string? DeviceText,
        BridgeBadgeKind BadgeKind,
        Color BadgeColor,
        bool ShowPrimary,
        string PrimaryTitle,
        bool PrimaryEnabled,
        PrimaryAction PrimaryAction,
        bool ShowSecondary,
        string SecondaryTitle,
        bool SecondaryEnabled,
        SecondaryAction SecondaryAction
    );
}
