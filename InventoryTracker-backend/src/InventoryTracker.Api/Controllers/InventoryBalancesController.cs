using InventoryTracker.Api.Contracts;
using InventoryTracker.Domain.Auth;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/inventory-balances")]
public sealed class InventoryBalancesController : ControllerBase
{
    private readonly AppDbContext _db;

    public InventoryBalancesController(AppDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Roles = AppRoles.CanReadMasterData)]
    public async Task<ActionResult<PagedResponse<InventoryBalancesRow>>> Get(
        [FromQuery] Guid warehouseId,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty)
            return BadRequest("warehouseId is required.");

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 20;

        // Ensure warehouse exists (optional but good UX)
        var whExists = await _db.Warehouses.AsNoTracking()
            .AnyAsync(x => x.Id == warehouseId && x.IsActive, ct);

        if (!whExists)
            return NotFound("Warehouse not found.");

        var q = _db.InventoryBalances
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.IsActive)
            .Join(
                _db.Products.AsNoTracking().Where(p => p.IsActive),
                bal => bal.ProductId,
                prod => prod.Id,
                (bal, prod) => new { bal, prod }
            );

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.prod.Sku.ToLower().Contains(s) ||
                x.prod.Name.ToLower().Contains(s));
        }

        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderBy(x => x.prod.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new InventoryBalancesRow(
                x.bal.Id,
                x.bal.WarehouseId,
                x.prod.Id,
                x.prod.Sku,
                x.prod.Name,
                x.prod.UnitOfMeasure,
                x.bal.OnHand
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<InventoryBalancesRow>(items, page, pageSize, total));
    }

    public sealed record InventoryBalancesRow(
        Guid Id,
        Guid WarehouseId,
        Guid ProductId,
        string Sku,
        string Name,
        string UnitOfMeasure,
        decimal OnHand
    );
}
