using MinecraftHost.Models.Logging;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace MinecraftHost.Services.Logging;

public static class ConsoleLineColorizer
{
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*[mKJH]", RegexOptions.Compiled);
    private static readonly Regex ErrorRegex = new(@"\b(FATAL|ERROR)\b", RegexOptions.Compiled);
    private static readonly Regex WarnRegex = new(@"\bWARN\b", RegexOptions.Compiled);
    private static readonly Regex DebugRegex = new(@"\bDEBUG\b", RegexOptions.Compiled);

    private static readonly SolidColorBrush ErrorBrush = CreateFrozenBrush(255, 100, 100);
    private static readonly SolidColorBrush WarnBrush = CreateFrozenBrush(255, 200, 60);
    private static readonly SolidColorBrush DebugBrush = CreateFrozenBrush(110, 110, 110);
    private static readonly SolidColorBrush JoinBrush = CreateFrozenBrush(63, 185, 80);
    private static readonly SolidColorBrush LeaveBrush = CreateFrozenBrush(180, 120, 80);
    private static readonly SolidColorBrush DefaultBrush = CreateFrozenBrush(190, 190, 190);

    public static ColoredConsoleLine Colorize(string rawLine)
    {
        var clean = AnsiEscapeRegex.Replace(rawLine, string.Empty);
        return new ColoredConsoleLine { Text = clean, Foreground = ResolveBrush(clean) };
    }

    private static SolidColorBrush ResolveBrush(string line)
    {
        if (ErrorRegex.IsMatch(line))
            return ErrorBrush;
        if (WarnRegex.IsMatch(line))
            return WarnBrush;
        if (DebugRegex.IsMatch(line))
            return DebugBrush;
        if (line.Contains("joined the game"))
            return JoinBrush;
        if (line.Contains("left the game"))
            return LeaveBrush;
        return DefaultBrush;
    }

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}