using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Persistence;

namespace InventoryTracker.Infrastructure.Auditing;

public sealed class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;

    public AuditLogger(AppDbContext db) => _db = db;

    public async Task LogAsync(
        string action,
        string entity,
        Guid? entityId,
        Guid? warehouseId,
        Guid? productId,
        bool success,
        string? message,
        string? dataJson,
        CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            Entity = entity,
            EntityId = entityId,
            WarehouseId = warehouseId,
            ProductId = productId,
            Success = success,
            Message = message,
            DataJson = dataJson,
            IsActive = true,
            IsDeleted = false
        });

        await _db.SaveChangesAsync(ct);
    }
}
