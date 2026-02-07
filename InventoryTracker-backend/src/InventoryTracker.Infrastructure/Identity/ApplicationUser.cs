using Microsoft.AspNetCore.Identity;

namespace InventoryTracker.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    // optional: if you want user profile fields later
    public string? FullName { get; set; }
}
