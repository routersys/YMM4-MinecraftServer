using MinecraftHost.ViewModels.Pages;
using MinecraftHost.Views;
using YukkuriMovieMaker.Plugin;

namespace MinecraftHost;

internal class MinecraftHostPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(MainPageViewModel);
    public Type ViewType => typeof(MainPage);
    public string Name => "MC Server Host";
    public bool AllowMultipleInstances => false;
}