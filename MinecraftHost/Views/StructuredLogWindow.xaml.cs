using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using System.Windows;

namespace MinecraftHost.Views;

public partial class StructuredLogWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public StructuredLogWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
    }
}