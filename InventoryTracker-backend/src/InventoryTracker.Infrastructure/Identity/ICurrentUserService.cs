namespace InventoryTracker.Infrastructure.Identity;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? Email { get; }
}
