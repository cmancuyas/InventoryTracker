namespace InventoryTracker.Infrastructure.Dtos;

public static class ProductDtos
{
    public sealed record ProductRow(
        Guid Id,
        string Sku,
        string Name,
        string UnitOfMeasure
    );

    public sealed record PagedResult<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        int Total
    );
}
