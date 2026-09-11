SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamUploadSession', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamUploadSession
    (
        SessionId uniqueidentifier NOT NULL CONSTRAINT PK_MamUploadSession PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        Title nvarchar(300) NOT NULL,
        OriginalFileName nvarchar(260) NOT NULL,
        ExpectedLength bigint NOT NULL,
        ExpectedSha256 char(64) NOT NULL,
        ChunkSizeBytes int NOT NULL,
        ReceivedLength bigint NOT NULL CONSTRAINT DF_MamUploadSession_ReceivedLength DEFAULT (0),
        State tinyint NOT NULL CONSTRAINT DF_MamUploadSession_State DEFAULT (0),
        IsQuarantined bit NOT NULL CONSTRAINT DF_MamUploadSession_IsQuarantined DEFAULT (0),
        PrimaryObjectKey nvarchar(1024) NULL,
        Error nvarchar(1000) NULL,
        CreatedBy nvarchar(200) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        ExpiresAtUtc datetime2(7) NOT NULL,
        CONSTRAINT UQ_MamUploadSession_AssetId UNIQUE (AssetId),
        CONSTRAINT CK_MamUploadSession_ExpectedLength CHECK (ExpectedLength > 0),
        CONSTRAINT CK_MamUploadSession_ChunkSizeBytes CHECK (ChunkSizeBytes > 0),
        CONSTRAINT CK_MamUploadSession_ReceivedLength CHECK (ReceivedLength >= 0 AND ReceivedLength <= ExpectedLength)
    );
    CREATE INDEX IX_MamUploadSession_State_Expires ON dbo.MamUploadSession(State, ExpiresAtUtc);
    CREATE INDEX IX_MamUploadSession_ExpectedSha256 ON dbo.MamUploadSession(ExpectedSha256);
END;

IF OBJECT_ID(N'dbo.MamUploadChunkReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamUploadChunkReceipt
    (
        SessionId uniqueidentifier NOT NULL,
        Offset bigint NOT NULL,
        Length int NOT NULL,
        ChunkSha256 char(64) NOT NULL,
        ReceivedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamUploadChunkReceipt_ReceivedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamUploadChunkReceipt PRIMARY KEY (SessionId, Offset),
        CONSTRAINT FK_MamUploadChunkReceipt_Session FOREIGN KEY (SessionId) REFERENCES dbo.MamUploadSession(SessionId),
        CONSTRAINT CK_MamUploadChunkReceipt_Offset CHECK (Offset >= 0),
        CONSTRAINT CK_MamUploadChunkReceipt_Length CHECK (Length > 0)
    );
END;

IF OBJECT_ID(N'dbo.MamMediaOriginal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamMediaOriginal
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MamMediaOriginal PRIMARY KEY,
        StorageTargetId nvarchar(100) NOT NULL,
        ObjectKey nvarchar(1024) NOT NULL,
        OriginalFileName nvarchar(260) NOT NULL,
        Length bigint NOT NULL,
        Sha256 char(64) NOT NULL,
        VerifiedAtUtc datetime2(7) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamMediaOriginal_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamMediaOriginal_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT UQ_MamMediaOriginal_ObjectKey UNIQUE (ObjectKey),
        CONSTRAINT CK_MamMediaOriginal_Length CHECK (Length > 0)
    );
    CREATE INDEX IX_MamMediaOriginal_Sha256 ON dbo.MamMediaOriginal(Sha256);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0003_p03_durable_upload')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0003_p03_durable_upload');
END;

COMMIT TRANSACTION;
