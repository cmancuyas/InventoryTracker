using InventoryTracker.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace InventoryTracker.Domain.Entities
{
    public sealed class Warehouse : BaseModel
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Location { get; set; }
    }
}
