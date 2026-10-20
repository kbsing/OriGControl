# 手机端蓝牙抓包指南

本指南说明如何在 Android 手机上记录蓝牙 HCI 日志，并使用
[`btsnoop-extract.py`](btsnoop-extract.py) 提取耳机 SPP 协议帧。
适用于协议分析、问题排查与功能验证。

---

## 一、准备工作

- 一台已与耳机完成配对的 Android 手机
- 原道官方 App
- `adb`（推荐，用于导出日志）
- Python 3.8 及以上

---

## 二、开启 HCI 监听日志

```text
设置 → 关于手机 → 连续点击「版本号」7 次 → 输入锁屏密码 → 开启开发者模式
设置 → 系统 → 开发者选项 → 找到「启用蓝牙 HCI 监听日志」→ 打开
```

不同系统的入口略有差异，可在开发者选项中搜索关键词 **「HCI」** 或
**「蓝牙 HCI 日志」**。

| 系统 | 路径 |
|---|---|
| 原生 Android / Pixel | 开发者选项 → 启用蓝牙 HCI 监听日志 |
| HyperOS / MIUI | 开发者选项 → 启用蓝牙 HCI 监听日志 |
| ColorOS / realme UI | 开发者选项 → 蓝牙 HCI 监听日志 |
| HarmonyOS | 开发者选项 → 蓝牙 HCI 日志 |

> 部分定制系统会隐藏该选项。若找不到，可直接使用第五节的 bugreport 方案；
> 新版 Android 的 bugreport 默认包含 btsnoop 日志。

---

## 三、重新初始化蓝牙

打开日志开关后，需要**关闭蓝牙再重新打开**（或重启手机），
让蓝牙协议栈重新初始化并开始写入新日志。否则日志中可能只有旧数据。

---

## 四、录制操作

1. 让手机连接耳机（确认连接方是手机，而不是电脑）
2. 打开原道官方 App
3. 逐项执行需要分析的操作，**每步间隔 2–3 秒**，便于区分帧：
   - 降噪：关闭 → 通透 → 普通 → 深度，逐档切换
   - 均衡器：依次切换各个预设
   - 功能开关：游戏模式、入耳检测、双设备连接、抗风噪，分别开关一次
   - 其它：重命名、查找耳机、触控设置、固件检查（仅查看，不要升级）
4. 操作完成后可关闭日志开关

---

## 五、导出日志

```bash
# 方式 A：有 root 权限
adb shell su -c 'cp /data/misc/bluetooth/logs/btsnoop_hci.log /sdcard/'
adb pull /sdcard/btsnoop_hci.log

# 方式 B：无 root 权限，使用 bugreport（推荐）
adb bugreport bugreport.zip
# 解压后取：bugreport-*/FS/data/misc/bluetooth/logs/btsnoop_hci.log
```

Android 13 及以上版本可能将日志命名为 `bt_hci_*.cfa`，格式与
`btsnoop_hci.log` 相同，可直接使用。

> Android 11+ 支持只抓取蓝牙部分，文件体积更小：
> `adb bugreport --bluetooth bugreport-bt.zip`
> （不同版本参数名可能为 `-b bluetooth`，可用 `adb bugreport --help` 确认）

---

## 六、解析日志

```bash
python tools/btsnoop-extract.py btsnoop_hci.log
```

常用参数：

| 参数 | 说明 |
|---|---|
| `--raw` | 同时输出原始十六进制 |
| `--session` | 按时间顺序输出会话 |
| `--all` | 不过滤 opcode（可能包含误报） |

也可以使用 **Wireshark** 直接打开 btsnoop 日志：

- 过滤 `btbrfcomm` 或 `rfcomm`
- 协议帧以 `4e` 开头，便于人工识别

---

## 七、常见问题

| 现象 | 可能原因 | 处理 |
|---|---|---|
| 日志文件不存在 | 开关未开启，或蓝牙未重新初始化 | 检查第二节、第三节 |
| 文件很小（几 KB） | 开关已开，但协议栈未重新初始化 | 关闭再打开蓝牙后重新录制 |
| 抓不到任何 `4e` 帧 | 录制时手机未连接耳机 | 确认音频由手机输出 |
| 只有 A2DP 数据 | App 未建立 SPP 连接 | 在 App 中执行一次控制操作；确认使用的是官方 App |
| 有数据但解析不出 | 日志格式或通道不同 | 先使用 `--all --raw` 查看原始数据，再手动比对 `4e` 帧头 |

---

## 八、脚本说明

`btsnoop-extract.py` 仅使用 Python 标准库，无需安装依赖。它会：

1. 解析 btsnoop 记录（支持 datalink 1002 H4 / 1001）
2. 拆分 HCI ACL 与 L2CAP 数据
3. 在每个 ACL 包的数据区内匹配协议帧头（`4E` magic、保留位、长度与 opcode）
4. 按 opcode 解码并汇总收发帧

输出示例：

```text
共 6 帧，3 个 opcode

0x0005  电量
    TX ×1   [ 0] （无参数）
    RX ×1   [ 3] L=90% R=91% 仓=--
0x0101  降噪状态
    TX ×1   [ 0] （无参数）
    RX ×1   [ 1] 深度降噪
0x0201  设降噪
    TX ×1   [ 2] 深度降噪
    RX ×1   [ 2] 深度降噪
```
