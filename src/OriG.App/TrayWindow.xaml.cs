namespace OriG.App;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OriG.Core;
using OriG.Protocol;

public partial class TrayWindow : Window
{
    private static readonly (byte Mode, string Name)[] AncItems =
    [
        (AncMode.Off, "关闭"),
        (AncMode.Transparency, "通透"),
        (AncMode.Normal, "普通降噪"),
        (AncMode.Deep, "深度降噪"),
    ];

    private static readonly (byte Preset, string Name)[] EqItems =
    [
        (EqPreset.Blue, "悔恨之泪"),
        (EqPreset.Balanced, "均衡中正"),
        (EqPreset.Bass, "欧美澎湃"),
        (EqPreset.Pure, "真律还原"),
        (EqPreset.Game, "游戏优化"),
        (EqPreset.Fine, "细腻佳音"),
        (EqPreset.Vocal, "温婉人声"),
    ];

    /// <summary>
    /// 降噪四档图标，取自 Segoe Fluent Icons（Win11 自带图标字体）：
    /// E74F 静音 / E767 扬声器 / E93E 声波 / E9D9 波形框。
    /// </summary>
    private static readonly int[] AncGlyphs = [0xE74F, 0xE767, 0xE93E, 0xE9D9];

    /// <summary>Win11 自带图标字体，Win10 上回退到 Segoe MDL2 Assets。</summary>
    private static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static readonly (Toggle Toggle, string Name)[] ToggleItems =
    [
        (Toggle.GameMode, "游戏模式"),
        (Toggle.LowLatency, "低延迟"),
        (Toggle.DualConn, "双设备连接"),
        (Toggle.InEar, "入耳检测"),
        (Toggle.Wind, "抗风噪"),
    ];

    private readonly AppSettings _settings;
    private readonly HeadsetSession _session = HeadsetSession.CreateDefault();
    private readonly Dictionary<byte, RadioButton> _ancButtons = [];
    private readonly Dictionary<Toggle, CheckBox> _toggleBoxes = [];

    /// <summary>正在下发的控件不参与状态同步，否则会被设备旧状态覆盖而闪烁。</summary>
    private readonly HashSet<Toggle> _toggleInFlight = [];

    private int _ancInFlight;
    private int _eqInFlight;
    private bool _suppress;
    private bool _busy;
    private bool _captureExcluded;
    private LiveBackdrop? _backdrop;
    private DateTime _shownAt = DateTime.MinValue;
    private (int Left, int Right, BatteryStyle Style)? _lastBattery;
    private SettingsWindow? _settingsWindow;

    public bool IsExiting { get; set; }

    public HeadsetSession Session => _session;

    public TrayWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        BuildControls();

        SourceInitialized += (_, _) => ApplyWindowMaterial();

        _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(ApplyState);
        _session.StatusChanged += (_, _) => Dispatcher.BeginInvoke(ApplyStatus);

