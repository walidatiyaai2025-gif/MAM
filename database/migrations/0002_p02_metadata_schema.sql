SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamMetadataSchema', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamMetadataSchema
    (
        SchemaKey nvarchar(100) NOT NULL CONSTRAINT PK_MamMetadataSchema PRIMARY KEY,
        SchemaVersion int NOT NULL,
        DisplayNameEn nvarchar(200) NOT NULL,
        DisplayNameAr nvarchar(200) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_MamMetadataSchema_IsActive DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamMetadataSchema_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.MamMetadataField', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamMetadataField
    (
        SchemaKey nvarchar(100) NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        DisplayNameEn nvarchar(200) NOT NULL,
        DisplayNameAr nvarchar(200) NOT NULL,
        DataType nvarchar(50) NOT NULL,
        IsRequired bit NOT NULL,
        MaxLength int NULL,
        SortOrder int NOT NULL,
        CONSTRAINT PK_MamMetadataField PRIMARY KEY (SchemaKey, FieldKey),
        CONSTRAINT FK_MamMetadataField_Schema FOREIGN KEY (SchemaKey) REFERENCES dbo.MamMetadataSchema(SchemaKey),
        CONSTRAINT CK_MamMetadataField_MaxLength CHECK (MaxLength IS NULL OR MaxLength > 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMetadataSchema WHERE SchemaKey = N'core-media-v1')
BEGIN
    INSERT dbo.MamMetadataSchema(SchemaKey, SchemaVersion, DisplayNameEn, DisplayNameAr, IsActive)
    VALUES(N'core-media-v1', 1, N'Core Media Metadata', N'البيانات الوصفية الأساسية للوسائط', 1);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMetadataField WHERE SchemaKey = N'core-media-v1' AND FieldKey = N'title')
BEGIN
    INSERT dbo.MamMetadataField(SchemaKey, FieldKey, DisplayNameEn, DisplayNameAr, DataType, IsRequired, MaxLength, SortOrder) VALUES
    (N'core-media-v1', N'title', N'Title', N'العنوان', N'text', 1, 300, 10),
    (N'core-media-v1', N'eventDate', N'Event date', N'تاريخ الحدث', N'date', 0, NULL, 20),
    (N'core-media-v1', N'category', N'Category', N'التصنيف', N'text', 0, 120, 30),
    (N'core-media-v1', N'tags', N'Tags', N'الوسوم', N'text', 0, 1000, 40),
    (N'core-media-v1', N'preservationNotes', N'Preservation notes', N'ملاحظات الحفظ', N'text', 0, 2000, 50);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0002_p02_metadata_schema')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0002_p02_metadata_schema');
END;

COMMIT TRANSACTION;
