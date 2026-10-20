namespace OriG.App;

using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

public partial class App : Application
{
    private const int HotkeyCycleAnc = 1;
    private const int HotkeyCycleEq = 2;
    private const int HotkeyTogglePanel = 3;

    private Mutex? _mutex;
    private WinForms.NotifyIcon? _tray;
    private TrayWindow? _window;
    private HotkeyService? _hotkeys;
    private AppSettings _settings = new();
    private bool _lowBatteryNotified;
    private bool _isProbeRun;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\OriGControl.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _settings = AppSettings.Load();
        ThemeManager.Apply(_settings.Theme);
        _isProbeRun = e.Args.Any(a => a.StartsWith("--probe", StringComparison.Ordinal)
                                      || a.StartsWith("--snapshot", StringComparison.Ordinal));
        Log.Write($"=== 启动 {string.Join(' ', e.Args)} ===");

        _window = new TrayWindow(_settings);
        _window.HotkeysChanged += (_, _) => RegisterHotkeys();
        _window.Session.StateChanged += (_, _) => CheckLowBattery();

        CreateTray();
        RegisterHotkeys();

        _ = _window.InitializeAsync();

        if (e.Args.Contains("--panel"))
            _window.ShowNearTray();

        int snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
        if (snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
            _ = SnapshotThenExitAsync(e.Args[snapshotIndex + 1], settings: false);

        int settingsIndex = Array.IndexOf(e.Args, "--snapshot-settings");
        if (settingsIndex >= 0 && settingsIndex + 1 < e.Args.Length)
            _ = SnapshotThenExitAsync(e.Args[settingsIndex + 1], settings: true);

        int probeIndex = Array.IndexOf(e.Args, "--probe-backdrop");
        if (probeIndex >= 0 && probeIndex + 1 < e.Args.Length)
            _ = ProbeBackdropThenExitAsync(e.Args[probeIndex + 1], transparent: false);

        int rawIndex = Array.IndexOf(e.Args, "--probe-raw");
        if (rawIndex >= 0 && rawIndex + 1 < e.Args.Length)
            _ = ProbeBackdropThenExitAsync(e.Args[rawIndex + 1], transparent: true);

        int excludeIndex = Array.IndexOf(e.Args, "--probe-exclude");
        if (excludeIndex >= 0 && excludeIndex + 1 < e.Args.Length)
            _ = ProbeExcludeThenExitAsync(e.Args[excludeIndex + 1]);

        int plainIndex = Array.IndexOf(e.Args, "--probe-plain");
        if (plainIndex >= 0 && plainIndex + 1 < e.Args.Length)
            _ = ProbePlainThenExitAsync(e.Args[plainIndex + 1]);

        int appearanceIndex = Array.IndexOf(e.Args, "--probe-appearance");
        if (appearanceIndex >= 0 && appearanceIndex + 2 < e.Args.Length)
            _ = ProbeAppearanceThenExitAsync(e.Args[appearanceIndex + 1], e.Args[appearanceIndex + 2]);

        int mockIndex = Array.IndexOf(e.Args, "--snapshot-mock");
        if (mockIndex >= 0 && mockIndex + 1 < e.Args.Length)
            _ = SnapshotMockThenExitAsync(e.Args[mockIndex + 1]);

        int glyphIndex = Array.IndexOf(e.Args, "--glyph-sheet");
        if (glyphIndex >= 0 && glyphIndex + 1 < e.Args.Length)
        {
            DumpGlyphSheet(e.Args[glyphIndex + 1]);
            Shutdown();
        }
    }

