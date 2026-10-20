namespace OriG.Protocol;

public static class TouchSide
{
    public const byte Left = 0x01;
    public const byte Right = 0x02;

    public static string Name(int value) => value switch
    {
        Left => "左耳",
        Right => "右耳",
        _ => $"未知({value})",
    };
}

public static class TouchGesture
{
    public const byte Single = 0x01;
    public const byte Double = 0x02;
    public const byte Triple = 0x03;
    public const byte LongPress = 0x05;

    public static readonly byte[] All = [Single, Double, Triple, LongPress];

    public static string Name(int value) => value switch
    {
        Single => "单击",
        Double => "双击",
        Triple => "三击",
        LongPress => "长按",
        _ => $"未知({value})",
    };
}

public static class TouchAction
{
    public const byte None = 0x00;
    public const byte PlayPause = 0x01;
    public const byte PreviousTrack = 0x02;
    public const byte NextTrack = 0x03;
    public const byte NoiseSwitch = 0x04;
    public const byte VolumeDown = 0x05;
    public const byte VolumeUp = 0x06;
    public const byte GameMode = 0x07;
    public const byte VoiceAssistant = 0x08;

    public static readonly byte[] All =
    [
        None, PlayPause, PreviousTrack, NextTrack, NoiseSwitch,
        VolumeDown, VolumeUp, GameMode, VoiceAssistant,
    ];

    public static string Name(int value) => value switch
    {
        None => "无",
        PlayPause => "播放 / 暂停",
        PreviousTrack => "上一曲",
        NextTrack => "下一曲",
        NoiseSwitch => "降噪切换",
        VolumeDown => "音量 -",
        VolumeUp => "音量 +",
        GameMode => "游戏模式",
        VoiceAssistant => "语音助手",
        _ => $"未知({value})",
    };
}

public readonly record struct TouchBinding(byte Side, byte Enabled, byte Gesture, byte Action);

public static class TouchConfig
{
    public const int BindingSize = 4;

    public static IReadOnlyList<TouchBinding> Parse(byte[]? table)
    {
        if (table is null || table.Length == 0 || table.Length % BindingSize != 0)
            return [];

        var result = new List<TouchBinding>(table.Length / BindingSize);
        for (int i = 0; i < table.Length; i += BindingSize)
        {
            result.Add(new TouchBinding(
                table[i],
                table[i + 1],
                table[i + 2],
                table[i + 3]));
        }
        return result;
    }

    public static TouchBinding? Find(byte[]? table, byte side, byte gesture)
    {
        foreach (var binding in Parse(table))
        {
            if (binding.Side == side && binding.Gesture == gesture)
                return binding;
        }
        return null;
    }

    public static string AncCycleText(byte[]? data)
    {
        if (data is null || data.Length < 2 || data.Length % 2 != 0)
            return "未读取";

        var modes = new List<string>(data.Length / 2);
        for (int i = 0; i < data.Length; i += 2)
        {
            if (data[i + 1] != 0)
                modes.Add(AncMode.Name(data[i]));
        }
        return modes.Count == 0 ? "无" : string.Join(" → ", modes);
    }
}

public static class CodecProtocol
{
    public static string Name(int value) => value switch
    {
        0x00 => "AAC",
        0x01 => "LHDC",
        0x02 => "SBC",
        _ => "未知",
    };

    public static string Describe(int? value) =>
        value is null ? "未读取" : $"{Name(value.Value)} (0x{value.Value:X2})";
}
