namespace OriG.App;

using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

/// <summary>全局热键。用消息专用窗口接收 WM_HOTKEY。</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private const int ProbeId = 0x7FFF;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private readonly Dictionary<int, string> _gestures = [];
    private readonly List<int> _registered = [];

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("OriGHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>注册一个热键，返回是否成功（可能被其它程序占用）。</summary>
    public bool Register(int id, string gesture, Action action)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            Unregister(id);
            return false;
        }

        if (_registered.Contains(id)
            && _gestures.TryGetValue(id, out string? current)
            && string.Equals(current, gesture, StringComparison.OrdinalIgnoreCase))
        {
            _handlers[id] = action;
            return true;
        }

        if (!TryParse(gesture, out uint modifiers, out uint vk))
            return false;

        // 先探测新组合是否空闲。若直接 Unregister 旧键再注册新键，
        // 新键被占用时会把原本可用的热键也弄丢。
        if (!IsGestureFree(modifiers, vk))
        {
            Log.Write($"热键注册失败：{gesture}（可能已被占用）");
            return false;
        }

        Unregister(id);
        if (!RegisterHotKey(_source.Handle, id, modifiers, vk))
            return false;

        _handlers[id] = action;
        _gestures[id] = gesture;
        _registered.Add(id);
        Log.Write($"热键已注册：{gesture} → id {id}");
        return true;
    }

    private bool IsGestureFree(uint modifiers, uint vk)
    {
        if (!RegisterHotKey(_source.Handle, ProbeId, modifiers, vk))
            return false;

        UnregisterHotKey(_source.Handle, ProbeId);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(_source.Handle, id);

        _handlers.Remove(id);
        _gestures.Remove(id);
    }

    // ---------------------------------------------------------------- 解析

    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    if (!TryParseKey(part, out vk))
                        return false;
                    break;
            }
        }

        return vk != 0 && modifiers != 0;
    }

    private static bool TryParseKey(string text, out uint vk)
    {
        vk = 0;
        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }
        if (text.Length is 2 or 3 && (text[0] == 'F' || text[0] == 'f')
            && int.TryParse(text[1..], out int fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1);
            return true;
        }
        return false;
    }

    /// <summary>把按键事件格式化成 "Ctrl+Alt+A"，供设置页捕获用。</summary>
    public static string? Format(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System or Key.None)
            return null;

        string main = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            _ => "",
        };
        if (main.Length == 0)
            return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0)
            return null;

        parts.Add(main);
        return string.Join('+', parts);
    }

    public void Dispose()
    {
        foreach (int id in _registered.ToArray())
            UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _handlers.Clear();
        _gestures.Clear();
        _source.Dispose();
    }
}
