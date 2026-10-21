using System.Diagnostics;
using OriG.Protocol;
using OriG.Transport;

namespace OriG.Core.Tests;

public class HeadsetSessionTests
{
    [Fact]
    public async Task Connect_WithoutAnyResponse_DoesNotReportReady()
    {
        var transport = new FakeTransport();
        await using var session = CreateSession(transport, gapMs: 1, settleMs: 20);

        bool ok = await session.ConnectAsync();

        Assert.False(ok);
        Assert.Equal(SessionStatus.Faulted, session.Status);
        Assert.Contains("未响应", session.LastError);
    }

    [Fact]
    public async Task Connect_WithBatteryResponse_BecomesReady()
    {
        var transport = new FakeTransport { Responder = BatteryOnly };
        await using var session = CreateSession(transport, gapMs: 1, settleMs: 50);

        bool ok = await session.ConnectAsync();

        Assert.Equal(SessionStatus.Ready, session.Status);
        Assert.True(ok);
        Assert.Equal(60, session.State.BatteryLeft);
        Assert.Equal(58, session.State.BatteryRight);
        Assert.True(session.State.CasePresent);
        Assert.Equal("YUANDAO OriG in", session.DeviceName);

        await session.DisconnectAsync();
        Assert.Equal(SessionStatus.Disconnected, session.Status);
    }

    [Fact]
    public async Task Refresh_SendsOnlyTheUiQueries()
    {
        var transport = new FakeTransport { Responder = BatteryOnly };
        await using var session = CreateSession(transport, gapMs: 1, settleMs: 50);
        Assert.True(await session.ConnectAsync());

        int before = transport.Writes.Count;
        await session.RefreshAsync();

        ushort[] sent = transport.Writes
            .Skip(before)
            .Select(bytes => FrameCodec.TryParse(bytes, 0, out Frame frame, out _) ? frame.OpCode : (ushort)0)
            .ToArray();

        Assert.Equal(OpCodes.Refresh, sent);
    }

    [Fact]
    public async Task SetAnc_AcceptsSetEchoWithoutFollowUpQuery()
    {
        var transport = new FakeTransport { Responder = BatteryOnly };
        await using var session = CreateSession(transport, gapMs: 1, settleMs: 50);
        Assert.True(await session.ConnectAsync());

        // 只回显 SET，不回答后面的 AncQuery。状态仍应更新，验证 0x0201 广播处理。
        transport.Responder = EchoAncSet;

        bool ok = await session.SetAncAsync(AncMode.Deep);

        Assert.True(ok);
        Assert.Equal(AncMode.Deep, session.State.Anc);
    }

    [Fact]
    public async Task Pump_AppliesSetBroadcastFromOtherClients()
    {
        var transport = new FakeTransport { Responder = BatteryOnly };
        await using var session = CreateSession(transport, gapMs: 1, settleMs: 50);
        Assert.True(await session.ConnectAsync());

        transport.Push(FrameCodec.Build(OpCodes.EqSet, EqPreset.Bass));

        Assert.True(await WaitUntilAsync(() => session.State.Eq == EqPreset.Bass));
    }

    private static HeadsetSession CreateSession(FakeTransport transport, int gapMs, int settleMs)
    {
        var service = new HeadsetServiceInfo(
            "device-id", "YUANDAO OriG in", "00:11:22:33:44:55", "rfcomm", default);

        return new HeadsetSession(transport,
            _ => Task.FromResult<IReadOnlyList<HeadsetServiceInfo>>([service]))
        {
            QueryGap = TimeSpan.FromMilliseconds(gapMs),
            HandshakeSettleDelay = TimeSpan.FromMilliseconds(settleMs),
        };
    }

    private static IReadOnlyList<byte[]> BatteryOnly(byte[] request)
    {
        return IsOp(request, OpCodes.Battery)
            ? [FrameCodec.Build(OpCodes.Battery, 60, 58, 100)]
            : [];
    }

    private static IReadOnlyList<byte[]> EchoAncSet(byte[] request)
    {
        return IsOp(request, OpCodes.AncSet) ? [request] : [];
    }

    private static bool IsOp(byte[] request, ushort op) =>
        FrameCodec.TryParse(request, 0, out Frame frame, out _) && frame.OpCode == op;

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return condition();
    }
}