        ApplyStatus();
        UpdateBatteryStyleIcon();
    }

    // ------------------------------------------------------------- 外观

    private void ApplyWindowMaterial()
    {
        DwmBackdrop.UseLightFrame(this);
        DwmBackdrop.EnableRoundedCorners(this);
        ApplyCapturePolicy();
    }

    /// <summary>抓屏策略：只有「实时模糊」才需要让窗口对捕获隐身。</summary>
    private void ApplyCapturePolicy()
    {
        bool needLiveCapture = _settings.LiveBlur && !HasBackgroundImage;

        if (needLiveCapture)
        {
            _captureExcluded = DwmBackdrop.ExcludeFromCapture(this);
            if (!_captureExcluded)
                Log.Write("窗口无法从抓屏排除，退回静态背景");
        }
        else
        {
            DwmBackdrop.AllowCapture(this);
            _captureExcluded = false;
        }
    }

    private bool IsDark => _settings.Theme == AppTheme.Dark;

    /// <summary>卡片底色：有背景（图片/实时模糊）时半透明，纯色模式下不透明。</summary>
    private Brush CardScrim(bool hasBackdrop)
    {
        Color color = (hasBackdrop, IsDark) switch
        {
            (true, false) => Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
            (true, true) => Color.FromArgb(0xD9, 0x1A, 0x1D, 0x22),
            (false, false) => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            (false, true) => Color.FromArgb(0xFF, 0x1A, 0x1D, 0x22),
        };
        return new SolidColorBrush(color);
    }

    /// <summary>设置页改了模糊程度后调用：只换效果，不重载背景图。</summary>
    public void OnBlurChanged()
    {
        BackdropImage.Effect = FrostedBackdrop.CreateBlur(_settings.BlurRadius);
    }

    public bool HasBackgroundImage =>
        !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath)
        && System.IO.File.Exists(_settings.BackgroundImagePath);

    /// <summary>设置页改了外观后调用。</summary>
    public void OnAppearanceChanged()
    {
        bool wasVisible = IsVisible;

        // 关键：SetWindowDisplayAffinity 会重置 DWM 合成表面，在窗口可见时改会导致
        // 画面残缺（上半部分留鬼影、蒙版消失）。必须先藏起来再改，然后重走显示流程。
        if (wasVisible)
        {
            _backdrop?.Stop();
            Hide();
        }

        ApplyCapturePolicy();

        if (wasVisible)
            ShowNearTray(activate: false);
    }

    // ------------------------------------------------------------- 构建控件

    private void BuildControls()
    {
        var segmentStyle = (Style)FindResource("Segment");
        var switchStyle = (Style)FindResource("Switch");

        for (int i = 0; i < AncItems.Length; i++)
        {
            var (mode, name) = AncItems[i];

            // 图标颜色由 RadioButton 的 Foreground 继承（选中变蓝）
            var icon = new TextBlock
            {
                Text = char.ConvertFromUtf32(AncGlyphs[i]),
                FontFamily = GlyphFont,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            });

            var segment = new RadioButton
            {
                Style = segmentStyle,
                Content = content,
                GroupName = "Anc",
                Tag = mode,
            };
            segment.Checked += Anc_Checked;
            _ancButtons[mode] = segment;
            AncPanel.Children.Add(segment);
        }

        foreach (var (preset, name) in EqItems)
            EqCombo.Items.Add(new ComboBoxItem { Content = name, Tag = preset });

        foreach (var (toggle, name) in ToggleItems)
        {
            var box = new CheckBox
            {
                Style = switchStyle,
                Content = name,
                Tag = toggle,
                Margin = new Thickness(0, 0, 0, 10),
            };
            box.Checked += Toggle_Changed;
            box.Unchecked += Toggle_Changed;
            _toggleBoxes[toggle] = box;
            TogglePanel.Children.Add(box);
        }
    }

    // ------------------------------------------------------------- 生命周期

    public async Task InitializeAsync()
    {
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_busy)
            return;
        _busy = true;
        ReconnectButton.IsEnabled = false;
        HintText.Text = "正在连接耳机…";

        try
        {
            bool ok = await _session.ConnectAsync();
            if (ok)
            {
                HintText.Text = "";
                await _session.RefreshAsync();
            }
            else
            {
                HintText.Text = _session.LastError ?? "连接失败";
            }
        }
        finally
        {
            _busy = false;
            ReconnectButton.IsEnabled = true;
            ApplyStatus();
            ApplyState();
        }
    }

    public void ShowNearTray(bool activate = true)
    {
        // 1. 不显示窗口，先量出内容高度（Measure 不渲染，不会把自己抓进背景）
        SizeToContent = SizeToContent.Manual;
        RootGrid.Measure(new Size(Width, double.PositiveInfinity));
        double contentHeight = RootGrid.DesiredSize.Height;
        if (contentHeight < 40)
            contentHeight = 460;
        Height = contentHeight;

        // 2. 贴右下角
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - contentHeight - 12;

        // 3. 背景来源：自定义图片 > 实时模糊 > 静态抓屏兜底 > 纯色
        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath,
                Width, contentHeight, _settings.BlurRadius);

        bool wantBlur = !imageMode && _settings.LiveBlur;
        bool liveBlur = wantBlur && _captureExcluded;

        if (wantBlur && !_captureExcluded && !ProbeMode)
        {
            double scale = FrostedBackdrop.GetScale(this);
            FrostedBackdrop.Capture(this, BackdropImage, Width, contentHeight, scale, _settings.BlurRadius);
        }

        bool hasBackdrop = imageMode || wantBlur;
        if (!hasBackdrop)
        {
            _backdrop?.Stop();
            BackdropImage.Source = null;   // 纯色主题：不需要背景图
        }

        RootCard.Background = CardScrim(hasBackdrop);

        if (ProbeTransparent)
            RootCard.Background = Brushes.Transparent;

        // 4. 显示
        ShowActivated = activate;
        Opacity = 1;
        Show();

        // 5. 实时背景（必须在 Show 之后，窗口可见才抓得到正确的屏幕坐标）
        if (liveBlur)
        {
            _backdrop ??= new LiveBackdrop(this, BackdropImage);
            _backdrop.Start(Width, contentHeight, _settings.BlurRadius);
        }
        else
        {
            _backdrop?.Stop();
        }

        _shownAt = DateTime.UtcNow;

        if (activate)
        {
            Activate();
            Focus();
        }
    }

    public void HidePanel()
    {
        _backdrop?.Stop();
        Hide();
    }

    /// <summary>自检用：让界面假装已连接，便于评估真实观感。</summary>
    public bool MockReady { get; set; }

    public void ApplyMockState()
    {
        var st = _session.State;
        st.Apply(new Protocol.Frame(OpCodes.Battery, [70, 66, 0], 9));
        st.Apply(new Protocol.Frame(OpCodes.Firmware, [8, 4], 8));
        st.Apply(new Protocol.Frame(OpCodes.AncQuery, [3], 7));
        st.Apply(new Protocol.Frame(OpCodes.EqQuery, [2], 7));
        st.Apply(new Protocol.Frame(OpCodes.GameModeQuery, [1], 7));
        st.Apply(new Protocol.Frame(OpCodes.LowLatencyQuery, [1], 7));
        st.Apply(new Protocol.Frame(OpCodes.DualConnQuery, [1], 7));
        st.Apply(new Protocol.Frame(OpCodes.InEarQuery, [0], 7));
        st.Apply(new Protocol.Frame(OpCodes.WindQuery, [1], 7));

        MockReady = true;
        ApplyStatus();
        ApplyState();

        DeviceNameText.Text = "YUANDAO OriG in";
        HintText.Text = "";
    }

    /// <summary>自检用：把窗口在屏幕上的实际样子截下来（含系统背景材质）。</summary>
    public bool ProbeMode { get; set; }

    /// <summary>自检用：把卡片背景设成全透明，直接暴露底层材质。</summary>
    public bool ProbeTransparent { get; set; }

    /// <summary>自检用：让窗口对抓屏不可见，验证能否抓到背后桌面。</summary>
    public bool ProbeExclude { get; set; }

    public void CaptureScreenTo(string path, bool allowCapture = true)
    {
        // 自检截图时先解除「对抓屏不可见」，否则截不到自己
        if (allowCapture)
        {
            DwmBackdrop.AllowCapture(this);
            System.Threading.Thread.Sleep(250);
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        int x = (int)Math.Round(Left * dpi.DpiScaleX);
        int y = (int)Math.Round(Top * dpi.DpiScaleY);

        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsExiting || _settingsWindow is { IsVisible: true })
            return;

        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250)
            return;

        HidePanel();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!IsExiting)
        {
            e.Cancel = true;
            HidePanel();
        }
        base.OnClosing(e);
    }

    // ------------------------------------------------------------- 状态刷新

    private void ApplyStatus()
    {
        Log.Write($"状态 → {_session.Status}" + (_session.LastError is null ? "" : $"（{_session.LastError}）"));

        (Color dot, string text) = _session.Status switch
        {
            SessionStatus.Ready => (Color.FromRgb(0x2F, 0xB3, 0x5E), "已连接"),
            SessionStatus.Connecting => (Color.FromRgb(0xE0, 0x9B, 0x2D), "正在连接…"),
            SessionStatus.Handshaking => (Color.FromRgb(0xE0, 0x9B, 0x2D), "正在同步…"),
            SessionStatus.Faulted => (Color.FromRgb(0xD9, 0x3D, 0x3D), _session.LastError ?? "连接失败"),
            _ => (Color.FromRgb(0x9A, 0xA0, 0xA8), "未连接"),
        };

        StatusDot.Fill = new SolidColorBrush(dot);
        StatusText.Text = text;

        bool ready = _session.Status == SessionStatus.Ready || MockReady;

        if (MockReady)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x2F, 0xB3, 0x5E));
            StatusText.Text = "已连接";
        }

        AncPanel.IsEnabled = ready;
        EqCombo.IsEnabled = ready;
        TogglePanel.IsEnabled = ready;
    }

    private void ApplyState()
    {
        var state = _session.State;

        DeviceNameText.Text = string.IsNullOrWhiteSpace(_session.DeviceName) ? "原道耳机" : _session.DeviceName;
        // 固件版本放进悬停提示，保持界面简洁
        DeviceNameText.ToolTip = state.FirmwareMain < 0 ? null : $"固件 {state.FirmwareText}";

        // 电量图形重建有开销，值没变就不动，否则会闪
        var battery = (state.BatteryLeft, state.BatteryRight, _settings.BatteryStyle);
        if (_lastBattery != battery)
        {
            BatteryDisplay.Render(BatteryPanel, state.BatteryLeft, state.BatteryRight, _settings.BatteryStyle);
            _lastBattery = battery;
        }

        _suppress = true;
        try
        {
            if (_ancInFlight == 0 && _ancButtons.TryGetValue((byte)state.Anc, out var ancChip))
                ancChip.IsChecked = true;

            if (_eqInFlight == 0)
            {
                int index = Array.FindIndex(EqItems, x => x.Preset == state.Eq);
                if (index >= 0 && EqCombo.SelectedIndex != index)
                    EqCombo.SelectedIndex = index;
            }

            foreach (var (toggle, box) in _toggleBoxes)
            {
                if (_toggleInFlight.Contains(toggle))
                    continue;

                bool? value = toggle switch
                {
                    Toggle.GameMode => state.GameMode,
                    Toggle.LowLatency => state.LowLatency,
                    Toggle.DualConn => state.DualConn,
                    Toggle.InEar => state.InEar,
                    Toggle.Wind => state.Wind,
                    _ => null,
                };
                if (value.HasValue)
                    box.IsChecked = value.Value;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    // ------------------------------------------------------------- 交互

    private async void Anc_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: byte mode })
            return;

        _ancInFlight++;
        try
        {
            bool ok = await _session.SetAncAsync(mode);
            HintText.Text = ok ? "" : "耳机未确认该设置";
            Log.Write($"设降噪 0x{mode:X2} → {(ok ? "成功" : "未确认")}");
        }
        finally
        {
            _ancInFlight--;
            ApplyState();
        }
    }

    private async void Eq_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || EqCombo.SelectedItem is not ComboBoxItem { Tag: byte preset })
            return;

        _eqInFlight++;
        try
        {
            bool ok = await _session.SetEqAsync(preset);
            HintText.Text = ok ? "" : "耳机未确认该设置";
            Log.Write($"设 EQ 0x{preset:X2} → {(ok ? "成功" : "未确认")}");
        }
        finally
        {
            _eqInFlight--;
            ApplyState();
        }
    }

    private async void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not CheckBox { Tag: Toggle toggle } box)
            return;

        bool on = box.IsChecked == true;
        _toggleInFlight.Add(toggle);
        try
        {
            bool ok = await _session.SetToggleAsync(toggle, on);
            HintText.Text = ok ? "" : "该项可能不被此固件支持";
            Log.Write($"开关 {toggle} → {on}  {(ok ? "成功" : "未确认")}");
        }
        finally
        {
            _toggleInFlight.Remove(toggle);
            ApplyState();
        }
    }

    private void BatteryStyle_Click(object sender, RoutedEventArgs e)
    {
        _settings.BatteryStyle = _settings.BatteryStyle == BatteryStyle.Ring ? BatteryStyle.Bar : BatteryStyle.Ring;
        _settings.Save();
        RefreshBatteryStyle();
    }

    /// <summary>设置页改了电量样式后调用。</summary>
    public void RefreshBatteryStyle()
    {
        UpdateBatteryStyleIcon();
        _lastBattery = null;
        ApplyState();
    }

    private void UpdateBatteryStyleIcon()
    {
        bool ring = _settings.BatteryStyle == BatteryStyle.Ring;
        StyleIconRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        StyleIconBar.Visibility = ring ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        await _session.DisconnectAsync();
        await ConnectAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings, _session);
            _settingsWindow.HotkeysChanged += (_, _) => HotkeysChanged?.Invoke(this, EventArgs.Empty);
            _settingsWindow.BatteryStyleChanged += (_, _) => RefreshBatteryStyle();
            _settingsWindow.AppearanceChanged += (_, _) => OnAppearanceChanged();
            _settingsWindow.BlurChanged += (_, _) => OnBlurChanged();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.ShowNear(this);
    }

    /// <summary>设置页改了热键，需要 App 重新注册。</summary>
    public event EventHandler? HotkeysChanged;

    public async Task ReconnectAsync()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await _session.DisconnectAsync();
            await ConnectAsync();
        }).Task.Unwrap();
    }

    // ------------------------------------------------------------- 热键动作

    /// <summary>循环切降噪（热键用）。</summary>
    public async Task CycleAncAsync()
    {
        if (_session.Status != SessionStatus.Ready)
            return;

        int current = Array.FindIndex(AncItems, x => x.Mode == _session.State.Anc);
        var next = AncItems[(current + 1) % AncItems.Length];
        await _session.SetAncAsync(next.Mode);
        ApplyState();
    }

    /// <summary>循环切均衡器（热键用）。</summary>
    public async Task CycleEqAsync()
    {
        if (_session.Status != SessionStatus.Ready)
            return;

        int current = Array.FindIndex(EqItems, x => x.Preset == _session.State.Eq);
        var next = EqItems[(current + 1) % EqItems.Length];
        await _session.SetEqAsync(next.Preset);
        ApplyState();
    }

    public async Task ShutdownAsync()
    {
        IsExiting = true;
        _backdrop?.Dispose();
        _backdrop = null;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }
        await _session.DisposeAsync();
    }

    /// <summary>调试用：设置页快照。</summary>
    public void SnapshotSettings(string path) => _settingsWindow?.SaveSnapshot(path);

    /// <summary>
    /// 把面板渲染成 PNG。磨砂底本身就是 WPF 视觉树的一部分，所以快照能真实反映效果。
    /// </summary>
    public void SaveSnapshot(string path)
    {
        UpdateLayout();

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 400;
        const double scale = 2.0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var backdrop = new SolidColorBrush(Color.FromRgb(0xE9, 0xEB, 0xEF));
            dc.DrawRectangle(backdrop, null, new Rect(0, 0, w, h));

            // 背景图带负 Margin 超出布局边界，VisualBrush 默认会按内容边界拉伸导致画面被缩小，
            // 这里锁定 Viewbox/Viewport 为窗口矩形，保持 1:1。
            var brush = new VisualBrush(RootGrid)
            {
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }

        var bitmap = new RenderTargetBitmap(
            (int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
        Log.Write($"快照已保存：{path}");
    }
}
