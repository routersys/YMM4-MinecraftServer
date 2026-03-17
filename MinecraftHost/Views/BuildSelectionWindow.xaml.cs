using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using System.Collections.ObjectModel;
using System.Windows;

namespace MinecraftHost.Views;

public partial class BuildSelectionWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public ObservableCollection<string> BuildIdentifiers { get; }

    public string? SelectedBuild { get; set; }

    public BuildSelectionWindow(IEnumerable<string> buildIdentifiers, string? selectedBuild)
    {
        BuildIdentifiers = new ObservableCollection<string>(buildIdentifiers);
        SelectedBuild = selectedBuild;
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DataContext = this;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        SelectedBuild = BuildIdentifiersList.SelectedItem as string ?? SelectedBuild;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}