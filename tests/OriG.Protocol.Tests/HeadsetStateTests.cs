namespace OriG.Protocol.Tests;

public class HeadsetStateTests
{
    private static Frame Frame(ushort op, params byte[] parameters) => new(op, parameters);

    [Fact]
    public void Apply_BatteryUpdatesBothSidesAndCase()
    {
        var state = new HeadsetState();

        Assert.True(state.Apply(Frame(OpCodes.Battery, 0x50, 0x42, 0x01)));
        Assert.Equal(0x50, state.BatteryLeft);
        Assert.Equal(0x42, state.BatteryRight);
        Assert.True(state.CasePresent);

        Assert.False(state.Apply(Frame(OpCodes.Battery, 0x50, 0x42, 0x01)));
    }

    [Fact]
    public void Apply_FirmwareUsesSubMajorOrder()
    {
        var state = new HeadsetState();

        Assert.True(state.Apply(Frame(OpCodes.Firmware, 0x08, 0x04)));
        Assert.Equal(8, state.FirmwareSub);
        Assert.Equal(4, state.FirmwareMain);
        Assert.Equal("4.08", state.FirmwareText);
    }

    [Theory]
    [InlineData(OpCodes.GameModeQuery, OpCodes.GameModeSet)]
    [InlineData(OpCodes.LowLatencyQuery, OpCodes.LowLatencySet)]
    [InlineData(OpCodes.DualConnQuery, OpCodes.DualConnSet)]
    [InlineData(OpCodes.InEarQuery, OpCodes.InEarSet)]
    [InlineData(OpCodes.WindQuery, OpCodes.WindSet)]
    public void Apply_BothQueryAndSetFramesUpdateSwitch(ushort queryOp, ushort setOp)
    {
        var state = new HeadsetState();

        Assert.True(state.Apply(Frame(queryOp, 0x01)));
        Assert.True(Value(state, queryOp));
        Assert.False(state.Apply(Frame(queryOp, 0x01)));

        Assert.True(state.Apply(Frame(setOp, 0x00)));
        Assert.False(Value(state, queryOp));
    }

    [Theory]
    [InlineData(OpCodes.AncQuery, OpCodes.AncSet)]
    [InlineData(OpCodes.EqQuery, OpCodes.EqSet)]
    public void Apply_BothQueryAndSetFramesUpdateValue(ushort queryOp, ushort setOp)
    {
        var state = new HeadsetState();

        Assert.True(state.Apply(Frame(queryOp, 0x03)));
        Assert.Equal(0x03, queryOp == OpCodes.AncQuery ? state.Anc : state.Eq);

        Assert.True(state.Apply(Frame(setOp, 0x01)));
        Assert.Equal(0x01, queryOp == OpCodes.AncQuery ? state.Anc : state.Eq);
    }

    [Theory]
    [InlineData(OpCodes.Query102, OpCodes.Set202)]
    public void Apply_ToneVolumeQueryAndSetFrames(ushort queryOp, ushort setOp)
    {
        var state = new HeadsetState();

        Assert.True(state.Apply(Frame(queryOp, 0x05)));
        Assert.Equal(5, state.Query102);
        Assert.True(state.Apply(Frame(setOp, 0x07)));
        Assert.Equal(7, state.Query102);
    }

    [Fact]
    public void Apply_IgnoresFramesWithMissingParameters()
    {
        var state = new HeadsetState();

        Assert.False(state.Apply(Frame(OpCodes.Battery, 0x50)));
        Assert.False(state.Apply(Frame(OpCodes.AncQuery)));
        Assert.Equal(-1, state.BatteryLeft);
        Assert.Equal(-1, state.Anc);
    }

    [Fact]
    public void Apply_ConfigTableUpdatesOncePerChange()
    {
        var state = new HeadsetState();
        byte[] table = [0x01, 0x01, 0x01, 0x05, 0x02, 0x01, 0x02, 0x04];

        Assert.True(state.Apply(Frame(OpCodes.ConfigRead, table)));
        Assert.Equal(table, state.ConfigTable);
        Assert.False(state.Apply(Frame(OpCodes.ConfigRead, table)));
    }

    [Fact]
    public void Apply_UnknownFrameDoesNotChangeState()
    {
        var state = new HeadsetState();

        Assert.False(state.Apply(Frame(0x7FFF, 0x01, 0x02)));
    }

    private static bool Value(HeadsetState state, ushort queryOp) => queryOp switch
    {
        OpCodes.GameModeQuery => state.GameMode == true,
        OpCodes.LowLatencyQuery => state.LowLatency == true,
        OpCodes.DualConnQuery => state.DualConn == true,
        OpCodes.InEarQuery => state.InEar == true,
        OpCodes.WindQuery => state.Wind == true,
        _ => throw new ArgumentOutOfRangeException(nameof(queryOp)),
    };
}
