SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamNavigationItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamNavigationItem
    (
        NavigationKey nvarchar(64) NOT NULL CONSTRAINT PK_MamNavigationItem PRIMARY KEY,
        RouteKey nvarchar(64) NULL,
        ParentKey nvarchar(64) NULL,
        ItemType nvarchar(16) NOT NULL,
        LabelEn nvarchar(100) NOT NULL,
        LabelAr nvarchar(100) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_MamNavigationItem_IsEnabled DEFAULT (1),
        SortOrder int NOT NULL,
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_MamNavigationItem_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedBy nvarchar(200) NULL,
        CONSTRAINT CK_MamNavigationItem_Type CHECK (ItemType IN (N'route', N'group')),
        CONSTRAINT CK_MamNavigationItem_LabelEn CHECK (LEN(LTRIM(RTRIM(LabelEn))) > 0),
        CONSTRAINT CK_MamNavigationItem_LabelAr CHECK (LEN(LTRIM(RTRIM(LabelAr))) > 0),
        CONSTRAINT CK_MamNavigationItem_SortOrder CHECK (SortOrder BETWEEN 0 AND 9999)
    );
END;

DECLARE @Defaults TABLE
(
    NavigationKey nvarchar(64) NOT NULL,
    RouteKey nvarchar(64) NULL,
    ParentKey nvarchar(64) NULL,
    ItemType nvarchar(16) NOT NULL,
    LabelEn nvarchar(100) NOT NULL,
    LabelAr nvarchar(100) NOT NULL,
    SortOrder int NOT NULL
);

INSERT @Defaults(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,SortOrder)
VALUES
(N'dashboard',N'dashboard',NULL,N'route',N'Dashboard',N'لوحة التحكم',10),
(N'library',N'library',NULL,N'route',N'Media Library',N'مكتبة الوسائط',20),
(N'curation-actions',N'curation-actions',NULL,N'route',N'Curation Actions',N'إجراءات التهيئة',30),
(N'ingest',N'ingest',NULL,N'route',N'New Ingest',N'إدخال جديد',40),
(N'upload',N'upload',NULL,N'route',N'Add Media',N'إضافة ميديا',50),
(N'queue',N'queue',NULL,N'route',N'Processing Queue',N'قائمة المعالجة',60),
(N'reports',N'reports',NULL,N'route',N'Reports',N'التقارير',70),
(N'protection',N'protection',NULL,N'route',N'Backup Protection',N'حماية النسخة الاحتياطية',80),
(N'system-admin',NULL,NULL,N'group',N'System Administrator Settings',N'إعدادات مسؤول النظام',90),
(N'admin',N'admin',N'system-admin',N'route',N'Administration',N'الإدارة',10),
(N'settings',N'settings',N'system-admin',N'route',N'Settings',N'الإعدادات',20),
(N'categories',N'categories',N'system-admin',N'route',N'Categories',N'التصنيفات',30),
(N'references',N'references',N'system-admin',N'route',N'Reference Library',N'مكتبة المراجع',40),
(N'mediaPermissions',N'mediaPermissions',N'system-admin',N'route',N'Media Type Permissions',N'صلاحيات أنواع الوسائط',50),
(N'admin-actions',N'admin-actions',NULL,N'route',N'Administration Actions',N'إجراءات الإدارة',100),
(N'search',N'search',NULL,N'route',N'Content Search',N'البحث في المحتوى',110);

MERGE dbo.MamNavigationItem AS target
USING @Defaults AS source
ON target.NavigationKey = source.NavigationKey
WHEN NOT MATCHED BY TARGET THEN
    INSERT(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy)
    VALUES(source.NavigationKey,source.RouteKey,source.ParentKey,source.ItemType,source.LabelEn,source.LabelAr,1,source.SortOrder,SYSUTCDATETIME(),N'migration');

COMMIT TRANSACTION;
