using MinecraftHost.Services.Server;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Items;

public class PlayerViewModel : Bindable
{
    private readonly PlayerProfileCacheService _profileCacheService = new();

    public string Name { get; }

    private ImageSource? _skinImage;
    public ImageSource? SkinImage
    {
        get => _skinImage;
        private set => Set(ref _skinImage, value);
    }

    public PlayerViewModel(string name)
    {
        Name = name;
        LoadSkinAsync();
    }

    private async void LoadSkinAsync()
    {
        try
        {
            var profile = await _profileCacheService.GetOrCreateAsync(Name);
            if (profile is null || string.IsNullOrWhiteSpace(profile.AvatarPath) || !File.Exists(profile.AvatarPath))
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(profile.AvatarPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                SkinImage = bitmap;
            });
        }
        catch { }
    }
}