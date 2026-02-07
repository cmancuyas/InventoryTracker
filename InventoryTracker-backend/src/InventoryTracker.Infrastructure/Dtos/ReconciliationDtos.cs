namespace InventoryTracker.Infrastructure.Dtos;

public static class ReconciliationDtos
{
    // Existing
    public sealed record SyncBalancesRequest(
        Guid? WarehouseId = null,
        Guid? ProductId = null,
        int MaxCreates = 50_000
    );

    public sealed record SyncBalancesResult(
        int Created,
        int SkippedExisting,
        int ConsideredPairs,
        IReadOnlyList<CreatedBalanceRow> CreatedRows
    );

    public sealed record CreatedBalanceRow(
        Guid Id,
        Guid WarehouseId,
        Guid ProductId,
        decimal OnHand
    );

    // New reconciliation run endpoint
    public sealed record RunRequest(
        Guid? WarehouseId = null,
        Guid? ProductId = null,
        DateTime? FromUtc = null,
        DateTime? ToUtc = null,
        bool SafeMode = true,
        bool DryRun = false
    );

    public sealed record DiffRow(
        Guid WarehouseId,
        Guid ProductId,
        decimal? CurrentOnHand,
        decimal ComputedOnHand,
        decimal Delta
    );

    public sealed record RunResponse(
        bool SafeMode,
        bool DryRun,
        int AffectedPairs,
        int MissingCreated,
        int UpdatedExisting,
        int Unchanged,
        DiffRow[] Diffs
    );
}
