# tools —— 辅助工具与抓包指南

用于手机端 HCI 抓包、协议帧提取与逆向排障。

---

## 一、手机端 HCI 抓包（M8 协议扩展的核心原料）

目标：拿到「官方 App 每条操作对应什么指令」的完整证据。**比反编译可靠得多**——直接看空口明文。

### 1.1 开启 HCI 监听日志

```
设置 → 关于手机 → 连续点击「版本号」7 次 → 输入锁屏密码 → 开启开发者模式
设置 → 系统 → 开发者选项 → 找到「启用蓝牙 HCI 监听日志」→ 打开
```

不同系统路径略有差异，搜索关键词：**「HCI」** 或 **「蓝牙 HCI 监听日志」**。

| 系统 | 路径 |
|---|---|
| 原生 Android / Pixel | 开发者选项 → 启用蓝牙 HCI 监听日志 |
| HyperOS / MIUI | 开发者选项 → 启用蓝牙 HCI 监听日志 |
| ColorOS / realme UI | 开发者选项 → 蓝牙 HCI 监听日志 |
| HarmonyOS | 开发者选项 → 蓝牙 HCI 日志 |

> 部分定制系统把这一项隐藏了。若找不到，直接用 1.3 的 bugreport 方案，
> 新版 Android 的 bugreport 里**默认就带** btsnoop。

### 1.2 关键动作：开关一次蓝牙

**这一步最容易漏。** 打开开关后必须**关闭蓝牙再重新打开**（或重启手机），
蓝牙协议栈才会重新初始化并开始写入新日志。否则日志里只有旧数据。

### 1.3 执行测试并取回日志

**先清空旧日志**（避免几十 MB 历史数据混进来），然后：

1. 让手机连上耳机（确保是**手机**连，不是电脑）
2. 打开原道官方 App（`com.yuandao.nicehck`）
3. **每做一个操作，停 3 秒**，让帧之间的间隔清晰可辨：
   - 切降噪：关闭 → 通透 → 普通 → 深度，逐档停留
   - 切 EQ：把 7 个预设逐个点一遍
   - 每个开关：游戏模式 / 入耳检测 / 双设备 / 抗风噪，各开各关一次
   - 其他：改名、查找耳机、触控设置、固件检查（**只看不升级**）
4. 取日志：

```bash
# 方式 A：有 root（最快）
adb shell su -c 'cp /data/misc/bluetooth/logs/btsnoop_hci.log /sdcard/'
adb pull /sdcard/btsnoop_hci.log

# 方式 B：无 root —— 用 bugreport（推荐，通用）
adb bugreport bugreport.zip
# 解压后取：bugreport-*/FS/data/misc/bluetooth/logs/btsnoop_hci.log
```

> Android 11+ 支持只抓蓝牙部分，体积小很多：
> `adb bugreport --bluetooth bugreport-bt.zip`
> （不同版本参数名可能是 `-b bluetooth`，`adb bugreport --help` 确认）

### 1.4 解析

```bash
python tools/btsnoop-extract.py btsnoop_hci.log --raw
```

也可以直接用 **Wireshark** 打开（原生支持 btsnoop 格式）：
- 过滤 `btbrfcomm` 或 `rfcomm`
- 我们关心的 payload 全部以 `4e` 开头，肉眼可辨

### 1.5 排障

| 现象 | 原因 | 处理 |
|---|---|---|
| 文件不存在 | 开关没开 / 没重启蓝牙 | 回 1.1、1.2 |
| 文件很小（几 KB） | 开关开了但没重新初始化 | 关蓝牙再开，重新操作 |
| 抓不到任何 `4e` 帧 | 操作时手机没连耳机 / 用的是电脑连的 | 确认音频从手机出声 |
| 只有 A2DP 数据 | App 没真正连上 SPP | 在 App 里触发一次控制动作 |
| `--all-cids` 后有数据但解析不出 | 通道号不同 | 先看原始十六进制，手动比对 `4e` |

---

## 二、`btsnoop-extract.py`

零依赖（仅标准库）的 btsnoop 解析器，专门抽取原道 SPP 协议帧。

```bash
python tools/btsnoop-extract.py btsnoop_hci.log          # 只列帧
python tools/btsnoop-extract.py btsnoop_hci.log --raw    # 附带原始十六进制
python tools/btsnoop-extract.py btsnoop_hci.log --all-cids   # 含 SDP 等所有通道
```

输出示例：

```
=== TX  CID=0x0047  (3 帧) ===
  [TX] 0x0005 电量                  
  [TX] 0x0101 降噪状态
  [TX] 0x0201 设降噪                深度降噪
=== RX  CID=0x0047  (3 帧) ===
  [RX] 0x0005 电量                  L=90% R=91% 仓=--
  [RX] 0x0101 降噪状态              深度降噪
  [RX] 0x0201 设降噪                深度降噪
```

它做的事：
1. 解析 btsnoop 记录（支持 datalink 1002 H4 / 1001）
2. 拆 HCI ACL → L2CAP
3. 按通道重组 RFCOMM 流并拆帧（含 SABM/UA/UIH 识别）
4. 在 payload 中扫描 `4E` magic，按长度字段切帧并解码

---

## 三、`tfm-check` —— WinRT 蓝牙可用性自检

一个最小控制台程序，验证当前机器能否用 WinRT 访问原道耳机的自定义 UUID 服务。

```bash
cd tools/tfm-check
dotnet run
```

**2026-10-20 在目标机实测结果（已通过）**

```
[OK] RfcommServiceId 构造成功: {0000A100-1000-8000-4E48-434B4354524C}
[OK] 枚举到 1 个设备
     Name = YUANDAO OriG in
     Id   = Bluetooth#Bluetoothaa:bb:cc:dd:ee:02-aa:bb:cc:dd:ee:01#RFCOMM:00000000:{0000a100-...}
     [OK] ConnectionHostName    = (AA:BB:CC:DD:EE:01)
     [OK] MaxProtectionLevel    = BluetoothEncryptionWithAuthentication
```

**用途**：
- 换机器 / 换适配器 / 重装系统后，先跑这个判断 T1 是否可用
- 输出的 `MaxProtectionLevel` 决定 `StreamSocket` 连接时的保护级别参数
- 若这里失败 → 直接走 T2（Winsock），不要浪费时间调 T1

**注意**：`ConnectionServiceName` 里的 `RFCOMM:00000000` 不是通道号，
是 Windows 设备接口路径的占位段。真实通道号仍需在连接成功后从 SDP 或抓包确认（M1 的 A1）。
