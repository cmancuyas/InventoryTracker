using InventoryTracker.Infrastructure.Dtos;

namespace InventoryTracker.Infrastructure.Inventory;

public interface IInventoryMovementService
{
    Task<InventoryMovementDtos.MovementResponse> CreateDraftAsync(InventoryMovementDtos.CreateDraftRequest req, CancellationToken ct);
    Task<InventoryMovementDtos.MovementResponse> UpdateDraftAsync(Guid id, InventoryMovementDtos.UpdateDraftRequest req, CancellationToken ct);

    Task<InventoryMovementDtos.MovementResponse> AddLineAsync(Guid id, InventoryMovementDtos.LineRequest req, CancellationToken ct);
    Task<InventoryMovementDtos.MovementResponse> ReplaceLinesAsync(Guid id, InventoryMovementDtos.ReplaceLinesRequest req, CancellationToken ct);

    Task<InventoryMovementDtos.MovementResponse> PostAsync(Guid id, CancellationToken ct);
    Task<InventoryMovementDtos.MovementResponse> CancelAsync(Guid id, CancellationToken ct);
}
