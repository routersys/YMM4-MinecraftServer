using System.Globalization;
using System.Windows.Data;

namespace MinecraftHost.Converters;

public sealed class WidthThresholdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width)
        {
            return false;
        }

        var mode = "ge";
        var threshold = 980d;

        if (parameter is string parameterText && !string.IsNullOrWhiteSpace(parameterText))
        {
            var parts = parameterText.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                mode = parts[0].ToLowerInvariant();
                _ = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out threshold);
            }
            else if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedThreshold))
            {
                threshold = parsedThreshold;
            }
        }

        return mode switch
        {
            "lt" => width < threshold,
            "le" => width <= threshold,
            "gt" => width > threshold,
            _ => width >= threshold,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}