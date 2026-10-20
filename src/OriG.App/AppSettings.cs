namespace OriG.App;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum BatteryStyle
{
    Ring,
    Bar,
}

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>用户设置，存 %LOCALAPPDATA%\OriGControl\settings.json。</summary>
public sealed class AppSettings
{
    public bool AutoStart { get; set; }

    public string HotkeyCycleAnc { get; set; } = "Ctrl+Alt+A";
    public string HotkeyCycleEq { get; set; } = "Ctrl+Alt+E";
    public string HotkeyTogglePanel { get; set; } = "Ctrl+Alt+O";

    public BatteryStyle BatteryStyle { get; set; } = BatteryStyle.Ring;

    /// <summary>自定义背景图路径，用作磨砂底。为空则卡片为纯色底（主题色）。</summary>
    public string BackgroundImagePath { get; set; } = "";

    /// <summary>背景模糊半径（0 = 不模糊）。默认偏小，保证还能看出背景图内容。</summary>
    public double BlurRadius { get; set; } = 16;

    /// <summary>明暗主题。深色适合夜间使用。</summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    public bool LowBatteryNotify { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;

    /// <summary>面板关闭后闲置多少分钟自动断开，0 = 从不。</summary>
    public int IdleDisconnectMinutes { get; set; } = 5;

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OriGControl", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"读取设置失败：{ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Write($"保存设置失败：{ex.Message}");
        }
    }
}
