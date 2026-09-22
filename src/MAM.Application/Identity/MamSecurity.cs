namespace MAM.Application.Identity;

public static class MamRoles
{
    public const string Administrator = "Administrator";
    public const string CatalogEditor = "CatalogEditor";
    public const string Viewer = "Viewer";
    public const string TapeManager = "TapeManager";
    public const string TapeOperator = "TapeOperator";
    public const string TapeViewer = "TapeViewer";
    public const string CatalogManager = "CatalogManager";
}

public static class MamPermissions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string CatalogDelete = "catalog.delete";
    public const string AuditRead = "audit.read";
    public const string Administration = "administration.manage";

    public const string TapeView = "tape.view";
    public const string TapeCreate = "tape.create";
    public const string TapeEdit = "tape.edit";
    public const string TapeDelete = "tape.delete";
    public const string TapePrint = "tape.print";
    public const string TapeSearch = "tape.search";
    public const string TapeManageFormats = "tape.formats.manage";
    public const string TapeManageDepartments = "tape.departments.manage";

    public const string SystemFunctionsView = "system-functions.view";
    public const string SystemFunctionsManage = "system-functions.manage";
}

public static class MamSecurity
{
    public const string PermissionClaimType = "mam.permission";

    public const string CatalogReadPolicy = "mam.catalog.read";
    public const string CatalogWritePolicy = "mam.catalog.write";
    public const string CatalogDeletePolicy = "mam.catalog.delete";
    public const string AuditReadPolicy = "mam.audit.read";
    public const string AdministrationPolicy = "mam.administration";

    public const string TapeViewPolicy = "mam.tape.view";
    public const string TapeCreatePolicy = "mam.tape.create";
    public const string TapeEditPolicy = "mam.tape.edit";
    public const string TapeDeletePolicy = "mam.tape.delete";
    public const string TapePrintPolicy = "mam.tape.print";
    public const string TapeSearchPolicy = "mam.tape.search";
    public const string TapeManageFormatsPolicy = "mam.tape.formats.manage";
    public const string TapeManageDepartmentsPolicy = "mam.tape.departments.manage";

    public const string SystemFunctionsViewPolicy = "mam.system-functions.view";
    public const string SystemFunctionsManagePolicy = "mam.system-functions.manage";

    public static IReadOnlyCollection<string> PermissionsForRole(string role) => role switch
    {
        MamRoles.Administrator =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.CatalogWrite,
            MamPermissions.CatalogDelete,
            MamPermissions.AuditRead,
            MamPermissions.Administration,
            MamPermissions.TapeView,
            MamPermissions.TapeCreate,
            MamPermissions.TapeEdit,
            MamPermissions.TapeDelete,
            MamPermissions.TapePrint,
            MamPermissions.TapeSearch,
            MamPermissions.TapeManageFormats,
            MamPermissions.TapeManageDepartments,
            MamPermissions.SystemFunctionsView,
            MamPermissions.SystemFunctionsManage
        ],
        MamRoles.CatalogManager =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.CatalogWrite,
            MamPermissions.CatalogDelete
        ],
        MamRoles.CatalogEditor =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.CatalogWrite,
            MamPermissions.TapeView,
            MamPermissions.TapeCreate,
            MamPermissions.TapeEdit,
            MamPermissions.TapePrint,
            MamPermissions.TapeSearch
        ],
        MamRoles.Viewer =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.TapeView,
            MamPermissions.TapeSearch
        ],
        MamRoles.TapeManager =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.TapeView,
            MamPermissions.TapeCreate,
            MamPermissions.TapeEdit,
            MamPermissions.TapeDelete,
            MamPermissions.TapePrint,
            MamPermissions.TapeSearch,
            MamPermissions.TapeManageFormats,
            MamPermissions.TapeManageDepartments
        ],
        MamRoles.TapeOperator =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.TapeView,
            MamPermissions.TapeCreate,
            MamPermissions.TapeEdit,
            MamPermissions.TapePrint,
            MamPermissions.TapeSearch
        ],
        MamRoles.TapeViewer =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.TapeView,
            MamPermissions.TapeSearch
        ],
        _ => Array.Empty<string>()
    };
}
