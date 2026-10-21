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

    /// <summary>面板上显示的降噪档位；实验性降噪只在这里出现，不进入热键循环。</summary>
    private static readonly (byte Mode, string Name, int Glyph)[] AncUiItems =
    [
        (AncMode.Off, "关闭", 0xE74F),
        (AncMode.Transparency, "通透", 0xE767),
        (AncMode.Normal, "普通降噪", 0xE93E),
        (AncMode.Deep, "深度降噪", 0xE9D9),
        (AncMode.Experimental, "实验降噪", 0xE9D9),
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
    private bool _finding;
    private System.Windows.Threading.DispatcherTimer? _findTimer;
    private bool _suppress;
    private bool _busy;
    private bool _modalOpen;
    private DateTime _shownAt = DateTime.MinValue;
    private readonly System.Windows.Threading.DispatcherTimer _reconnectTimer = new();
    private readonly System.Windows.Threading.DispatcherTimer _idleTimer = new();
    private readonly System.Windows.Threading.DispatcherTimer _pollTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30),
    };
    private int _reconnectAttempt;
    private bool _idleDisconnected;
    private HudWindow? _hud;
    private (int Left, int Right, BatteryStyle Style)? _lastBattery;
    private SettingsWindow? _settingsWindow;

    public bool IsExiting { get; set; }

    public HeadsetSession Session => _session;

    public TrayWindow(AppSettings settings)
    {
        _settings = settings;
        _session.PreferredDeviceId = settings.LastDeviceId;
        InitializeComponent();
        BuildControls();

        SourceInitialized += (_, _) => ApplyWindowMaterial();

        _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(ApplyState);
        _session.StatusChanged += (_, _) => Dispatcher.BeginInvoke(ApplyStatus);

        _reconnectTimer.Tick += ReconnectTimer_Tick;
        _idleTimer.Tick += IdleTimer_Tick;
        _pollTimer.Tick += PollTimer_Tick;

        ApplyStatus();
    }

    // ------------------------------------------------------------- 外观

    private void ApplyWindowMaterial()
    {
        DwmBackdrop.UseLightFrame(this);
        DwmBackdrop.EnableRoundedCorners(this);
    }

    private bool IsDark => _settings.Theme == AppTheme.Dark;

    /// <summary>卡片底色：有背景图时半透明，纯色模式下不透明。</summary>
    private Brush CardScrim(bool hasBackdrop)
    {
        Color color = (hasBackdrop, IsDark) switch
        {
            (true, false) => Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
            (true, true) => Color.FromArgb(0xE6, 0x18, 0x1A, 0x17),
            (false, false) => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            (false, true) => Color.FromArgb(0xFF, 0x18, 0x1A, 0x17),
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
        ApplyStatus();
        _lastBattery = null;
        ApplyState();

        if (IsVisible)
            ShowNearTray(activate: false);
    }

    // ------------------------------------------------------------- 构建控件

    private void BuildControls()
    {
        var segmentStyle = (Style)FindResource("Segment");
        var switchStyle = (Style)FindResource("Switch");

        for (int i = 0; i < AncUiItems.Length; i++)
        {
            var (mode, name, glyph) = AncUiItems[i];

            // 图标颜色由 RadioButton 的 Foreground 继承（选中变蓝）
            var icon = new TextBlock
            {
                Text = char.ConvertFromUtf32(glyph),
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
            if (mode == AncMode.Experimental)
                segment.ToolTip = "实验性降噪：使用更激进的算法，可能出现声音异常";
            _ancButtons[mode] = segment;
            AncPanel.Children.Add(segment);
        }

        foreach (var (preset, name) in EqItems)
            EqCombo.Items.Add(new ComboBoxItem { Content = name, Tag = preset });

        for (int i = 0; i < ToggleItems.Length; i++)
        {
            var (toggle, name) = ToggleItems[i];

            var box = new CheckBox
            {
                Style = switchStyle,
                Content = name,
                Tag = toggle,
                Margin = new Thickness(0, 7, 0, 7),
            };
            box.Checked += Toggle_Changed;
            box.Unchecked += Toggle_Changed;
            _toggleBoxes[toggle] = box;
            TogglePanel.Children.Add(box);

            if (i < ToggleItems.Length - 1)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2) };
                divider.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
                TogglePanel.Children.Add(divider);
            }
        }
    }

    // ------------------------------------------------------------- 生命周期

    public async Task InitializeAsync()
    {
        await ConnectAsync();
    }

    /// <summary>闲置断开后，用户再次操作时按需重连。</summary>
    private async Task EnsureConnectedAsync()
    {
        if (IsExiting || ProbeMode || MockReady)
            return;
        if (_session.Status is SessionStatus.Ready or SessionStatus.Connecting or SessionStatus.Handshaking)
            return;

        _idleDisconnected = false;
        _reconnectAttempt = 0;
        _reconnectTimer.Stop();
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
                if (!string.IsNullOrWhiteSpace(_session.DeviceId)
                    && !string.Equals(_settings.LastDeviceId, _session.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.LastDeviceId = _session.DeviceId;
                    _settings.Save();
                }
            }
            else
            {
                HintText.Text = _session.LastError ?? "连接失败";
            }
        }
        catch (Exception ex)
        {
            // ConnectAsync 自身已做清理，这里只兜底，避免 async void 调用方崩溃。
            Log.Write($"连接异常：{ex.GetType().Name}: {ex.Message}");
            HintText.Text = "连接异常，将稍后重试";
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
        _idleTimer.Stop();
        _pollTimer.Start();
        _ = EnsureConnectedAsync();

        // 1. 不显示窗口，先量出内容高度（Measure 不渲染，不会把自己抓进背景）
        SizeToContent = SizeToContent.Manual;
        RootGrid.Measure(new Size(Width, double.PositiveInfinity));
        double contentHeight = RootGrid.DesiredSize.Height;
        if (contentHeight < 40)
            contentHeight = 460;
        Height = contentHeight;

        // 2. 贴鼠标所在屏幕的右下角，多屏时不要总跑回主屏
        Rect area = ScreenWorkArea.ForCursor(this);
        Left = Math.Max(area.Left + 12, area.Right - Width - 12);
        Top = Math.Max(area.Top + 12, area.Bottom - contentHeight - 12);

        // 3. 背景来源：自定义图片 > 纯色
        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath,
                Width, contentHeight, _settings.BlurRadius);

        if (!imageMode)
            BackdropImage.Source = null;   // 纯色主题：不需要背景图

        RootCard.Background = CardScrim(imageMode);

        if (ProbeTransparent)
            RootCard.Background = Brushes.Transparent;

        // 4. 显示
        // 先记时间再 Show：Show/Activate 期间可能同步触发 Deactivated，
        // 若这时 _shownAt 还是旧值，窗口会被误判为“用户点了别处”而立刻隐藏。
        _shownAt = DateTime.UtcNow;
        ShowActivated = activate;
        Opacity = 1;
        Show();

        if (activate)
        {
            Activate();
            Focus();
        }

        Log.Write($"面板显示：Visible={IsVisible} 位置={Left:0},{Top:0} 大小={Width:0}x{Height:0}");
    }

    public void HidePanel()
    {
        Hide();
        if (_settingsWindow is not { IsVisible: true })
        {
            _pollTimer.Stop();
            ArmIdleTimer();
        }
    }

    // ------------------------------------------------------------- 自动重连 / 闲置断开

    private void ScheduleReconnect()
    {
        if (IsExiting || ProbeMode || MockReady || _idleDisconnected)
            return;
        if (_session.Status is SessionStatus.Connecting or SessionStatus.Handshaking)
            return;
        if (_busy || _reconnectTimer.IsEnabled)
            return;

        int seconds = Math.Min(30, 3 * (1 << Math.Min(_reconnectAttempt, 4)));
        _reconnectTimer.Interval = TimeSpan.FromSeconds(seconds);
        _reconnectTimer.Start();

        if (_reconnectAttempt == 0)
            HintText.Text = _session.Status == SessionStatus.Faulted
                ? "连接失败，将自动重试…"
                : "连接已断开，正在自动重连…";
    }

    /// <summary>
    /// 面板/设置页可见时周期性拉一次状态。耳机不会把所有变更都主动推送过来，
    /// 例如在手机上改了降噪，PC 界面需要靠这一轮查询才能跟上。
    /// </summary>
    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (IsExiting || ProbeMode || MockReady)
            return;
        if (!IsVisible && _settingsWindow is not { IsVisible: true })
            return;
        if (_session.Status != SessionStatus.Ready)
            return;

        await _session.RefreshAsync();
        ApplyState();
    }

    private void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        _reconnectTimer.Stop();

        if (IsExiting || _idleDisconnected || ProbeMode || MockReady)
            return;
        if (_session.Status == SessionStatus.Ready)
        {
            _reconnectAttempt = 0;
            return;
        }
        if (_busy)
        {
            ScheduleReconnect();
            return;
        }

        _reconnectAttempt++;
        Log.Write($"自动重连第 {_reconnectAttempt} 次");
        _ = ConnectAsync();
    }

    private void ArmIdleTimer()
    {
        _idleTimer.Stop();

        int minutes = _settings.IdleDisconnectMinutes;
        if (minutes <= 0 || IsExiting || ProbeMode || _session.Status != SessionStatus.Ready)
            return;

        _idleTimer.Interval = TimeSpan.FromMinutes(minutes);
        _idleTimer.Start();
    }

    private async void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();

        if (IsExiting || IsVisible || _settingsWindow is { IsVisible: true })
            return;
        if (_session.Status != SessionStatus.Ready)
            return;

        _idleDisconnected = true;
        _reconnectTimer.Stop();
        Log.Write($"闲置 {_settings.IdleDisconnectMinutes} 分钟，断开连接");
        await _session.DisconnectAsync();
        HintText.Text = "已闲置断开，打开面板或使用热键会自动重连";
    }

    /// <summary>系统从休眠唤醒后重新建立链路（空闲断开状态则保持不变）。</summary>
    public async Task OnResumeAsync()
    {
        if (IsExiting || ProbeMode || MockReady || _idleDisconnected)
            return;

        _idleTimer.Stop();
        Log.Write("系统唤醒，重新连接");
        await ReconnectAsync();
    }

    /// <summary>自检用：让界面假装已连接，便于评估真实观感。</summary>
    public bool MockReady { get; set; }

    public void ApplyMockState()
    {
        var st = _session.State;
        st.Apply(new Protocol.Frame(OpCodes.Battery, [70, 66, 0]));
        st.Apply(new Protocol.Frame(OpCodes.Firmware, [8, 4]));
        st.Apply(new Protocol.Frame(OpCodes.AncQuery, [3]));
        st.Apply(new Protocol.Frame(OpCodes.EqQuery, [2]));
        st.Apply(new Protocol.Frame(OpCodes.GameModeQuery, [1]));
        st.Apply(new Protocol.Frame(OpCodes.LowLatencyQuery, [1]));
        st.Apply(new Protocol.Frame(OpCodes.DualConnQuery, [1]));
        st.Apply(new Protocol.Frame(OpCodes.InEarQuery, [0]));
        st.Apply(new Protocol.Frame(OpCodes.WindQuery, [1]));

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

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsExiting || _modalOpen || _settingsWindow is { IsVisible: true })
            return;

        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250)
            return;

        HidePanel();
    }

    /// <summary>弹模态框时窗口会 Deactivated，用标志避免面板被自动隐藏。</summary>
    private MessageBoxResult Confirm(string message, string title)
    {
        _modalOpen = true;
        try
        {
            return MessageBox.Show(this, message, title, MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
        }
        finally
        {
            _modalOpen = false;
        }
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

        Brush accent = ThemeManager.Brush("AccentBrush", "#C8F25A");
        Color readyColor = accent is SolidColorBrush { Color: var color } ? color : Color.FromRgb(0x2F, 0xB3, 0x5E);

        (Color dot, string text) = _session.Status switch
        {
            SessionStatus.Ready => (readyColor, "已连接"),
            SessionStatus.Connecting => (Color.FromRgb(0xE0, 0x9B, 0x2D), "正在连接…"),
            SessionStatus.Handshaking => (Color.FromRgb(0xE0, 0x9B, 0x2D), "正在同步…"),
            SessionStatus.Faulted => (Color.FromRgb(0xD9, 0x3D, 0x3D), _session.LastError ?? "连接失败"),
            _ => (Color.FromRgb(0x9A, 0xA0, 0xA8), "未连接"),
        };

        if (_session.Status == SessionStatus.Ready && _session.State.FirmwareMain >= 0)
            text = $"已连接 · 固件 {_session.State.FirmwareText}";

        StatusDot.Fill = new SolidColorBrush(dot);
        StatusText.Text = text;

        bool ready = _session.Status == SessionStatus.Ready || MockReady;

        if (MockReady)
        {
            StatusDot.Fill = new SolidColorBrush(readyColor);
            StatusText.Text = "已连接";
        }

        if (_session.Status == SessionStatus.Ready)
        {
            _reconnectAttempt = 0;
            _reconnectTimer.Stop();
            if (!IsVisible && _settingsWindow is not { IsVisible: true })
                ArmIdleTimer();
        }
        else if (_session.Status is SessionStatus.Disconnected or SessionStatus.Faulted)
        {
            ScheduleReconnect();
        }

        AncPanel.IsEnabled = ready;
        EqCombo.IsEnabled = ready;
        TogglePanel.IsEnabled = ready;
        FindEarbudsButton.IsEnabled = ready;

        if (!ready)
            ResetFindUi();
    }

    private void ApplyState()
    {
        var state = _session.State;

        DeviceNameText.Text = string.IsNullOrWhiteSpace(_session.DeviceName) ? "原道耳机" : _session.DeviceName;
        // 固件版本同时显示在状态行，悬停提示保留
        DeviceNameText.ToolTip = state.FirmwareMain < 0 ? null : $"固件 {state.FirmwareText}";
        if (_session.Status == SessionStatus.Ready && state.FirmwareMain >= 0)
            StatusText.Text = $"已连接 · 固件 {state.FirmwareText}";

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

        if (mode == AncMode.Experimental)
        {
            var choice = Confirm(
                "实验性降噪使用更激进的降噪算法，可能出现声音异常。\n\n确定要开启吗？",
                "实验性降噪");
            if (choice != MessageBoxResult.Yes)
            {
                ApplyState();
                return;
            }
        }

        _ancInFlight++;
        try
        {
            bool ok = await _session.SetAncAsync(mode);
            HintText.Text = ok
                ? (mode == AncMode.Experimental ? "实验性降噪已开启；如声音异常请切回普通降噪" : "")
                : (mode == AncMode.Experimental ? "耳机未确认该设置，此固件可能不支持实验性降噪" : "耳机未确认该设置");
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

    private async void FindEarbuds_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Status != SessionStatus.Ready)
            return;

        FindEarbudsButton.IsEnabled = false;
        try
        {
            if (!_finding)
            {
                var choice = Confirm(
                    "此功能会让耳机发出较大的提示音。\n" +
                    "如果耳机正在您的耳朵里，请不要使用。\n" +
                    "建议在安全的环境下使用，提示音会持续几秒钟。\n\n" +
                    "确定要开始寻找耳机吗？",
                    "查找耳机");
                if (choice != MessageBoxResult.Yes)
                    return;

                if (!await _session.FindEarbudsAsync(true))
                {
                    HintText.Text = "查找指令发送失败";
                    return;
                }

                _finding = true;
                FindEarbudsText.Text = "停止查找";
                HintText.Text = "正在寻找耳机…（耳机会发出提示音）";
                StartFindTimer();
                Log.Write("查找耳机 → 开始");
            }
            else
            {
                bool ok = await _session.FindEarbudsAsync(false);
                ResetFindUi();
                HintText.Text = ok ? "" : "停止指令发送失败";
                Log.Write($"查找耳机 → 停止 {(ok ? "成功" : "失败")}");
            }
        }
        finally
        {
            FindEarbudsButton.IsEnabled = _session.Status == SessionStatus.Ready;
        }
    }

    private void StartFindTimer()
    {
        _findTimer?.Stop();
        _findTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _findTimer.Tick += async (_, _) =>
        {
            _findTimer?.Stop();
            if (!_finding)
                return;

            await _session.FindEarbudsAsync(false);
            ResetFindUi();
            HintText.Text = "已停止查找";
            Log.Write("查找耳机 → 自动停止");
        };
        _findTimer.Start();
    }

    private void ResetFindUi()
    {
        _findTimer?.Stop();
        _findTimer = null;
        _finding = false;
        FindEarbudsText.Text = "查找耳机";
    }

    /// <summary>设置页改了电量样式后调用。</summary>
    public void RefreshBatteryStyle()
    {
        _lastBattery = null;
        ApplyState();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HidePanel();

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        await _session.DisconnectAsync();
        await ConnectAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        _idleTimer.Stop();
        _pollTimer.Start();
        _ = EnsureConnectedAsync();

        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings, _session);
            _settingsWindow.HotkeysChanged += (_, _) => HotkeysChanged?.Invoke(this, EventArgs.Empty);
            _settingsWindow.BatteryStyleChanged += (_, _) => RefreshBatteryStyle();
            _settingsWindow.AppearanceChanged += (_, _) => OnAppearanceChanged();
            _settingsWindow.BlurChanged += (_, _) => OnBlurChanged();
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                if (!IsVisible)
                {
                    _pollTimer.Stop();
                    ArmIdleTimer();
                }
            };
        }

        _settingsWindow.ShowNear(this);
    }

    /// <summary>设置页改了热键，需要 App 重新注册。</summary>
    public event EventHandler? HotkeysChanged;

    public async Task ReconnectAsync()
    {
        _idleDisconnected = false;
        _reconnectAttempt = 0;
        _reconnectTimer.Stop();
        _idleTimer.Stop();

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
        await EnsureConnectedAsync();
        if (_session.Status != SessionStatus.Ready)
            return;

        int current = Array.FindIndex(AncItems, x => x.Mode == _session.State.Anc);
        var next = AncItems[(current + 1) % AncItems.Length];
        bool ok = await _session.SetAncAsync(next.Mode);
        ApplyState();
        if (ok)
        {
            int glyph = Array.Find(AncUiItems, x => x.Mode == next.Mode).Glyph;
            ShowHud("降噪", next.Name, glyph);
        }
    }

    /// <summary>循环切均衡器（热键用）。</summary>
    public async Task CycleEqAsync()
    {
        await EnsureConnectedAsync();
        if (_session.Status != SessionStatus.Ready)
            return;

        int current = Array.FindIndex(EqItems, x => x.Preset == _session.State.Eq);
        var next = EqItems[(current + 1) % EqItems.Length];
        bool ok = await _session.SetEqAsync(next.Preset);
        ApplyState();
        if (ok)
            ShowHud("均衡器", next.Name, 0xE9E9);
    }

    private void ShowHud(string title, string value, int glyph)
    {
        if (ProbeMode)
            return;

        _hud ??= new HudWindow();
        _hud.ShowMessage(title, value, char.ConvertFromUtf32(glyph));
    }

    public async Task ShutdownAsync()
    {
        IsExiting = true;
        _reconnectTimer.Stop();
        _idleTimer.Stop();
        _pollTimer.Stop();
        _hud?.Close();
        _hud = null;
        if (_finding)
        {
            await _session.FindEarbudsAsync(false);
            ResetFindUi();
        }
        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }
        await _session.DisposeAsync();
    }

    /// <summary>调试用：设置页快照。</summary>
    public void SnapshotSettings(string path) => _settingsWindow?.SaveSnapshotTabs(path);

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

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
        Log.Write($"快照已保存：{path}");
    }
}
