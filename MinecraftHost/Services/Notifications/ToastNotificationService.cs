using MinecraftHost.Localization;
using MinecraftHost.Models.Logging;
using MinecraftHost.Services.Logging;
using Windows.UI.Notifications;

namespace MinecraftHost.Services.Notifications;

public sealed class ToastNotificationService : Interfaces.Notifications.IToastNotificationService
{
    public void ShowServerStartedToast(string? ipv4Address, string? ipv6Address, int port)
    {
        try
        {
            var title = Texts.ServerStartedToast_Title;
            
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(ipv4Address))
            {
                parts.Add(port is 25565 or 19132 
                    ? ipv4Address 
                    : $"{ipv4Address}:{port}");
            }

            if (!string.IsNullOrWhiteSpace(ipv6Address))
            {
                parts.Add(port is 25565 or 19132 
                    ? ipv6Address 
                    : $"[{ipv6Address}]:{port}");
            }

            var joinedAddresses = string.Join(" / ", parts);
            var message = string.Format(Texts.ServerStartedToast_Message, joinedAddresses);

            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = template.GetElementsByTagName("text");
            textNodes[0].AppendChild(template.CreateTextNode(title));
            textNodes[1].AppendChild(template.CreateTextNode(message));

            var toast = new ToastNotification(template);
            toast.Activated += (sender, args) =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    System.Windows.Clipboard.SetText(joinedAddresses);
                });
            };
            
            ToastNotificationManager.CreateToastNotifier(AppDomain.CurrentDomain.FriendlyName).Show(toast);
            
            StructuredLogServiceProvider.Instance.Log(StructuredLogLevel.Information, nameof(ToastNotificationService), $"トースト通知を表示しました: {message}", "ShowToast");
        }
        catch (Exception ex)
        {
            StructuredLogServiceProvider.Instance.Log(StructuredLogLevel.Information, nameof(ToastNotificationService), "トースト通知の表示に失敗しました。", "ShowToast", exception: ex);
        }
    }
}