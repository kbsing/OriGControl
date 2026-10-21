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
    private readonly Func<CancellationToken, Task<IReadOnlyList<HeadsetServiceInfo>>> _findServices;
    private readonly StreamDecoder _decoder = new();

    /// <summary>RFCOMM 写操作不线程安全，所有下发命令都必须经过这把锁。</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>连接/断开互斥，避免两个流程同时操作同一个 socket 与读线程。</summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private CancellationTokenSource _cts = new();

    private Task? _pump;

    /// <summary>握手期间收到的有效帧数量。为 0 说明链路只是“看起来连上了”。</summary>
    private int _receivedFrames;

    public HeadsetState State { get; } = new();
    public SessionStatus Status { get; private set; } = SessionStatus.Disconnected;
    public string? LastError { get; private set; }
    public string DeviceName { get; private set; } = "";
    public string DeviceId { get; private set; } = "";

    /// <summary>上次成功连接的设备 ID；名称不匹配时优先选它。</summary>
    public string? PreferredDeviceId { get; set; }

    public event EventHandler? StateChanged;
    public event EventHandler? StatusChanged;

    public HeadsetSession(IHeadsetTransport transport) : this(transport, null)
    {
    }

    internal HeadsetSession(IHeadsetTransport transport,
        Func<CancellationToken, Task<IReadOnlyList<HeadsetServiceInfo>>>? findServices)
    {
        _transport = transport;
        _findServices = findServices ?? (ct => HeadsetServiceLocator.FindAsync(ct: ct));
    }

    /// <summary>握手/刷新时相邻 QUERY 的间隔，测试可缩短。</summary>
    internal TimeSpan QueryGap { get; init; } = TimeSpan.FromMilliseconds(110);

    /// <summary>最后一条握手查询后等待回帧的时间。</summary>
    internal TimeSpan HandshakeSettleDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    public static HeadsetSession CreateDefault() => new(new WinRtRfcommTransport());

    // ------------------------------------------------------------- 连接

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (Status is SessionStatus.Ready or SessionStatus.Handshaking)
            return true;

        await _lifecycleGate.WaitAsync(ct);
        try
        {
            if (Status is SessionStatus.Ready or SessionStatus.Handshaking)
                return true;

            // 先把上一次可能残留的读线程/链路彻底停掉，再建新会话。
            await StopSessionAsync();

            LastError = null;
            SetStatus(SessionStatus.Connecting);

            try
            {
                var services = await _findServices(ct);
                if (services.Count == 0)
                    return Fail("未找到耳机。请先在 Windows 设置里配对。");

                var service = HeadsetServiceLocator.PickTarget(services, PreferredDeviceId);
                if (service is null)
                    return Fail("找到多个候选设备，但名称都不匹配。");

                // 不能用 BluetoothDevice.ConnectionStatus 做硬性预检：耳机只连着手机、
                // 或 PC 侧 ACL 尚未建立时它可能报 false，但 RFCOMM 仍可正常连接。
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await _transport.ConnectAsync(service, connectTimeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await StopSessionAsync();
                    return Fail("连接超时。请确认耳机已开机、不在充电仓里且处于范围内。");
                }

                DeviceName = service.DisplayName;
                DeviceId = service.DeviceId;

                // 上一会话可能在半帧处断开，缓冲里的残字节不能带进新链路。
                _decoder.Reset();
                Volatile.Write(ref _receivedFrames, 0);

                SetStatus(SessionStatus.Handshaking);
                _pump = PumpAsync(_cts.Token);

                // 官方 App 的握手序列：16 条，110ms 间隔
                foreach (var op in OpCodes.Handshake)
                {
                    await _transport.WriteAsync(FrameCodec.Query(op), ct);
                    await Task.Delay(QueryGap, ct);
                }
                await Task.Delay(HandshakeSettleDelay, ct);

                // 读线程在握手期间已经退出，说明链路并未真正建立。
                if (_pump.IsCompleted)
                {
                    await StopSessionAsync();
                    return Fail("握手期间连接已断开");
                }

                // 一条回帧都没有 = 耳机没有响应，不能对外宣称“已连接”。
                if (Volatile.Read(ref _receivedFrames) == 0)
                {
                    await StopSessionAsync();
                    return Fail("耳机未响应握手查询（可能已关机、在充电仓里或不在范围内）");
                }

                SetStatus(SessionStatus.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                await StopSessionAsync();
                SetStatus(SessionStatus.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                await StopSessionAsync();

                // WinRT 有些异常 Message 是空的，退化成类型名 + HRESULT，否则界面只显示「连接失败：」
                string detail = string.IsNullOrWhiteSpace(ex.Message)
                    ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                    : ex.Message;
                return Fail($"连接失败：{detail}");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            await StopSessionAsync();
            SetStatus(SessionStatus.Disconnected);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 重新拉取一轮 UI 可见状态。用于面板打开时，以及设备可能被其它客户端修改后的兜底。
    /// 命令会经过写锁，不会与用户操作并发写 RFCOMM。
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (Status != SessionStatus.Ready)
            return;

        bool entered = false;
        try
        {
            await _writeGate.WaitAsync(ct);
            entered = true;

            foreach (ushort op in OpCodes.Refresh)
            {
                if (Status != SessionStatus.Ready)
                    return;

                await _transport.WriteAsync(FrameCodec.Query(op), ct);
                await Task.Delay(QueryGap, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 面板关闭或程序退出，忽略
        }
        catch (Exception ex)
        {
            Fail($"刷新状态失败：{ex.Message}");
        }
        finally
        {
            if (entered)
                _writeGate.Release();
        }
    }

    /// <summary>
    /// 停掉读线程并关闭链路。调用方必须持有 <see cref="_lifecycleGate"/>。
    /// 先 await 读线程再改状态，避免旧读线程在断开后把状态改回 Faulted。
    /// </summary>
    private async Task StopSessionAsync()
    {
        try { await _cts.CancelAsync(); } catch { /* 忽略 */ }

        if (_pump is not null)
        {
            try { await _pump; } catch { /* 忽略 */ }
            _pump = null;
        }

        try { await _transport.DisconnectAsync(); } catch { /* 忽略 */ }
        _decoder.Reset();

        _cts.Dispose();
        _cts = new CancellationTokenSource();
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

    private async Task PumpAsync(CancellationToken token)
    {
        var buffer = new byte[512];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await _transport.ReadAsync(buffer, token);
                if (read == 0)
                {
                    Fail("连接已被耳机断开");
                    return;
                }

                bool changed = false;
                foreach (var frame in _decoder.Feed(buffer.AsSpan(0, read)))
                {
                    Interlocked.Increment(ref _receivedFrames);
                    changed |= State.Apply(frame);
                }

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

    public Task<bool> SetAncAsync(byte mode, CancellationToken ct = default) =>
        SetAndVerifyAsync(FrameCodec.SetAnc(mode), OpCodes.AncQuery,
            () => State.Anc == mode, ct);

    /// <summary>
    /// 查找耳机。0x03A2 只下发、不回读：官方 App 也没有对应的查询指令。
    /// </summary>
    public async Task<bool> FindEarbudsAsync(bool on, CancellationToken ct = default)
    {
        if (Status != SessionStatus.Ready)
            return false;

        bool entered = false;
        try
        {
            await _writeGate.WaitAsync(ct);
            entered = true;

            await _transport.WriteAsync(FrameCodec.SetFindEarbuds(on), ct);
            await Task.Delay(20, ct);
            return true;
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
            if (entered)
                _writeGate.Release();
        }
    }

    public async Task<bool> SetEqAsync(byte preset, CancellationToken ct = default)
    {
        bool ok = await SetAndVerifyAsync(FrameCodec.SetEq(preset), OpCodes.EqQuery,
            () => State.Eq == preset, ct);

        // 协议规定 EQ=0x04 会联动打开游戏模式（docs/PROTOCOL-CAPTURED.md），
        // 设备不一定主动上报，这里显式同步一次，避免 UI 与设备不一致。
        if (ok && preset == EqPreset.Game && State.GameMode != true)
            await SetToggleAsync(Toggle.GameMode, true, ct);

        return ok;
    }

    public Task<bool> SetToneVolumeAsync(byte volume, CancellationToken ct = default)
    {
        if (volume > 16)
            return Task.FromResult(false);

        return SetAndVerifyAsync(FrameCodec.SetToneVolume(volume), OpCodes.Query102,
            () => State.Query102 == volume, ct);
    }

    public Task<bool> SetTouchActionAsync(byte side, byte gesture, byte action, byte enabled = 1,
        CancellationToken ct = default)
    {
        if (side is not (TouchSide.Left or TouchSide.Right)
            || !TouchGesture.All.Contains(gesture)
            || !TouchAction.All.Contains(action)
            || enabled is not (0 or 1))
        {
            return Task.FromResult(false);
        }

        return SetAndVerifyAsync(FrameCodec.SetTouchAction(side, gesture, action, enabled), OpCodes.ConfigRead,
            () => TouchConfig.Find(State.ConfigTable, side, gesture) is { } binding
                  && binding.Action == action
                  && binding.Enabled == enabled, ct);
    }

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

        bool entered = false;
        try
        {
            // 放在 try 内：取消时 WaitAsync 会抛异常，不能让调用方看到未处理异常。
            await _writeGate.WaitAsync(ct);
            entered = true;

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
            if (entered)
                _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        await _transport.DisposeAsync();

        // _writeGate / _lifecycleGate 不显式 Dispose：SemaphoreSlim 没有等待句柄时
        // 不释放也不会泄漏，反而可能与仍在执行的命令发生 ObjectDisposedException 竞态。
        _cts.Dispose();
    }
}
