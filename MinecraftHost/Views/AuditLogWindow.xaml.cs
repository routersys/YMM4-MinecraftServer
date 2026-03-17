using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Windows;
namespace MinecraftHost.Views;

public partial class AuditLogWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public AuditLogWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DataContext = new AuditLogViewerViewModel();
    }
}