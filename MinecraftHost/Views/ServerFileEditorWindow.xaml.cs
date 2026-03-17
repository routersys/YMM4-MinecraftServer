using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Windows;

namespace MinecraftHost.Views;

public partial class ServerFileEditorWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public ServerFileEditorWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
    }

    private void WorldSlotDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void WorldSlotDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ServerFileEditorViewModel vm)
            return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        if (sender is not FrameworkElement element || element.Tag is not WorldSlotViewModel slot)
            return;

        vm.TryAssignWorldDrop(slot, files[0]);
    }
}