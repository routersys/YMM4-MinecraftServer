using MinecraftHost.Localization;

namespace MinecraftHost.ViewModels.Items;

internal static class EulaGuidanceDetector
{
    public static bool TryGetMessage(string line, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (!line.Contains("eula.txt", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!line.Contains("agree to the eula", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("you need to agree", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("set eula=true", StringComparison.OrdinalIgnoreCase))
            return false;

        message = Texts.EulaGuidance_InitialAgreementRequired;
        return true;
    }
}
