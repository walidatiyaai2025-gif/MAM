SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.inv_tape_attachments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inv_tape_attachments
    (
        AttachmentId uniqueidentifier NOT NULL CONSTRAINT PK_inv_tape_attachments PRIMARY KEY,
        TapeId uniqueidentifier NOT NULL,
        AssetId uniqueidentifier NOT NULL,
        DisplayName nvarchar(512) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        CreatedBy nvarchar(256) NOT NULL,
        CONSTRAINT UQ_inv_tape_attachments_AssetId UNIQUE(AssetId),
        CONSTRAINT FK_inv_tape_attachments_Tape FOREIGN KEY(TapeId) REFERENCES dbo.inv_tapes(TapeId) ON DELETE CASCADE,
        CONSTRAINT FK_inv_tape_attachments_Asset FOREIGN KEY(AssetId) REFERENCES dbo.MediaAsset(AssetId) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.inv_tape_attachments')
      AND name=N'IX_inv_tape_attachments_TapeId_CreatedAtUtc'
)
    CREATE INDEX IX_inv_tape_attachments_TapeId_CreatedAtUtc
        ON dbo.inv_tape_attachments(TapeId,CreatedAtUtc DESC,AttachmentId);

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0018_tape_attachments_ocr')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0018_tape_attachments_ocr');

COMMIT TRANSACTION;
