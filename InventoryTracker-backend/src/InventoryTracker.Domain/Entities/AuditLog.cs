using InventoryTracker.Domain.Common;

namespace InventoryTracker.Domain.Entities;

public sealed class AuditLog : BaseModel
{
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public Guid? EntityId { get; set; }

    public Guid? WarehouseId { get; set; }
    public Guid? ProductId { get; set; }

    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? DataJson { get; set; }
}
