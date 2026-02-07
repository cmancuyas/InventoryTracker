using InventoryTracker.Domain.Entities;
using InventoryTracker.Infrastructure.Idempotency.Services;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventoryTracker.Infrastructure.Idempotency.Services;

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly AppDbContext _db;

    public IdempotencyService(AppDbContext db) => _db = db;

    public async Task<(bool Hit, int StatusCode, string ResponseJson)> TryGetCachedAsync(
        string key, string endpoint, string? userId, CancellationToken ct)
    {
        var rec = await _db.IdempotencyRecords.AsNoTracking()
            .Where(x => x.Key == key && x.Endpoint == endpoint && x.UserId == userId && x.Completed)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (rec is null || string.IsNullOrWhiteSpace(rec.ResponseJson))
            return (false, 0, "");

        return (true, rec.StatusCode, rec.ResponseJson!);
    }

    public async Task<Guid> BeginAsync(string key, string endpoint, string? userId, CancellationToken ct)
    {
        var rec = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            Endpoint = endpoint,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Completed = false,
            Succeeded = false,
            StatusCode = 0
        };

        _db.IdempotencyRecords.Add(rec);
        await _db.SaveChangesAsync(ct);
        return rec.Id;
    }

    public async Task CompleteAsync(Guid recordId, bool succeeded, int statusCode, string? responseJson, string? errorMessage, CancellationToken ct)
    {
        var rec = await _db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Id == recordId, ct);
        if (rec is null) return;

        rec.Completed = true;
        rec.Succeeded = succeeded;
        rec.StatusCode = statusCode;
        rec.ResponseJson = responseJson;
        rec.ErrorMessage = errorMessage;

        await _db.SaveChangesAsync(ct);
    }
}
