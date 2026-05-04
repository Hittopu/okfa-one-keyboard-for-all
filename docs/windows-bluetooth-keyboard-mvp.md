# Windows 双端蓝牙键盘代理 MVP 方案

## 1. 目标定义

目标不是把 Windows 笔记本伪装成一把标准蓝牙 HID 键盘，而是做一个双端软件系统：

- 笔记本端负责捕获键盘事件
- 蓝牙负责传输事件
- 台式机端负责把事件注入为本机键盘输入

这样可以最大化复用 Windows 自带蓝牙能力，同时绕开“PC 直接模拟标准蓝牙键盘”的高风险路径。

---

## 2. MVP 范围

### 要做

- 只支持 `Windows -> Windows`
- 只支持 `键盘`
- 两边都安装软件
- 使用 `蓝牙 RFCOMM` 作为首版传输
- 支持一个全局快捷键切换“本机输入 / 台式机输入”
- 台式机端通过 `SendInput` 注入键盘事件
- 支持断连保护和“强制释放全部按键”

### 不做

- 不做鼠标
- 不做剪贴板同步
- 不做文件传输
- 不做登录界面、UAC 安全桌面、`Ctrl+Alt+Del`
- 不追求“无需安装接收端”

---

## 3. 首版用户体验

### 首次使用

1. 在笔记本和台式机都安装 app
2. 在 Windows 蓝牙设置里手动完成两台机器配对
3. 在台式机 app 中开启“接收模式”
4. 在笔记本 app 中选择目标台式机并保存绑定
5. 之后通过快捷键进入/退出远程输入模式

### 日常使用

1. 用户在笔记本上按 `Ctrl+Alt+F9`
2. 笔记本进入“远程输入模式”
3. 笔记本键盘事件不再送给本机应用，而是发给台式机
4. 台式机收到事件后立即注入到当前前台窗口
5. 用户再次按 `Ctrl+Alt+F9` 退出远程输入模式

### 紧急保护

- `Ctrl+Alt+F10`：本地强制退出远程模式
- 退出时发送 `ReleaseAll`
- 台式机断连/超时后自动释放所有已按下键

---

## 4. 技术路线

### 推荐路线

首版推荐：

- 语言：`C# / .NET 8`
- UI 壳：`WPF`
- 蓝牙传输：`Windows.Devices.Bluetooth.Rfcomm`
- 本机键盘捕获：`SetWindowsHookEx(WH_KEYBOARD_LL)`
- 全局切换热键：`RegisterHotKey`
- 台式机输入注入：`SendInput`

### 为什么这样选

- `C#` 在 Windows 桌面开发、托盘程序、P/Invoke、日志和安装上效率最高
- `WPF` 做托盘常驻、启动项、设置页比 WinUI 3 更省成本
- `Windows.Devices.Bluetooth.Rfcomm` 官方明确可用于 `desktop apps`
- `SendInput` 是官方支持的键盘注入路径

### 备选路线

如果后续发现某些机器上 WinRT RFCOMM 存在权限或兼容性问题，传输层切到 `Win32 Bluetooth + Winsock RFCOMM`，上层协议和输入模块保持不变。

---

## 5. 关键约束

### 配对方式

MVP 不做 app 内发起蓝牙配对。

原因：

- 微软文档对 desktop apps 明确列出 `DeviceInformationPairing.PairAsync` 不受支持

所以首版采用：

- 用户先在系统蓝牙设置中完成一次配对
- app 只负责发现已配对目标、建立 RFCOMM 连接、维持会话

### 权限边界

- 台式机端若要控制“管理员权限”窗口，接收端自己也最好以管理员权限运行
- `SendInput` 受 `UIPI` 限制，不能向更高完整性级别的目标随意注入

### 场景边界

首版不覆盖：

- Windows 登录前
- UAC 安全桌面
- `Ctrl+Alt+Del`
- BIOS

---

## 6. 系统架构

建议做成一个解决方案、两个运行角色、一个共享核心。

### 逻辑模块

#### 1. Sender Agent（笔记本）

职责：

- 注册全局快捷键
- 进入远程模式后安装低层键盘钩子
- 捕获 `keydown/keyup`
- 识别本地保留快捷键
- 将键盘事件编码并发送给台式机
- 显示当前状态

#### 2. Receiver Agent（台式机）

职责：

- 发布或监听 RFCOMM 服务
- 接收消息并校验会话状态
- 将按键事件转换为 `SendInput`
- 跟踪当前“按下中的键”
- 在断连、异常、超时时统一释放

#### 3. Shared Core

职责：

- 协议定义
- 会话状态机
- 日志
- 配置
- 设备绑定信息
- 心跳与重连策略

### 目录建议

```text
src/
  KeyboardForAll.Core/
  KeyboardForAll.Transport.Rfcomm/
  KeyboardForAll.InputCapture/
  KeyboardForAll.InputInjection/
  KeyboardForAll.Sender/
  KeyboardForAll.Receiver/
tests/
  KeyboardForAll.Core.Tests/
  KeyboardForAll.IntegrationTests/
docs/
```

