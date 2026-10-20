#!/usr/bin/env python3
"""
btsnoop-extract.py —— 从 Android HCI 抓包日志中提取原道(NiceHCK) SPP 协议帧

零依赖，仅用 Python 标准库。
支持 .cfa（Android 13+ 的新命名）与 btsnoop_hci.log —— 两者格式相同。

用法:
    python btsnoop-extract.py bt_hci_xxx.cfa
    python btsnoop-extract.py bt_hci_xxx.cfa --raw        # 附带原始十六进制
    python btsnoop-extract.py bt_hci_xxx.cfa --session    # 按时间顺序输出会话
    python btsnoop-extract.py bt_hci_xxx.cfa --all        # 不过滤 opcode（含误报）

设计说明:
    不使用 L2CAP/RFCOMM 流式重组 —— 实测抓包日志里 ACL 包的分片标志不可靠，
    重组会导致错位。改为在每个 ACL 包的数据区内直接按严格规则匹配帧头：
      magic=0x4E、reserved=0x00、length 与包内剩余长度吻合、opcode 在已知表内。
    这样既准确又对分片/丢包免疫。
"""

import sys
import struct
import argparse
from collections import Counter, defaultdict

MAGIC = 0x4E

# 已由抓包确认的 opcode（2026-10-20 实测，固件 4.08）
KNOWN_OPS = {
    0x0001, 0x0003, 0x0004, 0x0005, 0x0006,
    0x0101, 0x0102, 0x0103, 0x0104, 0x0105, 0x0106,
    0x0107, 0x0108, 0x0109, 0x010A, 0x01E1,
    0x0201, 0x0202, 0x0203, 0x0204, 0x0206, 0x0207, 0x02E1,
    0x03A2,
}

OP_NAMES = {
    0x0001: "查询1", 0x0003: "固件版本", 0x0004: "查询4",
    0x0005: "电量", 0x0006: "查询6",
    0x0101: "降噪状态", 0x0102: "查询0x0102", 0x0103: "配置表读",
    0x0104: "查询0x0104", 0x0105: "双设备连接", 0x0106: "低延迟",
    0x0107: "EQ 状态", 0x0108: "游戏模式", 0x0109: "入耳检测",
    0x010A: "查询0x010A", 0x01E1: "抗风噪",
    0x0201: "设降噪", 0x0202: "设0x0202", 0x0203: "配置表写",
    0x0204: "事件上报", 0x0206: "设低延迟", 0x0207: "设 EQ",
    0x02E1: "设抗风噪", 0x03A2: "设0x03A2",
}

ANC_NAMES = {0x00: "关闭", 0x01: "通透", 0x02: "普通降噪", 0x03: "深度降噪",
             0x10: "模式0x10", 0x11: "模式0x11"}
EQ_NAMES = {0: "悔恨之泪", 1: "均衡中正", 2: "欧美澎湃", 3: "真律还原",
            4: "游戏优化", 5: "细腻佳音", 6: "温婉人声"}

DL_H4 = 1002
DL_UNENCAPSULATED = 1001


def iter_acl(path):
    """产出 (direction, data, ts)，data 为 HCI ACL 包的数据区（已去掉 4 字节 ACL 头）"""
    raw = open(path, "rb").read()
    if not raw.startswith(b"btsnoop\x00"):
        raise ValueError("不是 btsnoop 格式（缺少 magic 'btsnoop\\0'）")
    _ver, datalink = struct.unpack(">II", raw[8:16])

    pos, n = 16, len(raw)
    while pos + 24 <= n:
        _o, incl, flags, _d, ts = struct.unpack(">IIIIQ", raw[pos:pos + 24])
        pos += 24
        if pos + incl > n:
            break
        pkt = raw[pos:pos + incl]
        pos += incl
        if not pkt:
            continue
        direction = "RX" if (flags & 0x01) else "TX"

        if datalink == DL_H4:
            if pkt[0] != 0x02:          # 0x02 = ACL
                continue
            body = pkt[1:]
        elif datalink == DL_UNENCAPSULATED:
            body = pkt
        else:
            continue

        if len(body) < 4:
            continue
        dlen = struct.unpack("<HH", body[:4])[1] & 0x0FFF
        yield direction, body[4:4 + dlen], ts


