namespace OriG.App;

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OriG.Core;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HeadsetSession _session;

    private bool _loading;
    private bool _captureExcluded;
    private LiveBackdrop? _backdrop;
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

    public bool HasBackgroundImage =>
        !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath)
        && System.IO.File.Exists(_settings.BackgroundImagePath);

    public SettingsWindow(AppSettings settings, HeadsetSession session)
    {
        _settings = settings;
        _session = session;

        InitializeComponent();
        SourceInitialized += (_, _) => ApplyMaterial();

        AboutText.Text = "OriG Control 0.1.0 · 非官方第三方工具，与原道 / NiceHCK 无关联";

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };

        LoadValues();
    }

    private void ApplyMaterial()
    {
        DwmBackdrop.UseLightFrame(this);
        DwmBackdrop.EnableRoundedCorners(this);
    }

    /// <summary>只有「实时模糊」才需要让窗口对捕获隐身。</summary>
    private void ApplyCapturePolicyBeforeShow()
    {
        if (_settings.LiveBlur && !HasBackgroundImage)
        {
            _captureExcluded = DwmBackdrop.ExcludeFromCapture(this);
        }
        else
        {
            DwmBackdrop.AllowCapture(this);
            _captureExcluded = false;
        }
    }

    private bool IsDark => _settings.Theme == AppTheme.Dark;

    private Brush CardScrim(bool hasBackdrop) => new SolidColorBrush((hasBackdrop, IsDark) switch
    {
        (true, false) => Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
        (true, true) => Color.FromArgb(0xD9, 0x1A, 0x1D, 0x22),
        (false, false) => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        (false, true) => Color.FromArgb(0xFF, 0x1A, 0x1D, 0x22),
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

            foreach (ComboBoxItem item in ThresholdBox.Items)
            {
                if (item.Tag is string tag && tag == _settings.LowBatteryThreshold.ToString())
                {
                    ThresholdBox.SelectedItem = item;
                    break;
                }
            }
            if (ThresholdBox.SelectedItem is null && ThresholdBox.Items.Count > 0)
                ThresholdBox.SelectedIndex = 1;

            ThresholdBox.IsEnabled = _settings.LowBatteryNotify;
            RingChip.IsChecked = _settings.BatteryStyle == BatteryStyle.Ring;
            BarChip.IsChecked = _settings.BatteryStyle == BatteryStyle.Bar;

            UpdateBackgroundText();

            BlurSlider.Value = Math.Clamp(_settings.BlurRadius, BlurSlider.Minimum, BlurSlider.Maximum);
            BlurValueText.Text = ((int)BlurSlider.Value).ToString();

            LiveBlurBox.IsChecked = _settings.LiveBlur;
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

    private void LiveBlur_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _settings.LiveBlur = LiveBlurBox.IsChecked == true;
        _settings.Save();
        RefreshBackdropNow();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        Log.Write($"实时模糊 → {_settings.LiveBlur}");
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
            : "未设置（使用实时模糊）";
    }

    public void ShowNear(Window owner)
    {
        SizeToContent = SizeToContent.Manual;
        RootGrid.Measure(new Size(Width, double.PositiveInfinity));
        double contentHeight = RootGrid.DesiredSize.Height;
        if (contentHeight < 40)
            contentHeight = 480;
        Height = contentHeight;

        double left = owner.Left - Width - 10;
        if (left < 0)
            left = owner.Left + owner.ActualWidth + 10;

        Left = left;
        Top = Math.Max(0, owner.Top + owner.ActualHeight - contentHeight);

        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath, Width, contentHeight, _settings.BlurRadius);

        bool wantBlur = !imageMode && _settings.LiveBlur;
        bool liveBlur = wantBlur && _captureExcluded;

        if (wantBlur && !_captureExcluded)
        {
            double scale = FrostedBackdrop.GetScale(this);
            FrostedBackdrop.Capture(this, BackdropImage, Width, contentHeight, scale, _settings.BlurRadius);
        }

        bool hasBackdrop = imageMode || wantBlur;
        if (!hasBackdrop)
        {
            _backdrop?.Stop();
            BackdropImage.Source = null;
        }

        RootCard.Background = CardScrim(hasBackdrop);

        ApplyCapturePolicyBeforeShow();

        Opacity = 1;
        Show();

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
        Activate();
    }

    /// <summary>只刷新自身背景的视觉，不改抓屏策略（那会破坏合成表面）。</summary>
    private void RefreshBackdropNow()
    {
        bool imageMode = HasBackgroundImage
            && FrostedBackdrop.ApplyImage(BackdropImage, _settings.BackgroundImagePath, Width, ActualHeight, _settings.BlurRadius);

        bool wantBlur = !imageMode && _settings.LiveBlur;

        if (!imageMode && !wantBlur)
        {
            _backdrop?.Stop();
            BackdropImage.Source = null;
        }

        RootCard.Background = CardScrim(imageMode || wantBlur);

        if (wantBlur && _captureExcluded && IsVisible)
        {
            _backdrop ??= new LiveBackdrop(this, BackdropImage);
            _backdrop.Start(Width, ActualHeight, _settings.BlurRadius);
        }
        else
        {
            _backdrop?.Stop();
        }
    }

    private void HidePanel()
    {
        _backdrop?.Stop();
        Hide();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250)
            return;
        HidePanel();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

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

        if (e.Key == Key.Escape)
        {
            LoadValues();
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            box.Text = "";
            SaveHotkey(box, "");
            return;
        }

        string? gesture = HotkeyService.Format(e.Key, Keyboard.Modifiers);
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
        _settings.AutoStart = enabled;
        _settings.Save();
        Log.Write($"开机自启 → {enabled}");
    }

    private void LowBattery_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _settings.LowBatteryNotify = LowBatteryBox.IsChecked == true;
        _settings.Save();
        ThresholdBox.IsEnabled = _settings.LowBatteryNotify;
    }

    private void Threshold_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThresholdBox.SelectedItem is not ComboBoxItem item)
            return;

        if (item.Tag is string tag && int.TryParse(tag, out int value))
        {
            _settings.LowBatteryThreshold = value;
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

        if (dialog.ShowDialog(this) != true)
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
        Log.Write("清除背景图片，回到实时模糊");
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

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}
