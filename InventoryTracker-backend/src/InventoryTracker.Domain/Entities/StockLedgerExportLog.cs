// InventoryTracker.Domain/Entities/StockLedgerExportLog.cs
using InventoryTracker.Domain.Common;

namespace InventoryTracker.Domain.Entities;

public sealed class StockLedgerExportLog : BaseModel
{
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public string? Search { get; set; }
    public string SortBy { get; set; } = "postedAt";
    public string SortDir { get; set; } = "asc";

    public string Format { get; set; } = "csv";     // csv | xlsx
    public int RowCount { get; set; }
    public int? MaxRows { get; set; }

    // Optional: link to IdempotencyRecord if used
    public string? IdempotencyKey { get; set; }

    // Convenience for tracing
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
}
