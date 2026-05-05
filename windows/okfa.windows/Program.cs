using System.Windows.Forms;
using KeyboardBridge.Windows.UI;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var runSender = args.Any(static argument =>
    argument.Equals("--sender", StringComparison.OrdinalIgnoreCase)
    || argument.Equals("/sender", StringComparison.OrdinalIgnoreCase)
);

Application.Run(runSender ? new SenderMainForm() : new BridgeMainForm());
