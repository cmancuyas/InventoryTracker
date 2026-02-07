namespace InventoryTracker.Infrastructure.Dtos;

public static class StockLedgerExportDtos
{
    public sealed record ExportRequest(
        Guid? WarehouseId = null,
        Guid? ProductId = null,
        DateTime? From = null,
        DateTime? To = null,
        string? Search = null,
        string SortBy = "postedAt",
        string SortDir = "desc",
        string Format = "csv",   // csv | xlsx
        int MaxRows = 200_000
    );
}
