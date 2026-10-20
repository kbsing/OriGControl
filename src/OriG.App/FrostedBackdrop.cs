namespace OriG.App;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

/// <summary>
/// 磨砂背景：把用户指定的图片铺在窗口最底层，按设置的半径做高斯模糊。
/// </summary>
internal static class FrostedBackdrop
{
    /// <summary>向外多铺一圈，避免模糊半径把边缘吃出透明</summary>
    public const double Padding = 64;

    /// <summary>用用户指定的图片做磨砂底。未设置或读取失败时返回 false。</summary>
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
}
