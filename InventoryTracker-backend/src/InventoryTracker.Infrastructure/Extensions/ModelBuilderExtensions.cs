using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using InventoryTracker.Domain.Common;

namespace InventoryTracker.Infrastructure.Persistence.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplySoftDeleteQueryFilter(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (!typeof(BaseModel).IsAssignableFrom(clrType))
                continue;

            var parameter = Expression.Parameter(clrType, "e");
            var isDeletedProperty = Expression.Property(parameter, nameof(BaseModel.IsDeleted));
            var condition = Expression.Equal(isDeletedProperty, Expression.Constant(false));

            var lambda = Expression.Lambda(condition, parameter);

            modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        }
    }
}
