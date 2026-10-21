namespace OriG.Protocol.Tests;

public class TouchConfigTests
{
    private static readonly byte[] Table =
    [
        0x01, 0x01, 0x01, 0x05,
        0x01, 0x00, 0x02, 0x04,
        0x02, 0x01, 0x01, 0x06,
        0x02, 0x01, 0x05, 0x00,
    ];

    [Fact]
    public void Parse_ReturnsOneBindingPerFourBytes()
    {
        var bindings = TouchConfig.Parse(Table);

        Assert.Equal(4, bindings.Count);
        Assert.Equal(new TouchBinding(0x01, 0x01, 0x01, 0x05), bindings[0]);
        Assert.Equal(new TouchBinding(0x02, 0x01, 0x05, 0x00), bindings[3]);
    }

    [Fact]
    public void Parse_RejectsMalformedTables()
    {
        Assert.Empty(TouchConfig.Parse(null));
        Assert.Empty(TouchConfig.Parse([]));
        Assert.Empty(TouchConfig.Parse([0x01, 0x01, 0x01]));
    }

    [Fact]
    public void Find_MatchesSideAndGesture()
    {
        var leftDouble = TouchConfig.Find(Table, TouchSide.Left, TouchGesture.Double);

        Assert.NotNull(leftDouble);
        Assert.Equal(0, leftDouble.Value.Enabled);
        Assert.Equal(TouchAction.NoiseSwitch, leftDouble.Value.Action);
        Assert.Null(TouchConfig.Find(Table, TouchSide.Right, TouchGesture.Triple));
    }

    [Fact]
    public void AncCycleText_SkipsDisabledEntries()
    {
        byte[] data = [0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x03, 0x01];

        Assert.Equal("关闭 → 普通降噪 → 深度降噪", TouchConfig.AncCycleText(data));
        Assert.Equal("未读取", TouchConfig.AncCycleText(null));
        Assert.Equal("未读取", TouchConfig.AncCycleText([0x00]));
        Assert.Equal("无", TouchConfig.AncCycleText([0x00, 0x00]));
    }

    [Theory]
    [InlineData(0x00, "AAC")]
    [InlineData(0x01, "LHDC")]
    [InlineData(0x02, "SBC")]
    [InlineData(0x7F, "未知")]
    public void CodecProtocol_MapsKnownValues(int value, string expected)
    {
        Assert.Equal(expected, CodecProtocol.Name(value));
        Assert.Equal("未读取", CodecProtocol.Describe(null));
    }
}
