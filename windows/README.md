# okfa Windows

## 中文

这是 `okfa` 的 Windows 端工程。

当前公开版本主要服务于 `Mac -> Windows` 的连接路径。  
`Windows -> Windows` 版本会在后续更新中补充。

当前职责：

- 扫描 Mac 端暴露的自定义 BLE GATT 服务
- 连接可信 Mac 会话
- 订阅控制、输入事件和快照特征值
- 接收远程键盘事件
- 使用 `SendInput` 在本机注入键盘输入
- 提供轻量桌面控制面板

### 项目结构

- 应用源码：[`okfa.windows/`](./okfa.windows)
- UI 入口：[`Program.cs`](./okfa.windows/Program.cs)

### 构建

```powershell
cd windows\okfa.windows
dotnet build
```

### 运行

```powershell
cd windows\okfa.windows
dotnet run
```

当前输出文件名：

- `okfa.exe`

### 使用方式

1. 启动 Windows 端应用
2. 保持 Mac 端应用运行
3. 让 Windows 端扫描并发现 Mac
4. 在 Windows 端建立连接
5. 如果 Mac 端出现批准提示，完成批准
6. 在 Mac 端进入远程输入模式
7. 开始用这套键盘控制 Windows

### 说明

这不是 Windows 设置里标准的蓝牙键盘配对流程，而是应用层自定义 BLE 连接流程。

### 反馈

如果你在使用过程中遇到问题，欢迎提交 issue。

## English

This is the Windows side of `okfa`.

The current public version is mainly focused on the `Mac -> Windows` workflow.  
`Windows -> Windows` support will be added in a later update.

Current responsibilities:

- scan for the custom BLE GATT service exposed by the Mac app
- connect to the trusted Mac session
- subscribe to control, input event, and snapshot characteristics
- receive remote keyboard events
- inject keyboard input locally with `SendInput`
- present a lightweight desktop control panel

### Project layout

- app source: [`okfa.windows/`](./okfa.windows)
- UI entry point: [`Program.cs`](./okfa.windows/Program.cs)

### Build

```powershell
cd windows\okfa.windows
dotnet build
```

### Run

```powershell
cd windows\okfa.windows
dotnet run
```

Current output file name:

- `okfa.exe`

### Usage

1. Launch the Windows app
2. Keep the Mac app running
3. Let Windows scan for and discover the Mac
4. Establish the connection from the Windows side
5. Approve the Windows client if the Mac asks for trust
6. Enable remote input mode on the Mac
7. Start using the same keyboard to control Windows

### Notes

This is not the standard Bluetooth keyboard pairing flow from the Windows Settings app. It uses a custom BLE application-layer connection flow.

### Feedback

If you run into any issues while using `okfa`, please open an issue.


### ??

?????????? `okfa`???? GitHub `Releases` ???? Windows ????
?????? `okfa-windows-v0.1.exe`?

### Download

If you only want to use `okfa`, prefer downloading the Windows release package from GitHub `Releases`.
The recommended asset is `okfa-windows-v0.1.exe`.
