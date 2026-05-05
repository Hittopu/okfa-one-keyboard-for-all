using KeyboardBridge.Windows.Bluetooth;
using KeyboardBridge.Windows.Diagnostics;
using KeyboardBridge.Windows.Input;
using KeyboardBridge.Windows.Protocol;
using KeyboardBridge.Windows.Trust;

namespace KeyboardBridge.Windows.UI;

public sealed class SenderMainForm : Form
{
    private readonly WindowsGattSenderService _senderService = new(new TrustedClientStore());
    private readonly WindowsKeyboardCaptureManager _captureManager = new();

    private string _currentStatus = "Starting...";
    private string _currentConnectionStatus = "Control subs: 0 | Input subs: 0 | Snapshot subs: 0 | Approved: 0";
    private string _currentCaptureStatus = "Keyboard capture starting...";
    private int _currentTrustedCount;
    private PendingClientApproval? _currentPendingClient;
    private bool _isRemoteInputLive;
    private bool _isStarting;
    private PrimaryAction _primaryAction = PrimaryAction.None;
    private SecondaryAction _secondaryAction = SecondaryAction.None;

    private readonly Panel _surfacePanel = new();
    private readonly BridgeStatusBadge _statusBadge = new();
    private readonly Label _brandLabel = new();
    private readonly Label _headlineLabel = new();
    private readonly Label _messageLabel = new();
    private readonly Label _shortcutLabel = new();
    private readonly Label _deviceCaptionLabel = new();
    private readonly Label _deviceLabel = new();
    private readonly BridgeButton _primaryButton = new();
    private readonly BridgeButton _secondaryButton = new();
    private readonly FlowLayoutPanel _buttonRow = new();

    public SenderMainForm()
    {
        Text = "okfa Sender";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 600);
        ClientSize = new Size(820, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(243, 246, 251);
        Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        WireEvents();
        BuildLayout();
        RefreshUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BridgeTheme.TryApplyWindowBackdrop(this);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BridgeLog.Write("SenderMainForm", "Window shown. Starting sender service.");
        _captureManager.Start();
        await StartSenderAsync();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _captureManager.Dispose();
        await _senderService.StopAsync();
    }

