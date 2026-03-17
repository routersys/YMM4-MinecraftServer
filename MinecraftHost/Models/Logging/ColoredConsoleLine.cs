using System.Windows.Media;

namespace MinecraftHost.Models.Logging;

public class ColoredConsoleLine
{
    private static readonly SolidColorBrush DefaultBrush = CreateDefaultBrush();

    public string Text { get; set; } = string.Empty;
    public SolidColorBrush Foreground { get; set; } = DefaultBrush;

    private static SolidColorBrush CreateDefaultBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(190, 190, 190));
        brush.Freeze();
        return brush;
    }
}