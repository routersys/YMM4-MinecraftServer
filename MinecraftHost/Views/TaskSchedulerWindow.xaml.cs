using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Windows;
using System.Windows.Input;

namespace MinecraftHost.Views;

public partial class TaskSchedulerWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public TaskSchedulerWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DataContext = new TaskSchedulerWindowViewModel();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}