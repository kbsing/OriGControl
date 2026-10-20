namespace OriG.App;

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OriG.Core;
using OriG.Protocol;
using TouchActionKind = OriG.Protocol.TouchAction;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HeadsetSession _session;

    private bool _loading;
    private bool _modalOpen;
    private DateTime _shownAt = DateTime.MinValue;

    public event EventHandler? HotkeysChanged;
    public event EventHandler? BatteryStyleChanged;
    public event EventHandler? AppearanceChanged;

    /// <summary>只改了模糊程度（不需要重新加载背景图）。</summary>
    public event EventHandler? BlurChanged;

    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };

    private readonly System.Windows.Threading.DispatcherTimer _toneTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350),
    };

    private readonly Dictionary<ComboBox, (byte Side, byte Gesture)> _touchBoxes = [];
    private readonly Dictionary<CheckBox, (byte Side, byte Gesture)> _touchEnableBoxes = [];
    private bool _toneBusy;
    private int _pendingTone;

    public bool HasBackgroundImage =>
        !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath)
        && System.IO.File.Exists(_settings.BackgroundImagePath);

    public SettingsWindow(AppSettings settings, HeadsetSession session)
    {
        _settings = settings;
        _session = session;

        _loading = true;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyMaterial();

        AboutText.Text = "非官方第三方工具，与原道 / NiceHCK 无关联";

        string? version = typeof(App).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionValueText.Text = string.IsNullOrWhiteSpace(version)
            ? "未知"
            : version.Split('+')[0];

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };

        _toneTimer.Tick += async (_, _) =>
        {
            _toneTimer.Stop();
            await SendToneVolumeAsync();
        };

        BuildTouchEditor();
        LoadValues();
        ApplyHeadsetState();
        GeneralTab.IsChecked = true;
        LeftSideChip.IsChecked = true;

        _session.StateChanged += Session_StateChanged;
        _session.StatusChanged += Session_StatusChanged;
        Closed += (_, _) =>
        {
            _session.StateChanged -= Session_StateChanged;
            _session.StatusChanged -= Session_StatusChanged;
            _toneTimer.Stop();
        };
    }

    private void BuildTouchEditor()
    {
        BuildTouchSide(TouchSide.Left, LeftTouchPanel);
        BuildTouchSide(TouchSide.Right, RightTouchPanel);
    }

    private void BuildTouchSide(byte side, Panel host)
    {
        var comboStyle = (Style)FindResource("Combo");
        var rowStyle = (Style)FindResource("RowTitle");
        var miniStyle = (Style)FindResource("MiniSwitch");

        for (int i = 0; i < TouchGesture.All.Length; i++)
        {
            byte gesture = TouchGesture.All[i];

            var title = new TextBlock
            {
                Text = TouchGesture.Name(gesture),
                Style = rowStyle,
                FontSize = 12.5,
            };

            var combo = new ComboBox
            {
                Style = comboStyle,
                Width = 132,
                Margin = new Thickness(0, 0, 12, 0),
                Tag = (side, gesture),
                VerticalAlignment = VerticalAlignment.Center,
            };

            foreach (byte action in TouchActionKind.All)
                combo.Items.Add(new ComboBoxItem { Content = TouchActionKind.Name(action), Tag = action });

            combo.SelectionChanged += Touch_SelectionChanged;
            _touchBoxes[combo] = (side, gesture);

            var enable = new CheckBox
            {
                Style = miniStyle,
                Tag = (side, gesture),
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            enable.Checked += TouchEnable_Changed;
            enable.Unchecked += TouchEnable_Changed;
            _touchEnableBoxes[enable] = (side, gesture);

            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(combo, 1);
            Grid.SetColumn(enable, 2);
            row.Children.Add(title);
            row.Children.Add(combo);
            row.Children.Add(enable);

            if (host.Children.Count > 0)
            {
                var divider = new Border { Height = 1 };
                divider.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
                host.Children.Add(divider);
            }

            host.Children.Add(row);
        }
    }

    private void Session_StateChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ApplyHeadsetState);

    private void Session_StatusChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ApplyHeadsetState);

    private void ApplyHeadsetState()
    {
        var state = _session.State;
        bool ready = _session.Status == SessionStatus.Ready;
        HeadsetPanel.IsEnabled = ready;
        TouchEditorPanel.IsEnabled = ready;
        ThresholdSlider.IsEnabled = _settings.LowBatteryNotify;

        _loading = true;
        try
        {
            if (!_toneBusy && !_toneTimer.IsEnabled)
            {
                if (state.Query102 is int volume)
                {
                    ToneSlider.Value = Math.Clamp(volume, 0, 16);
                    ToneValueText.Text = volume.ToString();
                    _pendingTone = volume;
                }
                else
                {
                    ToneValueText.Text = "未读取";
                }
            }

            CodecValueText.Text = CodecProtocol.Describe(state.Query104);
            TouchCycleText.Text = TouchConfig.AncCycleText(state.Query10A);
            DeviceNameValueText.Text = string.IsNullOrWhiteSpace(_session.DeviceName)
                ? "原道耳机"
                : _session.DeviceName;
            FirmwareValueText.Text = state.FirmwareMain < 0 ? "未读取" : state.FirmwareText;

            if (!ready)
            {
                HeadsetHintText.Text = "耳机未连接";
                return;
            }

            if (HeadsetHintText.Text == "耳机未连接")
                HeadsetHintText.Text = "";

            foreach (var (combo, cell) in _touchBoxes)
            {
                var binding = TouchConfig.Find(state.ConfigTable, cell.Side, cell.Gesture);
                int action = binding?.Action ?? -1;
                ComboBoxItem? match = null;
                foreach (ComboBoxItem item in combo.Items)
                {
                    if (item.Tag is byte value && value == action)
                    {
                        match = item;
                        break;
                    }
                }
                combo.SelectedItem = match;
                combo.ToolTip = binding is null
                    ? "未读取该触控项"
                    : $"协议值：{binding.Value.Side:X2} {binding.Value.Enabled:X2} {binding.Value.Gesture:X2} {binding.Value.Action:X2}";
            }

            foreach (var (box, cell) in _touchEnableBoxes)
            {
                var binding = TouchConfig.Find(state.ConfigTable, cell.Side, cell.Gesture);
                box.IsEnabled = binding is not null;
                box.IsChecked = binding is { Enabled: 1 };
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void Tone_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int value = (int)Math.Round(e.NewValue);
        ToneValueText.Text = value.ToString();

        if (_loading)
            return;

        _pendingTone = value;
        _toneTimer.Stop();
        _toneTimer.Start();
    }

    private async Task SendToneVolumeAsync()
    {
        if (_toneBusy || _session.Status != SessionStatus.Ready)
            return;

        _toneBusy = true;
        ToneSlider.IsEnabled = false;
        bool ok;
        try
        {
            ok = await _session.SetToneVolumeAsync((byte)_pendingTone);
            HeadsetHintText.Text = ok ? "" : "耳机未确认提示音音量";
        }
        finally
        {
            _toneBusy = false;
            ToneSlider.IsEnabled = _session.Status == SessionStatus.Ready;
        }

        if (!ok)
            ApplyHeadsetState();
    }

    private async void Touch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading
            || sender is not ComboBox { Tag: ValueTuple<byte, byte> cell, SelectedItem: ComboBoxItem { Tag: byte action } })
        {
            return;
        }

        TouchEditorPanel.IsEnabled = false;
        try
        {
            byte enabled = TouchConfig.Find(_session.State.ConfigTable, cell.Item1, cell.Item2)?.Enabled ?? 1;
            bool ok = await _session.SetTouchActionAsync(cell.Item1, cell.Item2, action, enabled);
            HeadsetHintText.Text = ok ? "" : "耳机未确认触控设置";
            if (!ok)
                ApplyHeadsetState();
        }
        finally
        {
            TouchEditorPanel.IsEnabled = _session.Status == SessionStatus.Ready;
        }
    }

    private async void TouchEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading
            || sender is not CheckBox { Tag: ValueTuple<byte, byte> cell } box)
        {
            return;
        }

        var binding = TouchConfig.Find(_session.State.ConfigTable, cell.Item1, cell.Item2);
        if (binding is null)
            return;

        byte enabled = (byte)(box.IsChecked == true ? 1 : 0);
        TouchEditorPanel.IsEnabled = false;
        try
        {
            bool ok = await _session.SetTouchActionAsync(
                cell.Item1, cell.Item2, binding.Value.Action, enabled);
            HeadsetHintText.Text = ok ? "" : "耳机未确认触控设置";
            if (!ok)
                ApplyHeadsetState();
        }
        finally
        {
            TouchEditorPanel.IsEnabled = _session.Status == SessionStatus.Ready;
        }
    }

    private void ApplyMaterial()
    {
        DwmBackdrop.UseLightFrame(this);
        DwmBackdrop.EnableRoundedCorners(this);
    }

    private bool IsDark => _settings.Theme == AppTheme.Dark;

    private Brush CardScrim(bool hasBackdrop) => new SolidColorBrush((hasBackdrop, IsDark) switch
    {
        (true, false) => Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
        (true, true) => Color.FromArgb(0xE6, 0x18, 0x1A, 0x17),
        (false, false) => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        (false, true) => Color.FromArgb(0xFF, 0x18, 0x1A, 0x17),
    });

    private void LoadValues()
    {
        _loading = true;
        try
        {
            AncHotkeyBox.Text = _settings.HotkeyCycleAnc;
            EqHotkeyBox.Text = _settings.HotkeyCycleEq;
            PanelHotkeyBox.Text = _settings.HotkeyTogglePanel;

            AutoStartBox.IsChecked = AutoStart.IsEnabled();
            LowBatteryBox.IsChecked = _settings.LowBatteryNotify;

            ThresholdSlider.Value = Math.Clamp(
                _settings.LowBatteryThreshold, ThresholdSlider.Minimum, ThresholdSlider.Maximum);
            ThresholdValueText.Text = $"{(int)ThresholdSlider.Value}%";
            ThresholdSlider.IsEnabled = _settings.LowBatteryNotify;
            SelectIdleDisconnect(_settings.IdleDisconnectMinutes);
            RingChip.IsChecked = _settings.BatteryStyle == BatteryStyle.Ring;
            BarChip.IsChecked = _settings.BatteryStyle == BatteryStyle.Bar;

            UpdateBackgroundText();

            BlurSlider.Value = Math.Clamp(_settings.BlurRadius, BlurSlider.Minimum, BlurSlider.Maximum);
            BlurValueText.Text = ((int)BlurSlider.Value).ToString();

            LightChip.IsChecked = _settings.Theme == AppTheme.Light;
            DarkChip.IsChecked = _settings.Theme == AppTheme.Dark;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Blur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int value = (int)Math.Round(e.NewValue);
        BlurValueText.Text = value.ToString();

        if (_loading)
            return;

        _settings.BlurRadius = value;
        _saveTimer.Stop();
        _saveTimer.Start();

        // 立即预览，不用重载图片
        BackdropImage.Effect = FrostedBackdrop.CreateBlur(value);
        BlurChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _settings.Theme = DarkChip.IsChecked == true ? AppTheme.Dark : AppTheme.Light;
        _settings.Save();
        ThemeManager.Apply(_settings.Theme);
        RefreshBackdropNow();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateBackgroundText()
    {
        BackgroundImageText.Text = HasBackgroundImage
            ? System.IO.Path.GetFileName(_settings.BackgroundImagePath)
            : "未设置";
    }

    public void ShowNear(Window owner)
    {
        Width = 460;
        Height = Math.Min(620, Math.Max(420, SystemParameters.WorkArea.Height - 40));

        double left = owner.Left - Width - 10;
        if (left < 0)
            left = owner.Left + owner.ActualWidth + 10;

        Left = left;
        Top = Math.Max(0, owner.Top + owner.ActualHeight - Height);

        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath, Width, Height, _settings.BlurRadius);

        if (!imageMode)
            BackdropImage.Source = null;

        RootCard.Background = CardScrim(imageMode);

        Opacity = 1;
        Show();

        // 测量时内容还没溢出，滚动条出现后需要重新量一次宽度，否则右侧会被裁掉
        ContentScroller.InvalidateMeasure();
        UpdateLayout();

        _shownAt = DateTime.UtcNow;
        Activate();
    }

    /// <summary>只刷新自身背景的视觉。</summary>
    private void RefreshBackdropNow()
    {
        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath, Width, ActualHeight, _settings.BlurRadius);

        if (!imageMode)
            BackdropImage.Source = null;

        RootCard.Background = CardScrim(imageMode);
    }

    private void HidePanel()
    {
        Hide();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_modalOpen)
            return;

        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250)
            return;
        HidePanel();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    // ------------------------------------------------------------- 页签

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralPanel is null)
            return;

        int index = sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int value)
            ? value
            : 0;
        SelectTab(index);
    }

    public void SelectTab(int index)
    {
        if (GeneralPanel is null)
            return;

        GeneralPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        GesturePanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        RadioButton tab = index switch
        {
            1 => GestureTab,
            2 => AudioTab,
            3 => AboutTab,
            _ => GeneralTab,
        };
        if (tab.IsChecked != true)
            tab.IsChecked = true;

        ContentScroller.ScrollToTop();
    }

    private void TouchSide_Checked(object sender, RoutedEventArgs e)
    {
        if (LeftTouchCard is null)
            return;

        bool left = LeftSideChip.IsChecked == true;
        LeftTouchCard.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
        RightTouchCard.Visibility = left ? Visibility.Collapsed : Visibility.Visible;
        TouchSideLabel.Text = left ? "左耳触控" : "右耳触控";
    }

    // ------------------------------------------------------------- 热键

    private void Hotkey_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        e.Handled = true;

        // Alt+键在 WPF 里是 Key.System，真实键在 SystemKey 里。
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            LoadValues();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            box.Text = "";
            SaveHotkey(box, "");
            return;
        }

        string? gesture = HotkeyService.Format(key, Keyboard.Modifiers);
        if (gesture is null)
            return;

        box.Text = gesture;
        SaveHotkey(box, gesture);
    }

    private void SaveHotkey(TextBox box, string value)
    {
        switch (box.Name)
        {
            case nameof(AncHotkeyBox):
                _settings.HotkeyCycleAnc = value;
                break;
            case nameof(EqHotkeyBox):
                _settings.HotkeyCycleEq = value;
                break;
            case nameof(PanelHotkeyBox):
                _settings.HotkeyTogglePanel = value;
                break;
            default:
                return;
        }

        _settings.Save();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        Log.Write($"热键 {box.Name} → \"{value}\"");
    }

    // ------------------------------------------------------------- 其它设置

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        bool enabled = AutoStartBox.IsChecked == true;
        AutoStart.Set(enabled);
        Log.Write($"开机自启 → {enabled}");
    }

    private void LowBattery_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _settings.LowBatteryNotify = LowBatteryBox.IsChecked == true;
        _settings.Save();
        ThresholdSlider.IsEnabled = _settings.LowBatteryNotify;
    }

    private void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int value = (int)Math.Round(e.NewValue);
        if (ThresholdValueText is not null)
            ThresholdValueText.Text = $"{value}%";

        if (_loading)
            return;

        _settings.LowBatteryThreshold = value;
        _settings.Save();
    }

    private void SelectIdleDisconnect(int minutes)
    {
        foreach (ComboBoxItem item in IdleDisconnectBox.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int value) && value == minutes)
            {
                IdleDisconnectBox.SelectedItem = item;
                return;
            }
        }
        if (IdleDisconnectBox.Items.Count > 0)
            IdleDisconnectBox.SelectedIndex = 1;
    }

    private void IdleDisconnect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IdleDisconnectBox.SelectedItem is not ComboBoxItem item)
            return;

        if (item.Tag is string tag && int.TryParse(tag, out int value))
        {
            _settings.IdleDisconnectMinutes = value;
            _settings.Save();
        }
    }

    private void BatteryStyle_Checked(object sender, RoutedEventArgs e)    {
        if (_loading)
            return;

        _settings.BatteryStyle = RingChip.IsChecked == true ? BatteryStyle.Ring : BatteryStyle.Bar;
        _settings.Save();
        BatteryStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- 背景图片

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|所有文件|*.*",
            CheckFileExists = true,
        };

        // 选图期间会触发 Deactivated，先挡住自动隐藏
        _modalOpen = true;
        bool? picked;
        try
        {
            picked = dialog.ShowDialog(this);
        }
        finally
        {
            _modalOpen = false;
        }

        if (picked != true)
            return;

        _settings.BackgroundImagePath = dialog.FileName;
        _settings.Save();
        UpdateBackgroundText();
        RefreshBackdropNow();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        Log.Write($"设置背景图片：{dialog.FileName}");
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundImagePath = "";
        _settings.Save();
        UpdateBackgroundText();
        RefreshBackdropNow();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        Log.Write("清除背景图片，卡片改为纯色底");
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.File.Exists(Log.FilePath))
                Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe",
                    $"/select,\"{Log.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"打开日志失败：{ex.Message}");
        }
    }

    /// <summary>调试用：把设置页的四个页签分别渲染成 PNG。</summary>
    public void SaveSnapshotTabs(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        string directory = System.IO.Path.GetDirectoryName(fullPath)!;
        string name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
        string extension = System.IO.Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(extension))
            extension = ".png";

        string[] tabs = ["general", "gesture", "audio", "about"];
        for (int i = 0; i < tabs.Length; i++)
        {
            SelectTab(i);
            UpdateLayout();
            SaveSnapshot(System.IO.Path.Combine(directory, $"{name}-{tabs[i]}{extension}"));
        }

        SelectTab(0);
    }

    /// <summary>调试用：把设置页渲染成 PNG。</summary>
    public void SaveSnapshot(string path)
    {
        UpdateLayout();

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 480;
        const double scale = 2.0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE9, 0xEB, 0xEF)), null, new Rect(0, 0, w, h));

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
    }
}
