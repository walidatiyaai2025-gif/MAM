SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.inv_tape_departments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inv_tape_departments
    (
        Code nvarchar(256) NOT NULL CONSTRAINT PK_inv_tape_departments PRIMARY KEY,
        NameEn nvarchar(256) NOT NULL,
        NameAr nvarchar(256) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_inv_tape_departments_IsActive DEFAULT 1,
        SortOrder int NOT NULL CONSTRAINT DF_inv_tape_departments_SortOrder DEFAULT 0,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_inv_tape_departments_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy nvarchar(256) NOT NULL CONSTRAINT DF_inv_tape_departments_UpdatedBy DEFAULT N'system'
    );
END;

MERGE dbo.inv_tape_departments AS target
USING (VALUES
    (N'MEDIA',N'Media Department',N'الإدارة الإعلامية',10)
) AS source(Code,NameEn,NameAr,SortOrder)
ON target.Code=source.Code
WHEN NOT MATCHED THEN
    INSERT(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedBy)
    VALUES(source.Code,source.NameEn,source.NameAr,1,source.SortOrder,N'migration-0017');

INSERT dbo.inv_tape_departments(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedBy)
SELECT DISTINCT LEFT(LTRIM(RTRIM(t.OwnerDepartment)),256),
       LEFT(LTRIM(RTRIM(t.OwnerDepartment)),256),
       LEFT(LTRIM(RTRIM(t.OwnerDepartment)),256),
       1,1000,N'migration-0017-existing'
FROM dbo.inv_tapes t
WHERE t.OwnerDepartment IS NOT NULL
  AND LTRIM(RTRIM(t.OwnerDepartment))<>N''
  AND NOT EXISTS
      (SELECT 1 FROM dbo.inv_tape_departments d
       WHERE d.Code=LEFT(LTRIM(RTRIM(t.OwnerDepartment)),256));

IF OBJECT_ID(N'dbo.MamSystemFunction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamSystemFunction
    (
        FunctionKey nvarchar(128) NOT NULL CONSTRAINT PK_MamSystemFunction PRIMARY KEY,
        NameEn nvarchar(200) NOT NULL,
        NameAr nvarchar(200) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_MamSystemFunction_IsEnabled DEFAULT 1,
        Version bigint NOT NULL CONSTRAINT DF_MamSystemFunction_Version DEFAULT 1,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamSystemFunction_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy nvarchar(256) NOT NULL CONSTRAINT DF_MamSystemFunction_UpdatedBy DEFAULT N'system',
        CONSTRAINT CK_MamSystemFunction_Version CHECK (Version > 0)
    );
END;

MERGE dbo.MamSystemFunction AS target
USING (VALUES
    (N'tape.management',N'Tape Management',N'إدارة الأشرطة'),
    (N'tape.search.in-content',N'Tapes in Content Search',N'الأشرطة في البحث في المحتوى'),
    (N'tape.printing',N'Tape Barcode and Report Printing',N'طباعة باركود وتقارير الأشرطة'),
    (N'reports.printable',N'Printable Official Reports',N'التقارير الرسمية القابلة للطباعة')
) AS source(FunctionKey,NameEn,NameAr)
ON target.FunctionKey=source.FunctionKey
WHEN NOT MATCHED THEN
    INSERT(FunctionKey,NameEn,NameAr,IsEnabled,Version,UpdatedBy)
    VALUES(source.FunctionKey,source.NameEn,source.NameAr,1,1,N'migration-0017');

IF OBJECT_ID(N'dbo.MamNavigationItem', N'U') IS NOT NULL
BEGIN
    MERGE dbo.MamNavigationItem AS target
    USING (VALUES
        (N'tapes',N'tapes',CAST(NULL AS nvarchar(64)),N'route',N'Tape Management',N'إدارة الأشرطة',75),
        (N'systemFunctions',N'systemFunctions',CAST(NULL AS nvarchar(64)),N'route',N'System Functions',N'وظائف النظام',95)
    ) AS source(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,SortOrder)
    ON target.NavigationKey=source.NavigationKey
    WHEN NOT MATCHED THEN
        INSERT(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy)
        VALUES(source.NavigationKey,source.RouteKey,source.ParentKey,source.ItemType,source.LabelEn,source.LabelAr,1,source.SortOrder,SYSUTCDATETIME(),N'migration-0017');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0017_t22_tape_labels_departments_system_functions')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0017_t22_tape_labels_departments_system_functions');

COMMIT TRANSACTION;
