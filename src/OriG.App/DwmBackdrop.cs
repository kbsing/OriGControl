namespace OriG.App;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

/// <summary>
/// Win11 官方系统背景材质（Mica / Acrylic）。
///
/// 注意不要用老的 SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)：
/// 在 Win11 24H2/25H2 上它返回成功但不做模糊，半透明白底直接混到黑色上，整窗变灰。
/// DWMWA_SYSTEMBACKDROP_TYPE 是官方接口，实时模糊，Windows 自己的弹窗就用它。
/// </summary>
internal static class DwmBackdrop
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>DWMSBT_TRANSIENTWINDOW —— 亚克力，用于浮出层/弹窗</summary>
    public const int Acrylic = 3;

    /// <summary>DWMSBT_MAINWINDOW —— Mica，用于主窗口</summary>
    public const int Mica = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 开启系统背景材质。要求窗口带 DWM 边框（WindowStyle 不能是 None），
    /// 且 AllowsTransparency=False。
    /// </summary>
    public static bool Enable(Window window, int backdropType)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        // 把边框延伸到整个客户区，否则材质只画在标题栏那条上
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int type = backdropType;
        int hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
        Log.Write($"系统背景材质 type={backdropType} → HRESULT=0x{hr:X8}");
        return hr == 0;
    }

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
