using InventoryTracker.Infrastructure.Identity;

namespace InventoryTracker.Api.Auth;

public interface IJwtTokenService
{
    string CreateToken(ApplicationUser user, IReadOnlyList<string> roles);
}
