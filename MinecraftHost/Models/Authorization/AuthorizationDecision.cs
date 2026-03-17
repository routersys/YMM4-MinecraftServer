namespace MinecraftHost.Models.Authorization;

public readonly record struct AuthorizationDecision(bool Allowed, string Reason)
{
    public static AuthorizationDecision Allow() => new(true, string.Empty);
    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}