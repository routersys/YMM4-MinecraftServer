using MinecraftHost.ViewModels.Pages;
using System.Windows.Controls;

namespace MinecraftHost.Settings;

public partial class SettingPage : UserControl
{
    public SettingPage()
    {
        InitializeComponent();
        DataContext = new SettingPageViewModel();
    }
}