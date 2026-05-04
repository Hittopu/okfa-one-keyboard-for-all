# okfa — one keyboard for all

## 中文

`okfa` 是一个跨设备键盘接管工具。  
它解决的是这样一个高频桌面场景：你正在用一台笔记本工作，但偶尔需要临时控制另一台电脑，不想再把手移到另一把键盘上。

`okfa` 通过一个轻量的双端应用，让一套键盘在两台设备之间快速切换。

### 当前支持

- `Mac -> Windows`

当前公开版本主要聚焦 `Mac -> Windows` 的稳定体验。  
`Windows -> Windows` 版本会在后续更新中补充。

### 核心能力

- 可信 BLE 会话建立
- 远程键盘事件传输
- Windows 本地输入注入
- 远程输入模式切换
- 面向桌面环境的轻量状态 UI

### 技术路线

`okfa` 不是标准蓝牙 HID 键盘配对流程。  
它采用的是自定义 BLE GATT 应用层协议：

1. Mac 端发布自定义 BLE GATT 服务
2. Windows 端扫描并建立可信连接
3. Mac 端进入远程模式后捕获键盘输入
4. Windows 端接收事件并通过本地输入路径注入到前台窗口

这条路线的重点不是“模拟一把标准蓝牙键盘”，而是提供一个更可控、更适合桌面工作流的键盘接管方案。

### 下载

如果你只是想直接使用 `okfa`，优先从 GitHub Releases 下载发布包：

- Releases: [v0.1](https://github.com/Hittopu/okfa-one-keyboard-for-all/releases/tag/v0.1)

当前发布物：

- macOS: `okfa-macos-v0.1.dmg`
- Windows: `okfa-windows-v0.1.exe`

如果你需要自行构建，请查看下面的源码结构和平台说明。

### 使用方式

1. 打开 Mac 端应用
2. 打开 Windows 端应用
3. 在 Windows 端扫描并连接 Mac
4. 如果 Mac 端出现信任或批准提示，完成批准
5. 在 Mac 端进入远程输入模式
6. 开始使用一套键盘控制 Windows

### 仓库结构

- [`mac/`](./mac)：macOS 端源码
- [`windows/`](./windows)：Windows 端源码
- [`docs/`](./docs)：设计与架构文档
- [`assets/branding/`](./assets/branding)：统一品牌资源
- [`archive/`](./archive)：历史原型与旧快照

### 构建

#### macOS

见 [`mac/README.md`](./mac/README.md)

```bash
cd mac
./build_mac_phase1.sh
```

#### Windows

见 [`windows/README.md`](./windows/README.md)

```powershell
cd windows\okfa.windows
dotnet build
dotnet run
```

### 权限

Mac 端需要：

- Bluetooth
- Accessibility
- Input Monitoring

Windows 端在控制提升权限窗口时，可能也需要以管理员权限运行。

### 反馈

如果你在使用过程中遇到问题，欢迎提交 issue：

- Issues: [github.com/Hittopu/okfa-one-keyboard-for-all/issues](https://github.com/Hittopu/okfa-one-keyboard-for-all/issues)

### 品牌

项目品牌为：

- `okfa`
- `one keyboard for all`

推荐的 GitHub 仓库名：

- `okfa-one-keyboard-for-all`

### 许可证

本项目使用 MIT License，见 [`LICENSE`](./LICENSE)。

---

## English

`okfa` is a cross-device keyboard handoff tool.  
It is built for a common desk workflow: you work on one laptop, but occasionally need to control another computer without reaching for a second keyboard.

`okfa` uses a lightweight two-end application model to hand one keyboard between two machines quickly.

### Current support

- `Mac -> Windows`

The current public version is focused on a stable `Mac -> Windows` workflow.  
`Windows -> Windows` support will be added in a later update.

### Core capabilities

- trusted BLE session establishment
- remote keyboard event delivery
- local Windows input injection
- remote input mode switching
- lightweight desktop status UI

### Technical approach

`okfa` is not a standard Bluetooth HID keyboard pairing flow.  
It uses a custom BLE GATT application-layer protocol:

1. the Mac app publishes a custom BLE GATT service
2. the Windows app scans for it and establishes a trusted connection
3. the Mac app captures keyboard input when remote mode is enabled
4. the Windows app receives those events and injects them into the active foreground target

The goal is not to imitate a generic Bluetooth keyboard.  
The goal is to provide a more controllable keyboard handoff workflow for real desktop use.

### Download

If you only want to use `okfa`, prefer downloading the release artifacts from GitHub Releases:

- Releases: [v0.1](https://github.com/Hittopu/okfa-one-keyboard-for-all/releases/tag/v0.1)

Current release assets:

- macOS: `okfa-macos-v0.1.dmg`
- Windows: `okfa-windows-v0.1.exe`

If you want to build from source, see the repository layout and platform-specific READMEs below.

### Usage

1. Open the Mac app
2. Open the Windows app
3. Scan for and connect to the Mac from Windows
4. If the Mac asks for trust or approval, approve it
5. Enable remote input mode on the Mac
6. Start using one keyboard to control Windows

### Repository layout

- [`mac/`](./mac): macOS app source
- [`windows/`](./windows): Windows app source
- [`docs/`](./docs): design and architecture notes
- [`assets/branding/`](./assets/branding): shared brand assets
- [`archive/`](./archive): historical prototypes and snapshots

### Build

#### macOS

See [`mac/README.md`](./mac/README.md)

```bash
cd mac
./build_mac_phase1.sh
```

#### Windows

See [`windows/README.md`](./windows/README.md)

```powershell
cd windows\okfa.windows
dotnet build
dotnet run
```

### Permissions

The Mac app requires:

- Bluetooth
- Accessibility
- Input Monitoring

The Windows app may need to run elevated if you want it to control elevated target windows reliably.

### Feedback

If you run into any issues while using `okfa`, please open an issue:

- Issues: [github.com/Hittopu/okfa-one-keyboard-for-all/issues](https://github.com/Hittopu/okfa-one-keyboard-for-all/issues)

### Branding

The project branding is:

- `okfa`
- `one keyboard for all`

Recommended GitHub repository name:

- `okfa-one-keyboard-for-all`

### License

This project is released under the MIT License. See [`LICENSE`](./LICENSE).
