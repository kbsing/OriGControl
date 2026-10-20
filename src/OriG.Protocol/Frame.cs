namespace OriG.Protocol;

using System.Text;

/// <summary>一个已解析的协议帧。</summary>
public readonly record struct Frame(ushort OpCode, byte[] Parameters, int TotalLength)
{
    /// <summary>取参数第 i 字节；越界返回 -1（便于把「缺失」与「值为 0」区分开）。</summary>
    public int this[int i] => i >= 0 && i < Parameters.Length ? Parameters[i] : -1;

    public bool IsOn(int i) => this[i] == 0x01;

    public string Hex => Convert.ToHexString(Parameters);
}

/// <summary>
/// 帧编解码。
/// 帧格式：[0]=0x4E magic | [1..2]=length(LE) | [3]=0x00 reserved | [4..5]=opcode(LE) | [6..]=params
/// 其中 length = 3 + params.Length，总长度 = 3 + length = 6 + params.Length。
/// </summary>
public static class FrameCodec
{
    public const byte Magic = 0x4E;
    public const int HeaderSize = 6;
    public const int MaxFrameSize = 4096;

    public static byte[] Build(ushort opCode, params byte[] parameters)
    {
        int payloadLength = 3 + parameters.Length;
        var buffer = new byte[3 + payloadLength];
        buffer[0] = Magic;
        buffer[1] = (byte)(payloadLength & 0xFF);
        buffer[2] = (byte)((payloadLength >> 8) & 0xFF);
        buffer[3] = 0x00;                       // reserved，必须为 0
        buffer[4] = (byte)(opCode & 0xFF);
        buffer[5] = (byte)((opCode >> 8) & 0xFF);
        parameters.CopyTo(buffer, HeaderSize);
        return buffer;
    }

    public static byte[] Query(ushort opCode) => Build(opCode);

    public static byte[] SetAnc(byte mode) => Build(OpCodes.AncSet, mode, 0x00);
    public static byte[] SetEq(byte preset) => Build(OpCodes.EqSet, preset);
    public static byte[] SetWind(bool on) => Build(OpCodes.WindSet, (byte)(on ? 1 : 0));
    public static byte[] SetLowLatency(bool on) => Build(OpCodes.LowLatencySet, (byte)(on ? 1 : 0));

    /// <summary>从 buffer[offset] 起解析一帧。成功返回 true 并给出消耗字节数。</summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, int offset, out Frame frame, out int consumed)
    {
        frame = default;
        consumed = 0;

        if (offset < 0 || offset + HeaderSize > buffer.Length)
            return false;
        if (buffer[offset] != Magic || buffer[offset + 3] != 0x00)
            return false;

        int length = buffer[offset + 1] | (buffer[offset + 2] << 8);
        int total = length + 3;
        if (total < HeaderSize || total > MaxFrameSize)
            return false;
        if (offset + total > buffer.Length)
            return false;

        int paramCount = total - HeaderSize;
        var parameters = new byte[paramCount];
        buffer.Slice(offset + HeaderSize, paramCount).CopyTo(parameters);

        int opCode = buffer[offset + 4] | (buffer[offset + 5] << 8);
        frame = new Frame((ushort)opCode, parameters, total);
        consumed = total;
        return true;
    }

    public static string Hex(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return string.Empty;
        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
