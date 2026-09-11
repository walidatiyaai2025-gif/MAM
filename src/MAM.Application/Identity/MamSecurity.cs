namespace MAM.Application.Identity;

public static class MamRoles
{
    public const string Administrator = "Administrator";
    public const string CatalogEditor = "CatalogEditor";
    public const string Viewer = "Viewer";
}

public static class MamPermissions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string AuditRead = "audit.read";
    public const string Administration = "administration.manage";
}

public static class MamSecurity
{
    public const string PermissionClaimType = "mam.permission";
    public const string CatalogReadPolicy = "mam.catalog.read";
    public const string CatalogWritePolicy = "mam.catalog.write";
    public const string AuditReadPolicy = "mam.audit.read";
    public const string AdministrationPolicy = "mam.administration";

    public static IReadOnlyCollection<string> PermissionsForRole(string role) => role switch
    {
        MamRoles.Administrator =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.CatalogWrite,
            MamPermissions.AuditRead,
            MamPermissions.Administration
        ],
        MamRoles.CatalogEditor =>
        [
            MamPermissions.CatalogRead,
            MamPermissions.CatalogWrite
        ],
        MamRoles.Viewer =>
        [
            MamPermissions.CatalogRead
        ],
        _ => Array.Empty<string>()
    };
}
