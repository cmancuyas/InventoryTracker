// InventoryTracker.Api/Dtos/StockLedger/StockLedgerExportAuditDto.cs
namespace InventoryTracker.Api.Dtos.StockLedger;

public sealed record StockLedgerExportAuditDto(
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
