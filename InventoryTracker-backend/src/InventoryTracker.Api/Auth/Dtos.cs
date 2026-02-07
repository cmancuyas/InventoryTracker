namespace InventoryTracker.Api.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    string AccessToken,
    int ExpiresInMinutes,
    string UserId,
    string Email,
    IReadOnlyList<string> Roles
);
