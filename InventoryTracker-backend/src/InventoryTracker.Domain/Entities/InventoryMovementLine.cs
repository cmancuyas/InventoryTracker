using InventoryTracker.Domain.Common;

namespace InventoryTracker.Domain.Entities;

public sealed class InventoryMovementLine : BaseModel
{
    public Guid InventoryMovementId { get; set; }
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    public InventoryMovement? InventoryMovement { get; set; }
    public Product? Product { get; set; }
}
