namespace InventoryTracker.Domain.Auth;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string InventoryManager = "InventoryManager";
    public const string WarehouseStaff = "WarehouseStaff";
    public const string ReadOnly = "ReadOnly";

    // for [Authorize(Roles="...")]
    public const string CanManageMasterData = Admin + "," + InventoryManager;
    public const string CanReadMasterData = Admin + "," + InventoryManager + "," + WarehouseStaff + "," + ReadOnly;
}
