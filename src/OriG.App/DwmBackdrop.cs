namespace OriG.App;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

/// <summary>
/// Win11 窗口外观：圆角、浅色边框、允许抓屏。
///
/// 注意不要用老的 SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)：
/// 在 Win11 24H2/25H2 上它返回成功但不做模糊，半透明白底直接混到黑色上，整窗变灰。
/// </summary>
internal static class DwmBackdrop
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void EnableRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        int preference = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    public static void UseLightFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        int light = 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref light, sizeof(int));
    }

    private const uint WdaNone = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    /// <summary>确保窗口可被捕获（自检截图时需要）。</summary>
    public static bool AllowCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && SetWindowDisplayAffinity(hwnd, WdaNone);
    }
}
