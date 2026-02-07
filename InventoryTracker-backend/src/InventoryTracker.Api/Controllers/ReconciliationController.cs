using System.Text.Json;
using InventoryTracker.Domain.Auth;
using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Auditing;
using InventoryTracker.Infrastructure.Dtos;
using InventoryTracker.Infrastructure.Idempotency.Services;
using InventoryTracker.Infrastructure.Identity;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/reconciliation")]
public sealed class ReconciliationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IAuditLogger _audit;
    private readonly IIdempotencyService _idem;

    public ReconciliationController(
        AppDbContext db,
        ICurrentUserService current,
        IAuditLogger audit,
        IIdempotencyService idem)
    {
        _db = db;
        _current = current;
        _audit = audit;
        _idem = idem;
    }

    /// <summary>
    /// SAFE MODE: creates missing InventoryBalances rows only.
    /// Does NOT recalculate OnHand and does NOT change existing rows.
    ///
    /// If "Idempotency-Key" header is provided, the response is cached and replayed.
    /// </summary>
    [HttpPost("sync-balances")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<ActionResult<ReconciliationDtos.SyncBalancesResult>> SyncBalances(
        [FromBody] ReconciliationDtos.SyncBalancesRequest req,
        CancellationToken ct)
        => await Idempotent("POST:/api/reconciliation/sync-balances", async () =>
        {
            var maxCreates = req.MaxCreates <= 0 ? 50_000 : Math.Min(req.MaxCreates, 200_000);

            // 1) Candidate pairs from POSTED movement lines
            var pairsQuery =
                from l in _db.InventoryMovementLines.AsNoTracking()
                join m in _db.InventoryMovements.AsNoTracking() on l.InventoryMovementId equals m.Id
                where l.IsActive
                      && !l.IsDeleted
                      && m.IsActive
                      && !m.IsDeleted
                      && m.Status == (int)InventoryMovementStatus.Posted
                select new { m.WarehouseId, l.ProductId };

            if (req.WarehouseId.HasValue)
                pairsQuery = pairsQuery.Where(x => x.WarehouseId == req.WarehouseId.Value);

            if (req.ProductId.HasValue)
                pairsQuery = pairsQuery.Where(x => x.ProductId == req.ProductId.Value);

            var pairs = await pairsQuery.Distinct().ToListAsync(ct);
            var considered = pairs.Count;

            if (considered == 0)
            {
                var empty = new ReconciliationDtos.SyncBalancesResult(
                    Created: 0,
                    SkippedExisting: 0,
                    ConsideredPairs: 0,
                    CreatedRows: Array.Empty<ReconciliationDtos.CreatedBalanceRow>());

                await _audit.LogAsync(
                    action: "RECON_SYNC_BALANCES",
                    entity: "InventoryBalance",
                    entityId: null,
                    warehouseId: req.WarehouseId,
                    productId: req.ProductId,
                    success: true,
                    message: "No posted movement pairs found. No action taken.",
                    dataJson: JsonSerializer.Serialize(new { req, result = empty }),
                    ct: ct);

                return (StatusCodes.Status200OK, empty);
            }

            // 2) Load existing balances demonstrating presence by pair
            var whIds = pairs.Select(x => x.WarehouseId).Distinct().ToList();
            var prodIds = pairs.Select(x => x.ProductId).Distinct().ToList();

            var existingPairs = await _db.InventoryBalances.AsNoTracking()
                .Where(b => b.IsActive
                            && !b.IsDeleted
                            && whIds.Contains(b.WarehouseId)
                            && prodIds.Contains(b.ProductId))
                .Select(b => new { b.WarehouseId, b.ProductId })
                .ToListAsync(ct);

            var existingSet = existingPairs
                .Select(x => (x.WarehouseId, x.ProductId))
                .ToHashSet();

            // 3) Create missing rows (OnHand = 0)
            var createdBy = _current.UserId ?? "system";

            var toCreate = new List<InventoryBalance>(capacity: Math.Min(considered, maxCreates));
            var createdRows = new List<ReconciliationDtos.CreatedBalanceRow>(capacity: Math.Min(considered, maxCreates));

            foreach (var p in pairs)
            {
                if (existingSet.Contains((p.WarehouseId, p.ProductId)))
                    continue;

                if (toCreate.Count >= maxCreates)
                    break;

                var bal = new InventoryBalance
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = p.WarehouseId,
                    ProductId = p.ProductId,
                    OnHand = 0,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.UtcNow
                };

                toCreate.Add(bal);
                createdRows.Add(new ReconciliationDtos.CreatedBalanceRow(
                    bal.Id, bal.WarehouseId, bal.ProductId, bal.OnHand
                ));
            }

            if (toCreate.Count > 0)
            {
                await _db.InventoryBalances.AddRangeAsync(toCreate, ct);
                await _db.SaveChangesAsync(ct);
            }

            var created = toCreate.Count;

            // "skipped existing" = pairs that already had a balance
            var skippedExisting = existingSet.Count;

            var result = new ReconciliationDtos.SyncBalancesResult(
                Created: created,
                SkippedExisting: skippedExisting,
                ConsideredPairs: considered,
                CreatedRows: createdRows
            );

            await _audit.LogAsync(
                action: "RECON_SYNC_BALANCES",
                entity: "InventoryBalance",
                entityId: null,
                warehouseId: req.WarehouseId,
                productId: req.ProductId,
                success: true,
                message: created == 0 ? "All balances already exist." : $"Created {created} missing InventoryBalance rows.",
                dataJson: JsonSerializer.Serialize(new
                {
                    req,
                    considered,
                    created,
                    skippedExisting,
                    maxCreates,
                    createdBy
                }),
                ct: ct);

            return (StatusCodes.Status200OK, result);

        }, ct);

    /// <summary>
    /// Recompute balances from POSTED movements.
    ///
    /// SafeMode=true:
    ///   - creates missing rows only (default)
    ///   - does NOT change existing rows
    /// SafeMode=false:
    ///   - creates missing rows AND updates existing to match computed totals
    ///
    /// DryRun=true:
    ///   - returns diffs but does not write anything
    ///
    /// If "Idempotency-Key" header is provided, the response is cached and replayed.
    /// </summary>
    [HttpPost("run")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<ActionResult<ReconciliationDtos.RunResponse>> Run(
        [FromBody] ReconciliationDtos.RunRequest req,
        CancellationToken ct)
        => await Idempotent("POST:/api/reconciliation/run", async () =>
        {
            // IMPORTANT:
            // This endpoint computes OnHand from posted movements.
            // It assumes your movement posting already prevented negative stock,
            // but we still won’t force negative checks here—this is a reconciliation tool.

            // -------- build posted movement query --------
            var movQ = _db.InventoryMovements.AsNoTracking()
                .Where(m => m.IsActive && !m.IsDeleted && m.Status == (int)InventoryMovementStatus.Posted);

            if (req.WarehouseId is Guid wid && wid != Guid.Empty)
                movQ = movQ.Where(m => m.WarehouseId == wid);

            if (req.FromUtc is DateTime fromUtc)
                movQ = movQ.Where(m => m.PostedAt != null && m.PostedAt >= fromUtc);

            if (req.ToUtc is DateTime toUtc)
                movQ = movQ.Where(m => m.PostedAt != null && m.PostedAt <= toUtc);

            var lineQ = _db.InventoryMovementLines.AsNoTracking()
                .Where(l => l.IsActive && !l.IsDeleted);

            if (req.ProductId is Guid pid && pid != Guid.Empty)
                lineQ = lineQ.Where(l => l.ProductId == pid);

            // Pull minimal shape; compute in-memory (safe & simple)
            var raw = await (
                from m in movQ
                join l in lineQ on m.Id equals l.InventoryMovementId
                select new { m.WarehouseId, l.ProductId, m.Type, l.Quantity }
            ).ToListAsync(ct);

            decimal Delta(string type, decimal qty)
            {
                var t = (type ?? "").Trim().ToUpperInvariant();
                return t switch
                {
                    "IN" => qty,
                    "OUT" => -qty,
                    "ADJUSTMENT" => qty,
                    _ => throw new InvalidOperationException($"Invalid movement type '{type}'.")
                };
            }

            var computedMap = raw
                .GroupBy(x => (x.WarehouseId, x.ProductId))
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(x => Delta(x.Type, x.Quantity))
                );

            var pairs = computedMap.Keys.ToList();
            if (pairs.Count == 0)
            {
                var empty = new ReconciliationDtos.RunResponse(
                    SafeMode: req.SafeMode,
                    DryRun: req.DryRun,
                    AffectedPairs: 0,
                    MissingCreated: 0,
                    UpdatedExisting: 0,
                    Unchanged: 0,
                    Diffs: Array.Empty<ReconciliationDtos.DiffRow>()
                );

                await _audit.LogAsync(
                    action: "RECON_RUN",
                    entity: "InventoryBalance",
                    entityId: null,
                    warehouseId: req.WarehouseId,
                    productId: req.ProductId,
                    success: true,
                    message: "No posted movement rows found for filters.",
                    dataJson: JsonSerializer.Serialize(new { req, result = empty }),
                    ct: ct);

                return (StatusCodes.Status200OK, empty);
            }

            var whIds = pairs.Select(p => p.WarehouseId).Distinct().ToList();
            var prodIds = pairs.Select(p => p.ProductId).Distinct().ToList();

            var existing = await _db.InventoryBalances
                .Where(b => b.IsActive && !b.IsDeleted
                         && whIds.Contains(b.WarehouseId)
                         && prodIds.Contains(b.ProductId))
                .ToListAsync(ct);

            var existingMap = existing.ToDictionary(b => (b.WarehouseId, b.ProductId), b => b);

            var diffs = new List<ReconciliationDtos.DiffRow>(pairs.Count);
            int created = 0, updated = 0, unchanged = 0;

            // If we will write, keep it atomic
            await using var tx = req.DryRun ? null : await _db.Database.BeginTransactionAsync(ct);

            var actor = _current.UserId ?? "system";
            var now = DateTime.UtcNow;

            foreach (var key in pairs)
            {
                var computedOnHand = computedMap[key];
                existingMap.TryGetValue(key, out var bal);

                var currentOnHand = bal?.OnHand; // null => missing
                var delta = computedOnHand - (currentOnHand ?? 0m);

                diffs.Add(new ReconciliationDtos.DiffRow(
                    WarehouseId: key.WarehouseId,
                    ProductId: key.ProductId,
                    CurrentOnHand: currentOnHand,
                    ComputedOnHand: computedOnHand,
                    Delta: delta
                ));

                if (bal is null)
                {
                    created++;

                    if (!req.DryRun)
                    {
                        // IMPORTANT policy:
                        // For reconciliation, missing balance should be created with computed total.
                        // If you want "always 0", change this line to: OnHand = 0
                        _db.InventoryBalances.Add(new InventoryBalance
                        {
                            Id = Guid.NewGuid(),
                            WarehouseId = key.WarehouseId,
                            ProductId = key.ProductId,
                            OnHand = computedOnHand,
                            IsActive = true,
                            IsDeleted = false,
                            CreatedBy = actor,
                            CreatedAt = now
                        });
                    }

                    continue;
                }

                if (req.SafeMode)
                {
                    // Safe mode: don't touch existing rows
                    unchanged++;
                    continue;
                }

                if (bal.OnHand != computedOnHand)
                {
                    updated++;

                    if (!req.DryRun)
                    {
                        bal.OnHand = computedOnHand;
                        bal.UpdatedBy = actor;
                        bal.UpdatedAt = now;
                    }
                }
                else
                {
                    unchanged++;
                }
            }

            if (!req.DryRun)
            {
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }

            var result = new ReconciliationDtos.RunResponse(
                SafeMode: req.SafeMode,
                DryRun: req.DryRun,
                AffectedPairs: pairs.Count,
                MissingCreated: created,
                UpdatedExisting: updated,
                Unchanged: unchanged,
                Diffs: diffs.ToArray()
            );

            // Audit summary (+ sample diffs)
            await _audit.LogAsync(
                action: "RECON_RUN",
                entity: "InventoryBalance",
                entityId: null,
                warehouseId: req.WarehouseId,
                productId: req.ProductId,
                success: true,
                message: req.DryRun
                    ? "Reconciliation dry-run completed."
                    : (req.SafeMode ? "Reconciliation safe-mode completed." : "Reconciliation full-mode completed."),
                dataJson: JsonSerializer.Serialize(new
                {
                    req,
                    result,
                    sampleDiffs = diffs.Take(50).ToArray()
                }),
                ct: ct);

            return (StatusCodes.Status200OK, result);

        }, ct);

    // ---------------- idempotency helper ----------------

    private async Task<ActionResult<T>> Idempotent<T>(
        string endpointKey,
        Func<Task<(int StatusCode, T Body)>> work,
        CancellationToken ct)
    {
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
        {
            var (s, b) = await work();
            return StatusCode(s, b);
        }

        var userId = _current.UserId;

        var cached = await _idem.TryGetCachedAsync(idemKey, endpointKey, userId, ct);
        if (cached.Hit)
        {
            var cachedBody = JsonSerializer.Deserialize<T>(cached.ResponseJson);
            if (cachedBody is null) return StatusCode(cached.StatusCode);
            return StatusCode(cached.StatusCode, cachedBody);
        }

        var recordId = await _idem.BeginAsync(idemKey, endpointKey, userId, ct);

        try
        {
            var (status, body) = await work();

            var json = JsonSerializer.Serialize(body);
            await _idem.CompleteAsync(
                recordId,
                succeeded: true,
                statusCode: status,
                responseJson: json,
                errorMessage: null,
                ct: ct);

            return StatusCode(status, body);
        }
        catch (Exception ex)
        {
            await _idem.CompleteAsync(
                recordId,
                succeeded: false,
                statusCode: 500,
                responseJson: null,
                errorMessage: ex.Message,
                ct: ct);

            await _audit.LogAsync(
                action: endpointKey.Contains("/run") ? "RECON_RUN_FAILED" : "RECON_SYNC_BALANCES_FAILED",
                entity: "InventoryBalance",
                entityId: null,
                warehouseId: null,
                productId: null,
                success: false,
                message: ex.Message,
                dataJson: null,
                ct: ct);

            return Problem(ex.Message);
        }
    }
}

