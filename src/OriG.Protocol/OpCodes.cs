namespace OriG.Protocol;

/// <summary>
/// NiceHCK / 原道 协议 opcode 表。
/// 全部经 2026-10-20 HCI 抓包验证，固件 4.08。
/// 详见 docs/PROTOCOL-CAPTURED.md。
/// </summary>
public static class OpCodes
{
    // ---- 0x00xx：设备信息 ----
    public const ushort Query1 = 0x0001;
    public const ushort Firmware = 0x0003;
    public const ushort Query4 = 0x0004;
    public const ushort Battery = 0x0005;
    public const ushort Query6 = 0x0006;

    // ---- 0x01xx：状态查询 ----
    public const ushort AncQuery = 0x0101;
    public const ushort Query102 = 0x0102;
    public const ushort ConfigRead = 0x0103;
    public const ushort Query104 = 0x0104;
    public const ushort DualConnQuery = 0x0105;
    public const ushort LowLatencyQuery = 0x0106;
    public const ushort EqQuery = 0x0107;
    public const ushort GameModeQuery = 0x0108;
    public const ushort InEarQuery = 0x0109;
    public const ushort Query10A = 0x010A;
    public const ushort WindQuery = 0x01E1;

    // ---- 0x02xx：设置 / 事件 ----
    public const ushort AncSet = 0x0201;
    public const ushort Set202 = 0x0202;
    public const ushort ConfigWrite = 0x0203;
    /// <summary>耳机主动上报事件（带递增序号）。只读，不要下发。</summary>
    public const ushort Event204 = 0x0204;
    public const ushort DualConnSet = 0x0205;
    public const ushort LowLatencySet = 0x0206;
    public const ushort EqSet = 0x0207;
    public const ushort GameModeSet = 0x0208;
    public const ushort InEarSet = 0x0209;
    public const ushort WindSet = 0x02E1;

    /// <summary>查找耳机：SET [01] 开始响铃，[00] 停止。官方 App 的 bt.find_earbuds 指令。</summary>
    public const ushort FindEarbudsSet = 0x03A2;

    /// <summary>
    /// 官方 App 的握手顺序（实测 16 条，间隔约 110ms）。
    /// 注意 0x01E1 排在 0x0109 之前，与编号顺序不同。
    /// </summary>
    public static readonly ushort[] Handshake =
    [
        Query1, Firmware, Query4, Battery, Query6,
        AncQuery, Query102, ConfigRead, Query104,
        DualConnQuery, LowLatencyQuery, EqQuery, GameModeQuery,
        WindQuery, InEarQuery, Query10A,
    ];

    /// <summary>
    /// 面板打开时的轻量刷新序列。固件与只读字段不会变，只查 UI 用得到的状态。
    /// 间隔沿用 110ms，避免连续下发过快。
    /// </summary>
    public static readonly ushort[] Refresh =
    [
        Battery, AncQuery, Query102, Query104, ConfigRead,
        DualConnQuery, LowLatencyQuery, EqQuery, GameModeQuery,
        WindQuery, InEarQuery, Query10A,
    ];

    public static string Name(ushort op) => op switch
    {
        Query1 => "查询1",
        Firmware => "固件版本",
        Query4 => "查询4",
        Battery => "电量",
        Query6 => "查询6",
        AncQuery => "降噪状态",
        Query102 => "提示音音量",
        ConfigRead => "触控配置",
        Query104 => "当前编码",
        DualConnQuery => "双设备连接",
        LowLatencyQuery => "低延迟",
        EqQuery => "EQ 状态",
        GameModeQuery => "游戏模式",
        InEarQuery => "入耳检测",
        Query10A => "降噪循环",
        WindQuery => "抗风噪",
        AncSet => "设降噪",
        Set202 => "设提示音音量",
        ConfigWrite => "设触控",
        Event204 => "事件上报",
        LowLatencySet => "设低延迟",
        EqSet => "设 EQ",
        GameModeSet => "设游戏模式",
        DualConnSet => "设双设备",
        InEarSet => "设入耳检测",
        WindSet => "设抗风噪",
        FindEarbudsSet => "查找耳机",
        _ => $"未知 0x{op:X4}",
    };
}

/// <summary>
/// 降噪档位。0x10 / 0x11 的语义已由官方 App 反编译确认：
/// 0x10 = 实验性降噪（更激进的算法，可能异常），0x11 = 风噪模式。
/// </summary>
public static class AncMode
{
    public const byte Off = 0x00;
    public const byte Transparency = 0x01;
    public const byte Normal = 0x02;
    public const byte Deep = 0x03;
    public const byte Experimental = 0x10;
    public const byte WindNoise = 0x11;

    public static string Name(int v) => v switch
    {
        Off => "关闭",
        Transparency => "通透",
        Normal => "普通降噪",
        Deep => "深度降噪",
        Experimental => "实验性降噪",
        WindNoise => "风噪模式",
        _ => $"未知(0x{v:X2})",
    };
}

/// <summary>均衡器预设，7 档全部实测可设。</summary>
public static class EqPreset
{
    public const byte Blue = 0x00;
    public const byte Balanced = 0x01;
    public const byte Bass = 0x02;
    public const byte Pure = 0x03;
    public const byte Game = 0x04;
    public const byte Fine = 0x05;
    public const byte Vocal = 0x06;

    public static string Name(int v) => v switch
    {
        Blue => "悔恨之泪",
        Balanced => "均衡中正",
        Bass => "欧美澎湃",
        Pure => "真律还原",
        Game => "游戏优化",
        Fine => "细腻佳音",
        Vocal => "温婉人声",
        _ => $"未知({v})",
    };
}