    private void WireEvents()
    {
        _senderService.StatusChanged += (_, status) => RunOnUi(() =>
        {
            _currentStatus = status;
            RefreshUi();
        });
        _senderService.ConnectionStatusChanged += (_, status) => RunOnUi(() =>
        {
            _currentConnectionStatus = status;
            RefreshUi();
        });
        _senderService.TrustedClientCountChanged += (_, count) => RunOnUi(() =>
        {
            _currentTrustedCount = count;
            RefreshUi();
        });
        _senderService.PendingClientChanged += (_, pendingClient) => RunOnUi(() =>
        {
            _currentPendingClient = pendingClient;
            RefreshUi();
        });
        _senderService.RemoteInputStateChanged += (_, isActive) => RunOnUi(() =>
        {
            _isRemoteInputLive = isActive;
            _captureManager.IsRemoteInputActive = isActive;
            RefreshUi();
        });

        _captureManager.StatusChanged += (_, status) => RunOnUi(() =>
        {
            _currentCaptureStatus = status;
            RefreshUi();
        });
        _captureManager.CommandReceived += async (_, command) =>
        {
            switch (command)
            {
                case CaptureCommand.ToggleRemoteInput:
                    await ToggleRemoteInputAsync();
                    break;
                case CaptureCommand.EmergencyStop:
                    await StopRemoteInputAsync();
                    break;
            }
        };
        _captureManager.InputCaptured += async (_, inputEvent) =>
        {
            await _senderService.SendCapturedKeyEventAsync(inputEvent);
        };
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 66));

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Anchor = AnchorStyles.None,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 8; index += 1)
        {
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

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
        _messageLabel.Margin = new Padding(0, 0, 0, 12);

        _shortcutLabel.AutoSize = true;
        _shortcutLabel.Font = new Font("Cascadia Mono", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        _shortcutLabel.ForeColor = Color.FromArgb(108, 116, 132);
        _shortcutLabel.MaximumSize = new Size(520, 0);
        _shortcutLabel.TextAlign = ContentAlignment.MiddleCenter;
        _shortcutLabel.Anchor = AnchorStyles.None;
        _shortcutLabel.Margin = new Padding(0, 0, 0, 22);

        _deviceCaptionLabel.AutoSize = true;
        _deviceCaptionLabel.Font = new Font("Segoe UI Variable Text", 10f, FontStyle.Bold, GraphicsUnit.Point);
        _deviceCaptionLabel.ForeColor = Color.FromArgb(108, 116, 132);
        _deviceCaptionLabel.TextAlign = ContentAlignment.MiddleCenter;
        _deviceCaptionLabel.Anchor = AnchorStyles.None;
        _deviceCaptionLabel.Margin = new Padding(0, 0, 0, 8);
        _deviceCaptionLabel.Text = "Receiver PC";

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
        _secondaryButton.Click += async (_, _) => await HandleSecondaryActionAsync();

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
        content.Controls.Add(_shortcutLabel, 0, 4);
        content.Controls.Add(_deviceCaptionLabel, 0, 5);
        content.Controls.Add(_deviceLabel, 0, 6);
        content.Controls.Add(_buttonRow, 0, 7);

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

    private async Task StartSenderAsync()
    {
        if (_isStarting)
        {
            return;
        }

        _isStarting = true;
        RefreshUi();
        try
        {
            await _senderService.StartAsync();
        }
        catch (Exception exception)
        {
            BridgeLog.WriteException("SenderMainForm", exception);
            _currentStatus = exception.Message;
        }
        finally
        {
            _isStarting = false;
            RefreshUi();
        }
    }

    private async Task HandlePrimaryActionAsync()
    {
        switch (_primaryAction)
        {
            case PrimaryAction.Start:
                await StartSenderAsync();
                break;
            case PrimaryAction.Approve:
                await _senderService.ApprovePendingClientAsync();
                break;
            case PrimaryAction.ToggleRemote:
                await ToggleRemoteInputAsync();
                break;
        }
    }

    private async Task HandleSecondaryActionAsync()
    {
        switch (_secondaryAction)
        {
            case SecondaryAction.Deny:
                await _senderService.DenyPendingClientAsync();
                break;
            case SecondaryAction.Retry:
                await StartSenderAsync();
                break;
        }
    }

    private async Task ToggleRemoteInputAsync()
    {
        var isActive = await _senderService.ToggleRemoteInputModeAsync();
        _captureManager.IsRemoteInputActive = isActive;
    }

    private async Task StopRemoteInputAsync()
    {
        await _senderService.DeactivateRemoteInputAsync();
        _captureManager.IsRemoteInputActive = false;
    }

    private void RefreshUi()
    {
        var state = ComputeState();

        _headlineLabel.Text = state.Headline;
        _messageLabel.Text = state.Message;
        _statusBadge.Apply(state.BadgeKind, state.BadgeColor);

        _shortcutLabel.Visible = state.ShortcutText is not null;
        _shortcutLabel.Text = state.ShortcutText ?? string.Empty;

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
        if (_currentPendingClient is not null)
        {
            return new VisualState(
                "Approve receiver PC",
                "A nearby receiver PC wants to accept keyboard input from this PC.",
                null,
                $"Windows {ShortId(_currentPendingClient.ClientId)}",
                BridgeBadgeKind.Question,
                BridgeTheme.WarningOrange,
                true,
                "Approve",
                true,
                PrimaryAction.Approve,
                true,
                "Deny",
                true,
                SecondaryAction.Deny
            );
        }

        if (_isRemoteInputLive)
        {
            return new VisualState(
                "Connected",
                "Keyboard input is exclusive to the receiver PC.",
                "Quick Switch: Ctrl+Alt+F9    Emergency: Ctrl+Alt+F10",
                "Exclusive",
                BridgeBadgeKind.Check,
                BridgeTheme.SuccessGreen,
                true,
                "Stop Sharing",
                true,
                PrimaryAction.ToggleRemote,
                false,
                string.Empty,
                false,
                SecondaryAction.None
            );
        }

        if (_senderService.HasApprovedConnectedReceiver)
        {
            return new VisualState(
                "Ready",
                "Press Share Keyboard or use Quick Switch to hand the keyboard over.",
                "Quick Switch: Ctrl+Alt+F9",
                "Receiver PC connected",
                BridgeBadgeKind.Link,
                BridgeTheme.AccentBlue,
                true,
                "Share Keyboard",
                true,
                PrimaryAction.ToggleRemote,
                false,
                string.Empty,
                false,
                SecondaryAction.None
            );
        }

        if (_isStarting)
        {
            return new VisualState(
                "Starting",
                "Preparing Bluetooth and keyboard capture.",
                null,
                null,
                BridgeBadgeKind.Waves,
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

        if (IsBluetoothErrorState())
        {
            return new VisualState(
                "Bluetooth Unavailable",
                _currentStatus,
                _currentCaptureStatus,
                null,
                BridgeBadgeKind.Warning,
                BridgeTheme.WarningOrange,
                true,
                "Retry",
                true,
                PrimaryAction.Start,
                false,
                string.Empty,
                false,
                SecondaryAction.None
            );
        }

        return new VisualState(
            _currentTrustedCount > 0 ? "Waiting for receiver PC" : "Open the receiver app",
            _currentTrustedCount > 0
                ? "This PC is advertising as okfa. Connect from the receiver app."
                : "Launch okfa on the receiver PC, then connect to this PC.",
            "Quick Switch: Ctrl+Alt+F9    Emergency: Ctrl+Alt+F10",
            _currentConnectionStatus,
            BridgeBadgeKind.Waves,
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

    private bool IsBluetoothErrorState()
    {
        var normalized = _currentStatus.ToLowerInvariant();
        return normalized.Contains("not found")
            || normalized.Contains("cannot publish")
            || normalized.Contains("failed");
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

    private static string ShortId(ulong value) => value.ToString("X16")[^6..];

    private enum PrimaryAction
    {
        None,
        Start,
        Approve,
        ToggleRemote,
    }

    private enum SecondaryAction
    {
        None,
        Retry,
        Deny,
    }

    private readonly record struct VisualState(
        string Headline,
        string Message,
        string? ShortcutText,
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
