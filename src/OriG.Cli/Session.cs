namespace OriG.Cli;

using OriG.Protocol;
using OriG.Transport;

/// <summary>一次连接的会话：后台读线程 + 状态聚合 + 帧记录。</summary>
internal sealed class Session : IAsyncDisposable
{
    private readonly IHeadsetTransport _transport;
    private readonly StreamDecoder _decoder = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<(DateTimeOffset At, Frame Frame)> _frames = [];
    private readonly Lock _gate = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private Task? _pump;

    public HeadsetState State { get; } = new();
    public bool Echo { get; set; }
    public bool TraceRaw { get; set; }

    /// <summary>
    /// 折叠重复帧。实测 PC 链路上耳机对同一请求会连发 2–4 次完全相同的回帧
    /// （协议无 ACK，固件做了冗余重发），手机链路上只有 1 次。
    /// 状态应用是幂等的，重复帧只影响可读性，故默认折叠。
    /// </summary>
    public bool Dedupe { get; set; } = true;

    private const int DedupeWindowMs = 250;
    private (ushort Op, string Params, DateTimeOffset At)? _lastFrame;

    /// <summary>解析出的帧总数（不折叠重复）。</summary>
    public int RawFrameCount { get; private set; }

    public bool Closed { get; private set; }
    public Exception? Failure { get; private set; }

    private Session(IHeadsetTransport transport) => _transport = transport;

    public static async Task<Session> OpenAsync(HeadsetServiceInfo service, bool echo)
    {
        var transport = new WinRtRfcommTransport();
        await transport.ConnectAsync(service);
        var session = new Session(transport) { Echo = echo };
        session._pump = session.PumpAsync();
        return session;
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[512];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int read = await _transport.ReadAsync(buffer, _cts.Token);
                if (read == 0)
                {
                    Closed = true;
                    return;
                }

                if (TraceRaw)
                    Console.WriteLine($"  RAW({read}) @{_clock.ElapsedMilliseconds,5}ms  {FrameCodec.Hex(buffer.AsSpan(0, read))}");

                foreach (var frame in _decoder.Feed(buffer.AsSpan(0, read)))
                {
                    State.Apply(frame);
                    RawFrameCount++;

                    bool duplicate = false;
                    if (Dedupe)
                    {
                        string parameters = Convert.ToHexString(frame.Parameters);
                        var now = DateTimeOffset.Now;
                        duplicate = _lastFrame is { } last
                                    && last.Op == frame.OpCode
                                    && last.Params == parameters
                                    && (now - last.At).TotalMilliseconds < DedupeWindowMs;
                        _lastFrame = (frame.OpCode, parameters, now);
                    }
                    if (duplicate)
                        continue;

                    lock (_gate)
                        _frames.Add((DateTimeOffset.Now, frame));

                    if (Echo)
                    {
                        var (name, extra) = Describe(frame);
                        Console.WriteLine($"  RX  0x{frame.OpCode:X4} {name,-12} {extra}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            Failure = ex;
            Closed = true;
        }
    }

    public async Task SendAsync(byte[] frame, string? note = null)
    {
        if (Echo)
        {
            int op = frame.Length >= 6 ? (frame[4] | (frame[5] << 8)) : 0;
            Console.WriteLine($"  TX  0x{op:X4} {note ?? "",-12} {FrameCodec.Hex(frame)}");
        }
        await _transport.WriteAsync(frame, _cts.Token);
    }

    public async Task SendQueryAsync(ushort op) => await SendAsync(FrameCodec.Query(op));

    public IReadOnlyList<(DateTimeOffset At, Frame Frame)> Snapshot()
    {
        lock (_gate)
            return _frames.ToArray();
    }

    public void ClearFrames()
    {
        lock (_gate)
            _frames.Clear();
        _lastFrame = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_pump is not null)
        {
            try { await _pump; } catch { /* 已记录在 Failure */ }
        }
        await _transport.DisposeAsync();
        _cts.Dispose();
    }

    public static (string Name, string Extra) Describe(Frame f)
    {
        string name = OpCodes.Name(f.OpCode);
        string extra = f.OpCode switch
        {
            OpCodes.Battery => $"L={f[0]}%  R={f[1]}%  仓={(f[2] > 0 ? "在仓" : "不在仓")}",
            OpCodes.Firmware => $"固件 {f[1]}.{f[0]:00}",
            OpCodes.AncQuery or OpCodes.AncSet => AncMode.Name(f[0]),
            OpCodes.EqQuery or OpCodes.EqSet => EqPreset.Name(f[0]),
            OpCodes.FindEarbudsSet => f.IsOn(0) ? "开始响铃" : "停止响铃",
            OpCodes.Query102 or OpCodes.Set202 => $"提示音音量 {f[0]}",
            OpCodes.ConfigRead => string.Join("；", TouchConfig.Parse(f.Parameters).Select(b =>
                $"{TouchSide.Name(b.Side)}{TouchGesture.Name(b.Gesture)}={TouchAction.Name(b.Action)}")),
            OpCodes.ConfigWrite => f.Parameters.Length >= 4
                ? $"{TouchSide.Name(f[0])}{TouchGesture.Name(f[2])}={TouchAction.Name(f[3])}"
                : $"ACK {FrameCodec.Hex(f.Parameters)}",
            OpCodes.Query104 => CodecProtocol.Describe(f[0]),
            OpCodes.Query10A => TouchConfig.AncCycleText(f.Parameters),
            OpCodes.GameModeQuery or OpCodes.GameModeSet
                or OpCodes.LowLatencyQuery or OpCodes.LowLatencySet
                or OpCodes.DualConnQuery or OpCodes.DualConnSet
                or OpCodes.InEarQuery or OpCodes.InEarSet
                or OpCodes.WindQuery or OpCodes.WindSet => f.IsOn(0) ? "开" : "关",
            // 事件负载语义未解，原样打印，避免越界或把未知字段当成已知值。
            OpCodes.Event204 => $"seq={f[0]}  数据={FrameCodec.Hex(f.Parameters)}",
            _ => FrameCodec.Hex(f.Parameters),
        };
        return (name, extra);
    }
}
