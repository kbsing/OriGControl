namespace OriG.Cli;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using OriG.Protocol;
using OriG.Transport;

internal static class App
{
    /// <summary>握手间隔：官方 App 实测 105–113ms。</summary>
    private static readonly TimeSpan HandshakeGap = TimeSpan.FromMilliseconds(110);

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Usage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => await ScanAsync(),
                "probe" => await ProbeAsync(),
                "query" => await QueryAsync(args),
                "set" => await SetAsync(args),
                "monitor" => await MonitorAsync(args),
                "watch" => await WatchAsync(args),
                "raw" => await RawAsync(args),
                "-h" or "--help" or "help" => UsageAndOk(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ 失败：{ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    // ------------------------------------------------------------------ scan

    private static async Task<int> ScanAsync()
    {
        Console.WriteLine("已配对的经典蓝牙设备：");
        var paired = await HeadsetServiceLocator.ListPairedAsync();
        if (paired.Count == 0)
            Console.WriteLine("  （无）");
        foreach (var p in paired)
            Console.WriteLine($"  {(p.Connected ? "●" : "○")} {p.Name,-30} {p.AddressText}");

        Console.WriteLine();
        Console.WriteLine($"查找 NHCKCTRL 服务（{HeadsetServiceLocator.ServiceUuid}）：");
        var services = await HeadsetServiceLocator.FindAsync();
        if (services.Count == 0)
        {
            Console.WriteLine("  ✗ 未找到。可能原因：");
            Console.WriteLine("    1. 耳机未配对 → 先在 Windows 设置里配对");
            Console.WriteLine("    2. 耳机已被手机占用");
            Console.WriteLine("    3. 蓝牙适配器异常");
            return 1;
        }

        foreach (var s in services)
        {
            var channel = await SdpChannelReader.GetRfcommChannelAsync(s.DeviceId);
            Console.WriteLine($"  ✓ {s.DisplayName}");
            Console.WriteLine($"      Host     = {s.HostName}");
            Console.WriteLine($"      保护级别  = {s.ProtectionLevel}");
            Console.WriteLine($"      通道号    = {(channel is null ? "读取失败" : channel.Value.ToString(CultureInfo.InvariantCulture))}");
        }
        return 0;
    }

    // ----------------------------------------------------------------- probe

    private static async Task<int> ProbeAsync()
    {
        Console.WriteLine("=== 1. 定位服务 ===");
        var services = await HeadsetServiceLocator.FindAsync();
        if (services.Count == 0)
        {
            Console.WriteLine("✗ 未找到 NHCKCTRL 服务，先跑 `origctl scan` 排查。");
            return 1;
        }
        foreach (var s in services)
            Console.WriteLine($"  · {s.DisplayName}  ({s.HostName})");

        var target = HeadsetServiceLocator.PickTarget(services);
        if (target is null)
        {
            Console.WriteLine("✗ 有多个候选但名称都不匹配，无法自动选择。");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== 2. 读取 SDP 通道号（A1）===");
        var channel = await SdpChannelReader.GetRfcommChannelAsync(target.DeviceId);
        Console.WriteLine(channel is null
            ? "  ⚠ 未能从 SDP 解析出通道号（不影响 T1 连接）"
            : $"  ✓ RFCOMM 通道号 = {channel}");

        Console.WriteLine();
        Console.WriteLine("=== 3. 建立 RFCOMM 连接 ===");
        var connectTimer = Stopwatch.StartNew();

        await using (var session = await Session.OpenAsync(target, echo: true))
        {
            connectTimer.Stop();
            Console.WriteLine($"  ✓ 已连接，耗时 {connectTimer.ElapsedMilliseconds} ms");

            Console.WriteLine();
            Console.WriteLine("=== 4. 握手（16 条 QUERY，间隔 110ms）===");
            var handshakeTimer = Stopwatch.StartNew();
            foreach (var op in OpCodes.Handshake)
            {
                await session.SendQueryAsync(op);
                await Task.Delay(HandshakeGap);
            }
            await Task.Delay(400);
            handshakeTimer.Stop();
            Console.WriteLine($"  ✓ 握手完成，耗时 {handshakeTimer.ElapsedMilliseconds} ms");

            Console.WriteLine();
            Console.WriteLine("=== 5. 状态汇总 ===");
            var st = session.State;
            Console.WriteLine($"  固件       {st.FirmwareText}");
            Console.WriteLine($"  电量       {st.BatteryText}");
            Console.WriteLine($"  降噪       {st.AncText}");
            Console.WriteLine($"  均衡器     {st.EqText}");
            Console.WriteLine($"  游戏模式   {HeadsetState.Tri(st.GameMode)}");
            Console.WriteLine($"  低延迟     {HeadsetState.Tri(st.LowLatency)}");
            Console.WriteLine($"  双设备     {HeadsetState.Tri(st.DualConn)}");
            Console.WriteLine($"  入耳检测   {HeadsetState.Tri(st.InEar)}");
            Console.WriteLine($"  抗风噪     {HeadsetState.Tri(st.Wind)}");
            Console.WriteLine($"  查询0x0102 {st.Query102?.ToString(CultureInfo.InvariantCulture) ?? "无"}");
            Console.WriteLine($"  查询0x0104 {st.Query104?.ToString(CultureInfo.InvariantCulture) ?? "无"}");
            Console.WriteLine($"  查询1      {(st.Query1 is null ? "无" : FrameCodec.Hex(st.Query1))}");
            Console.WriteLine($"  查询4      {(st.Query4 is null ? "无" : FrameCodec.Hex(st.Query4))}");
            Console.WriteLine($"  查询6      {(st.Query6 is null ? "无" : FrameCodec.Hex(st.Query6))}");
            Console.WriteLine($"  查询0x010A {(st.Query10A is null ? "无" : FrameCodec.Hex(st.Query10A))}");
            Console.WriteLine($"  配置表     {(st.ConfigTable is null ? "无" : FrameCodec.Hex(st.ConfigTable))}");

            Console.WriteLine();
            Console.WriteLine("=== 6. 帧统计（已折叠重复）===");
            var frames = session.Snapshot();
            Console.WriteLine($"  去重后 {frames.Count} 帧，原始 {session.RawFrameCount} 帧");
            foreach (var g in frames.GroupBy(f => f.Frame.OpCode).OrderBy(g => g.Key))
                Console.WriteLine($"    0x{g.Key:X4} {OpCodes.Name(g.Key),-12} ×{g.Count()}");

            Console.WriteLine();
            Console.WriteLine("=== 7. 连续发送稳定性（20 条电量查询，间隔 60ms）===");
            session.Dedupe = false;
            session.Echo = false;
            int before = session.RawFrameCount;
            for (int i = 0; i < 20; i++)
            {
                await session.SendQueryAsync(OpCodes.Battery);
                await Task.Delay(60);
            }
            await Task.Delay(600);
            int rawReplies = session.RawFrameCount - before;
            Console.WriteLine($"  原始回帧 {rawReplies} 条 / 20 次查询 = 冗余系数 {rawReplies / 20.0:F1}×");
            Console.WriteLine($"  链路状态：{(session.Closed ? "✗ 已断开" : "✓ 仍连接")}");
            if (rawReplies < 20)
                Console.WriteLine("  ⚠ 有查询未获响应，需关注");
        }

        Console.WriteLine();
        Console.WriteLine("=== 8. 重连冷却测试（A7）===");
        for (int i = 1; i <= 3; i++)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                await using var session = await Session.OpenAsync(target, echo: false);
                await session.SendQueryAsync(OpCodes.Battery);
                await Task.Delay(300);
                timer.Stop();
                bool gotReply = session.RawFrameCount > 0;
                Console.WriteLine($"  第 {i} 次：{(gotReply ? "✓ 连接并收到回帧" : "⚠ 连上但无回帧")}  耗时 {timer.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                timer.Stop();
                Console.WriteLine($"  第 {i} 次：✗ 失败（{ex.Message}）  耗时 {timer.ElapsedMilliseconds} ms");
            }
            await Task.Delay(300);
        }

        return 0;
    }

    // ----------------------------------------------------------------- query

    private static async Task<int> QueryAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法：origctl query <opcode十六进制>   例：origctl query 0005");
            return 1;
        }
        if (!TryParseOpcode(args[1], out ushort op))
        {
            Console.WriteLine($"✗ 无法解析 opcode：{args[1]}");
            return 1;
        }

        await using var session = await OpenTargetAsync(echo: true);
        if (session is null)
            return 1;

        session.TraceRaw = true;
        session.ClearFrames();
        await session.SendQueryAsync(op);
        await Task.Delay(500);

        var replies = session.Snapshot().Where(f => f.Frame.OpCode == op).ToArray();
        Console.WriteLine(replies.Length == 0
            ? "  （无回帧）"
            : $"  收到 {replies.Length} 条回帧（已去重，原始 {session.RawFrameCount} 帧）");
        return replies.Length > 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------- set

    private static async Task<int> SetAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("用法：origctl set anc <0|1|2|3|16|17>");
            Console.WriteLine("      origctl set eq  <0..6>");
            return 1;
        }

