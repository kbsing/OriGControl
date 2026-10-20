namespace OriG.App;

using System.Windows;

/// <summary>
/// 运行时切换明暗主题：替换 Application.Resources 里第 0 个合并字典。
/// 所有控件颜色都用 DynamicResource 引用，换字典即生效。
/// </summary>
internal static class ThemeManager
{
    private const int PaletteIndex = 0;

    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count <= PaletteIndex)
            return;

        var uri = new Uri(
            theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
            UriKind.Relative);

        var palette = new ResourceDictionary { Source = uri };
        dictionaries[PaletteIndex] = palette;
        Log.Write($"主题 → {theme}");
    }

    /// <summary>取当前调色板里的画刷，供代码绘制的部分使用。</summary>
    public static System.Windows.Media.Brush Brush(string key, string fallback)
    {
        if (Application.Current.TryFindResource(key) is System.Windows.Media.Brush brush)
            return brush;
        var solid = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback));
        solid.Freeze();
        return solid;
    }
}