---

## 7. 运行时数据流

### 连接阶段

1. 台式机启动 Receiver
2. Receiver 进入可连接状态并监听 RFCOMM 服务
3. 笔记本启动 Sender
4. Sender 查找绑定过的目标服务并建立连接
5. 双方交换 `Hello`
6. 建立会话并进入 `Connected`

### 输入阶段

1. 用户按下切换热键
2. Sender 进入 `RemoteTyping`
3. Sender 捕获键盘事件
4. 保留热键在本地消费，不转发
5. 其他事件按顺序发送给 Receiver
6. Receiver 注入到本机前台窗口
7. 双方通过心跳维持连接

### 异常阶段

1. 蓝牙断连或心跳超时
2. Receiver 触发 `ReleaseAll`
3. Sender 退出远程输入模式
4. UI 明确提示连接已断开

---

## 8. 输入模型设计

### 发送什么

传输的是“键盘事件”，不是字符。

每个事件至少包含：

- `eventType`：`KeyDown` / `KeyUp`
- `scanCode`
- `virtualKey`
- `flags`：是否扩展键、是否系统键等
- `modifiersSnapshot`
- `sequenceId`
- `timestamp`

### 为什么传事件而不是字符

- 能支持 `Ctrl+C`、`Alt+Tab` 这类组合键
- 能保留台式机自己的输入法和键盘布局
- 行为更接近真实键盘
- 后续扩展鼠标或媒体键时也更自然

### 键盘捕获

笔记本端进入远程模式后：

- 安装 `WH_KEYBOARD_LL`
- 非保留快捷键一律转发
- 同时拦截本地投递，避免本机也打出字符

### 输入注入

台式机端优先按扫描码注入：

- 使用 `SendInput`
- 优先走 `KEYEVENTF_SCANCODE`
- 对扩展键设置 `KEYEVENTF_EXTENDEDKEY`

这样比直接发字符更稳定，也更接近真实键盘输入。

### 状态恢复

Receiver 维护一个 `PressedKeys` 集合：

- 收到 `KeyDown` 时加入
- 收到 `KeyUp` 时移除
- 断连、超时、异常退出时，对集合中的全部键补发 `KeyUp`

这是首版里最关键的保护机制之一。

---

## 9. 快捷键和状态机

### 建议默认快捷键

- `Ctrl+Alt+F9`：切换远程输入模式
- `Ctrl+Alt+F10`：强制本地解锁并释放全部按键

后续可配置，但首版先固定。

### Sender 状态机

```text
Idle
  -> Connecting
  -> ConnectedLocal
  -> RemoteTyping
  -> Reconnecting
  -> Error
```

#### 状态说明

- `Idle`：未连接目标
- `Connecting`：正在连台式机
- `ConnectedLocal`：已连接，但键盘仍控制本机
- `RemoteTyping`：笔记本键盘正被转发给台式机
- `Reconnecting`：连接断开后自动重试
- `Error`：需要用户干预

### Receiver 状态机

```text
Stopped
  -> Listening
  -> SessionOpen
  -> SessionBroken
```

---

## 10. 协议设计

协议目标：

- 足够简单
- 二进制
- 有顺序号
- 支持以后扩展 LAN/Wi-Fi 传输

### 帧格式

建议使用长度前缀二进制帧：

```text
[Length:uint32][Version:uint8][Type:uint8][Sequence:uint32][Payload...]
```

RFCOMM 本身是可靠流，不需要首版再做复杂重传层。

### 消息类型

- `Hello`
- `HelloAck`
- `StartRemoteTyping`
- `StopRemoteTyping`
- `KeyEvent`
- `ReleaseAll`
- `Heartbeat`
- `Error`

### KeyEvent 载荷

```text
EventType: byte
VirtualKey: uint16
ScanCode: uint16
Flags: uint16
Modifiers: uint16
TimestampTicks: int64
```

### 版本策略

- 协议头带 `Version`
- 首版只接受同主版本
- 次版本差异通过可选字段兼容

---

## 11. 蓝牙设计

### 为什么首版选 RFCOMM

- 更适合“持续的小数据流”
- 编程模型接近 socket
- 官方有 RFCOMM sample
- 比 BLE GATT 更像一个稳定的点对点连接

### 服务模型

- Receiver 提供自定义 RFCOMM 服务
- Sender 按服务 ID 发现并连接
- 一次只允许一个 Sender 占用会话

### 连接策略

- 启动后后台保持连接
- 真正切换输入时只切换模式，不临时建连
- 断连后指数退避重连，例如 `1s / 2s / 5s / 10s`

### 设备绑定

保存以下信息：

- 台式机蓝牙设备 ID
- 自定义服务 ID
- 用户给设备起的别名
- 上次成功连接时间

不要只按设备名识别，因为设备名不稳定。

---

## 12. 安全设计

MVP 不做很重的密码学层，但要做最基本的会话保护。

### 首版安全措施

- 只接受已绑定设备
- 只接受已建立连接后的协议消息
- 首次连接时做应用层 `Hello` 校验
- 会话期间校验 `sequenceId`
- Receiver 仅允许单活会话

