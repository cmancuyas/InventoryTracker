using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace InventoryTracker.Infrastructure.Identity;

public sealed class IdentityAppDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Place Identity-specific conventions here if you need them.
        // Keep it minimal unless you have custom tables/columns.
    }
}
