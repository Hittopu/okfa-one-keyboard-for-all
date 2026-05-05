# okfa - one keyboard for all

## 中文

`okfa` 是一个跨设备键盘接管工具。它让一台电脑的键盘可以在需要时切换到另一台电脑上使用，适合工位上同时使用笔记本和台式机、但不想频繁更换键盘的场景。

当前公开版本提供两条链路：

- `Windows -> Windows`：Windows 发送端捕获本机键盘，通过自定义 BLE GATT 服务发送输入事件，Windows 接收端注入为真实键盘输入。
- `Mac -> Windows`：macOS 端发布同一套 okfa BLE GATT 协议，Windows 端负责连接、信任确认和输入注入。

### 下载

请从 GitHub Releases 下载正式发布包：

- Releases: https://github.com/Hittopu/okfa-one-keyboard-for-all/releases
- Windows -> Windows v0.2.0: `okfa-win2win-v0.2.0-setup.exe`

Windows installer 是自包含安装包，普通用户不需要单独安装 .NET。安装后开始菜单会出现两个入口：

- `okfa Receiver`：安装在被控制的 Windows 电脑上。
- `okfa Sender`：安装在键盘所在的 Windows 电脑上。

GitHub release 会自动生成对应 tag 的 source code 压缩包，源码与 release 资产保持同一版本。

### Windows -> Windows 使用方式

1. 在被控制的电脑上打开 `okfa Receiver`。
2. 在键盘所在的电脑上打开 `okfa Sender`。
3. 在 `okfa Receiver` 中选择发现到的 sender PC 并连接。
4. 在 `okfa Sender` 中批准 receiver PC。
5. 点击 `Share Keyboard`，或按 `Ctrl + Alt + F9`，把键盘切换到接收端。
6. 再按 `Ctrl + Alt + F9` 回到本机输入。
7. 如需应急退出，按 `Ctrl + Alt + F10` 释放远程输入。

### Mac -> Windows 使用方式

1. 在 macOS 上打开 `okfa.app`。
2. 授予 Bluetooth、Accessibility、Input Monitoring 权限。
3. 在 Windows 上打开 `okfa Receiver`。
4. 在 Windows 端连接 Mac，并在 Mac 端批准。
5. 使用 macOS 端快捷键切换远程输入。

### 技术架构

okfa 使用应用层 BLE GATT 协议传输键盘事件，而不是依赖系统蓝牙设置中的标准键盘配对流程。核心链路包括：

- BLE discovery：接收端扫描 okfa service UUID 和 okfa 广播名。
- Trust handshake：接收端发送 `ClientHello`，发送端返回 `TrustStatus`。
- Remote mode：发送端通过 `ModeChange` 控制远程输入状态。
- Input stream：按键事件通过 `InputEvent` notify 发送。
- Recovery：断开或异常时通过 `ReleaseAll` 和 snapshot 释放粘滞按键。

### 构建

Windows app：

```powershell
cd windows\okfa.windows
dotnet build
```

Windows installer：

```powershell
.\windows\installer\build-installer.ps1
```

macOS app：

```bash
cd mac
./build_mac_phase1.sh
```

### 仓库结构

```text
assets/branding/                 shared logo and brand assets
docs/                            protocol and design notes
mac/                             macOS sender app
windows/okfa.windows/            Windows receiver and sender app
windows/installer/               Windows installer build scripts
```

### 反馈

如果你在使用过程中遇到问题，欢迎在 GitHub Issues 里提交复现步骤、系统版本、蓝牙适配器型号和本地日志。

Windows 日志位置：

```powershell
Get-Content "$env:LOCALAPPDATA\okfa\bridge.log" -Tail 120
```

macOS 日志位置：

```text
~/Library/Logs/okfa.log
```

## English

`okfa` is a cross-device keyboard handoff tool. It lets one computer temporarily drive another computer with the same physical keyboard, which is useful for desk setups with both a laptop and a desktop PC.

The current public codebase supports two paths:

- `Windows -> Windows`: the sender PC captures local keyboard events, publishes a custom BLE GATT service, and the receiver PC injects those events as real keyboard input.
- `Mac -> Windows`: the macOS app publishes the same okfa BLE GATT protocol, while the Windows app handles discovery, trust approval, and input injection.

### Download

Download packaged builds from GitHub Releases:

- Releases: https://github.com/Hittopu/okfa-one-keyboard-for-all/releases
- Windows -> Windows v0.2.0: `okfa-win2win-v0.2.0-setup.exe`

The Windows installer is self-contained, so end users do not need to install .NET separately. After installation, the Start Menu contains:

- `okfa Receiver`: run this on the Windows PC being controlled.
- `okfa Sender`: run this on the Windows PC with the keyboard.

GitHub automatically generates source code archives for each release tag, so the release assets and source snapshot point to the same version.

### Windows -> Windows Usage

1. Open `okfa Receiver` on the PC you want to control.
2. Open `okfa Sender` on the PC with the keyboard.
3. Select the discovered sender PC in `okfa Receiver` and connect.
4. Approve the receiver PC in `okfa Sender`.
5. Click `Share Keyboard`, or press `Ctrl + Alt + F9`, to hand the keyboard over.
6. Press `Ctrl + Alt + F9` again to return keyboard input to the sender PC.
7. Press `Ctrl + Alt + F10` for emergency stop and key release.

### Mac -> Windows Usage

1. Open `okfa.app` on macOS.
2. Grant Bluetooth, Accessibility, and Input Monitoring permissions.
3. Open `okfa Receiver` on Windows.
4. Connect to the Mac from Windows and approve the Windows client on macOS.
5. Use the macOS shortcut to switch remote input mode.

### Architecture

okfa transports keyboard events with an application-layer BLE GATT protocol. The main flow is:

- BLE discovery: receiver scans for the okfa service UUID and local name.
- Trust handshake: receiver sends `ClientHello`, sender replies with `TrustStatus`.
- Remote mode: sender controls remote input with `ModeChange`.
- Input stream: keyboard events are delivered through `InputEvent` notifications.
- Recovery: disconnects and emergency stops use `ReleaseAll` and snapshots to release pressed keys.

### Build

Windows app:

```powershell
cd windows\okfa.windows
dotnet build
```

Windows installer:

```powershell
.\windows\installer\build-installer.ps1
```

macOS app:

```bash
cd mac
./build_mac_phase1.sh
```

### Repository Layout

```text
assets/branding/                 shared logo and brand assets
docs/                            protocol and design notes
mac/                             macOS sender app
windows/okfa.windows/            Windows receiver and sender app
windows/installer/               Windows installer build scripts
```

### Feedback

If you run into issues, please open a GitHub issue with reproduction steps, OS version, Bluetooth adapter model, and logs.

Windows log:

```powershell
Get-Content "$env:LOCALAPPDATA\okfa\bridge.log" -Tail 120
```

macOS log:

```text
~/Library/Logs/okfa.log
```

## License

MIT License. See `LICENSE`.
