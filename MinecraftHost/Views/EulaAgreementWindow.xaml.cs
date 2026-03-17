using MinecraftHost.Services.Interfaces.UI;
using MinecraftHost.Services.UI;
using MinecraftHost.ViewModels.Windows;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace MinecraftHost.Views;

public partial class EulaAgreementWindow : Window
{
    private static readonly IWindowThemeService ThemeService = new WindowThemeService();

    public EulaAgreementWindow()
    {
        WindowSharedResources.Apply(this);
        InitializeComponent();
        ThemeService.Bind(this);
        DataContext = new EulaAgreementViewModel(
            () => { DialogResult = true; Close(); },
            () => { DialogResult = false; Close(); }
        );
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}