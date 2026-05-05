using KeyboardBridge.Windows.Bluetooth;

namespace KeyboardBridge.Windows.UI;

public sealed class AllDevicesForm : Form
{
    private readonly ulong? _connectedAddress;
    private readonly ListView _deviceList = new();
    private readonly BridgeButton _connectButton = new();

    public AllDevicesForm(IReadOnlyList<BridgeAdvertisement> devices, ulong? selectedAddress, ulong? connectedAddress)
    {
        _connectedAddress = connectedAddress;

        Text = "okfa Sender Picker";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(640, 500);
        ClientSize = new Size(680, 520);
        BackColor = Color.FromArgb(243, 246, 251);
        Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout(devices, selectedAddress);
    }

    public BridgeAdvertisement? SelectedAdvertisement { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BridgeTheme.TryApplyWindowBackdrop(this);
    }

    private void BuildLayout(IReadOnlyList<BridgeAdvertisement> devices, ulong? selectedAddress)
    {
        Padding = new Padding(24);

        var surfacePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(249, 250, 252),
        };
        surfacePanel.Paint += (_, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(226, 231, 238), 1f);
            var rect = new Rectangle(0, 0, surfacePanel.Width - 1, surfacePanel.Height - 1);
            e.Graphics.DrawRectangle(borderPen, rect);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(28),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Variable Display", 22f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(24, 28, 34),
            Margin = new Padding(0, 0, 0, 8),
            Text = "Choose a Sender PC",
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Variable Text", 12.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(100, 108, 124),
            Margin = new Padding(0, 0, 0, 20),
            Text = "Select the sender PC you want this receiver to listen to.",
        };

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 247, 251),
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 22),
        };
        listHost.Paint += (_, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(214, 220, 229), 1f);
            var rect = new Rectangle(0, 0, listHost.Width - 1, listHost.Height - 1);
            e.Graphics.DrawRectangle(borderPen, rect);
        };

        _deviceList.Dock = DockStyle.Fill;
        _deviceList.View = View.Details;
        _deviceList.FullRowSelect = true;
        _deviceList.MultiSelect = false;
        _deviceList.HideSelection = false;
        _deviceList.BorderStyle = BorderStyle.None;
        _deviceList.BackColor = Color.White;
        _deviceList.ForeColor = Color.FromArgb(24, 28, 34);
        _deviceList.Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        _deviceList.Columns.Add("Sender PC", 250);
        _deviceList.Columns.Add("Signal", 100, HorizontalAlignment.Right);
        _deviceList.Columns.Add("Match", 120);
        _deviceList.Columns.Add("State", 120);
        _deviceList.SelectedIndexChanged += (_, _) => UpdateButtons();
        _deviceList.DoubleClick += (_, _) => ConfirmSelection();

        foreach (var device in devices)
        {
            var state = _connectedAddress == device.BluetoothAddress ? "Connected" : "Available";
            var item = new ListViewItem(device.LocalName)
            {
                Tag = device,
            };
            item.SubItems.Add(device.Rssi.ToString());
            item.SubItems.Add(device.HasTargetService ? "Verified" : "Name match");
            item.SubItems.Add(state);

            if (_connectedAddress == device.BluetoothAddress)
            {
                item.ForeColor = BridgeTheme.SuccessGreen;
            }

            _deviceList.Items.Add(item);
        }

        foreach (ColumnHeader column in _deviceList.Columns)
        {
            column.Width = -2;
        }

        if (_deviceList.Items.Count > 0)
        {
            var initialIndex = 0;
            if (selectedAddress is not null)
            {
                for (var index = 0; index < _deviceList.Items.Count; index += 1)
                {
                    if (_deviceList.Items[index].Tag is BridgeAdvertisement advertisement
                        && advertisement.BluetoothAddress == selectedAddress.Value)
                    {
                        initialIndex = index;
                        break;
                    }
                }
            }

            _deviceList.Items[initialIndex].Selected = true;
        }

        listHost.Controls.Add(_deviceList);

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        ConfigureButton(_connectButton, BridgeButtonKind.Primary, 170);
        _connectButton.Text = "Use This PC";
        _connectButton.Margin = new Padding(12, 0, 0, 0);
        _connectButton.Click += (_, _) => ConfirmSelection();

        var closeButton = new BridgeButton
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
        };
        ConfigureButton(closeButton, BridgeButtonKind.Secondary, 130);
        closeButton.Margin = Padding.Empty;
        closeButton.Click += (_, _) => Close();

        buttonRow.Controls.Add(_connectButton);
        buttonRow.Controls.Add(closeButton);

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(subtitleLabel, 0, 1);
        layout.Controls.Add(listHost, 0, 2);
        layout.Controls.Add(buttonRow, 0, 3);

        surfacePanel.Controls.Add(layout);
        Controls.Add(surfacePanel);
        AcceptButton = _connectButton;
        CancelButton = closeButton;

        UpdateButtons();
    }

    private static void ConfigureButton(BridgeButton button, BridgeButtonKind kind, int width)
    {
        button.AutoSize = false;
        button.Width = width;
        button.Height = 42;
        button.BridgeKind = kind;
        button.Font = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        button.Cursor = Cursors.Hand;
    }

    private void ConfirmSelection()
    {
        if (_deviceList.SelectedItems.Count == 0 || _deviceList.SelectedItems[0].Tag is not BridgeAdvertisement advertisement)
        {
            return;
        }

        SelectedAdvertisement = advertisement;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateButtons()
    {
        _connectButton.Enabled = _deviceList.SelectedItems.Count > 0
            && _deviceList.SelectedItems[0].Tag is BridgeAdvertisement;
    }
}
