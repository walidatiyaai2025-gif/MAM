SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamTranscriptRevision', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTranscriptRevision
    (
        RevisionId uniqueidentifier NOT NULL CONSTRAINT PK_MamTranscriptRevision PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        RevisionNumber bigint IDENTITY(1,1) NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        Language nvarchar(20) NULL,
        IsFinal bit NOT NULL CONSTRAINT DF_MamTranscriptRevision_IsFinal DEFAULT (0),
        IsReady bit NOT NULL CONSTRAINT DF_MamTranscriptRevision_IsReady DEFAULT (0),
        Note nvarchar(500) NULL,
        CreatedBy nvarchar(256) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTranscriptRevision_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamTranscriptRevision_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId) ON DELETE CASCADE,
        CONSTRAINT UQ_MamTranscriptRevision_Asset_Source UNIQUE (AssetId,SourceKind),
        CONSTRAINT UQ_MamTranscriptRevision_Number UNIQUE (RevisionNumber)
    );
    CREATE INDEX IX_MamTranscriptRevision_Asset ON dbo.MamTranscriptRevision(AssetId,CreatedAtUtc DESC,RevisionNumber DESC)
        INCLUDE(SourceKind,Language,IsFinal,IsReady,CreatedBy,Note);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0009_p12_transcript_revisions')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0009_p12_transcript_revisions');

COMMIT TRANSACTION;
