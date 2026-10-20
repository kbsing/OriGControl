# OriG Control

Windows 托盘小工具，用来控制 **原道 OriG in** 蓝牙耳机。

> **非官方项目**，与原道（NiceHCK）及其关联公司无任何关联，
> 未被授权或认可。仅用于互操作性研究与个人使用。

---

## 功能

| 功能 | 状态 |
|---|---|
| 连接耳机（经典蓝牙 SPP / RFCOMM） | 可用 |
| 读取电量、固件版本、降噪、均衡器与 5 个功能开关 | 可用 |
| 切换降噪档位（关闭 / 通透 / 普通 / 深度等） | 可用 |
| 切换均衡器（7 档预设） | 可用 |
| 监听耳机主动上报（触控操作实时同步） | 可用 |
| 托盘常驻，左键弹出控制面板 | 可用 |
| 全局热键 | 可用（冲突时自动降级） |
| 开机自启、低电量提醒 | 可用 |
| 电量显示样式（圆环 / 电池） | 可用 |
| 自定义背景图片、实时模糊 | 可用 |
| 浅色 / 深色主题 | 可用 |
| 切档 HUD 浮层 | 未实现 |

界面为 Material You 风格，图标使用系统自带的 **Segoe Fluent Icons**
（Win10 回退到 Segoe MDL2 Assets）。

---

## 环境要求

- Windows 10 2004（Build 19041）或更高版本 / Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（运行）
- .NET 10 SDK（构建）
- 耳机已与电脑完成**经典蓝牙配对**
- 仅支持 Windows，不支持 Linux / macOS

---

## 构建与运行

```powershell
dotnet build -c Release
.\src\OriG.App\bin\Release\net10.0-windows10.0.19041.0\OriGControl.exe
```

发布为单文件（框架依赖，需要目标机器安装 .NET 10 桌面运行时）：

```powershell
dotnet publish .\src\OriG.App\OriG.App.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

启动后**没有主窗口**，只驻留系统托盘：

- **左键单击托盘图标**：打开 / 关闭控制面板
- **右键单击**：打开面板、设置、重新连接、退出
- **面板左下角「设置」**：热键、开机自启、低电量提醒、背景图片与模糊程度、明暗主题、电量样式

---

## 默认热键

| 组合键 | 作用 |
|---|---|
| `Ctrl+Alt+A` | 循环切换降噪档位 |
| `Ctrl+Alt+W` | 循环切换均衡器 |
| `Ctrl+Shift+O` | 打开 / 关闭面板 |

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
- `--snapshot-settings <path>`：渲染设置页截图后退出

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
| `set anc <n>` | 设置降噪：`0` 关闭 / `1` 通透 / `2` 普通 / `3` 深度 / `16`、`17` 待定档 |
| `set eq <n>` | 设置均衡器：`0`–`6` |
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

---

## 已知问题

- **回帧冗余**：PC 链路上同一回帧可能重复 2–4 次（手机通常只有 1 次），
  CLI 会按时间窗口自动折叠。
- **RFCOMM 独占**：同一时刻只能有一个程序占用 SPP。实测手机与电脑同时连接、
  甚至手机官方 App 持有 SPP 时，PC 控制仍可共存。
- **仅实测一款耳机**：`YUANDAO OriG in` 固件 4.08；同协议族的其它型号未经验证。
- **截图限制**：实时模糊依赖「窗口对抓屏隐身」，因此截图 / 录屏工具看不到面板。
  在设置中选择一张背景图片，或关闭实时模糊，即可恢复截图能力。

---

## 工具

- [手机端蓝牙抓包指南](tools/README.md)

---

## 许可

[MIT](LICENSE)，仅覆盖本仓库代码。不包含任何厂商代码、固件或图片资源。
