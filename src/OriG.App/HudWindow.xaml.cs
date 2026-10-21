namespace OriG.App;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

/// <summary>热键切档时右下角短暂出现的提示浮层，不抢焦点、不挡点击。</summary>
public partial class HudWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1500),
    };

    private bool _hiding;

    public HudWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
    }

    public void ShowMessage(string title, string value, string glyph)
    {
        TitleText.Text = title;
        ValueText.Text = value;
        GlyphText.Text = glyph;

        _hiding = false;
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (!IsVisible)
            Show();

        UpdateLayout();
        Position();
        _hideTimer.Start();
    }

    private void Position()
    {
        Rect area = ScreenWorkArea.ForCursor(this);
        Left = Math.Max(area.Left + 12, area.Right - ActualWidth - 24);
        Top = Math.Max(area.Top + 12, area.Bottom - ActualHeight - 28);
    }

    private void FadeOut()
    {
        if (_hiding)
            return;

        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            _hiding = false;
            Hide();
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>调试用：把浮层渲染成 PNG。</summary>
    public void SaveSnapshot(string path)
    {
        UpdateLayout();

        double w = (HudCard.ActualWidth > 0 ? HudCard.ActualWidth : 220)
                   + HudCard.Margin.Left + HudCard.Margin.Right;
        double h = (HudCard.ActualHeight > 0 ? HudCard.ActualHeight : 60)
                   + HudCard.Margin.Top + HudCard.Margin.Bottom;

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(HudCard);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}
