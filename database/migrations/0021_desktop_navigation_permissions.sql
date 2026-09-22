SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamNavigationItem', N'U') IS NOT NULL
BEGIN
    DECLARE @DesktopDefaults TABLE
    (
        NavigationKey nvarchar(64) NOT NULL,
        RouteKey nvarchar(64) NOT NULL,
        LabelEn nvarchar(100) NOT NULL,
        LabelAr nvarchar(100) NOT NULL,
        SortOrder int NOT NULL
    );

    INSERT @DesktopDefaults(NavigationKey,RouteKey,LabelEn,LabelAr,SortOrder)
    VALUES
    (N'desktop-dashboard',N'dashboard',N'Dashboard',N'لوحة التحكم',10),
    (N'desktop-library',N'library',N'Media Library',N'مكتبة الوسائط',20),
    (N'desktop-asset',N'asset',N'Asset Details',N'تفاصيل الأصل',30),
    (N'desktop-ingest',N'ingest',N'New Ingest',N'إدخال جديد',40),
    (N'desktop-tapes',N'tapes',N'Tape Management',N'إدارة الأشرطة',50),
    (N'desktop-upload',N'upload',N'Add Media',N'إضافة ميديا',60),
    (N'desktop-queue',N'queue',N'Processing Queue',N'قائمة المعالجة',70),
    (N'desktop-admin',N'admin',N'Administration',N'الإدارة',80),
    (N'desktop-settings',N'settings',N'Settings',N'الإعدادات',90);

    MERGE dbo.MamNavigationItem AS target
    USING @DesktopDefaults AS source
      ON target.NavigationKey=source.NavigationKey
    WHEN NOT MATCHED BY TARGET THEN
      INSERT(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy)
      VALUES(source.NavigationKey,source.RouteKey,NULL,N'route',source.LabelEn,source.LabelAr,1,source.SortOrder,SYSUTCDATETIME(),N'migration-0021');
END;

COMMIT TRANSACTION;
