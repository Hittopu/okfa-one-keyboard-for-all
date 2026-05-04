using System.Windows.Forms;
using KeyboardBridge.Windows.UI;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new BridgeMainForm());
