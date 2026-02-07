namespace InventoryTracker.Infrastructure.Idempotency.Services;

public interface IIdempotencyService
{
    Task<(bool Hit, int StatusCode, string ResponseJson)> TryGetCachedAsync(
        string key, string endpoint, string? userId, CancellationToken ct);

    Task<Guid> BeginAsync(string key, string endpoint, string? userId, CancellationToken ct);

    Task CompleteAsync(Guid recordId, bool succeeded, int statusCode, string? responseJson, string? errorMessage, CancellationToken ct);
}
