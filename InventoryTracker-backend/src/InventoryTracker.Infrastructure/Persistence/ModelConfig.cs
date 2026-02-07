using Microsoft.EntityFrameworkCore;
using InventoryTracker.Domain.Entities;

namespace InventoryTracker.Infrastructure.Persistence;

public static class ModelConfig
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<Warehouse>(e =>
        {
            e.ToTable("Warehouses");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<InventoryBalance>(e =>
        {
            e.ToTable("InventoryBalances");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<InventoryMovement>(e =>
        {
            e.ToTable("InventoryMovements");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<InventoryMovementLine>(e =>
        {
            e.ToTable("InventoryMovementLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords");
            e.HasKey(x => x.Id);
        });
    }
}
