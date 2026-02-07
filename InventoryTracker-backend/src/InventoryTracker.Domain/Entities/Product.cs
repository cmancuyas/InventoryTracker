using InventoryTracker.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace InventoryTracker.Domain.Entities
{
    public sealed class Product : BaseModel
    {
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public string UnitOfMeasure { get; set; } = "";
    }
}