        string what = args[1].ToLowerInvariant();
        if (!byte.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value))
        {
            Console.WriteLine($"✗ 无法解析数值：{args[2]}");
            return 1;
        }

        await using var session = await OpenTargetAsync(echo: true);
        if (session is null)
            return 1;

        byte[] frame;
        ushort verifyOp;
        switch (what)
        {
            case "anc":
                frame = FrameCodec.SetAnc(value);
                verifyOp = OpCodes.AncQuery;
                break;
            case "eq":
                frame = FrameCodec.SetEq(value);
                verifyOp = OpCodes.EqQuery;
                break;
            default:
                Console.WriteLine($"✗ 未知目标：{what}（支持 anc / eq）");
                return 1;
        }

        session.ClearFrames();
        await session.SendAsync(frame, $"设{what}");
        await Task.Delay(20);
        await session.SendQueryAsync(verifyOp);      // 背靠背回读，官方 App 的做法
        await Task.Delay(600);

        var state = session.State;
        Console.WriteLine();
        Console.WriteLine(what == "anc"
            ? $"  回读降噪 = {state.AncText}"
            : $"  回读 EQ   = {state.EqText}");

        if (what == "eq" && value == EqPreset.Game)
            Console.WriteLine($"  注意：EQ 设为「游戏优化」会联动打开游戏模式，当前 = {HeadsetState.Tri(state.GameMode)}");

        return 0;
    }

    // --------------------------------------------------------------- monitor

    private static async Task<int> MonitorAsync(string[] args)
    {
        int seconds = 10;
        if (args.Length >= 2)
            int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);

        await using var session = await OpenTargetAsync(echo: false);
        if (session is null)
            return 1;

        Console.WriteLine($"监听 {seconds} 秒（现在去按耳机触控，或用手机 App 改设置）…");
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)));

        var frames = session.Snapshot();
        Console.WriteLine();
        Console.WriteLine($"共 {frames.Count} 条（去重后），原始 {session.RawFrameCount} 帧：");
        foreach (var (at, frame) in frames)
        {
            var (name, extra) = Session.Describe(frame);
            Console.WriteLine($"  {at:HH:mm:ss.fff}  0x{frame.OpCode:X4} {name,-12} {extra}");
        }
        return 0;
    }

    // ----------------------------------------------------------------- watch

    /// <summary>
    /// 轮询若干 opcode，只在值变化时打印。用于观察 0x0006 之类的动态字段。
    /// </summary>
    private static async Task<int> WatchAsync(string[] args)
    {
        int seconds = args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int s) ? s : 15;
        int intervalMs = args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int i) ? Math.Max(200, i) : 1000;

        await using var session = await OpenTargetAsync(echo: false);
        if (session is null)
            return 1;

        ushort[] ops = [OpCodes.Battery, OpCodes.Query6, OpCodes.AncQuery, OpCodes.EqQuery];
        Console.WriteLine($"轮询 {string.Join(" / ", ops.Select(o => $"0x{o:X4}"))}，" +
                          $"每 {intervalMs}ms 一次，共 {seconds}s。只在值变化时打印：");
        Console.WriteLine();

        var last = new Dictionary<ushort, string>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            session.ClearFrames();
            foreach (var op in ops)
                await session.SendQueryAsync(op);

            await Task.Delay(350);

            foreach (var (_, frame) in session.Snapshot())
            {
                string current = FrameCodec.Hex(frame.Parameters);
                bool isNew = !last.TryGetValue(frame.OpCode, out string? previous);
                if (!isNew && previous == current)
                    continue;

                var (name, extra) = Session.Describe(frame);
                string change = isNew ? "" : $"   ← 原 {previous}";
                Console.WriteLine($"  {DateTime.Now:HH:mm:ss}  0x{frame.OpCode:X4} {name,-10} {extra}{change}");
                last[frame.OpCode] = current;
            }

            await Task.Delay(Math.Max(0, intervalMs - 350));
        }

        Console.WriteLine();
        Console.WriteLine("结束。");
        return 0;
    }

    // ------------------------------------------------------------------- raw
    private static async Task<int> RawAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法：origctl raw \"4E 03 00 00 05 00\"");
            return 1;
        }

        var bytes = ParseHex(args[1]);
        if (bytes is null)
        {
            Console.WriteLine($"✗ 无法解析十六进制：{args[1]}");
            return 1;
        }

        await using var session = await OpenTargetAsync(echo: true);
        if (session is null)
            return 1;

        session.ClearFrames();
        await session.SendAsync(bytes, "raw");
        await Task.Delay(600);

        Console.WriteLine($"  收到 {session.Snapshot().Count} 条回包（原始 {session.RawFrameCount} 帧）");
        return 0;
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<Session?> OpenTargetAsync(bool echo)
    {
        var services = await HeadsetServiceLocator.FindAsync();
        if (services.Count == 0)
        {
            Console.WriteLine("✗ 未找到耳机服务，先跑 `origctl scan`。");
            return null;
        }
        var target = HeadsetServiceLocator.PickTarget(services);
        if (target is null)
        {
            Console.WriteLine("✗ 无法确定目标设备。");
            return null;
        }
        Console.WriteLine($"连接 {target.DisplayName} …");
        return await Session.OpenAsync(target, echo);
    }

    private static bool TryParseOpcode(string text, out ushort op)
    {
        text = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out op);
    }

    private static byte[]? ParseHex(string text)
    {
        var cleaned = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
                          .Replace(" ", string.Empty)
                          .Replace("-", string.Empty)
                          .Replace(",", string.Empty);
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            return null;
        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch
        {
            return null;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.WriteLine($"✗ 未知命令：{cmd}");
        Usage();
        return 1;
    }

    private static int UsageAndOk()
    {
        Usage();
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("""
            OriG Control · M1 探针

              origctl scan                        列出已配对设备 + 查找 NHCKCTRL 服务 + 读通道号
              origctl probe                       完整流程：连接 → 握手 → 状态 → 稳定性 → 重连
              origctl query <hex>                 发单条查询        例：origctl query 0005
              origctl set anc <0|1|2|3|16|17>     设降噪（16/17 = 0x10/0x11）
              origctl set eq  <0..6>              设均衡器
              origctl monitor [秒]                监听耳机主动上报
              origctl watch [秒] [间隔ms]         轮询并只打印变化的字段
              origctl raw "<hex>"                 发原始帧          例：origctl raw "4E 03 00 00 05 00"
            """);
    }
}
