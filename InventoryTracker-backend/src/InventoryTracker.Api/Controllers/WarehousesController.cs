using InventoryTracker.Domain.Auth;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/warehouses")]
public sealed class WarehousesController : ControllerBase
{
    private readonly AppDbContext _db;

    public WarehousesController(AppDbContext db) => _db = db;

    // Used by UI dropdowns
    [HttpGet]
    [Authorize(Roles = AppRoles.CanReadMasterData)]
    public async Task<ActionResult<IReadOnlyList<WarehouseRow>>> Get(
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var q = _db.Warehouses.AsNoTracking();

        // Only active unless you explicitly want inactive too
        q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.Code.ToLower().Contains(s) ||
                x.Name.ToLower().Contains(s) ||
                (x.Location != null && x.Location.ToLower().Contains(s)));
        }

        var items = await q
            .OrderBy(x => x.Code)
            .Select(x => new WarehouseRow(x.Id, x.Code, x.Name, x.Location))
            .ToListAsync(ct);

        return Ok(items);
    }

    public sealed record WarehouseRow(Guid Id, string Code, string Name, string? Location);
}
