SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamVisualSegment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamVisualSegment
    (
        SegmentId uniqueidentifier NOT NULL CONSTRAINT PK_MamVisualSegment PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        SegmentIndex int NOT NULL,
        StartMs bigint NULL,
        EndMs bigint NULL,
        PageNumber int NULL,
        CaptureMs bigint NULL,
        ThumbnailObjectKey nvarchar(900) NULL,
        ThumbnailContentType nvarchar(100) NULL,
        ThumbnailLength bigint NULL,
        ThumbnailSha256 char(64) NULL,
        VisualState nvarchar(24) NOT NULL CONSTRAINT DF_MamVisualSegment_State DEFAULT N'Pending',
        LastError nvarchar(1000) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamVisualSegment_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamVisualSegment_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId) ON DELETE CASCADE,
        CONSTRAINT UQ_MamVisualSegment_Text UNIQUE (AssetId,SourceKind,SegmentIndex),
        CONSTRAINT CK_MamVisualSegment_Time CHECK (StartMs IS NULL OR EndMs IS NULL OR EndMs >= StartMs),
        CONSTRAINT CK_MamVisualSegment_Page CHECK (PageNumber IS NULL OR PageNumber > 0),
        CONSTRAINT CK_MamVisualSegment_Length CHECK (ThumbnailLength IS NULL OR ThumbnailLength > 0),
        CONSTRAINT CK_MamVisualSegment_State CHECK (VisualState IN (N'Pending',N'Ready',N'Unavailable',N'Failed'))
    );
    CREATE INDEX IX_MamVisualSegment_AssetSource ON dbo.MamVisualSegment(AssetId,SourceKind,SegmentIndex)
        INCLUDE(SegmentId,StartMs,EndMs,PageNumber,CaptureMs,VisualState,ThumbnailContentType,ThumbnailLength,ThumbnailSha256);
END;

IF OBJECT_ID(N'dbo.MamVisualIndex', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamVisualIndex
    (
        VisualIndexId uniqueidentifier NOT NULL CONSTRAINT PK_MamVisualIndex PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        SegmentId uniqueidentifier NULL,
        IndexScope nvarchar(20) NOT NULL,
        SourceKind nvarchar(40) NOT NULL,
        Provider nvarchar(100) NOT NULL,
        ModelId nvarchar(100) NOT NULL,
        ModelVersion int NOT NULL,
        Dimensions int NOT NULL,
        VectorPayload varbinary(max) NOT NULL,
        SourceSha256 char(64) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_MamVisualIndex_Active DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamVisualIndex_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamVisualIndex_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamVisualIndex_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId) ON DELETE CASCADE,
        CONSTRAINT FK_MamVisualIndex_Segment FOREIGN KEY (SegmentId) REFERENCES dbo.MamVisualSegment(SegmentId),
        CONSTRAINT CK_MamVisualIndex_Scope CHECK (IndexScope IN (N'Asset',N'Segment')),
        CONSTRAINT CK_MamVisualIndex_Dimensions CHECK (Dimensions > 0 AND Dimensions <= 8192),
        CONSTRAINT CK_MamVisualIndex_Version CHECK (ModelVersion > 0)
    );
    CREATE UNIQUE INDEX UX_MamVisualIndex_Logical
        ON dbo.MamVisualIndex(AssetId,SegmentId,IndexScope,Provider,ModelId,ModelVersion);
    CREATE INDEX IX_MamVisualIndex_Compatible
        ON dbo.MamVisualIndex(Provider,ModelId,ModelVersion,Dimensions,IsActive)
        INCLUDE(AssetId,SegmentId,SourceKind,SourceSha256,UpdatedAtUtc);
END;

-- Text timeline revisions invalidate visual search immediately but retain the old
-- thumbnail object key as a cleanup reference. The next visual job reuses the stable
-- segment identity/object key. Removed segments stay inactive and are purged with the asset.
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_MamAssetTextSegment_VisualCleanup
ON dbo.MamAssetTextSegment
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE vi
    SET IsActive=0, UpdatedAtUtc=SYSUTCDATETIME()
    FROM dbo.MamVisualIndex vi
    INNER JOIN dbo.MamVisualSegment vs ON vs.SegmentId=vi.SegmentId
    INNER JOIN deleted d ON d.AssetId=vs.AssetId AND d.SourceKind=vs.SourceKind AND d.SegmentIndex=vs.SegmentIndex
    WHERE vi.IsActive=1;

    UPDATE vs
    SET VisualState=N''Unavailable'',
        LastError=N''Source text timeline changed; visual rebuild required.'',
        UpdatedAtUtc=SYSUTCDATETIME()
    FROM dbo.MamVisualSegment vs
    INNER JOIN deleted d ON d.AssetId=vs.AssetId AND d.SourceKind=vs.SourceKind AND d.SegmentIndex=vs.SegmentIndex;
END;
');

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0012_p12_visual_segment_image_search')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0012_p12_visual_segment_image_search');

COMMIT TRANSACTION;