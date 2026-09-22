SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamRolePermission',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamRolePermission
    (
        RoleName nvarchar(100) NOT NULL,
        PermissionKey nvarchar(120) NOT NULL,
        IsAllowed bit NOT NULL CONSTRAINT DF_MamRolePermission_IsAllowed DEFAULT(0),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamRolePermission_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy nvarchar(200) NULL,
        CONSTRAINT PK_MamRolePermission PRIMARY KEY(RoleName,PermissionKey)
    );
    CREATE INDEX IX_MamRolePermission_PermissionKey ON dbo.MamRolePermission(PermissionKey,RoleName);
END;

DECLARE @Roles TABLE(RoleName nvarchar(100) PRIMARY KEY);
INSERT @Roles(RoleName)
SELECT RoleName
FROM dbo.MamRole
WHERE RoleName IN
(
    N'Administrator',N'CatalogManager',N'CatalogEditor',N'Viewer',
    N'TapeManager',N'TapeOperator',N'TapeViewer'
);

DECLARE @Permissions TABLE(PermissionKey nvarchar(120) PRIMARY KEY);
INSERT @Permissions(PermissionKey) VALUES
(N'catalog.read'),
(N'catalog.write'),
(N'catalog.delete'),
(N'audit.read'),
(N'administration.manage'),
(N'tape.view'),
(N'tape.create'),
(N'tape.edit'),
(N'tape.delete'),
(N'tape.print'),
(N'tape.search'),
(N'tape.formats.manage'),
(N'tape.departments.manage'),
(N'system-functions.view'),
(N'system-functions.manage');

INSERT dbo.MamRolePermission(RoleName,PermissionKey,IsAllowed,UpdatedBy)
SELECT r.RoleName,p.PermissionKey,
       CONVERT(bit,CASE
         WHEN r.RoleName=N'Administrator' THEN 1
         WHEN r.RoleName=N'CatalogManager' AND p.PermissionKey IN(N'catalog.read',N'catalog.write',N'catalog.delete') THEN 1
         WHEN r.RoleName=N'CatalogEditor' AND p.PermissionKey IN(N'catalog.read',N'catalog.write',N'tape.view',N'tape.create',N'tape.edit',N'tape.print',N'tape.search') THEN 1
         WHEN r.RoleName=N'Viewer' AND p.PermissionKey IN(N'catalog.read',N'tape.view',N'tape.search') THEN 1
         WHEN r.RoleName=N'TapeManager' AND p.PermissionKey IN(N'catalog.read',N'tape.view',N'tape.create',N'tape.edit',N'tape.delete',N'tape.print',N'tape.search',N'tape.formats.manage',N'tape.departments.manage') THEN 1
         WHEN r.RoleName=N'TapeOperator' AND p.PermissionKey IN(N'catalog.read',N'tape.view',N'tape.create',N'tape.edit',N'tape.print',N'tape.search') THEN 1
         WHEN r.RoleName=N'TapeViewer' AND p.PermissionKey IN(N'catalog.read',N'tape.view',N'tape.search') THEN 1
         ELSE 0
       END),
       N'migration-0022'
FROM @Roles r
CROSS JOIN @Permissions p
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.MamRolePermission existing
    WHERE existing.RoleName=r.RoleName AND existing.PermissionKey=p.PermissionKey
);

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0022_role_permission_matrix')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0022_role_permission_matrix');

COMMIT TRANSACTION;
