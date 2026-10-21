namespace OriG.App;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

/// <summary>把窗口放到鼠标所在/拥有者所在的屏幕，而不是固定主屏。</summary>
internal static class ScreenWorkArea
{
    public static Rect ForCursor(Visual visual)
    {
        Screen screen = Screen.FromPoint(Cursor.Position);
        return ToDip(screen.WorkingArea, VisualTreeHelper.GetDpi(visual));
    }

    public static Rect ForWindow(Window window)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        var bounds = new System.Drawing.Rectangle(
            (int)Math.Round(window.Left * dpi.DpiScaleX),
            (int)Math.Round(window.Top * dpi.DpiScaleY),
            Math.Max(1, (int)Math.Round(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(window.ActualHeight * dpi.DpiScaleY)));

        return ToDip(Screen.FromRectangle(bounds).WorkingArea, dpi);
    }

    private static Rect ToDip(System.Drawing.Rectangle area, DpiScale dpi)
    {
        return new Rect(
            area.X / dpi.DpiScaleX,
            area.Y / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX,
            area.Height / dpi.DpiScaleY);
    }
}