### 二期可加

- 绑定码确认
- 会话级共享密钥
- HMAC
- 设备白名单导入导出

---

## 13. UI 设计

首版 UI 不要复杂，重点是“明确知道现在键盘控制的是哪台机器”。

### 笔记本端

- 托盘图标
- 一个很小的设置窗口
- 切换状态时屏幕角落浮层提示

#### 图标状态

- 灰色：未连接
- 蓝色：已连接，本机模式
- 绿色：远程输入中
- 红色：错误或断连

### 台式机端

- 托盘图标
- 绑定设备列表
- “允许控制管理员窗口”开关
- 最近连接日志

---

## 14. 错误处理

### 必做保护

- 蓝牙断连后立即 `ReleaseAll`
- Sender 崩溃退出时尽可能发送 `StopRemoteTyping`
- Receiver 无心跳超时后自动释放全部键
- 连续协议错误则断开当前会话

### 常见异常

- 蓝牙未开启
- 设备未配对
- 服务未发布
- Receiver 未运行
- 台式机 Receiver 权限不够，无法控制高权限窗口

---

## 15. MVP 实施顺序

### Phase 0: 可行性 Spike

目标：先证明核心链路成立。

要完成：

- Receiver 能启动 RFCOMM 服务
- Sender 能连上 Receiver
- Sender 发一条测试消息
- Receiver 能收到并回 ACK

通过标准：

- 两台真实 Windows 机器上稳定连通 10 分钟

### Phase 1: 键盘链路

要完成：

- Sender 注册热键
- Sender 捕获键盘事件
- Receiver 用 `SendInput` 注入
- Notepad 中可以稳定输入英文和常见组合键

通过标准：

- `A-Z`、数字、回车、退格、`Ctrl+C/V/X/A` 正常

### Phase 2: 可用性保护

要完成：

- `PressedKeys` 跟踪
- `ReleaseAll`
- 心跳与断线保护
- 状态浮层
- 自动重连

通过标准：

- 人工断电/关蓝牙后不出现粘键

### Phase 3: 安装与常驻

要完成：

- 开机自启
- 托盘菜单
- 绑定目标设备
- 日志落盘

通过标准：

- 用户重启机器后无需重新配置即可使用

### Phase 4: 权限增强

要完成：

- 台式机 Receiver 支持以更高权限运行
- 明确提示当前权限级别

通过标准：

- 可控制管理员权限的目标窗口

---

## 16. 测试矩阵

### 系统

- Windows 10
- Windows 11

### 应用

- Notepad
- VS Code
- Chrome / Edge
- Word / Excel
- Windows Terminal

### 输入场景

- 英文输入
- 中文输入法切换
- 组合键
- 长按自动重复
- 快速连打
- 修饰键按下后断连

### 连接场景

- 正常重连
- 蓝牙关闭再打开
- Receiver 重启
- Sender 睡眠唤醒

---

## 17. 风险清单

### 风险 1：不同机器蓝牙硬件/驱动兼容性差

应对：

- Phase 0 先用真实机器验证
- 传输层抽象，必要时切换到 Win32 RFCOMM

### 风险 2：输入注入与某些特殊应用兼容性一般

应对：

- 首版只承诺普通桌面应用
- 管理员窗口要求 Receiver 提权

### 风险 3：键盘钩子处理不当导致本机误吞键

应对：

- 所有保留热键本地优先
- 加托盘菜单“立即切回本机”
- 加超时自动解除远程模式

### 风险 4：丢失 KeyUp 导致粘键

应对：

- Receiver 维护 `PressedKeys`
- 心跳超时统一释放
- 任何异常路径都走 `ReleaseAll`

---

## 18. 我的最终建议

这件事可以做，而且首版不该从“蓝牙 HID 模拟”切入，而应该从“蓝牙事件转发 + 本地输入注入”切入。

最务实的 MVP 路线是：

1. 双端常驻 agent
2. 手动系统配对
3. RFCOMM 长连接
4. 笔记本捕获扫描码
5. 台式机 `SendInput`
6. 先把“不断键、不粘键、切换明确”做好

只要这六点做扎实，这个产品就已经能解决真实办公问题。

---

## 19. 相关官方文档

- Microsoft: `Windows.Devices.Bluetooth.Rfcomm` 可用于 UWP 和 desktop apps
- Microsoft: `StreamSocketListener` 支持 Bluetooth RFCOMM
- Microsoft: desktop apps 中 `DeviceInformationPairing.PairAsync` 不受支持
- Microsoft: `RegisterHotKey`
- Microsoft: `SetWindowsHookEx`
- Microsoft: `SendInput`
- Microsoft: Win32 Bluetooth 支持 RFCOMM over Winsock

链接：

- https://learn.microsoft.com/en-us/uwp/api/
- https://learn.microsoft.com/en-us/uwp/api/Windows.Networking.Sockets.StreamSocketListener
- https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-supported-api
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexa
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- https://learn.microsoft.com/en-us/windows/win32/bluetooth/using-bluetooth
- https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-programming-with-windows-sockets
