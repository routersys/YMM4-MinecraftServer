using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Windows;

namespace MinecraftHost.Views;

public partial class PluginManagerWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public PluginManagerWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DragEnter += OnWindowDragEnter;
        DragLeave += OnWindowDragLeave;
        DragOver += OnWindowDragOver;
        Drop += OnWindowDrop;
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (DataContext is PluginManagerViewModel vm)
            await vm.InstallFilesAsync(files);
    }
}