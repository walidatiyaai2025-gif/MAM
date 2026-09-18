SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTag
    (
        TagId uniqueidentifier NOT NULL CONSTRAINT PK_MamTag PRIMARY KEY,
        Name nvarchar(120) NOT NULL,
        NormalizedName nvarchar(120) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_MamTag_Version DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTag_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTag_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_MamTag_NormalizedName UNIQUE(NormalizedName),
        CONSTRAINT CK_MamTag_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_MamTag_NormalizedName_NotBlank CHECK (LEN(LTRIM(RTRIM(NormalizedName))) > 0)
    );

    INSERT dbo.MamTag(TagId, Name, NormalizedName, Version, CreatedAtUtc, UpdatedAtUtc)
    SELECT NEWID(), MIN(TagDisplay), TagNormalized, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM dbo.MamAssetTag
    WHERE LEN(LTRIM(RTRIM(TagNormalized))) > 0
    GROUP BY TagNormalized;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0016_taxonomy_collections_tags_management')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0016_taxonomy_collections_tags_management');

COMMIT TRANSACTION;
