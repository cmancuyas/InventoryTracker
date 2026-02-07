namespace InventoryTracker.Infrastructure.Dtos;

public static class StockLedgerDtos
{
    public sealed record LedgerRow(
        Guid MovementId,
        Guid LineId,
        string Type,
        DateTime PostedAt,
        string ReferenceNo,
        Guid WarehouseId,
        string WarehouseName,
        Guid ProductId,
        string Sku,
        string ProductName,
        decimal Quantity,
        decimal Delta
    );

    public sealed record PagedResult<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        int Total
    );
}
