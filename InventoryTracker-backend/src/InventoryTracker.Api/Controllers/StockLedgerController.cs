using ClosedXML.Excel;
using InventoryTracker.Domain.Auth;
using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Idempotency.Services;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/stock-ledger")]
public sealed class StockLedgerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IIdempotencyService _idempotency;

    public StockLedgerController(AppDbContext db, IIdempotencyService idempotency)
    {
        _db = db;
        _idempotency = idempotency;
    }

    // ---------------------------
    // DTOs (local to controller)
    // ---------------------------
    public sealed record GetStockLedgerRequest(
        Guid WarehouseId,
        Guid ProductId,
        DateTime? From = null,
        DateTime? To = null,
        string? Search = null,
        string SortBy = "postedAt",   // postedAt | quantity | referenceNo | type
        string SortDir = "asc",       // asc | desc
        int Page = 1,
        int PageSize = 50
    );

    public sealed record ExportStockLedgerRequest(
        Guid WarehouseId,
        Guid ProductId,
        DateTime? From = null,
        DateTime? To = null,
        string? Search = null,
        string SortBy = "postedAt",
        string SortDir = "asc",
        string Format = "csv",      // csv | xlsx
        int MaxRows = 200_000       // safety cap
    );

    public sealed record StockLedgerRow(
        Guid MovementId,
        Guid LineId,
        string Type,
        DateTime? PostedAtUtc,
        string ReferenceNo,
        Guid WarehouseId,
        string WarehouseName,
        Guid ProductId,
        string Sku,
        string ProductName,
        decimal Quantity,
        decimal Delta
    );

    public sealed record PagedResponse<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        int Total
    );

    // Used internally for export (strong typed to avoid List<dynamic> problems)
    private sealed record ExportRow(
        Guid MovementId,
        Guid LineId,
        string Type,
        DateTime? PostedAtUtc,
        string ReferenceNo,
        Guid WarehouseId,
        string WarehouseName,
        Guid ProductId,
        string Sku,
        string ProductName,
        decimal Quantity
    );

    private sealed record ExportMeta(
        Guid WarehouseId,
        Guid ProductId,
        DateTime? From,
        DateTime? To,
        string? Search,
        string SortBy,
        string SortDir,
        string Format,
        int RowCount,
        int MaxRows,
        DateTime ExportedAtUtc
    );

    // ---------------------------
    // GET (paged)
    // ---------------------------
    [HttpGet]
    [Authorize(Roles = AppRoles.CanReadMasterData)]
    public async Task<ActionResult<PagedResponse<StockLedgerRow>>> Get([FromQuery] GetStockLedgerRequest req, CancellationToken ct)
    {
        if (req.WarehouseId == Guid.Empty) return BadRequest("WarehouseId is required.");
        if (req.ProductId == Guid.Empty) return BadRequest("ProductId is required.");
        if (req.Page <= 0) return BadRequest("Page must be >= 1.");
        if (req.PageSize <= 0) return BadRequest("PageSize must be >= 1.");

        var q = BuildBaseQuery(req.WarehouseId, req.ProductId, req.From, req.To, req.Search);

        var total = await q.CountAsync(ct);

        q = ApplySorting(q, req.SortBy, req.SortDir);

        var skip = (req.Page - 1) * req.PageSize;

        var rows = await q
            .Skip(skip)
            .Take(req.PageSize)
            .Select(x => new StockLedgerRow(
                x.MovementId,
                x.LineId,
                x.Type,
                x.PostedAtUtc,
                x.ReferenceNo,
                x.WarehouseId,
                x.WarehouseName,
                x.ProductId,
                x.Sku,
                x.ProductName,
                x.Quantity,
                DeltaFromType(x.Type, x.Quantity)
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<StockLedgerRow>(rows, req.Page, req.PageSize, total));
    }

    // ---------------------------
    // EXPORT (CSV / XLSX)
    // ---------------------------
    [HttpGet("export")]
    [Authorize(Roles = AppRoles.CanReadMasterData)]
    public async Task<IActionResult> Export([FromQuery] ExportStockLedgerRequest req, CancellationToken ct)
    {
        if (req.WarehouseId == Guid.Empty) return BadRequest("WarehouseId is required.");
        if (req.ProductId == Guid.Empty) return BadRequest("ProductId is required.");
        if (req.MaxRows <= 0) return BadRequest("MaxRows must be >= 1.");

        // Normalize / validate format
        var format = (req.Format ?? "csv").Trim().ToLowerInvariant();
        if (format is not ("csv" or "xlsx"))
            return BadRequest("Format must be 'csv' or 'xlsx'.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Stable endpoint key (don’t include query string)
        const string endpoint = "GET:/api/stock-ledger/export";

        // Optional idempotency (duplicate blocker; no file replay)
        var idemKey = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        Guid? idemRecordId = null;

        if (!string.IsNullOrWhiteSpace(idemKey))
        {
            var (hit, statusCode, responseJson) =
                await _idempotency.TryGetCachedAsync(idemKey!, endpoint, userId, ct);

            if (hit)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    message = "Duplicate export blocked by Idempotency-Key (file is not replayed).",
                    cachedStatusCode = statusCode,
                    cached = string.IsNullOrWhiteSpace(responseJson) ? null : JsonSerializer.Deserialize<object>(responseJson)
                });
            }

            idemRecordId = await _idempotency.BeginAsync(idemKey!, endpoint, userId, ct);
        }

        try
        {
            var q = BuildBaseQuery(req.WarehouseId, req.ProductId, req.From, req.To, req.Search);
            q = ApplySorting(q, req.SortBy, req.SortDir);

            var rows = await q
                .Take(req.MaxRows)
                .Select(x => new ExportRow(
                    x.MovementId,
                    x.LineId,
                    x.Type,
                    x.PostedAtUtc,
                    x.ReferenceNo,
                    x.WarehouseId,
                    x.WarehouseName,
                    x.ProductId,
                    x.Sku,
                    x.ProductName,
                    x.Quantity
                ))
                .ToListAsync(ct);

            var fileBase = $"stock-ledger_{req.WarehouseId:N}_{req.ProductId:N}_{DateTime.UtcNow:yyyyMMddHHmmss}";

            // Export audit log
            var log = new StockLedgerExportLog
            {
                WarehouseId = req.WarehouseId,
                ProductId = req.ProductId,
                From = req.From,
                To = req.To,
                Search = req.Search,
                SortBy = req.SortBy,
                SortDir = req.SortDir,
                Format = format,
                RowCount = rows.Count,
                MaxRows = req.MaxRows,
                IdempotencyKey = string.IsNullOrWhiteSpace(idemKey) ? null : idemKey,
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            };

            _db.StockLedgerExportLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            // Complete idempotency with metadata JSON (NOT the file)
            if (idemRecordId is not null)
            {
                var meta = new
                {
                    req.WarehouseId,
                    req.ProductId,
                    req.From,
                    req.To,
                    req.Search,
                    req.SortBy,
                    req.SortDir,
                    Format = format,
                    RowCount = rows.Count,
                    req.MaxRows,
                    ExportedAtUtc = DateTime.UtcNow
                };

                await _idempotency.CompleteAsync(
                    recordId: idemRecordId.Value,
                    succeeded: true,
                    statusCode: StatusCodes.Status200OK,
                    responseJson: JsonSerializer.Serialize(meta),
                    errorMessage: null,
                    ct: ct
                );
            }

            return format == "xlsx"
                ? ExportXlsx(rows, fileBase)
                : ExportCsv(rows, fileBase);
        }
        catch (Exception ex)
        {
            if (idemRecordId is not null)
            {
                await _idempotency.CompleteAsync(
                    recordId: idemRecordId.Value,
                    succeeded: false,
                    statusCode: StatusCodes.Status500InternalServerError,
                    responseJson: null,
                    errorMessage: ex.Message,
                    ct: ct
                );
            }

            throw;
        }
    }


    // ---------------------------
    // Query builder
    // ---------------------------
    private IQueryable<BaseRow> BuildBaseQuery(Guid warehouseId, Guid productId, DateTime? from, DateTime? to, string? search)
    {
        var q =
            from m in _db.InventoryMovements.AsNoTracking()
            join l in _db.InventoryMovementLines.AsNoTracking() on m.Id equals l.InventoryMovementId
            join w in _db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
            join p in _db.Products.AsNoTracking() on l.ProductId equals p.Id
            where m.IsActive && !m.IsDeleted
               && l.IsActive && !l.IsDeleted
               && w.IsActive && !w.IsDeleted
               && p.IsActive && !p.IsDeleted
               && m.WarehouseId == warehouseId
               && l.ProductId == productId
               && m.PostedAt != null
            select new BaseRow(
                MovementId: m.Id,
                LineId: l.Id,
                Type: m.Type,
                PostedAtUtc: m.PostedAt,
                ReferenceNo: m.ReferenceNo,
                WarehouseId: w.Id,
                WarehouseName: w.Name,
                ProductId: p.Id,
                Sku: p.Sku,
                ProductName: p.Name,
                Quantity: l.Quantity
            );

        if (from.HasValue)
            q = q.Where(x => x.PostedAtUtc != null && x.PostedAtUtc.Value >= from.Value);

        if (to.HasValue)
            q = q.Where(x => x.PostedAtUtc != null && x.PostedAtUtc.Value <= to.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(x =>
                (x.ReferenceNo ?? "").ToLower().Contains(s) ||
                (x.Sku ?? "").ToLower().Contains(s) ||
                (x.ProductName ?? "").ToLower().Contains(s) ||
                (x.WarehouseName ?? "").ToLower().Contains(s) ||
                (x.Type ?? "").ToLower().Contains(s)
            );
        }

        return q;
    }

    private static IQueryable<BaseRow> ApplySorting(IQueryable<BaseRow> q, string? sortBy, string? sortDir)
    {
        var by = (sortBy ?? "postedAt").Trim().ToLowerInvariant();
        var desc = string.Equals((sortDir ?? "asc").Trim(), "desc", StringComparison.OrdinalIgnoreCase);

        return (by, desc) switch
        {
            ("quantity", false) => q.OrderBy(x => x.Quantity).ThenBy(x => x.PostedAtUtc),
            ("quantity", true) => q.OrderByDescending(x => x.Quantity).ThenByDescending(x => x.PostedAtUtc),

            ("referenceno", false) => q.OrderBy(x => x.ReferenceNo).ThenBy(x => x.PostedAtUtc),
            ("referenceno", true) => q.OrderByDescending(x => x.ReferenceNo).ThenByDescending(x => x.PostedAtUtc),

            ("type", false) => q.OrderBy(x => x.Type).ThenBy(x => x.PostedAtUtc),
            ("type", true) => q.OrderByDescending(x => x.Type).ThenByDescending(x => x.PostedAtUtc),

            ("postedat", true) => q.OrderByDescending(x => x.PostedAtUtc).ThenByDescending(x => x.ReferenceNo),
            _ => q.OrderBy(x => x.PostedAtUtc).ThenBy(x => x.ReferenceNo),
        };
    }

    private sealed record BaseRow(
        Guid MovementId,
        Guid LineId,
        string Type,
        DateTime? PostedAtUtc,
        string ReferenceNo,
        Guid WarehouseId,
        string WarehouseName,
        Guid ProductId,
        string Sku,
        string ProductName,
        decimal Quantity
    );

    private static decimal DeltaFromType(string type, decimal qty)
    {
        var t = (type ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "OUT" => -qty,
            "IN" => qty,
            "ADJUSTMENT" => qty,
            _ => qty
        };
    }

    // ---------------------------
    // CSV export
    // ---------------------------
    private static IActionResult ExportCsv(List<ExportRow> rows, string fileBase)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(",",
            "MovementId", "LineId", "Type", "PostedAtUtc", "ReferenceNo",
            "WarehouseId", "WarehouseName",
            "ProductId", "Sku", "ProductName",
            "Quantity", "Delta"
        ));

        foreach (var r in rows)
        {
            var delta = DeltaFromType(r.Type, r.Quantity);

            sb.AppendLine(string.Join(",",
                Csv(r.MovementId.ToString()),
                Csv(r.LineId.ToString()),
                Csv(r.Type),
                Csv(r.PostedAtUtc?.ToUniversalTime().ToString("O") ?? ""),
                Csv(r.ReferenceNo),

                Csv(r.WarehouseId.ToString()),
                Csv(r.WarehouseName),

                Csv(r.ProductId.ToString()),
                Csv(r.Sku),
                Csv(r.ProductName),

                Csv(r.Quantity.ToString(CultureInfo.InvariantCulture)),
                Csv(delta.ToString(CultureInfo.InvariantCulture))
            ));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new FileContentResult(bytes, "text/csv")
        {
            FileDownloadName = fileBase + ".csv"
        };
    }

    private static string Csv(string? value)
    {
        value ??= "";
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote) return value;

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    // ---------------------------
    // XLSX export (ClosedXML)
    // ---------------------------
    private static IActionResult ExportXlsx(List<ExportRow> rows, string fileBase)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("StockLedger");

        var headers = new[]
        {
            "MovementId","LineId","Type","PostedAtUtc","ReferenceNo",
            "WarehouseId","WarehouseName",
            "ProductId","Sku","ProductName",
            "Quantity","Delta"
        };

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var rIdx = 2;
        foreach (var r in rows)
        {
            var delta = DeltaFromType(r.Type, r.Quantity);

            ws.Cell(rIdx, 1).Value = r.MovementId.ToString();
            ws.Cell(rIdx, 2).Value = r.LineId.ToString();
            ws.Cell(rIdx, 3).Value = r.Type;
            ws.Cell(rIdx, 4).Value = r.PostedAtUtc?.ToUniversalTime().ToString("O") ?? "";
            ws.Cell(rIdx, 5).Value = r.ReferenceNo;

            ws.Cell(rIdx, 6).Value = r.WarehouseId.ToString();
            ws.Cell(rIdx, 7).Value = r.WarehouseName;

            ws.Cell(rIdx, 8).Value = r.ProductId.ToString();
            ws.Cell(rIdx, 9).Value = r.Sku;
            ws.Cell(rIdx, 10).Value = r.ProductName;

            ws.Cell(rIdx, 11).Value = r.Quantity;
            ws.Cell(rIdx, 12).Value = delta;

            rIdx++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = fileBase + ".xlsx"
        };
    }
}
