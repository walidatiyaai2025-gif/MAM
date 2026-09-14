SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamCategory
    (
        CategoryId uniqueidentifier NOT NULL CONSTRAINT PK_MamCategory PRIMARY KEY,
        ParentCategoryId uniqueidentifier NULL,
        NameEn nvarchar(200) NOT NULL,
        NameAr nvarchar(200) NULL,
        NameNormalized nvarchar(200) NOT NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_MamCategory_IsSystem DEFAULT (0),
        SortOrder int NOT NULL CONSTRAINT DF_MamCategory_SortOrder DEFAULT (0),
        Version bigint NOT NULL CONSTRAINT DF_MamCategory_Version DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamCategory_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamCategory_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamCategory_Parent FOREIGN KEY (ParentCategoryId) REFERENCES dbo.MamCategory(CategoryId),
        CONSTRAINT CK_MamCategory_NameEn_NotBlank CHECK (LEN(LTRIM(RTRIM(NameEn))) > 0),
        CONSTRAINT CK_MamCategory_NoSelfParent CHECK (ParentCategoryId IS NULL OR ParentCategoryId <> CategoryId)
    );
    CREATE UNIQUE INDEX UX_MamCategory_Parent_NameNormalized ON dbo.MamCategory(ParentCategoryId, NameNormalized);
    CREATE INDEX IX_MamCategory_Parent ON dbo.MamCategory(ParentCategoryId, SortOrder, NameEn);
END;

DECLARE @Uncategorized uniqueidentifier = '00000000-0000-0000-0000-000000000001';
IF NOT EXISTS (SELECT 1 FROM dbo.MamCategory WHERE CategoryId=@Uncategorized)
BEGIN
    INSERT dbo.MamCategory(CategoryId,ParentCategoryId,NameEn,NameAr,NameNormalized,IsSystem,SortOrder)
    VALUES(@Uncategorized,NULL,N'Uncategorized',N'غير مصنف',N'uncategorized',1,-2147483648);
END;

IF OBJECT_ID(N'dbo.MamAssetCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetCategory
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MamAssetCategory PRIMARY KEY,
        CategoryId uniqueidentifier NOT NULL,
        AssignedBy nvarchar(200) NOT NULL,
        AssignedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAssetCategory_AssignedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamAssetCategory_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT FK_MamAssetCategory_Category FOREIGN KEY (CategoryId) REFERENCES dbo.MamCategory(CategoryId)
    );
    CREATE INDEX IX_MamAssetCategory_Category ON dbo.MamAssetCategory(CategoryId, AssetId);
END;

INSERT dbo.MamAssetCategory(AssetId,CategoryId,AssignedBy)
SELECT a.AssetId,@Uncategorized,N'migration'
FROM dbo.MediaAsset a
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamAssetCategory ac WHERE ac.AssetId=a.AssetId);

IF OBJECT_ID(N'dbo.MamAssetSearchContent', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetSearchContent
    (
        AssetId uniqueidentifier NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        Language nvarchar(20) NULL,
        ContentText nvarchar(max) NOT NULL,
        SearchTextNormalized nvarchar(max) NOT NULL,
        ContentSha256 char(64) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAssetSearchContent_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamAssetSearchContent PRIMARY KEY (AssetId,SourceKind),
        CONSTRAINT FK_MamAssetSearchContent_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId)
    );
END;

