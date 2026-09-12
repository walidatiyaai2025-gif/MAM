SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamAssetMetadata', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetMetadata
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MamAssetMetadata PRIMARY KEY,
        SchemaKey nvarchar(100) NOT NULL,
        TitleAr nvarchar(300) NULL,
        EventDate date NULL,
        Category nvarchar(120) NULL,
        CategoryNormalized nvarchar(120) NULL,
        TagsText nvarchar(1000) NULL,
        PreservationNotes nvarchar(2000) NULL,
        SearchTextNormalized nvarchar(4000) NOT NULL CONSTRAINT DF_MamAssetMetadata_SearchText DEFAULT (N''),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAssetMetadata_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamAssetMetadata_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT FK_MamAssetMetadata_Schema FOREIGN KEY (SchemaKey) REFERENCES dbo.MamMetadataSchema(SchemaKey)
    );

    CREATE INDEX IX_MamAssetMetadata_CategoryNormalized ON dbo.MamAssetMetadata(CategoryNormalized, AssetId);
    CREATE INDEX IX_MamAssetMetadata_EventDate ON dbo.MamAssetMetadata(EventDate, AssetId);
END;

IF OBJECT_ID(N'dbo.MamAssetTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetTag
    (
        AssetId uniqueidentifier NOT NULL,
        TagNormalized nvarchar(120) NOT NULL,
        TagDisplay nvarchar(120) NOT NULL,
        CONSTRAINT PK_MamAssetTag PRIMARY KEY (AssetId, TagNormalized),
        CONSTRAINT FK_MamAssetTag_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId)
    );
    CREATE INDEX IX_MamAssetTag_TagNormalized ON dbo.MamAssetTag(TagNormalized, AssetId);
END;

IF OBJECT_ID(N'dbo.MamCollection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamCollection
    (
        CollectionId uniqueidentifier NOT NULL CONSTRAINT PK_MamCollection PRIMARY KEY,
        NameEn nvarchar(200) NOT NULL,
        NameAr nvarchar(200) NULL,
        Version bigint NOT NULL CONSTRAINT DF_MamCollection_Version DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamCollection_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamCollection_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MamCollection_NameEn_NotBlank CHECK (LEN(LTRIM(RTRIM(NameEn))) > 0)
    );
    CREATE INDEX IX_MamCollection_UpdatedAtUtc ON dbo.MamCollection(UpdatedAtUtc DESC, CollectionId);
END;

IF OBJECT_ID(N'dbo.MamCollectionAsset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamCollectionAsset
    (
        CollectionId uniqueidentifier NOT NULL,
        AssetId uniqueidentifier NOT NULL,
        AddedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamCollectionAsset_AddedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamCollectionAsset PRIMARY KEY (CollectionId, AssetId),
        CONSTRAINT FK_MamCollectionAsset_Collection FOREIGN KEY (CollectionId) REFERENCES dbo.MamCollection(CollectionId),
        CONSTRAINT FK_MamCollectionAsset_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId)
    );
    CREATE INDEX IX_MamCollectionAsset_AssetId ON dbo.MamCollectionAsset(AssetId, CollectionId);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMetadataField WHERE SchemaKey = N'core-media-v1' AND FieldKey = N'titleAr')
BEGIN
    INSERT dbo.MamMetadataField(SchemaKey, FieldKey, DisplayNameEn, DisplayNameAr, DataType, IsRequired, MaxLength, SortOrder)
    VALUES(N'core-media-v1', N'titleAr', N'Arabic title', N'العنوان العربي', N'text', 0, 300, 15);
END;

INSERT dbo.MamAssetMetadata(AssetId, SchemaKey, SearchTextNormalized, UpdatedAtUtc)
SELECT a.AssetId, N'core-media-v1', LOWER(a.Title), a.UpdatedAtUtc
FROM dbo.MediaAsset a
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamAssetMetadata m WHERE m.AssetId = a.AssetId);

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0005_p05_search_curation')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0005_p05_search_curation');
END;

COMMIT TRANSACTION;
