namespace InventoryTracker.Infrastructure.Dtos;

public static class InventoryMovementDtos
{
    public sealed record CreateDraftRequest(
        Guid WarehouseId,
        string Type,
        string ReferenceNo,
        string? Notes
    );

    public sealed record UpdateDraftRequest(
        string ReferenceNo,
        string? Notes
    );

    public sealed record LineRequest(
        Guid ProductId,
        decimal Quantity
    );

    public sealed record ReplaceLinesRequest(
        IReadOnlyList<LineRequest> Lines
    );

    public sealed record MovementResponse(
        Guid Id,
        string Type,
        int Status,
        Guid WarehouseId,
        string ReferenceNo,
        string? Notes,
        DateTime? PostedAt,
        string? PostedBy,
        DateTime? CancelledAt,
        string? CancelledBy,
        IReadOnlyList<MovementLineResponse> Lines
    );

    public sealed record MovementLineResponse(
        Guid Id,
        Guid ProductId,
        decimal Quantity
    );
}
