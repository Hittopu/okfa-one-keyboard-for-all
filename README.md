# okfa - one keyboard for all

## 中文

`okfa` 是一个跨设备键盘接管工具，适合这样的桌面场景：你使用一台笔记本作为主工作设备，但偶尔需要快速操作另一台电脑，又不想频繁切换外接键盘。

当前仓库包含两个主要部分：

- `macOS` 发送端：负责捕获键盘输入，并发布自定义 BLE GATT 服务
- `Windows` 接收端：负责发现 Mac、建立可信会话，并在本机注入键盘输入

### 仓库结构

- [`mac/`](./mac)：macOS 端源码
- [`windows/`](./windows)：Windows 端源码
- [`docs/`](./docs)：设计与架构文档
- [`assets/branding/`](./assets/branding)：统一品牌资源
- [`archive/`](./archive)：历史原型与旧快照

### 工作方式

1. Mac 端发布自定义 BLE GATT 服务，并管理可信 Windows 客户端。
2. 进入远程模式后，Mac 端捕获键盘输入并发送归一化后的按键事件。
3. Windows 端扫描已信任的 Mac，订阅相关特征值，接收控制与输入事件。
4. Windows 端把事件注入到当前前台目标窗口中。

这不是标准蓝牙 HID 键盘配对流程，而是自定义的 BLE 应用层协议。

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

### 使用方式

1. 打开 Mac 端应用
2. 打开 Windows 端应用
3. 在 Windows 端扫描并连接 Mac
4. 如果 Mac 端出现信任或批准提示，完成批准
5. 在 Mac 端进入远程输入模式
6. 开始使用一套键盘控制 Windows

### 权限

Mac 端需要：

- Bluetooth
- Accessibility
- Input Monitoring

Windows 端在控制提升权限窗口时，可能也需要以管理员权限运行。

### 反馈

如果你在使用过程中遇到问题，欢迎提交 issue。

### 品牌

项目品牌为：

- `okfa`
- `one keyboard for all`

推荐的 GitHub 仓库名：

- `okfa-one-keyboard-for-all`

### 许可证

本项目使用 MIT License，见 [`LICENSE`](./LICENSE)。

## English

`okfa` is a cross-device keyboard handoff tool for desk setups where one laptop needs to temporarily control another computer without physically switching keyboards.

The repository contains two main parts:

- the macOS sender app, which captures keyboard input and publishes a custom BLE GATT service
- the Windows receiver app, which discovers the Mac, establishes a trusted session, and injects keyboard input locally

### Repository layout

- [`mac/`](./mac): macOS app source
- [`windows/`](./windows): Windows app source
- [`docs/`](./docs): design and architecture notes
- [`assets/branding/`](./assets/branding): shared brand assets
- [`archive/`](./archive): historical prototypes and snapshots

### How it works

1. The Mac app advertises a custom BLE GATT service and manages trust for known Windows clients.
2. When remote mode is enabled, the Mac captures keyboard input and streams normalized key events.
3. The Windows app scans for the trusted Mac, subscribes to the relevant characteristics, and receives control and input events.
4. The Windows app injects those events into the active foreground target using the native Windows input path.

This is not a standard Bluetooth HID keyboard pairing flow. It is a custom BLE application-layer protocol.

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

### Usage

1. Open the Mac app
2. Open the Windows app
3. Scan for and connect to the Mac from Windows
4. If the Mac asks for trust or approval, approve it
5. Enable remote input mode on the Mac
6. Start using one keyboard to control Windows

### Permissions

The Mac app requires:

- Bluetooth
- Accessibility
- Input Monitoring

The Windows app may need to run elevated if you want it to control elevated target windows reliably.

### Feedback

If you run into any issues while using `okfa`, please open an issue.

### Branding

The project branding is:

- `okfa`
- `one keyboard for all`

Recommended GitHub repository name:

- `okfa-one-keyboard-for-all`

### License

This project is released under the MIT License. See [`LICENSE`](./LICENSE).
