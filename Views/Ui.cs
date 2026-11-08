using Avalonia;
using Avalonia.Media;

namespace ButtonIsland.Views;

/// <summary>
/// 统一配色。
/// </summary>
public static class Ui
{
    public static readonly Color Panel = Color.Parse("#F00E1117");
    public static readonly Color Line = Color.Parse("#22FFFFFF");
    public static readonly Color TextPrimary = Color.Parse("#F2FFFFFF");
    public static readonly Color TextSecondary = Color.Parse("#8FFFFFFF");
    public static readonly Color Icon = Color.Parse("#B4FFFFFF");
    public static readonly Color Success = Color.Parse("#7FE0A0");
    public static readonly Color Danger = Color.Parse("#FF8F8F");

    public static readonly string[] Palette =
    {
        "#4C8DFF", "#34C759", "#FF9F0A", "#FF453A",
        "#BF5AF2", "#5AC8FA", "#FFD60A", "#A0A6B0"
    };

    public static SolidColorBrush Brush(Color color) => new(color);

    public static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    public static Color Tint(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? fallback : Color.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }
}