IF OBJECT_ID(N'dbo.MamAssetSearchToken', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetSearchToken
    (
        AssetId uniqueidentifier NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        TokenNormalized nvarchar(120) NOT NULL,
        OccurrenceCount int NOT NULL,
        CONSTRAINT PK_MamAssetSearchToken PRIMARY KEY (AssetId,SourceKind,TokenNormalized),
        CONSTRAINT FK_MamAssetSearchToken_Content FOREIGN KEY (AssetId,SourceKind) REFERENCES dbo.MamAssetSearchContent(AssetId,SourceKind) ON DELETE CASCADE
    );
    CREATE INDEX IX_MamAssetSearchToken_Token ON dbo.MamAssetSearchToken(TokenNormalized,AssetId,SourceKind) INCLUDE (OccurrenceCount);
END;

IF OBJECT_ID(N'dbo.MamAssetTextSegment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetTextSegment
    (
        AssetId uniqueidentifier NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        SegmentIndex int NOT NULL,
        StartMs bigint NULL,
        EndMs bigint NULL,
        PageNumber int NULL,
        Text nvarchar(max) NOT NULL,
        TextNormalized nvarchar(max) NOT NULL,
        CONSTRAINT PK_MamAssetTextSegment PRIMARY KEY (AssetId,SourceKind,SegmentIndex),
        CONSTRAINT FK_MamAssetTextSegment_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT CK_MamAssetTextSegment_Time CHECK (StartMs IS NULL OR EndMs IS NULL OR EndMs >= StartMs),
        CONSTRAINT CK_MamAssetTextSegment_Page CHECK (PageNumber IS NULL OR PageNumber > 0)
    );
    CREATE INDEX IX_MamAssetTextSegment_AssetSource ON dbo.MamAssetTextSegment(AssetId,SourceKind,SegmentIndex);
END;

IF OBJECT_ID(N'dbo.MamTextExtractionStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTextExtractionStatus
    (
        AssetId uniqueidentifier NOT NULL,
        ExtractionKind nvarchar(40) NOT NULL,
        State nvarchar(20) NOT NULL,
        ProgressPercent tinyint NOT NULL CONSTRAINT DF_MamTextExtractionStatus_Progress DEFAULT (0),
        Detail nvarchar(300) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTextExtractionStatus_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc datetime2(7) NULL,
        CONSTRAINT PK_MamTextExtractionStatus PRIMARY KEY (AssetId,ExtractionKind),
        CONSTRAINT FK_MamTextExtractionStatus_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT CK_MamTextExtractionStatus_Progress CHECK (ProgressPercent BETWEEN 0 AND 100)
    );
END;

IF OBJECT_ID(N'dbo.MamReferenceSubject', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamReferenceSubject
    (
        SubjectId uniqueidentifier NOT NULL CONSTRAINT PK_MamReferenceSubject PRIMARY KEY,
        NameEn nvarchar(200) NOT NULL,
        NameAr nvarchar(200) NULL,
        DescriptionEn nvarchar(1000) NULL,
        DescriptionAr nvarchar(1000) NULL,
        TagsText nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_MamReferenceSubject_IsActive DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamReferenceSubject_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamReferenceSubject_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MamReferenceSubject_NameEn CHECK (LEN(LTRIM(RTRIM(NameEn))) > 0)
    );
END;

IF OBJECT_ID(N'dbo.MamReferenceImage', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamReferenceImage
    (
        SubjectId uniqueidentifier NOT NULL,
        AssetId uniqueidentifier NOT NULL,
        AddedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamReferenceImage_AddedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamReferenceImage PRIMARY KEY (SubjectId,AssetId),
        CONSTRAINT FK_MamReferenceImage_Subject FOREIGN KEY (SubjectId) REFERENCES dbo.MamReferenceSubject(SubjectId),
        CONSTRAINT FK_MamReferenceImage_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId)
    );
    CREATE INDEX IX_MamReferenceImage_Asset ON dbo.MamReferenceImage(AssetId,SubjectId);
END;

IF OBJECT_ID(N'dbo.MamAssetReferenceTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAssetReferenceTag
    (
        AssetId uniqueidentifier NOT NULL,
        SubjectId uniqueidentifier NOT NULL,
        Confidence decimal(5,4) NULL,
        DetectionSource nvarchar(40) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAssetReferenceTag_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamAssetReferenceTag PRIMARY KEY (AssetId,SubjectId),
        CONSTRAINT FK_MamAssetReferenceTag_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT FK_MamAssetReferenceTag_Subject FOREIGN KEY (SubjectId) REFERENCES dbo.MamReferenceSubject(SubjectId),
        CONSTRAINT CK_MamAssetReferenceTag_Confidence CHECK (Confidence IS NULL OR (Confidence >= 0 AND Confidence <= 1))
    );
END;

IF OBJECT_ID(N'dbo.MamRoleMediaPermission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamRoleMediaPermission
    (
        RoleName nvarchar(100) NOT NULL,
        MediaKind nvarchar(40) NOT NULL,
        CanView bit NOT NULL,
        CanUpload bit NOT NULL,
        CanEdit bit NOT NULL,
        CanProcess bit NOT NULL,
        CanDownload bit NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamRoleMediaPermission_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamRoleMediaPermission PRIMARY KEY (RoleName,MediaKind)
    );
END;

DECLARE @Kinds TABLE(MediaKind nvarchar(40));
INSERT @Kinds(MediaKind) VALUES(N'Video'),(N'Audio'),(N'Image'),(N'Document'),(N'Other');
INSERT dbo.MamRoleMediaPermission(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload)
SELECT r.RoleName,k.MediaKind,r.CanView,r.CanUpload,r.CanEdit,r.CanProcess,r.CanDownload
FROM (VALUES
 (N'Administrator',CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit)),
 (N'CatalogEditor',CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit),CAST(1 AS bit)),
 (N'Viewer',CAST(1 AS bit),CAST(0 AS bit),CAST(0 AS bit),CAST(0 AS bit),CAST(1 AS bit))
) r(RoleName,CanView,CanUpload,CanEdit,CanProcess,CanDownload)
CROSS JOIN @Kinds k
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamRoleMediaPermission p WHERE p.RoleName=r.RoleName AND p.MediaKind=k.MediaKind);

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0008_p12_discovery_ai_indexing')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0008_p12_discovery_ai_indexing');

COMMIT TRANSACTION;
