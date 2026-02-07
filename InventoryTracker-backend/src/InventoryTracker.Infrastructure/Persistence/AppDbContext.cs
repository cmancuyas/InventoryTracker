using Microsoft.EntityFrameworkCore;
using InventoryTracker.Domain.Common;
using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Identity;
using InventoryTracker.Infrastructure.Persistence.Extensions;

namespace InventoryTracker.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementLine> InventoryMovementLines => Set<InventoryMovementLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<StockLedgerExportLog> StockLedgerExportLogs => Set<StockLedgerExportLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureBaseModelConventions(modelBuilder);
        ConfigureInventoryBalance(modelBuilder);
        ConfigureInventoryMovementLine(modelBuilder);
        ConfigureStockLedgerExportLog(modelBuilder);
        ModelConfig.Apply(modelBuilder);
    }
    private static void ConfigureInventoryBalance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryBalance>()
            .HasIndex(x => new { x.WarehouseId, x.ProductId })
            .IsUnique();
    }
    private static void ConfigureInventoryMovementLine(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryMovementLine>()
            .HasIndex(x => x.InventoryMovementId);
    }
    private static void ConfigureBaseModelConventions(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplySoftDeleteQueryFilter();

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseModel).IsAssignableFrom(entity.ClrType))
            {
                modelBuilder.Entity(entity.ClrType)
                    .Property<byte[]>(nameof(BaseModel.RowVersion))
                    .IsRowVersion()
                    .IsConcurrencyToken()
                    .HasColumnName("RowVersion");
            }
        }
    }
    private static void ConfigureStockLedgerExportLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockLedgerExportLog>(b =>
        {
            b.HasIndex(x => new { x.WarehouseId, x.ProductId, x.CreatedAt });
            b.HasIndex(x => x.IdempotencyKey);
            b.Property(x => x.Format).HasMaxLength(16);
            b.Property(x => x.SortBy).HasMaxLength(32);
            b.Property(x => x.SortDir).HasMaxLength(8);
            b.Property(x => x.Search).HasMaxLength(256);
            b.Property(x => x.ClientIp).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(256);
        });
    }
    public override int SaveChanges()
    {
        ApplyAuditRules();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditRules()
    {
        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId ?? "system";

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not BaseModel entity) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    entity.CreatedAt = now;
                    entity.CreatedBy ??= userId;

                    entity.UpdatedAt = null;
                    entity.UpdatedBy = null;

                    entity.DeletedAt = null;
                    entity.DeletedBy = null;

                    entity.IsDeleted = false;
                    entity.IsActive = true;
                    break;

                case EntityState.Modified:
                    entity.UpdatedAt = now;
                    entity.UpdatedBy ??= userId;

                    // Only do delete/restore bookkeeping if IsDeleted flag actually changed
                    var isDeletedProp = entry.Property(nameof(BaseModel.IsDeleted));
                    var isDeletedModified = isDeletedProp.IsModified;

                    if (isDeletedModified && entity.IsDeleted && entity.DeletedAt == null)
                    {
                        entity.DeletedAt = now;
                        entity.DeletedBy ??= userId;
                        entity.IsActive = false;
                    }

                    if (isDeletedModified && !entity.IsDeleted && entity.DeletedAt != null)
                    {
                        entity.DeletedAt = null;
                        entity.DeletedBy = null;
                        entity.IsActive = true;
                    }

                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;

                    entity.IsDeleted = true;
                    entity.IsActive = false;

                    entity.DeletedAt ??= now;
                    entity.DeletedBy ??= userId;

                    entity.UpdatedAt = now;
                    entity.UpdatedBy ??= userId;
                    break;
            }
        }
    }
}

