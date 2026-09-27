SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Roles TABLE(RoleName nvarchar(100) PRIMARY KEY);
INSERT @Roles(RoleName) VALUES
(N'VideoManagerFull'),
(N'ImageManagerFull'),
(N'MediaManagerFull'),
(N'MediaTapeManagerFull');

INSERT dbo.MamRole(RoleId,RoleName)
SELECT NEWID(),r.RoleName
FROM @Roles r
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamRole existing WHERE existing.RoleName=r.RoleName);

IF OBJECT_ID(N'dbo.MamRolePermission',N'U') IS NOT NULL
BEGIN
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

    MERGE dbo.MamRolePermission AS target
    USING
    (
        SELECT r.RoleName,p.PermissionKey,
               CONVERT(bit,CASE
                   WHEN p.PermissionKey IN (N'catalog.read',N'catalog.write',N'catalog.delete') THEN 1
                   WHEN r.RoleName=N'MediaTapeManagerFull'
                        AND p.PermissionKey IN
                        (
                            N'tape.view',N'tape.create',N'tape.edit',N'tape.delete',
                            N'tape.print',N'tape.search',N'tape.formats.manage',N'tape.departments.manage'
                        ) THEN 1
                   ELSE 0
               END) IsAllowed
        FROM @Roles r
        CROSS JOIN @Permissions p
    ) AS source(RoleName,PermissionKey,IsAllowed)
    ON target.RoleName=source.RoleName AND target.PermissionKey=source.PermissionKey
    WHEN MATCHED THEN
        UPDATE SET IsAllowed=source.IsAllowed,UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'migration-0024'
    WHEN NOT MATCHED THEN
        INSERT(RoleName,PermissionKey,IsAllowed,UpdatedBy)
        VALUES(source.RoleName,source.PermissionKey,source.IsAllowed,N'migration-0024');
END;

IF OBJECT_ID(N'dbo.MamRoleMediaPermission',N'U') IS NOT NULL
BEGIN
    DECLARE @MediaKinds TABLE(MediaKind nvarchar(40) PRIMARY KEY);
    INSERT @MediaKinds(MediaKind) VALUES(N'Video'),(N'Audio'),(N'Image'),(N'Document'),(N'Other');

    MERGE dbo.MamRoleMediaPermission AS target
    USING
    (
        SELECT r.RoleName,k.MediaKind,
               CONVERT(bit,CASE
                   WHEN r.RoleName=N'VideoManagerFull' AND k.MediaKind=N'Video' THEN 1
                   WHEN r.RoleName=N'ImageManagerFull' AND k.MediaKind=N'Image' THEN 1
                   WHEN r.RoleName IN(N'MediaManagerFull',N'MediaTapeManagerFull') AND k.MediaKind IN(N'Video',N'Image') THEN 1
                   ELSE 0
               END) Allowed
        FROM @Roles r
        CROSS JOIN @MediaKinds k
    ) AS source(RoleName,MediaKind,Allowed)
    ON target.RoleName=source.RoleName AND target.MediaKind=source.MediaKind
    WHEN MATCHED THEN
        UPDATE SET
            CanView=source.Allowed,
            CanUpload=source.Allowed,
            CanEdit=source.Allowed,
            CanProcess=source.Allowed,
            CanDownload=source.Allowed,
            UpdatedAtUtc=SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload)
        VALUES(source.RoleName,source.MediaKind,source.Allowed,source.Allowed,source.Allowed,source.Allowed,source.Allowed);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0024_media_role_presets')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0024_media_role_presets');

COMMIT TRANSACTION;
