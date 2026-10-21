namespace OriG.Protocol.Tests;

public class StreamDecoderTests
{
    [Fact]
    public void Feed_SingleCompleteFrame()
    {
        var decoder = new StreamDecoder();
        byte[] bytes = FrameCodec.Build(OpCodes.AncQuery, 0x02);

        var frames = decoder.Feed(bytes);

        Assert.Single(frames);
        Assert.Equal(OpCodes.AncQuery, frames[0].OpCode);
        Assert.Equal(new byte[] { 0x02 }, frames[0].Parameters);
    }

    [Fact]
    public void Feed_TwoFramesInOneChunk()
    {
        var decoder = new StreamDecoder();
        byte[] bytes =
        [
            .. FrameCodec.Build(OpCodes.Battery, 0x3C, 0x3A, 0x00),
            .. FrameCodec.Build(OpCodes.Firmware, 0x08, 0x04),
        ];

        var frames = decoder.Feed(bytes);

        Assert.Equal(2, frames.Count);
        Assert.Equal(OpCodes.Battery, frames[0].OpCode);
        Assert.Equal(OpCodes.Firmware, frames[1].OpCode);
    }

    [Fact]
    public void Feed_SplitFrameWaitsForRest()
    {
        var decoder = new StreamDecoder();
        byte[] bytes = FrameCodec.Build(OpCodes.Battery, 0x3C, 0x3A, 0x00);

        Assert.Empty(decoder.Feed(bytes.AsSpan(0, 4)));
        var frames = decoder.Feed(bytes.AsSpan(4));

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x3C, 0x3A, 0x00 }, frames[0].Parameters);
    }

    [Fact]
    public void Feed_SkipsGarbageBeforeMagic()
    {
        var decoder = new StreamDecoder();
        byte[] bytes = [0xFF, 0x00, 0x13, .. FrameCodec.Build(OpCodes.EqQuery, 0x05)];

        var frames = decoder.Feed(bytes);

        Assert.Single(frames);
        Assert.Equal(OpCodes.EqQuery, frames[0].OpCode);
    }

    [Fact]
    public void Feed_InvalidLengthIsSkipped()
    {
        var decoder = new StreamDecoder();
        byte[] bytes =
        [
            0x4E, 0x00, 0x00, 0x00, 0x01, 0x00,
            .. FrameCodec.Build(OpCodes.AncQuery, 0x01),
        ];

        var frames = decoder.Feed(bytes);

        Assert.Single(frames);
        Assert.Equal(OpCodes.AncQuery, frames[0].OpCode);
    }

    [Fact]
    public void Feed_EmptyOrSingleByteDoesNotThrow()
    {
        var decoder = new StreamDecoder();

        Assert.Empty(decoder.Feed([]));
        Assert.Empty(decoder.Feed([0x4E]));
    }

    [Fact]
    public void Reset_DropsBufferedPartialFrame()
    {
        var decoder = new StreamDecoder();
        byte[] bytes = FrameCodec.Build(OpCodes.Battery, 0x3C, 0x3A, 0x00);

        Assert.Empty(decoder.Feed(bytes.AsSpan(0, 4)));
        decoder.Reset();

        Assert.Empty(decoder.Feed(bytes.AsSpan(4)));
    }
}
