using System;
using System.Collections.Generic;
using System.Text;

namespace InventoryTracker.Domain.Entities
{
    public enum InventoryMovementStatus
    {
        Draft = 1,
        Posted = 2,
        Cancelled = 3
    }
}
