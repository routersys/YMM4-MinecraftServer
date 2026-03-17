using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Windows;

namespace MinecraftHost.Views;

public partial class OperationsCenterWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public OperationsCenterWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DataContext = new OperationsCenterViewModel();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}