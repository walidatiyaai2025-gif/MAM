SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Uncategorized uniqueidentifier = '00000000-0000-0000-0000-000000000001';

IF COL_LENGTH(N'dbo.MediaAsset', N'UploadedAtUtc') IS NULL
    ALTER TABLE dbo.MediaAsset ADD UploadedAtUtc datetime2(7) NULL;

UPDATE dbo.MediaAsset
SET UploadedAtUtc = CreatedAtUtc
WHERE UploadedAtUtc IS NULL;

ALTER TABLE dbo.MediaAsset ALTER COLUMN UploadedAtUtc datetime2(7) NOT NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MediaAsset')
      AND c.name = N'UploadedAtUtc'
)
    ALTER TABLE dbo.MediaAsset ADD CONSTRAINT DF_MediaAsset_UploadedAtUtc DEFAULT SYSUTCDATETIME() FOR UploadedAtUtc;

IF COL_LENGTH(N'dbo.MediaAsset', N'ProductionDate') IS NULL
    ALTER TABLE dbo.MediaAsset ADD ProductionDate date NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MediaAsset') AND name=N'IX_MediaAsset_UploadedAtUtc')
    CREATE INDEX IX_MediaAsset_UploadedAtUtc ON dbo.MediaAsset(UploadedAtUtc DESC, AssetId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MediaAsset') AND name=N'IX_MediaAsset_ProductionDate')
    CREATE INDEX IX_MediaAsset_ProductionDate ON dbo.MediaAsset(ProductionDate DESC, AssetId) WHERE ProductionDate IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.MamCategory WHERE CategoryId=@Uncategorized)
BEGIN
    INSERT dbo.MamCategory(CategoryId,ParentCategoryId,NameEn,NameAr,NameNormalized,IsSystem,SortOrder)
    VALUES(@Uncategorized,NULL,N'Uncategorized',N'غير مصنف',N'uncategorized',1,-2147483648);
END;

;WITH LegacyCategories AS
(
    SELECT DISTINCT
        LEFT(LTRIM(RTRIM(m.Category)), 200) AS NameEn,
        LEFT(LOWER(LTRIM(RTRIM(m.Category))), 200) AS NameNormalized
    FROM dbo.MamAssetMetadata m
    WHERE NULLIF(LTRIM(RTRIM(m.Category)), N'') IS NOT NULL
)
INSERT dbo.MamCategory(CategoryId,ParentCategoryId,NameEn,NameAr,NameNormalized,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc)
SELECT NEWID(),NULL,l.NameEn,NULL,l.NameNormalized,0,0,1,SYSUTCDATETIME(),SYSUTCDATETIME()
FROM LegacyCategories l
WHERE l.NameNormalized <> N'uncategorized'
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.MamCategory c
      WHERE c.ParentCategoryId IS NULL AND c.NameNormalized=l.NameNormalized
  );

UPDATE ac
SET CategoryId = c.CategoryId,
    AssignedBy = N'migration-0013-legacy-category',
    AssignedAtUtc = SYSUTCDATETIME()
FROM dbo.MamAssetCategory ac
INNER JOIN dbo.MamAssetMetadata m ON m.AssetId=ac.AssetId
INNER JOIN dbo.MamCategory c ON c.ParentCategoryId IS NULL
    AND c.NameNormalized=LEFT(LOWER(LTRIM(RTRIM(m.Category))),200)
WHERE ac.CategoryId=@Uncategorized
  AND NULLIF(LTRIM(RTRIM(m.Category)),N'') IS NOT NULL
  AND c.CategoryId<>@Uncategorized;

INSERT dbo.MamAssetCategory(AssetId,CategoryId,AssignedBy,AssignedAtUtc)
SELECT a.AssetId,@Uncategorized,N'migration-0013-default',SYSUTCDATETIME()
FROM dbo.MediaAsset a
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamAssetCategory ac WHERE ac.AssetId=a.AssetId);

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_MediaAsset_DefaultCategory
ON dbo.MediaAsset
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.MamAssetCategory(AssetId,CategoryId,AssignedBy,AssignedAtUtc)
    SELECT i.AssetId,''00000000-0000-0000-0000-000000000001'',N''system-default'',SYSUTCDATETIME()
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MamAssetCategory ac WHERE ac.AssetId=i.AssetId);
END;
');

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0013_p12_media_library_date_category_trees')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0013_p12_media_library_date_category_trees');

COMMIT TRANSACTION;
