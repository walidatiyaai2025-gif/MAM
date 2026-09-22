SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.MamRole WHERE RoleName=N'CatalogManager')
    INSERT dbo.MamRole(RoleId,RoleName) VALUES(NEWID(),N'CatalogManager');

IF OBJECT_ID(N'dbo.MamRoleMediaPermission',N'U') IS NOT NULL
BEGIN
    DECLARE @Kinds TABLE(MediaKind nvarchar(40));
    INSERT @Kinds(MediaKind) VALUES(N'Video'),(N'Audio'),(N'Image'),(N'Document'),(N'Other');

    INSERT dbo.MamRoleMediaPermission(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload)
    SELECT N'CatalogManager',k.MediaKind,1,1,1,1,1
    FROM @Kinds k
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.MamRoleMediaPermission p
        WHERE p.RoleName=N'CatalogManager' AND p.MediaKind=k.MediaKind
    );
END;

IF OBJECT_ID(N'dbo.MamNavigationItem',N'U') IS NOT NULL
BEGIN
    DECLARE @Desktop TABLE(
        NavigationKey nvarchar(64) NOT NULL,
        LabelEn nvarchar(100) NOT NULL,
        LabelAr nvarchar(100) NOT NULL,
        SortOrder int NOT NULL
    );
    INSERT @Desktop VALUES
      (N'desktop-dashboard',N'Dashboard',N'لوحة التحكم',10),
      (N'desktop-library',N'Media Library',N'مكتبة الوسائط',20),
      (N'desktop-asset',N'Asset Details',N'تفاصيل الأصل',30),
      (N'desktop-ingest',N'New Ingest',N'إدخال جديد',40),
      (N'desktop-tapes',N'Tape Management',N'إدارة الأشرطة',50),
      (N'desktop-upload',N'Upload',N'رفع الملفات',60),
      (N'desktop-queue',N'Processing Queue',N'قائمة المعالجة',70),
      (N'desktop-admin',N'Administration',N'الإدارة',80),
      (N'desktop-settings',N'Settings',N'الإعدادات',90);

    MERGE dbo.MamNavigationItem AS target
    USING @Desktop AS source ON target.NavigationKey=source.NavigationKey
    WHEN NOT MATCHED THEN
      INSERT(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy)
      VALUES(source.NavigationKey,NULL,NULL,N'route',source.LabelEn,source.LabelAr,1,source.SortOrder,SYSUTCDATETIME(),N'migration-0021');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0021_owner_navigation_permissions_references')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0021_owner_navigation_permissions_references');

COMMIT TRANSACTION;
