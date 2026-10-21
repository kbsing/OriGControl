namespace OriG.Protocol.Tests;

public class FrameCodecTests
{
    [Fact]
    public void Build_Parse_RoundTrips()
    {
        byte[] bytes = FrameCodec.Build(OpCodes.AncSet, 0x03, 0x00);

        Assert.True(FrameCodec.TryParse(bytes, 0, out Frame frame, out int consumed));
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(OpCodes.AncSet, frame.OpCode);
        Assert.Equal(new byte[] { 0x03, 0x00 }, frame.Parameters);
    }

    [Fact]
    public void Build_UsesLittleEndianLengthAndOpcode()
    {
        byte[] bytes = FrameCodec.Query(OpCodes.Query10A);

        Assert.Equal(0x4E, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(0x00, bytes[3]);
        Assert.Equal(0x0A, bytes[4]);
        Assert.Equal(0x01, bytes[5]);
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public void Parse_LongFrameWithHighLengthByte()
    {
        var parameters = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        byte[] bytes = FrameCodec.Build(0x1234, parameters);

        Assert.True(FrameCodec.TryParse(bytes, 0, out Frame frame, out int consumed));
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal((ushort)0x1234, frame.OpCode);
        Assert.Equal(parameters, frame.Parameters);
    }

    [Fact]
    public void Parse_IncompleteFrameReturnsFalse()
    {
        byte[] bytes = FrameCodec.Build(OpCodes.Battery, 0x01, 0x02, 0x03);

        Assert.False(FrameCodec.TryParse(bytes.AsSpan(0, bytes.Length - 1), 0, out _, out int consumed));
        Assert.Equal(0, consumed);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x03, 0x00, 0x00, 0x05, 0x00 })]
    [InlineData(new byte[] { 0x4E, 0x03, 0x00, 0x01, 0x05, 0x00 })]
    [InlineData(new byte[] { 0x4E, 0x01, 0x00, 0x00, 0x05, 0x00 })]
    [InlineData(new byte[] { 0x4E, 0x00, 0x10, 0x00, 0x05, 0x00 })]
    public void Parse_RejectsInvalidHeaders(byte[] bytes)
    {
        Assert.False(FrameCodec.TryParse(bytes, 0, out _, out _));
    }

    [Fact]
    public void FrameIndex_ReturnsMinusOneWhenMissing()
    {
        var frame = new Frame(OpCodes.Battery, [0x3C]);

        Assert.Equal(0x3C, frame[0]);
        Assert.Equal(-1, frame[1]);
        Assert.False(frame.IsOn(1));
    }

    [Fact]
    public void Hex_FormatsBytes()
    {
        Assert.Equal("4E 03 00", FrameCodec.Hex([0x4E, 0x03, 0x00]));
        Assert.Equal(string.Empty, FrameCodec.Hex([]));
    }
}
