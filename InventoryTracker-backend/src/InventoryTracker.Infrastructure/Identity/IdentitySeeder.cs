using InventoryTracker.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InventoryTracker.Infrastructure.Identity;

public sealed class IdentitySeeder
{
    private readonly IConfiguration _config;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        IConfiguration config,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger<IdentitySeeder> logger)
    {
        _config = config;
        _roleManager = roleManager;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seedEnabled = _config.GetValue<bool>("Seed:Enabled");
        if (!seedEnabled)
        {
            _logger.LogInformation("Identity seeding disabled (Seed:Enabled=false).");
            return;
        }

        // 1) Ensure roles exist
        var rolesToEnsure = new[]
        {
            AppRoles.Admin,
            AppRoles.InventoryManager,
            AppRoles.WarehouseStaff,
            AppRoles.ReadOnly
        };

        foreach (var roleName in rolesToEnsure)
        {
            ct.ThrowIfCancellationRequested();

            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var createRole = await _roleManager.CreateAsync(new ApplicationRole { Name = roleName });
                if (!createRole.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed creating role '{roleName}': {string.Join("; ", createRole.Errors.Select(e => e.Description))}");
                }
                _logger.LogInformation("Created role: {Role}", roleName);
            }
        }

        // 2) Ensure admin user exists
        var adminEmail = _config["Seed:AdminEmail"] ?? "admin@inventorytracker.com";
        var adminPassword = _config["Seed:AdminPassword"] ?? "Admin123!@#";
        var adminUserName = _config["Seed:AdminUserName"] ?? "admin";

        var admin = await _userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminUserName,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var createUser = await _userManager.CreateAsync(admin, adminPassword);
            if (!createUser.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed creating admin user: {string.Join("; ", createUser.Errors.Select(e => e.Description))}");
            }

            _logger.LogInformation("Created admin user: {Email}", adminEmail);
        }

        // 3) Ensure admin is in Admin role
        if (!await _userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var addRole = await _userManager.AddToRoleAsync(admin, AppRoles.Admin);
            if (!addRole.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed adding admin to role '{AppRoles.Admin}': {string.Join("; ", addRole.Errors.Select(e => e.Description))}");
            }

            _logger.LogInformation("Added {Email} to role: {Role}", adminEmail, AppRoles.Admin);
        }

        _logger.LogInformation("Identity seeding completed.");
    }
}
