namespace OriG.Core;

using OriG.Protocol;
using OriG.Transport;

public enum SessionStatus
{
    Disconnected,
    Connecting,
    Handshaking,
    Ready,
    Faulted,
}

/// <summary>耳机上的开关项。</summary>
public enum Toggle
{
    GameMode,
    LowLatency,
    DualConn,
    InEar,
    Wind,
}

/// <summary>
/// 耳机会话：负责连接、读线程、命令串行化、SET 后回读校验。
/// UI 只需订阅 <see cref="StateChanged"/> 并调用 Set* 方法。
/// </summary>
public sealed class HeadsetSession : IAsyncDisposable
{
    private readonly IHeadsetTransport _transport;
    private readonly StreamDecoder _decoder = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private Task? _pump;
    private HeadsetServiceInfo? _service;

    public HeadsetState State { get; } = new();
    public SessionStatus Status { get; private set; } = SessionStatus.Disconnected;
    public string? LastError { get; private set; }
    public string DeviceName { get; private set; } = "";

    public event EventHandler? StateChanged;
    public event EventHandler? StatusChanged;

    public HeadsetSession(IHeadsetTransport transport) => _transport = transport;

    public static HeadsetSession CreateDefault() => new(new WinRtRfcommTransport());

    // ------------------------------------------------------------- 连接

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (Status is SessionStatus.Ready or SessionStatus.Handshaking)
            return true;

        SetStatus(SessionStatus.Connecting);

        try
        {
            var services = await HeadsetServiceLocator.FindAsync(ct: ct);
            if (services.Count == 0)
                return Fail("未找到耳机。请先在 Windows 设置里配对。");

            _service = HeadsetServiceLocator.PickTarget(services);
            if (_service is null)
                return Fail("找到多个候选设备，但名称都不匹配。");

            // 预检：设备没连上时 WinRT 会卡十几秒才超时，先给个明确提示
            var paired = await HeadsetServiceLocator.ListPairedAsync(ct);
            var match = paired.FirstOrDefault(p =>
                string.Equals(p.Name, _service.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !match.Connected)
                return Fail("耳机未连接（可能已关机、在充电仓里或不在范围内）");

            await _transport.ConnectAsync(_service, ct);
            DeviceName = _service.DisplayName;

            SetStatus(SessionStatus.Handshaking);
            _pump = PumpAsync();

            // 官方 App 的握手序列：16 条，110ms 间隔
            foreach (var op in OpCodes.Handshake)
            {
                await _transport.WriteAsync(FrameCodec.Query(op), ct);
                await Task.Delay(110, ct);
            }
            await Task.Delay(300, ct);

            SetStatus(SessionStatus.Ready);
            return true;
        }
        catch (OperationCanceledException)
        {
            return Fail("已取消");
        }
        catch (Exception ex)
        {
            // WinRT 有些异常 Message 是空的，退化成类型名 + HRESULT，否则界面只显示「连接失败：」
            string detail = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                : ex.Message;
            return Fail($"连接失败：{detail}");
        }
    }

    public async Task DisconnectAsync()
    {
        await _cts.CancelAsync();
        if (_pump is not null)
        {
            try { await _pump; } catch { /* 忽略 */ }
            _pump = null;
        }
        await _transport.DisconnectAsync();
        SetStatus(SessionStatus.Disconnected);
    }

    private bool Fail(string message)
    {
        LastError = message;
        SetStatus(SessionStatus.Faulted);
        return false;
    }

    private void SetStatus(SessionStatus status)
    {
        if (Status == status)
            return;
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- 读线程

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
                    Fail("连接已被耳机断开");
                    return;
                }

                bool changed = false;
                foreach (var frame in _decoder.Feed(buffer.AsSpan(0, read)))
                    changed |= State.Apply(frame);

                if (changed)
                    StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            Fail($"链路异常：{ex.Message}");
        }
    }

    // ------------------------------------------------------------- 命令

    /// <summary>刷新全部状态（面板打开时调用）。</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (Status != SessionStatus.Ready)
            return;
        foreach (var op in OpCodes.Handshake)
        {
            await _transport.WriteAsync(FrameCodec.Query(op), ct);
            await Task.Delay(60, ct);
        }
    }

    public Task<bool> SetAncAsync(byte mode, CancellationToken ct = default) =>
        SetAndVerifyAsync(FrameCodec.SetAnc(mode), OpCodes.AncQuery,
            () => State.Anc == mode, ct);

    public Task<bool> SetEqAsync(byte preset, CancellationToken ct = default) =>
        SetAndVerifyAsync(FrameCodec.SetEq(preset), OpCodes.EqQuery,
            () => State.Eq == preset, ct);

    public Task<bool> SetToggleAsync(Toggle toggle, bool on, CancellationToken ct = default)
    {
        (ushort setOp, ushort queryOp) = toggle switch
        {
            Toggle.GameMode => (OpCodes.GameModeSet, OpCodes.GameModeQuery),
            Toggle.LowLatency => (OpCodes.LowLatencySet, OpCodes.LowLatencyQuery),
            Toggle.DualConn => (OpCodes.DualConnSet, OpCodes.DualConnQuery),
            Toggle.InEar => (OpCodes.InEarSet, OpCodes.InEarQuery),
            Toggle.Wind => (OpCodes.WindSet, OpCodes.WindQuery),
            _ => (OpCodes.WindSet, OpCodes.WindQuery),
        };

        bool Current() => toggle switch
        {
            Toggle.GameMode => State.GameMode == on,
            Toggle.LowLatency => State.LowLatency == on,
            Toggle.DualConn => State.DualConn == on,
            Toggle.InEar => State.InEar == on,
            Toggle.Wind => State.Wind == on,
            _ => false,
        };

        var frame = FrameCodec.Build(setOp, (byte)(on ? 1 : 0));
        return SetAndVerifyAsync(frame, queryOp, Current, ct);
    }

    /// <summary>下发 SET，20ms 后紧跟 QUERY 回读（官方 App 的做法），最多等 700ms。</summary>
    private async Task<bool> SetAndVerifyAsync(byte[] setFrame, ushort queryOp,
        Func<bool> satisfied, CancellationToken ct)
    {
        if (Status != SessionStatus.Ready)
            return false;

        await _writeGate.WaitAsync(ct);
        try
        {
            await _transport.WriteAsync(setFrame, ct);
            await Task.Delay(20, ct);
            await _transport.WriteAsync(FrameCodec.Query(queryOp), ct);

            var deadline = DateTime.UtcNow.AddMilliseconds(700);
            while (DateTime.UtcNow < deadline)
            {
                if (satisfied())
                    return true;
                await Task.Delay(40, ct);
            }
            return satisfied();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Fail($"下发失败：{ex.Message}");
            return false;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        await _transport.DisposeAsync();
        _writeGate.Dispose();
        _cts.Dispose();
    }
}
