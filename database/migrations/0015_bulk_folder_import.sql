SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamBulkImportSession', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamBulkImportSession
    (
        SessionId uniqueidentifier NOT NULL CONSTRAINT PK_MamBulkImportSession PRIMARY KEY,
        RootFolderName nvarchar(200) NOT NULL,
        State int NOT NULL,
        TotalFiles int NOT NULL,
        TotalBytes bigint NOT NULL,
        CreatedBy nvarchar(200) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NULL,
        CONSTRAINT CK_MamBulkImportSession_TotalFiles CHECK (TotalFiles > 0),
        CONSTRAINT CK_MamBulkImportSession_TotalBytes CHECK (TotalBytes >= 0),
        CONSTRAINT CK_MamBulkImportSession_State CHECK (State BETWEEN 0 AND 5)
    );
    CREATE INDEX IX_MamBulkImportSession_CreatedBy_Updated
        ON dbo.MamBulkImportSession(CreatedBy, UpdatedAtUtc DESC, SessionId);
END;

IF OBJECT_ID(N'dbo.MamBulkImportItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamBulkImportItem
    (
        ItemId uniqueidentifier NOT NULL CONSTRAINT PK_MamBulkImportItem PRIMARY KEY,
        SessionId uniqueidentifier NOT NULL,
        RelativePath nvarchar(1000) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        CategoryName nvarchar(200) NOT NULL,
        CategoryId uniqueidentifier NULL,
        ExpectedLength bigint NOT NULL,
        ExpectedSha256 char(64) NULL,
        UploadSessionId uniqueidentifier NULL,
        AssetId uniqueidentifier NULL,
        State int NOT NULL,
        ReasonCode nvarchar(100) NULL,
        Detail nvarchar(1000) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT UQ_MamBulkImportItem_Session_Path UNIQUE(SessionId, RelativePath),
        CONSTRAINT FK_MamBulkImportItem_Session FOREIGN KEY(SessionId) REFERENCES dbo.MamBulkImportSession(SessionId) ON DELETE CASCADE,
        CONSTRAINT FK_MamBulkImportItem_Category FOREIGN KEY(CategoryId) REFERENCES dbo.MamCategory(CategoryId),
        CONSTRAINT FK_MamBulkImportItem_UploadSession FOREIGN KEY(UploadSessionId) REFERENCES dbo.MamUploadSession(SessionId),
        CONSTRAINT FK_MamBulkImportItem_Asset FOREIGN KEY(AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT CK_MamBulkImportItem_Length CHECK (ExpectedLength >= 0),
        CONSTRAINT CK_MamBulkImportItem_State CHECK (State BETWEEN 0 AND 7)
    );
    CREATE INDEX IX_MamBulkImportItem_Session_State
        ON dbo.MamBulkImportItem(SessionId, State, RelativePath);
    CREATE INDEX IX_MamBulkImportItem_UploadSession
        ON dbo.MamBulkImportItem(UploadSessionId)
        WHERE UploadSessionId IS NOT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0015_bulk_folder_import')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0015_bulk_folder_import');

COMMIT TRANSACTION;