def scan_frames(payload, strict=True):
    """在字节流中定位完整帧。strict=True 时只接受已知 opcode。"""
    out, i, n = [], 0, len(payload)
    while i + 5 < n:
        if payload[i] != MAGIC or payload[i + 3] != 0x00:
            i += 1
            continue
        length = payload[i + 1] | (payload[i + 2] << 8)
        total = length + 3
        if total < 6 or total > 200 or i + total > n:
            i += 1
            continue
        op = payload[i + 4] | (payload[i + 5] << 8)
        if strict and op not in KNOWN_OPS:
            i += 1
            continue
        out.append((op, payload[i + 6:i + total], payload[i:i + total]))
        i += total
    return out


def describe(op, params):
    name = OP_NAMES.get(op, f"未知 0x{op:04X}")
    extra = ""
    if op == 0x0005 and len(params) >= 2:
        l, r = params[0], params[1]
        c = params[2] if len(params) > 2 else 0
        extra = f"L={l}%  R={r}%  仓={c}%"
    elif op == 0x0003 and len(params) >= 2:
        extra = f"固件 {params[1]}.{params[0]:02d}"
    elif op in (0x0101, 0x0201) and params:
        extra = ANC_NAMES.get(params[0], f"未知(0x{params[0]:02X})")
    elif op in (0x0107, 0x0207) and params:
        extra = EQ_NAMES.get(params[0], f"未知({params[0]})")
    elif op in (0x0204,) and len(params) >= 7:
        extra = f"seq={params[0]}  值=0x{params[6]:02X}"
    elif params:
        if op in (0x0105, 0x0106, 0x0108, 0x0109, 0x01E1, 0x0206, 0x02E1) \
                and params[0] in (0, 1):
            extra = "开" if params[0] else "关"
        else:
            extra = " ".join(f"{b:02X}" for b in params)
    return name, extra


def hexstr(b):
    return " ".join(f"{b:02X}" for b in b)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("file")
    ap.add_argument("--raw", action="store_true", help="附带原始十六进制")
    ap.add_argument("--session", action="store_true", help="按时间顺序输出")
    ap.add_argument("--all", action="store_true", help="不过滤 opcode（会含误报）")
    args = ap.parse_args()

    try:
        frames = []
        for direction, data, ts in iter_acl(args.file):
            for op, params, raw in scan_frames(data, strict=not args.all):
                frames.append((ts, direction, op, params, raw))
    except Exception as e:
        print(f"读取失败: {e}", file=sys.stderr)
        return 1

    if not frames:
        print("未找到 NHCK 协议帧。排查顺序：")
        print("  1. 抓包期间是否用【手机】连耳机并操作了官方 App")
        print("  2. 开启 HCI 日志后是否【重启过蓝牙】")
        print("  3. 试加 --all 看是否有非常规 opcode")
        return 0

    if args.session:
        t0 = frames[0][0]
        prev_tx = None
        print(f"{'Δms':>9}  {'方向':<4} {'opcode':<7} 说明")
        print("-" * 74)
        for ts, d, op, params, raw in frames:
            name, extra = describe(op, params)
            delta = ""
            if d == "TX":
                if prev_tx is not None:
                    delta = f"+{(ts - prev_tx) / 1000.0:.0f}ms"
                prev_tx = ts
            print(f"{(ts - t0) / 1000.0:>9.0f}  {d:<4} 0x{op:04X}  {name:<12} {extra:<24} {delta}")
            if args.raw:
                print(f"{'':>26}raw: {hexstr(raw)}")
        txs = [ts for ts, d, *_ in frames if d == "TX"]
        gaps = [(txs[i + 1] - txs[i]) / 1000.0 for i in range(len(txs) - 1)]
        if gaps:
            print(f"\nTX 间隔: 最小 {min(gaps):.0f}ms  中位 {sorted(gaps)[len(gaps)//2]:.0f}ms  最大 {max(gaps):.0f}ms")
        return 0

    # 汇总模式
    by_op = defaultdict(lambda: {"TX": Counter(), "RX": Counter()})
    for _ts, d, op, params, _raw in frames:
        by_op[op][d][params] += 1

    print(f"共 {len(frames)} 帧，{len(by_op)} 种 opcode\n")
    for op in sorted(by_op):
        name = OP_NAMES.get(op, f"未知 0x{op:04X}")
        print(f"0x{op:04X}  {name}")
        for d in ("TX", "RX"):
            for params, c in by_op[op][d].most_common(5):
                _, extra = describe(op, params)
                shown = extra if extra else (hexstr(params) or "（无参数）")
                print(f"    {d} ×{c:<3} [{len(params):>2}] {shown}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
