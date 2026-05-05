# okfa Mac

## 中文

这是 `okfa` 的 macOS 端工程。

当前职责：

- 发布自定义 BLE GATT 服务
- 管理可信 Windows 客户端
- 捕获全局键盘输入
- 在远程模式下把按键事件发送给 Windows
- 在本机输入与远程输入之间切换

### 身份信息

- App 名：`okfa`
- Bundle ID：`com.keyboardforall.okfa.mac`
- BLE 广播名：`okfa`

身份配置文件：

- [`app_identity.sh`](./app_identity.sh)

### 构建

```bash
cd mac
./build_mac_phase1.sh
```

默认产物：

- `mac/build/okfa.app`

复制到 `/Applications`：

```bash
ditto "mac/build/okfa.app" "/Applications/okfa.app"
```

启动：

```bash
open "/Applications/okfa.app"
```

### 使用方式

1. 启动 `okfa.app`
2. 确认系统已经授予 Bluetooth、Accessibility 和 Input Monitoring 权限
3. 保持应用运行，让 Windows 端发现它
4. 在 Windows 端完成连接
5. 如出现批准提示，允许对应 Windows 客户端
6. 进入远程输入模式，把键盘切换给 Windows

### 权限

为了让独占输入模式正常工作，`okfa.app` 需要：

- Bluetooth
- Accessibility
- Input Monitoring

如果修改了 App 名称或 Bundle ID，macOS 通常会要求重新授权。

### 快捷键

当前默认：

- 远程切换：`Command + Shift + Return`
- 紧急退出：`Command + Shift + Escape`

快捷键配置会持久化保存。

### 本地数据

- 日志：`~/Library/Logs/okfa.log`
- 信任客户端：`~/Library/Application Support/okfa/trusted-clients.json`

### 反馈

如果你在使用过程中遇到问题，欢迎提交 issue。

## English

This is the macOS side of `okfa`.

Current responsibilities:

- advertise the custom BLE GATT service
- manage trusted Windows clients
- capture global keyboard input
- stream keyboard events to Windows while remote mode is active
- switch between local typing and remote typing

### Identity

- App name: `okfa`
- Bundle ID: `com.keyboardforall.okfa.mac`
- BLE local name: `okfa`

Identity configuration file:

- [`app_identity.sh`](./app_identity.sh)

### Build

```bash
cd mac
./build_mac_phase1.sh
```

Default output:

- `mac/build/okfa.app`

Copy into `/Applications`:

```bash
ditto "mac/build/okfa.app" "/Applications/okfa.app"
```

Launch:

```bash
open "/Applications/okfa.app"
```

### Usage

1. Launch `okfa.app`
2. Make sure Bluetooth, Accessibility, and Input Monitoring permissions are granted
3. Keep the app running so the Windows side can discover it
4. Complete the connection from Windows
5. Approve the Windows client if the Mac asks for trust
6. Enable remote input mode and hand the keyboard over to Windows

### Permissions

For exclusive input capture to work correctly, `okfa.app` needs:

- Bluetooth
- Accessibility
- Input Monitoring

If you change the app name or bundle identifier, macOS will usually ask for those permissions again.

### Shortcuts

Current defaults:

- remote toggle: `Command + Shift + Return`
- emergency stop: `Command + Shift + Escape`

Shortcut configuration is persisted locally.

### Local data

- log file: `~/Library/Logs/okfa.log`
- trusted clients: `~/Library/Application Support/okfa/trusted-clients.json`

### Feedback

If you run into any issues while using `okfa`, please open an issue.
