using System.Text.Json;
using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Auditing;
using InventoryTracker.Infrastructure.Dtos;
using InventoryTracker.Infrastructure.Identity;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Infrastructure.Inventory;

public sealed class InventoryMovementService : IInventoryMovementService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IAuditLogger _audit;

    public InventoryMovementService(AppDbContext db, ICurrentUserService current, IAuditLogger audit)
    {
        _db = db;
        _current = current;
        _audit = audit;
    }

    public async Task<InventoryMovementDtos.MovementResponse> CreateDraftAsync(InventoryMovementDtos.CreateDraftRequest req, CancellationToken ct)
    {
        var type = NormalizeType(req.Type);

        if (req.WarehouseId == Guid.Empty) throw new InvalidOperationException("WarehouseId is required.");
        if (string.IsNullOrWhiteSpace(req.ReferenceNo)) throw new InvalidOperationException("ReferenceNo is required.");

        var whExists = await _db.Warehouses.AnyAsync(x => x.Id == req.WarehouseId && x.IsActive, ct);
        if (!whExists) throw new KeyNotFoundException("Warehouse not found.");

        var m = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            WarehouseId = req.WarehouseId,
            Type = type,
            Status = (int)InventoryMovementStatus.Draft,
            ReferenceNo = req.ReferenceNo.Trim(),
            Notes = req.Notes?.Trim(),
            IsActive = true,
            IsDeleted = false
        };

        _db.InventoryMovements.Add(m);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CREATE_DRAFT", "InventoryMovement", m.Id, m.WarehouseId, null, true, null, JsonSerializer.Serialize(req), ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    public async Task<InventoryMovementDtos.MovementResponse> UpdateDraftAsync(Guid id, InventoryMovementDtos.UpdateDraftRequest req, CancellationToken ct)
    {
        var m = await _db.InventoryMovements.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Movement not found.");

        EnsureDraft(m);

        m.ReferenceNo = string.IsNullOrWhiteSpace(req.ReferenceNo) ? m.ReferenceNo : req.ReferenceNo.Trim();
        m.Notes = req.Notes?.Trim();

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("UPDATE_DRAFT", "InventoryMovement", m.Id, m.WarehouseId, null, true, null, JsonSerializer.Serialize(req), ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    public async Task<InventoryMovementDtos.MovementResponse> AddLineAsync(Guid id, InventoryMovementDtos.LineRequest req, CancellationToken ct)
    {
        var m = await _db.InventoryMovements.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Movement not found.");

        EnsureDraft(m);

        if (req.ProductId == Guid.Empty) throw new InvalidOperationException("ProductId is required.");
        if (req.Quantity <= 0) throw new InvalidOperationException("Quantity must be > 0.");

        var prodExists = await _db.Products.AnyAsync(p => p.Id == req.ProductId && p.IsActive, ct);
        if (!prodExists) throw new KeyNotFoundException("Product not found.");

        var line = new InventoryMovementLine
        {
            Id = Guid.NewGuid(),
            InventoryMovementId = m.Id,
            ProductId = req.ProductId,
            Quantity = req.Quantity,
            IsActive = true,
            IsDeleted = false
        };

        _db.InventoryMovementLines.Add(line);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("ADD_LINE", "InventoryMovementLine", line.Id, m.WarehouseId, line.ProductId, true, null, JsonSerializer.Serialize(req), ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    public async Task<InventoryMovementDtos.MovementResponse> ReplaceLinesAsync(Guid id, InventoryMovementDtos.ReplaceLinesRequest req, CancellationToken ct)
    {
        var m = await _db.InventoryMovements.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Movement not found.");

        EnsureDraft(m);

        var lines = req.Lines ?? Array.Empty<InventoryMovementDtos.LineRequest>();
        if (lines.Count == 0) throw new InvalidOperationException("Lines are required.");

        if (lines.Any(x => x.ProductId == Guid.Empty)) throw new InvalidOperationException("All lines must have ProductId.");
        if (lines.Any(x => x.Quantity <= 0)) throw new InvalidOperationException("All lines must have Quantity > 0.");

        // Validate products
        var prodIds = lines.Select(x => x.ProductId).Distinct().ToList();
        var existing = await _db.Products.Where(p => prodIds.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToListAsync(ct);
        if (existing.Count != prodIds.Count) throw new InvalidOperationException("One or more products not found or inactive.");

        // Soft-delete existing lines for this movement (keeps audit trail)
        var existingLines = await _db.InventoryMovementLines.Where(x => x.InventoryMovementId == m.Id && x.IsActive).ToListAsync(ct);
        foreach (var l in existingLines)
        {
            l.IsDeleted = true;
            l.IsActive = false;
        }

        // Add new lines
        foreach (var l in lines)
        {
            _db.InventoryMovementLines.Add(new InventoryMovementLine
            {
                Id = Guid.NewGuid(),
                InventoryMovementId = m.Id,
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                IsActive = true,
                IsDeleted = false
            });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("REPLACE_LINES", "InventoryMovement", m.Id, m.WarehouseId, null, true, null, JsonSerializer.Serialize(req), ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    public async Task<InventoryMovementDtos.MovementResponse> PostAsync(Guid id, CancellationToken ct)
    {
        // Transaction required: balances + status update must be atomic
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var m = await _db.InventoryMovements.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Movement not found.");

        EnsureDraft(m);

        var lines = await _db.InventoryMovementLines
            .Where(x => x.InventoryMovementId == m.Id && x.IsActive)
            .ToListAsync(ct);

        if (lines.Count == 0) throw new InvalidOperationException("Cannot post movement with no lines.");

        // Apply deltas to balances (no negative)
        foreach (var line in lines)
        {
            var delta = GetDelta(m.Type, line.Quantity);

            var bal = await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.WarehouseId == m.WarehouseId && b.ProductId == line.ProductId && b.IsActive, ct);

            if (bal is null)
            {
                // "safe mode" for posting: create missing balance row with OnHand=0
                bal = new InventoryBalance
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = m.WarehouseId,
                    ProductId = line.ProductId,
                    OnHand = 0,
                    IsActive = true,
                    IsDeleted = false
                };
                _db.InventoryBalances.Add(bal);
                await _db.SaveChangesAsync(ct);
            }

            var newOnHand = bal.OnHand + delta;

            if (newOnHand < 0)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for ProductId {line.ProductId}. OnHand={bal.OnHand}, delta={delta}.");
            }

            bal.OnHand = newOnHand;
        }

        m.Status = (int)InventoryMovementStatus.Posted;
        m.PostedAt = DateTime.UtcNow;
        m.PostedBy = _current.UserId ?? "system";

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.LogAsync("POST", "InventoryMovement", m.Id, m.WarehouseId, null, true, null, null, ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    public async Task<InventoryMovementDtos.MovementResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var m = await _db.InventoryMovements.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Movement not found.");

        if (m.Status != (int)InventoryMovementStatus.Posted)
            throw new InvalidOperationException("Only POSTED movements can be cancelled.");

        var lines = await _db.InventoryMovementLines
            .Where(x => x.InventoryMovementId == m.Id && x.IsActive)
            .ToListAsync(ct);

        if (lines.Count == 0) throw new InvalidOperationException("Movement has no active lines.");

        // Reverse deltas
        foreach (var line in lines)
        {
            var originalDelta = GetDelta(m.Type, line.Quantity);
            var reverseDelta = -originalDelta;

            var bal = await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.WarehouseId == m.WarehouseId && b.ProductId == line.ProductId && b.IsActive, ct)
                ?? throw new InvalidOperationException($"Inventory balance missing for ProductId {line.ProductId}.");

            var newOnHand = bal.OnHand + reverseDelta;
            if (newOnHand < 0)
                throw new InvalidOperationException(
                    $"Cannot cancel because it would make stock negative for ProductId {line.ProductId}. OnHand={bal.OnHand}, delta={reverseDelta}.");

            bal.OnHand = newOnHand;
        }

        m.Status = (int)InventoryMovementStatus.Cancelled;
        m.CancelledAt = DateTime.UtcNow;
        m.CancelledBy = _current.UserId ?? "system";

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.LogAsync("CANCEL", "InventoryMovement", m.Id, m.WarehouseId, null, true, null, null, ct);

        return await LoadResponseAsync(m.Id, ct);
    }

    private static string NormalizeType(string type)
    {
        var t = (type ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "IN" => "IN",
            "OUT" => "OUT",
            "ADJUSTMENT" => "ADJUSTMENT",
            _ => throw new InvalidOperationException("Type must be IN, OUT, or ADJUSTMENT.")
        };
    }

    private static decimal GetDelta(string type, decimal qty)
    {
        // Convention:
        // IN increases
        // OUT decreases
        // ADJUSTMENT: qty sign controls (+/-). Here you can decide policy.
        // For simplicity: ADJUSTMENT uses qty as-is.
        return type switch
        {
            "IN" => qty,
            "OUT" => -qty,
            "ADJUSTMENT" => qty,
            _ => throw new InvalidOperationException("Invalid movement type.")
        };
    }

    private static void EnsureDraft(InventoryMovement m)
    {
        if (m.Status != (int)InventoryMovementStatus.Draft)
            throw new InvalidOperationException("Only DRAFT movements can be modified.");
    }

    private async Task<InventoryMovementDtos.MovementResponse> LoadResponseAsync(Guid id, CancellationToken ct)
    {
        var m = await _db.InventoryMovements.AsNoTracking()
            .FirstAsync(x => x.Id == id, ct);

        var lines = await _db.InventoryMovementLines.AsNoTracking()
            .Where(x => x.InventoryMovementId == id && x.IsActive)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new InventoryMovementDtos.MovementLineResponse(x.Id, x.ProductId, x.Quantity))
            .ToListAsync(ct);

        return new InventoryMovementDtos.MovementResponse(
            m.Id, m.Type, m.Status, m.WarehouseId, m.ReferenceNo, m.Notes,
            m.PostedAt, m.PostedBy, m.CancelledAt, m.CancelledBy,
            lines
        );
    }
}
