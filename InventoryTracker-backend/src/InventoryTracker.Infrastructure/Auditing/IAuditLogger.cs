using System;
using System.Collections.Generic;
using System.Text;

namespace InventoryTracker.Infrastructure.Auditing
{
    public interface IAuditLogger
    {
        Task LogAsync(
            string action,
            string entity,
            Guid? entityId,
            Guid? warehouseId,
            Guid? productId,
            bool success,
            string? message,
            string? dataJson,
            CancellationToken ct);
    }
}
