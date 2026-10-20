namespace OriG.Protocol;

/// <summary>耳机状态快照。由 <see cref="Apply"/> 逐帧更新。</summary>
public sealed class HeadsetState
{
    // 变更检测用的影子字段（bool? 用 raw+1 表示，0 = 未设置）
    private int _anc = -1;
    private int _eq = -1;
    private int _gameMode;
    private int _lowLatency;
    private int _dualConn;
    private int _inEar;
    private int _wind;
    private int _query102 = int.MinValue;
    private int _query104 = int.MinValue;
    private int _lastEventSeq = -1;
    private byte[]? _query1;
    private byte[]? _query4;
    private byte[]? _query6;
    private byte[]? _query10A;
    private byte[]? _configTable;

    public int BatteryLeft { get; private set; } = -1;
    public int BatteryRight { get; private set; } = -1;

    /// <summary>充电仓：协议里只有 0 或 100 两种值，本质是布尔状态而非百分比。</summary>
    public bool CasePresent { get; private set; }

    public int Anc { get; private set; } = -1;
    public int Eq { get; private set; } = -1;
    public bool? GameMode { get; private set; }
    public bool? LowLatency { get; private set; }
    public bool? DualConn { get; private set; }
    public bool? InEar { get; private set; }
    public bool? Wind { get; private set; }

    public int FirmwareMain { get; private set; } = -1;
    public int FirmwareSub { get; private set; } = -1;

    public byte[]? ConfigTable { get; private set; }
    public byte[]? Query1 { get; private set; }
    public byte[]? Query4 { get; private set; }
    public byte[]? Query6 { get; private set; }
    public byte[]? Query10A { get; private set; }
    public int? Query102 { get; private set; }
    public int? Query104 { get; private set; }
    public int LastEventSeq { get; private set; } = -1;

    /// <summary>
    /// 应用一帧，返回状态是否**真的发生变化**。
    /// 重复帧（PC 链路上很常见）必须返回 false，否则会触发多余的 UI 刷新，表现为控件闪烁。
    /// </summary>
    public bool Apply(Frame f)
    {
        switch (f.OpCode)
        {
            case OpCodes.Battery:
            {
                int l = f[0], r = f[1];
                bool c = f[2] > 0;
                if (l == BatteryLeft && r == BatteryRight && c == CasePresent)
                    return false;
                BatteryLeft = l;
                BatteryRight = r;
                CasePresent = c;
                return true;
            }

            case OpCodes.AncQuery:
            {
                int v = f[0];
                if (_anc == v)
                    return false;
                _anc = v;
                Anc = v;
                return true;
            }

            case OpCodes.EqQuery:
            {
                int v = f[0];
                if (_eq == v)
                    return false;
                _eq = v;
                Eq = v;
                return true;
            }

            case OpCodes.Firmware:
            {
                int sub = f[0], main = f[1];
                if (sub == FirmwareSub && main == FirmwareMain)
                    return false;
                FirmwareSub = sub;
                FirmwareMain = main;
                return true;
            }

            case OpCodes.GameModeQuery:
                return SetBool(ref _gameMode, f.IsOn(0), v => GameMode = v);
            case OpCodes.LowLatencyQuery:
                return SetBool(ref _lowLatency, f.IsOn(0), v => LowLatency = v);
            case OpCodes.DualConnQuery:
                return SetBool(ref _dualConn, f.IsOn(0), v => DualConn = v);
            case OpCodes.InEarQuery:
                return SetBool(ref _inEar, f.IsOn(0), v => InEar = v);
            case OpCodes.WindQuery:
                return SetBool(ref _wind, f.IsOn(0), v => Wind = v);

            case OpCodes.Query1:
                return SetBytes(ref _query1, f.Parameters, v => Query1 = v);
            case OpCodes.Query4:
                return SetBytes(ref _query4, f.Parameters, v => Query4 = v);
            case OpCodes.Query6:
                return SetBytes(ref _query6, f.Parameters, v => Query6 = v);
            case OpCodes.Query10A:
                return SetBytes(ref _query10A, f.Parameters, v => Query10A = v);
            case OpCodes.ConfigRead:
                return SetBytes(ref _configTable, f.Parameters, v => ConfigTable = v);

            case OpCodes.Query102:
            {
                int v = f[0];
                if (_query102 == v)
                    return false;
                _query102 = v;
                Query102 = v;
                return true;
            }

            case OpCodes.Query104:
            {
                int v = f[0];
                if (_query104 == v)
                    return false;
                _query104 = v;
                Query104 = v;
                return true;
            }

            case OpCodes.Event204:
            {
                int v = f[0];
                if (_lastEventSeq == v)
                    return false;
                _lastEventSeq = v;
                LastEventSeq = v;
                return true;
            }

            default:
                return false;
        }
    }

    private static bool SetBool(ref int field, bool value, Action<bool?> assign)
    {
        int raw = (value ? 1 : 0) + 1;
        if (field == raw)
            return false;
        field = raw;
        assign(value);
        return true;
    }

    private static bool SetBytes(ref byte[]? field, byte[] value, Action<byte[]> assign)
    {
        if (field is not null && field.AsSpan().SequenceEqual(value))
            return false;
        field = value;
        assign(value);
        return true;
    }

    public string BatteryText
    {
        get
        {
            if (BatteryLeft < 0 && BatteryRight < 0)
                return "未知";
            string l = BatteryLeft >= 0 ? $"{BatteryLeft}%" : "--";
            string r = BatteryRight >= 0 ? $"{BatteryRight}%" : "--";
            return $"L {l}  R {r}  仓 {(CasePresent ? "在仓" : "不在仓")}";
        }
    }

    public string FirmwareText => FirmwareMain < 0 ? "未知" : $"{FirmwareMain}.{FirmwareSub:00}";

    public string AncText => Anc < 0 ? "未知" : AncMode.Name(Anc);

    public string EqText => Eq < 0 ? "未知" : EqPreset.Name(Eq);

    public static string Tri(bool? v) => v switch { true => "开", false => "关", null => "未知" };
}
