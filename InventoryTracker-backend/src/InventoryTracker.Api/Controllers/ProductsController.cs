using InventoryTracker.Domain.Auth;
using InventoryTracker.Infrastructure.Dtos;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Roles = AppRoles.CanReadMasterData)]
    public async Task<ActionResult<ProductDtos.PagedResult<ProductDtos.ProductRow>>> Get(
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDir = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);

        var q = _db.Products.AsNoTracking().Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Sku.Contains(s) || x.Name.Contains(s));
        }

        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        q = (sortBy ?? "").Trim().ToLowerInvariant() switch
        {
            "sku" => desc ? q.OrderByDescending(x => x.Sku) : q.OrderBy(x => x.Sku),
            "createdat" => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
            _ => desc ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
        };

        var total = await q.CountAsync(ct);

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductDtos.ProductRow(
                x.Id,
                x.Sku,
                x.Name,
                x.UnitOfMeasure
            ))
            .ToListAsync(ct);

        return Ok(new ProductDtos.PagedResult<ProductDtos.ProductRow>(
            items, page, pageSize, total
        ));
    }
}
