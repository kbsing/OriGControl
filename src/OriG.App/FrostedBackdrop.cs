namespace OriG.App;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

/// <summary>
/// 磨砂背景：抓取窗口所在区域的屏幕内容，高斯模糊后铺在窗口最底层。
///
/// 两个关键点（都踩过）：
/// 1. 不用 DWM 的亚克力 —— Win11 24H2/25H2 上
///    SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND) 返回成功但不做模糊，
///    半透明白底直接混到黑色上，整窗变灰。
/// 2. 抓屏必须在窗口显示之前做 —— AllowsTransparency=False 的窗口即使 Opacity=0
///    也会渲染成黑色，抓进来就把自己糊成了灰底。
/// </summary>
internal static class FrostedBackdrop
{
    /// <summary>向外多抓一圈，避免模糊半径把边缘吃出透明</summary>
    public const double Padding = 64;

    /// <summary>
    /// 用用户指定的图片做磨砂底。设置后无需抓屏，窗口可以正常被截图。
    /// </summary>
    public static bool ApplyImage(Image target, string path, double width, double height, double blurRadius)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            target.Source = image;
            target.Width = width + Padding * 2;
            target.Height = height + Padding * 2;
            target.Margin = new Thickness(-Padding);
            target.HorizontalAlignment = HorizontalAlignment.Left;
            target.VerticalAlignment = VerticalAlignment.Top;
            target.Stretch = Stretch.UniformToFill;
            target.Effect = CreateBlur(blurRadius);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"加载背景图片失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>半径 0 时不加效果，直接显示原图。</summary>
    public static BlurEffect? CreateBlur(double radius) =>
        radius <= 0.5
            ? null
            : new BlurEffect
            {
                Radius = radius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance,
            };

    /// <summary>
    /// 抓取指定矩形（DIP）的屏幕内容并应用到 <paramref name="target"/>。
    /// 调用时窗口必须尚未显示。
    /// </summary>
    public static bool Capture(Window window, Image target, double width, double height,
        double scale, double blurRadius = 16)
    {
        try
        {
            if (width < 2 || height < 2)
                return false;

            int pixelWidth = (int)Math.Ceiling((width + Padding * 2) * scale);
            int pixelHeight = (int)Math.Ceiling((height + Padding * 2) * scale);
            int screenX = (int)Math.Round((window.Left - Padding) * scale);
            int screenY = (int)Math.Round((window.Top - Padding) * scale);

            using var bitmap = new System.Drawing.Bitmap(
                pixelWidth, pixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screenX, screenY, 0, 0,
                    new System.Drawing.Size(pixelWidth, pixelHeight),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
            stream.Position = 0;

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            target.Source = decoder.Frames[0];
            // 必须显式给 DIP 尺寸：位图按设备像素抓取，高 DPI 下若按自然尺寸参与布局会把窗口撑大
            target.Width = width + Padding * 2;
            target.Height = height + Padding * 2;
            target.Margin = new Thickness(-Padding);
            target.HorizontalAlignment = HorizontalAlignment.Left;
            target.VerticalAlignment = VerticalAlignment.Top;
            target.Effect = CreateBlur(blurRadius);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"磨砂背景捕获失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>捕获失败时的退路：给个不透明浅色底，至少文字看得清。</summary>
    public static void ApplyFallback(Border card)
    {
        card.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE9));
    }

    /// <summary>取当前 DPI 缩放。窗口还没显示时退回主显示器缩放。</summary>
    public static double GetScale(Window window)
    {
        if (PresentationSource.FromVisual(window) is not null)
            return VisualTreeHelper.GetDpi(window).DpiScaleX;

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null || SystemParameters.PrimaryScreenWidth <= 0)
            return 1.0;

        return screen.Bounds.Width / SystemParameters.PrimaryScreenWidth;
    }
}
