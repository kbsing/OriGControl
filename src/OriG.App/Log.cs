namespace OriG.App;

using System.IO;

/// <summary>
/// 极简文件日志。托盘应用没有控制台，出问题时靠它排查。
/// 位置：%LOCALAPPDATA%\OriGControl\log.txt
/// </summary>
internal static class Log
{
    private static readonly Lock Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OriGControl", "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // 超过 512KB 就重开，避免无限增长
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024)
                    File.Delete(FilePath);

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
