using InventoryTracker.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace InventoryTracker.Domain.Entities
{
    public sealed class InventoryBalance : BaseModel
    {
        public Guid WarehouseId { get; set; }
        public Guid ProductId { get; set; }

        public decimal OnHand { get; set; }

        public Warehouse? Warehouse { get; set; }
        public Product? Product { get; set; }
    }
}
