SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamNavigationItem', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.MamNavigationItem WHERE NavigationKey=N'capabilities')
BEGIN
    INSERT dbo.MamNavigationItem
    (
        NavigationKey,RouteKey,ParentKey,ItemType,
        LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy
    )
    VALUES
    (
        N'capabilities',N'capabilities',N'system-admin',N'route',
        N'Release Capabilities',N'وظائف النسخة الحالية',
        1,60,SYSUTCDATETIME(),N'migration-0020'
    );
END;

COMMIT TRANSACTION;