    /// <summary>自检：把候选图标字形渲染成一张对照表，用来挑选 Segoe Fluent Icons 的码点。</summary>
    private static void DumpGlyphSheet(string path)
    {
        int[] codes =
        [
            0xE8BE, 0xE9CA, 0xEA8E, 0xE71F, 0xEC4C, 0xE8D3,
            0xEBD4, 0xEC4B, 0xE9D5, 0xEA3A, 0xE8C8, 0xE7F8,
            0xE9D9, 0xE93E, 0xE767, 0xE74F, 0xE7ED, 0xE8F2,
            0xEC4F, 0xE8D6, 0xE9D2, 0xEBD3, 0xE9F9, 0xE946,
            0xEA77, 0xE9E9, 0xE7F6, 0xE72C, 0xE713, 0xE895,
        ];

        var panel = new System.Windows.Controls.WrapPanel
        {
            Width = 660,
            Background = System.Windows.Media.Brushes.White,
        };

        foreach (int code in codes)
        {
            var cell = new System.Windows.Controls.StackPanel
            {
                Width = 100,
                Margin = new System.Windows.Thickness(5),
            };
            cell.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = char.ConvertFromUtf32(code),
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 30,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });
            cell.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = code.ToString("X4"),
                FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
            });
            panel.Children.Add(cell);
        }

        panel.Measure(new System.Windows.Size(660, double.PositiveInfinity));
        panel.Arrange(new System.Windows.Rect(0, 0, 660, panel.DesiredSize.Height));

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            660, (int)Math.Ceiling(panel.DesiredSize.Height), 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(panel);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
        Log.Write($"字形表已保存：{path}");
    }

    /// <summary>自检：用假数据渲染界面，评估观感。</summary>
    private async Task SnapshotMockThenExitAsync(string path)
    {
        try
        {
            await Task.Delay(2500);

            await Dispatcher.InvokeAsync(() =>
            {
                _window!.ApplyMockState();
                _window.ShowNearTray();
            });

            await Task.Delay(800);

            await Dispatcher.InvokeAsync(() => _window!.SaveSnapshot(path));
            await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log.Write($"自检快照失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await ExitAsync();
        }
    }

    /// <summary>自检：面板开着时切换背景图，看画面会不会坏掉。</summary>
    private async Task ProbeAppearanceThenExitAsync(string imagePath, string outPath)
    {
        for (int i = 0; i < 60 && _window?.Session.Status != Core.SessionStatus.Ready; i++)
            await Task.Delay(200);

        await Dispatcher.InvokeAsync(() =>
        {
            _window!.ProbeMode = true;
            _window.ShowNearTray();
        });

        await Task.Delay(1000);

        // 模拟用户在设置里选图
        _settings.BackgroundImagePath = imagePath;
        await Dispatcher.InvokeAsync(() => _window!.OnAppearanceChanged());

        await Task.Delay(1200);

        await Dispatcher.InvokeAsync(() => _window!.CaptureScreenTo(outPath, allowCapture: false));
        await Task.Delay(200);
        await ExitAsync();
    }

    /// <summary>自检：不解除「对抓屏不可见」，直接截图。用来验证设置背景图后能否正常截图。</summary>
    private async Task ProbePlainThenExitAsync(string path)
    {
        for (int i = 0; i < 60 && _window?.Session.Status != Core.SessionStatus.Ready; i++)
            await Task.Delay(200);

        await Dispatcher.InvokeAsync(() =>
        {
            _window!.ProbeMode = true;
            _window.ShowNearTray();
        });

        await Task.Delay(1200);

        await Dispatcher.InvokeAsync(() => _window!.CaptureScreenTo(path, allowCapture: false));
        await Task.Delay(200);
        await ExitAsync();
    }

    /// <summary>自检：窗口显示状态下抓屏，看它是否被排除在捕获之外。</summary>
    private async Task ProbeExcludeThenExitAsync(string path)
    {
        await Task.Delay(3500);

        await Dispatcher.InvokeAsync(() =>
        {
            _window!.ProbeMode = true;
            _window.ProbeExclude = true;
            _window.ShowNearTray();
        });

        await Task.Delay(1200);

        await Dispatcher.InvokeAsync(() => _window!.CaptureScreenTo(path));
        await Task.Delay(200);
        await ExitAsync();
    }

    /// <summary>自检：截取窗口在屏幕上的真实样子，用来确认系统背景材质是否生效。</summary>
    private async Task ProbeBackdropThenExitAsync(string path, bool transparent)
    {
        // 等连接完成，否则控件是禁用态、截图没意义
        for (int i = 0; i < 60 && _window?.Session.Status != Core.SessionStatus.Ready; i++)
            await Task.Delay(200);

        await Dispatcher.InvokeAsync(() =>
        {
            _window!.ProbeMode = true;
            _window.ProbeTransparent = transparent;
            _window.ShowNearTray();
        });

        await Task.Delay(1200);

        await Dispatcher.InvokeAsync(() => _window!.CaptureScreenTo(path));
        await Task.Delay(200);
        await ExitAsync();
    }

    /// <summary>调试用：等连接完成后截一张图，然后退出。</summary>
    private async Task SnapshotThenExitAsync(string path, bool settings)
    {
        try
        {
            for (int i = 0; i < 40 && _window?.Session.Status != Core.SessionStatus.Ready; i++)
                await Task.Delay(200);

            await Dispatcher.InvokeAsync(() =>
            {
                _window?.ShowNearTray();
                _window?.UpdateLayout();
            });

            await Task.Delay(400);

            if (settings)
            {
                await Dispatcher.InvokeAsync(() => _window?.OpenSettings());
                await Task.Delay(800);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (settings)
                    _window?.SnapshotSettings(path);
                else
                    _window?.SaveSnapshot(path);
            });

            await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log.Write($"快照失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await ExitAsync();
        }
    }

    // ------------------------------------------------------------- 托盘

    private void CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开控制面板", null, (_, _) => ShowPanel());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("重新连接", null, async (_, _) => await ReconnectAsync());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, async (_, _) => await ExitAsync());

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OriG Control",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
                ShowPanel();
        };
        _tray.DoubleClick += (_, _) => ShowPanel();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        return resource is null
            ? System.Drawing.SystemIcons.Application
            : new System.Drawing.Icon(resource.Stream);
    }

    private void ShowPanel()
    {
        if (_window is null)
            return;

        if (_window.IsVisible)
        {
            _window.HidePanel();
            return;
        }
        _window.ShowNearTray();
    }

    private void OpenSettings()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
            _window.ShowNearTray();

        // 复用 TrayWindow 里的入口逻辑
        _window.OpenSettings();
    }

    // ------------------------------------------------------------- 热键

    private void RegisterHotkeys()
    {
        _hotkeys ??= new HotkeyService();
        if (_window is null)
            return;

        _hotkeys.Unregister(HotkeyCycleAnc);
        _hotkeys.Unregister(HotkeyCycleEq);
        _hotkeys.Unregister(HotkeyTogglePanel);

        BindHotkey(HotkeyCycleAnc, _settings.HotkeyCycleAnc, "Ctrl+Alt+A",
            ["Ctrl+Alt+A", "Ctrl+Alt+Q", "Ctrl+Shift+A", "Ctrl+Alt+9"],
            () => _ = _window!.CycleAncAsync(),
            v => _settings.HotkeyCycleAnc = v);

        BindHotkey(HotkeyCycleEq, _settings.HotkeyCycleEq, "Ctrl+Alt+E",
            ["Ctrl+Alt+E", "Ctrl+Alt+W", "Ctrl+Shift+E", "Ctrl+Alt+8"],
            () => _ = _window!.CycleEqAsync(),
            v => _settings.HotkeyCycleEq = v);

        BindHotkey(HotkeyTogglePanel, _settings.HotkeyTogglePanel, "Ctrl+Alt+O",
            ["Ctrl+Alt+O", "Ctrl+Alt+P", "Ctrl+Shift+O", "Ctrl+Alt+0"],
            ShowPanel,
            v => _settings.HotkeyTogglePanel = v);
    }

    /// <summary>
    /// 注册热键。若用户没改过（仍是默认值）而默认值被占用，自动降级到备选组合并写回设置；
    /// 用户自定义的组合键失败则保持原样，交给用户在设置页处理。
    /// </summary>
    private void BindHotkey(int id, string preferred, string defaultGesture,
        string[] candidates, Action action, Action<string> persist)
    {
        if (_hotkeys is null)
            return;

        if (!string.IsNullOrWhiteSpace(preferred) && _hotkeys.Register(id, preferred, action))
            return;

        if (!string.Equals(preferred, defaultGesture, StringComparison.OrdinalIgnoreCase))
        {
            Log.Write($"自定义热键 {preferred} 不可用，保持原样");
            return;
        }

        foreach (string candidate in candidates)
        {
            if (string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_hotkeys.Register(id, candidate, action))
                continue;

            persist(candidate);
            _settings.Save();
            Log.Write($"默认热键被占用，已降级为 {candidate}");
            return;
        }

        Log.Write($"热键 {preferred} 及备选全部不可用");
    }

    // ------------------------------------------------------------- 低电量

    private void CheckLowBattery()
    {
        if (_window is null || _tray is null || !_settings.LowBatteryNotify)
            return;

        if (_window.ProbeMode || _isProbeRun)
            return;   // 自检截图时不要弹气泡挡住画面

        var state = _window.Session.State;
        int threshold = _settings.LowBatteryThreshold;

        bool low = (state.BatteryLeft >= 0 && state.BatteryLeft <= threshold)
                   || (state.BatteryRight >= 0 && state.BatteryRight <= threshold);

        bool recovered = state.BatteryLeft > threshold + 5 && state.BatteryRight > threshold + 5;

        if (low && !_lowBatteryNotified)
        {
            _lowBatteryNotified = true;
            string detail = $"L {state.BatteryLeft}%  R {state.BatteryRight}%";
            _tray.ShowBalloonTip(4000, "耳机电量偏低", detail, WinForms.ToolTipIcon.Warning);
            Log.Write($"低电量提醒：{detail}");
        }
        else if (recovered)
        {
            _lowBatteryNotified = false;
        }
    }

    // ------------------------------------------------------------- 退出

    private async Task ReconnectAsync()
    {
        if (_window is not null)
            await _window.ReconnectAsync();
    }

    private async Task ExitAsync()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _hotkeys?.Dispose();
        _hotkeys = null;

        if (_window is not null)
            await _window.ShutdownAsync();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
