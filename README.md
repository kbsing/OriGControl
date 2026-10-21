# OriG Control

Windows 托盘小工具，用来控制 **原道 OriG in** 蓝牙耳机。

> **非官方项目**，与原道（NiceHCK）及其关联公司无任何关联，
> 未被授权或认可。仅用于互操作性研究与个人使用。

---

## 下载

从 [最新 Release](https://github.com/kbsing/OriGControl/releases/latest) 下载，两个版本功能完全一致，只有运行时打包方式不同：

| 版本 | 文件 | 体积 | 需要 .NET 10 Desktop Runtime | 适用场景 |
|---|---|---:|---|---|
| 便携版（推荐） | `OriGControl-v0.1.0-win-x64.exe` | 约 73MB | 否 | 新机器、离线机器、不想装运行时 |
| Lite 版 | `OriGControl-v0.1.0-lite-win-x64.exe` | 约 25MB | 是 | 已安装 .NET 10 桌面运行时，想减少磁盘占用 |

便携版首次启动会解压运行时，启动略慢；Lite 版依赖系统运行时，未安装时会提示缺少 .NET。
两个 exe 均未签名，SmartScreen 可能提示。每个文件旁边都有对应的 `.sha256` 校验值。

---

## 功能

| 功能 | 状态 |
|---|---|
| 连接耳机（经典蓝牙 SPP / RFCOMM） | 可用 |
| 读取电量、固件版本、降噪、均衡器与 5 个功能开关 | 可用 |
| 切换降噪档位（关闭 / 通透 / 普通 / 深度 / 实验性） | 可用 |
| 切换均衡器（7 档预设） | 可用 |
| 提示音音量（0–16） | 可用 |
| 左右耳触控自定义（单击 / 双击 / 三击 / 长按，可启用/禁用） | 可用 |
| 当前蓝牙编码显示（只读） | 可用 |
| 触控降噪循环显示（只读） | 可用 |
| 查找耳机（响铃 / 停止） | 可用 |
| 监听耳机主动上报（触控操作实时同步） | 可用 |
| 托盘常驻，左键弹出控制面板 | 可用 |
| 断线自动重连（含休眠唤醒） | 可用 |
| 闲置自动断开（时长可配，操作时自动重连） | 可用 |
| 全局热键 | 可用（冲突时自动降级） |
| 开机自启、低电量提醒 | 可用 |
| 电量显示样式（圆环 / 电池） | 可用 |
| 自定义背景图片（磨砂底） | 可用 |
| 浅色 / 深色主题 | 可用 |
| 切档 HUD 浮层 | 可用 |

界面为深色卡片 + 荧光绿强调色（浅色主题为同色系橄榄绿），设置页分为
**通用 / 手势 / 音频 / 关于** 四个页签。图标使用系统自带的 **Segoe Fluent Icons**
（Win10 回退到 Segoe MDL2 Assets）。

> 「实验性降噪」使用更激进的降噪算法，可能出现声音异常；「查找耳机」会让耳机
> 发出较大提示音，请勿在佩戴时使用。
>
> 抗风噪是独立开关，不属于降噪档位循环；编码与触控降噪循环均为耳机上报值的只读
> 显示，不代表 Windows 实际使用的 A2DP 编码。

---

## 环境要求

- Windows 10 2004（Build 19041）或更高版本 / Windows 11
- 自包含发布包无需安装 .NET；lite 包需要 .NET 10 Desktop Runtime
- 从源码构建需要 .NET 10 SDK
- 耳机已与电脑完成**经典蓝牙配对**
- 仅支持 Windows，不支持 Linux / macOS

---

## 构建与运行

```powershell
dotnet build -c Release
dotnet test tests\OriG.Core.Tests\OriG.Core.Tests.csproj
dotnet test tests\OriG.Protocol.Tests\OriG.Protocol.Tests.csproj
.\src\OriG.App\bin\Release\net10.0-windows10.0.19041.0\OriGControl.exe
```

发布为自包含单文件（目标机器无需安装 .NET）：

```powershell
dotnet publish .\src\OriG.App\OriG.App.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

产物为 `publish/OriGControl.exe`（约 73MB），可直接分发的压缩包与校验值为
`publish/OriGControl-win-x64.zip`（约 67MB）、`publish/OriGControl-win-x64.zip.sha256`。
发布默认只保留 `zh-Hans` 卫星资源，避免把十几份用不到的本地化资源打进包里。

体积敏感时可发布框架依赖 lite 版（约 25MB，zip 约 6MB，需要 .NET 10 Desktop Runtime）：

```powershell
dotnet publish .\src\OriG.App\OriG.App.csproj -c Release -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o publish\lite
```

启动后**没有主窗口**，只驻留系统托盘：

- **左键单击托盘图标**：打开 / 关闭控制面板
- **再次启动程序**：不会重复驻留，而是唤出已运行实例的面板
- **右键单击**：打开面板、设置、重新连接、退出
- **面板左下角「设置」**：
  - 通用：全局热键、开机自启、闲置断开、低电量提醒、电量样式、明暗主题、背景图片与模糊程度
  - 手势：左右耳单击 / 双击 / 三击 / 长按的动作与启用开关
  - 音频：提示音音量、当前编码、降噪循环
  - 关于：设备名称、固件版本、运行日志

---

## 默认热键

| 组合键 | 作用 |
|---|---|
| `Ctrl+Alt+A` | 循环切换降噪档位 |
| `Ctrl+Alt+E` | 循环切换均衡器 |
| `Ctrl+Alt+O` | 打开 / 关闭面板 |

默认组合被其它软件占用时会自动降级到备选组合，并写回设置。
可在设置页点击输入框后直接按下想要的组合键重新绑定。

---

## 数据位置

| 内容 | 路径 |
|---|---|
| 日志 | `%LOCALAPPDATA%\OriGControl\log.txt` |
| 设置 | `%LOCALAPPDATA%\OriGControl\settings.json` |

调试参数：

- `--panel`：启动后立即打开面板
- `--snapshot <path>`：渲染主面板截图后退出
- `--snapshot-settings <path>`：渲染设置页四个页签的截图后退出（文件名追加页签名）

---

## 命令行工具

`OriG.Cli` 提供逆向与排障命令：

```powershell
dotnet run --project .\src\OriG.Cli -- <command>
```

| 命令 | 说明 |
|---|---|
| `scan` | 列出已配对设备、查找 `NHCKCTRL` 服务、读取 RFCOMM 通道号 |
| `probe` | 连接 → 握手 → 状态汇总 → 稳定性与重连测试 |
| `query <hex>` | 发送单条查询，例如 `query 0005` |
| `set anc <n>` | 设置降噪：`0` 关闭 / `1` 通透 / `2` 普通 / `3` 深度 / `16` 实验性 / `17` 风噪 |
| `set eq <n>` | 设置均衡器：`0`–`6` |
| `set tone <n>` | 设置提示音音量：`0`–`16` |
| `set touch <left\|right> <single\|double\|triple\|long> <n>` | 设置触控动作：`0`–`8` |
| `find on\|off` | 查找耳机：开始响铃 / 停止响铃 |
| `monitor [秒]` | 监听耳机主动上报（操作耳机触控时观察） |
| `watch [秒] [间隔ms]` | 轮询并只打印发生变化的字段 |
| `raw "<hex>"` | 发送原始帧，用于逆向探测 |

---

## 项目结构

```text
src/
  OriG.Protocol/    纯协议层，零依赖（net10.0）
  OriG.Transport/   WinRT RFCOMM 传输与 SDP 解析（net10.0-windows10.0.19041.0）
  OriG.Core/        会话管理：连接、握手、命令串行化、SET 后回读校验
  OriG.Cli/         探针与长期排障工具
  OriG.App/         WPF 托盘应用
tools/
  btsnoop-extract.py    从手机 HCI 日志提取协议帧
  README.md             抓包指南
```

**关键工程约束**：`OriG.Transport` 的目标框架必须是
`net10.0-windows10.0.19041.0`；只写 `net10.0-windows` 无法调用 WinRT 蓝牙 API。

---

## 协议速查

经典蓝牙 SPP，服务 UUID `0000a100-1000-8000-4e48-434b4354524c`
（尾部对应 ASCII `NHCKCTRL`）。

```text
帧格式：[0]=0x4E | [1..2]=length(LE) | [3]=0x00 | [4..5]=opcode(LE) | [6..]=params
        length = 3 + params.Length，总长 = 3 + length
```

| 功能 | QUERY | SET |
|---|---|---|
| 电量 | `0x0005` | — |
| 固件 | `0x0003` | — |
| 降噪 | `0x0101` | `0x0201` + `[mode, 0x00]` |
| 均衡器 | `0x0107` | `0x0207` + `[preset]` |
| 提示音音量 | `0x0102` | `0x0202` + `[vol 0..16]` |
| 触控配置 | `0x0103` | `0x0203` + `[side, enabled, gesture, action]` |
| 当前编码 | `0x0104` | — |
| 触控降噪循环 | `0x010A` | — |
| 查找耳机 | — | `0x03A2` + `[01]` 开始 / `[00]` 停止 |

降噪 `mode`：`0x00` 关闭 / `0x01` 通透 / `0x02` 普通 / `0x03` 深度 /
`0x10` 实验性。抗风噪独立开关为 `0x01E1` / `0x02E1`，不参与降噪循环。

触控 `side`：`0x01` 左 / `0x02` 右；`gesture`：`0x01` 单击 / `0x02` 双击 /
`0x03` 三击 / `0x05` 长按；`action`：`0x00` 无 / `0x01` 播放暂停 / `0x02` 上一曲 /
`0x03` 下一曲 / `0x04` 降噪切换 / `0x05` 音量- / `0x06` 音量+ /
`0x07` 游戏模式 / `0x08` 语音助手。

编码只读映射：`0x00` AAC / `0x01` LHDC / `0x02` SBC。

---

## 已知问题

- **回帧冗余**：PC 链路上同一回帧可能重复 2–4 次（手机通常只有 1 次），
  CLI 会按时间窗口自动折叠。
- **RFCOMM 独占**：同一时刻只能有一个程序占用 SPP。实测手机与电脑同时连接、
  甚至手机官方 App 持有 SPP 时，PC 控制仍可共存。
- **仅实测一款耳机**：`YUANDAO OriG in` 固件 4.08；同协议族的其它型号未经验证。
- **传输仅有 T1**：当前只实现 WinRT RFCOMM；Winsock / 指定通道兜底尚未实现。
- **查找耳机计时**：UI 仍按 10 秒保护性停止，耳机端实际自动停止时间未标定。
- **多屏/混合 DPI**：窗口已跟随鼠标或拥有者所在屏幕，但混合 DPI 组合仍需实机验证。
- **未签名**：发布包没有代码签名，首次运行可能触发 SmartScreen 提示。

---

## 工具

- [手机端蓝牙抓包指南](tools/README.md)

---

## 许可

[MIT](LICENSE)，仅覆盖本仓库代码。不包含任何厂商代码、固件或图片资源。
