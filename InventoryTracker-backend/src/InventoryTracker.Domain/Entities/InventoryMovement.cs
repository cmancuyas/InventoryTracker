using InventoryTracker.Domain.Common;

namespace InventoryTracker.Domain.Entities;

public sealed class InventoryMovement : BaseModel
{
    public string Type { get; set; } = "";   // IN / OUT / ADJUSTMENT
    public int Status { get; set; }          // Draft / Posted / Cancelled

    public Guid WarehouseId { get; set; }

    public string ReferenceNo { get; set; } = "";
    public string? Notes { get; set; }

    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }

    public Warehouse? Warehouse { get; set; }
    public ICollection<InventoryMovementLine> Lines { get; set; } = new List<InventoryMovementLine>();
}
