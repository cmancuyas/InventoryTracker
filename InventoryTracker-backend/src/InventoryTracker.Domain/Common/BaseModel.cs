namespace InventoryTracker.Domain.Common;

public abstract class BaseModel
{
    public Guid Id { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = default!;
}

