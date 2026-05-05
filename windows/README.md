# okfa Windows

## 中文

这里是 `okfa` 的 Windows 工程。同一个可执行文件支持两个角色：

- `Receiver`：默认启动，运行在被控制的 Windows 电脑上，负责扫描、连接和 `SendInput` 注入。
- `Sender`：通过 `--sender` 启动，运行在键盘所在的 Windows 电脑上，负责键盘捕获和 BLE GATT 服务发布。

### 安装包

正式用户建议从 GitHub Releases 下载：

```text
okfa-win2win-v0.2.0-setup.exe
```

安装包是 self-contained 发布，用户不需要单独安装 .NET。安装后开始菜单会出现：

- `okfa Receiver`
- `okfa Sender`

### 使用方式

1. 被控制电脑打开 `okfa Receiver`。
2. 键盘所在电脑打开 `okfa Sender`。
3. `okfa Receiver` 扫描到 sender PC 后点击连接。
4. `okfa Sender` 出现批准提示后点击 `Approve`。
5. `okfa Sender` 点击 `Share Keyboard` 或按 `Ctrl + Alt + F9` 进入远程输入。
6. 再按 `Ctrl + Alt + F9` 返回本机输入。
7. 异常时按 `Ctrl + Alt + F10` 应急退出并释放按键。

### 从源码运行

接收端：

```powershell
cd windows\okfa.windows
dotnet run
```

发送端：

```powershell
cd windows\okfa.windows
dotnet run -- --sender
```

### 构建

应用：

```powershell
cd windows\okfa.windows
dotnet build
```

安装包：

```powershell
.\windows\installer\build-installer.ps1
```

### 关键文件

```text
okfa.windows/
  Program.cs                         role entry, Receiver by default, --sender for Sender
  UI/BridgeMainForm.cs               Receiver UI
  UI/SenderMainForm.cs               Sender UI
  UI/BridgeVisuals.cs                shared Windows visual controls and logo rendering
  Bluetooth/BridgeScanner.cs         Receiver BLE scanner
  Bluetooth/BridgeSession.cs         Receiver GATT session
  Bluetooth/WindowsGattSenderService.cs Sender GATT service
  Protocol/BridgeProtocol.cs         UUIDs, message types, codecs
  Input/InputInjector.cs             Receiver SendInput injector
  Input/WindowsKeyboardCaptureManager.cs Sender low-level keyboard hook
  Input/KeyboardUsageMap.cs          HID usage to Windows VK mapping
  Trust/TrustedSenderStore.cs        Receiver identity state
  Trust/TrustedClientStore.cs        Sender trust store for receiver PCs
installer/
  build-installer.ps1                self-contained publish + Inno Setup build
  okfa.iss                           Inno Setup script
```

### 日志

```powershell
Get-Content "$env:LOCALAPPDATA\okfa\bridge.log" -Tail 120
```

如果接收端停在 `Connecting`，优先看日志中卡在打开设备、发现 service、发现 characteristic、订阅 notify，还是发送 `ClientHello`。

### 权限

接收端使用 `SendInput` 注入键盘输入。普通权限可以控制普通桌面窗口；如果目标窗口以管理员权限运行，接收端也需要以管理员权限运行。

发送端使用低层键盘 hook 捕获输入。远程输入模式下会拦截本机键盘事件，并把规范化后的 HID usage 发送给接收端。

## English

This is the Windows project for `okfa`. The same executable supports two roles:

- `Receiver`: default mode. Runs on the Windows PC being controlled, scans for peers, connects, and injects input with `SendInput`.
- `Sender`: launched with `--sender`. Runs on the Windows PC with the keyboard, captures keyboard events, and publishes the BLE GATT service.

### Installer

End users should download the packaged build from GitHub Releases:

```text
okfa-win2win-v0.2.0-setup.exe
```

The installer is self-contained, so users do not need to install .NET separately. It creates two Start Menu entries:

- `okfa Receiver`
- `okfa Sender`

### Usage

1. Open `okfa Receiver` on the PC being controlled.
2. Open `okfa Sender` on the PC with the keyboard.
3. Connect to the discovered sender PC from `okfa Receiver`.
4. Approve the receiver PC in `okfa Sender`.
5. Click `Share Keyboard` or press `Ctrl + Alt + F9` to enter remote input mode.
6. Press `Ctrl + Alt + F9` again to return input to the sender PC.
7. Press `Ctrl + Alt + F10` for emergency stop and key release.

### Run From Source

Receiver:

```powershell
cd windows\okfa.windows
dotnet run
```

Sender:

```powershell
cd windows\okfa.windows
dotnet run -- --sender
```

### Build

App:

```powershell
cd windows\okfa.windows
dotnet build
```

Installer:

```powershell
.\windows\installer\build-installer.ps1
```

### Key Files

```text
okfa.windows/
  Program.cs                         role entry, Receiver by default, --sender for Sender
  UI/BridgeMainForm.cs               Receiver UI
  UI/SenderMainForm.cs               Sender UI
  UI/BridgeVisuals.cs                shared Windows visual controls and logo rendering
  Bluetooth/BridgeScanner.cs         Receiver BLE scanner
  Bluetooth/BridgeSession.cs         Receiver GATT session
  Bluetooth/WindowsGattSenderService.cs Sender GATT service
  Protocol/BridgeProtocol.cs         UUIDs, message types, codecs
  Input/InputInjector.cs             Receiver SendInput injector
  Input/WindowsKeyboardCaptureManager.cs Sender low-level keyboard hook
  Input/KeyboardUsageMap.cs          HID usage to Windows VK mapping
  Trust/TrustedSenderStore.cs        Receiver identity state
  Trust/TrustedClientStore.cs        Sender trust store for receiver PCs
installer/
  build-installer.ps1                self-contained publish + Inno Setup build
  okfa.iss                           Inno Setup script
```

### Logs

```powershell
Get-Content "$env:LOCALAPPDATA\okfa\bridge.log" -Tail 120
```

If the receiver stays on `Connecting`, check whether the log stops at device open, service discovery, characteristic discovery, notify subscription, or `ClientHello`.

### Permissions

The receiver uses `SendInput` to inject keyboard input. Standard privileges can control standard desktop windows. To control elevated windows, run the receiver with administrator privileges.

The sender uses a low-level keyboard hook. In remote input mode, it suppresses local keyboard events and streams normalized HID usages to the receiver.
