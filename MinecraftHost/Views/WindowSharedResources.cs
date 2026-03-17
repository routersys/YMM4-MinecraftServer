using System.Windows;

namespace MinecraftHost.Views;

internal static class WindowSharedResources
{
    private static readonly Uri MainPageStylesUri = new("pack://application:,,,/MinecraftHost;component/Views/MainPageStyles.xaml", UriKind.Absolute);

    public static void Apply(Window window)
    {
        var target = Application.Current?.Resources ?? window.Resources;
        foreach (var dictionary in target.MergedDictionaries)
        {
            if (Uri.Compare(dictionary.Source, MainPageStylesUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
                return;
        }

        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = MainPageStylesUri
        });
    }
}
