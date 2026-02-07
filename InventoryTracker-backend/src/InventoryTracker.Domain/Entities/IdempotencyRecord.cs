namespace InventoryTracker.Domain.Entities;

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }

    public string Key { get; set; } = "";
    public string Endpoint { get; set; } = "";

    public string? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool Completed { get; set; }
    public bool Succeeded { get; set; }

    public int StatusCode { get; set; }

    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
}
