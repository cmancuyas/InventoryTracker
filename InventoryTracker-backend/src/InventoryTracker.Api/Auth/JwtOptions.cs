namespace InventoryTracker.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string Key { get; init; } = ""; // min 32 chars
    public int ExpiresMinutes { get; init; } = 120;
}
