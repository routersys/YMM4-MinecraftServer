using MinecraftHost.Localization;

namespace MinecraftHost.Services.Authorization;

public static class AuthorizationUiText
{
    public static string ToInline(string reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : string.Format(Texts.Authorization_InlineDeniedFormat, reason);
    }

    public static string ToDialog(string reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? Texts.Authorization_DialogDenied
            : string.Format(Texts.Authorization_DialogDeniedWithReasonFormat, reason);
    }
}