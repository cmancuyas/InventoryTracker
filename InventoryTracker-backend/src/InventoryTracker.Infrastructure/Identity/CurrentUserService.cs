using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace InventoryTracker.Infrastructure.Identity;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    public string? UserId =>
        _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email =>
        _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
        ?? _http.HttpContext?.User?.FindFirstValue("email");
}
