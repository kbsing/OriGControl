namespace OriG.App;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

/// <summary>
/// 实时磨砂背景。
///
/// 核心技巧：用 SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) 让本窗口对屏幕捕获不可见，
/// 于是可以在窗口显示状态下持续抓取它背后的桌面，做高斯模糊后铺在窗口最底层。
/// 实测该排除在 GDI 的 CopyFromScreen 上确实生效（抓到的画面里看不到本窗口）。
///
/// 走过的弯路：
/// - DWM 亚克力（ACCENT_ENABLE_ACRYLICBLURBEHIND）在 Win11 25H2 返回成功但不模糊，整窗变灰。
/// - DWMWA_SYSTEMBACKDROP_TYPE 对 WindowStyle=None 的弹窗返回 S_OK 但同样不渲染。
/// - 窗口显示前抓屏再显示：模糊是静态的，效果一般。
/// </summary>
internal sealed class LiveBackdrop : IDisposable
{
    private const double Padding = 64;
    private const int IntervalMs = 90;

    private readonly Window _window;
    private readonly Image _target;
    private readonly DispatcherTimer _timer;

    private System.Drawing.Bitmap? _bitmap;
    private WriteableBitmap? _writeable;
    private byte[]? _buffer;
    private int _pixelWidth;
    private int _pixelHeight;
    private int _stride;
    private double _scale = 1.0;
    private double _dipWidth;
    private double _dipHeight;

    public LiveBackdrop(Window window, Image target)
    {
        _window = window;
        _target = target;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(IntervalMs),
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start(double dipWidth, double dipHeight, double blurRadius)
    {
        _dipWidth = dipWidth;
        _dipHeight = dipHeight;
        _scale = FrostedBackdrop.GetScale(_window);

        _target.Effect = FrostedBackdrop.CreateBlur(blurRadius);

        int width = (int)Math.Ceiling((dipWidth + Padding * 2) * _scale);
        int height = (int)Math.Ceiling((dipHeight + Padding * 2) * _scale);

        if (width < 4 || height < 4)
            return;

        if (_pixelWidth != width || _pixelHeight != height)
            Allocate(width, height);

        _target.Width = dipWidth + Padding * 2;
        _target.Height = dipHeight + Padding * 2;
        _target.Margin = new Thickness(-Padding);
        _target.HorizontalAlignment = HorizontalAlignment.Left;
        _target.VerticalAlignment = VerticalAlignment.Top;

        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Allocate(int width, int height)
    {
        _bitmap?.Dispose();

        _pixelWidth = width;
        _pixelHeight = height;
        _stride = width * 4;
        _bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        _buffer = new byte[_stride * height];

        _writeable = new WriteableBitmap(width, height, 96 * _scale, 96 * _scale, PixelFormats.Pbgra32, null);
        _target.Source = _writeable;
    }

    private void Refresh()
    {
        if (_bitmap is null || _writeable is null || _buffer is null || !_window.IsVisible)
            return;

        try
        {
            int x = (int)Math.Round((_window.Left - Padding) * _scale);
            int y = (int)Math.Round((_window.Top - Padding) * _scale);

            using (var graphics = System.Drawing.Graphics.FromImage(_bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0,
                    new System.Drawing.Size(_pixelWidth, _pixelHeight),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var data = _bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, _pixelWidth, _pixelHeight),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == _stride)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _buffer, 0, _buffer.Length);
                }
                else
                {
                    for (int row = 0; row < _pixelHeight; row++)
                    {
                        IntPtr source = data.Scan0 + row * data.Stride;
                        System.Runtime.InteropServices.Marshal.Copy(
                            source, _buffer, row * _stride, _stride);
                    }
                }
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }

            _writeable.WritePixels(
                new Int32Rect(0, 0, _pixelWidth, _pixelHeight), _buffer, _stride, 0);
        }
        catch (Exception ex)
        {
            Log.Write($"实时背景刷新失败：{ex.Message}");
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
        _writeable = null;
    }
}
