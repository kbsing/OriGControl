namespace OriG.Protocol;

/// <summary>
/// 流式解帧器：处理 TCP/RFCOMM 的分段到达与粘包。
/// 用 List&lt;byte&gt; 做缓冲，跳过非法字节，遇到半帧则保留等待后续数据。
/// </summary>
public sealed class StreamDecoder
{
    private readonly List<byte> _buffer = new(256);

    public IReadOnlyList<Frame> Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            _buffer.Add(b);

        var frames = new List<Frame>(4);
        int pos = 0;

        while (pos < _buffer.Count)
        {
            // 找 magic
            if (_buffer[pos] != FrameCodec.Magic)
            {
                pos++;
                continue;
            }

            if (pos + 3 > _buffer.Count)
                break;                                   // 头部不完整

            int length = _buffer[pos + 1] | (_buffer[pos + 2] << 8);
            int total = length + 3;

            if (total < FrameCodec.HeaderSize || total > FrameCodec.MaxFrameSize)
            {
                pos++;                                   // 长度非法，当垃圾字节跳过
                continue;
            }

            if (pos + total > _buffer.Count)
                break;                                   // 半帧，等更多数据

            if (_buffer[pos + 3] != 0x00)
            {
                pos++;                                   // reserved 非 0，不是有效帧
                continue;
            }

            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer)[pos..];
            if (FrameCodec.TryParse(span, 0, out var frame, out int consumed) && consumed == total)
                frames.Add(frame);

            pos += total;
        }

        if (pos > 0)
            _buffer.RemoveRange(0, pos);

        return frames;
    }

    public void Reset() => _buffer.Clear();
}
