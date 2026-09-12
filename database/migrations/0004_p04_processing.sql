SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamTechnicalMetadata', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTechnicalMetadata
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MamTechnicalMetadata PRIMARY KEY,
        MediaType nvarchar(40) NOT NULL,
        DurationSeconds float NULL,
        Width int NULL,
        Height int NULL,
        VideoCodec nvarchar(100) NULL,
        AudioCodec nvarchar(100) NULL,
        AudioChannels int NULL,
        AudioSampleRate int NULL,
        RawJson nvarchar(max) NOT NULL,
        InspectedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT FK_MamTechnicalMetadata_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT CK_MamTechnicalMetadata_Width CHECK (Width IS NULL OR Width > 0),
        CONSTRAINT CK_MamTechnicalMetadata_Height CHECK (Height IS NULL OR Height > 0),
        CONSTRAINT CK_MamTechnicalMetadata_AudioChannels CHECK (AudioChannels IS NULL OR AudioChannels > 0),
        CONSTRAINT CK_MamTechnicalMetadata_AudioSampleRate CHECK (AudioSampleRate IS NULL OR AudioSampleRate > 0)
    );
END;

IF OBJECT_ID(N'dbo.MamProcessingJob', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamProcessingJob
    (
        JobId uniqueidentifier NOT NULL CONSTRAINT PK_MamProcessingJob PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        ProfileId nvarchar(100) NOT NULL,
        ProfileVersion int NOT NULL,
        State tinyint NOT NULL CONSTRAINT DF_MamProcessingJob_State DEFAULT (0),
        AttemptCount int NOT NULL CONSTRAINT DF_MamProcessingJob_AttemptCount DEFAULT (0),
        LeaseOwner nvarchar(200) NULL,
        LeaseExpiresAtUtc datetime2(7) NULL,
        LastHeartbeatAtUtc datetime2(7) NULL,
        LastError nvarchar(2000) NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NULL,
        CONSTRAINT FK_MamProcessingJob_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT UQ_MamProcessingJob_Profile UNIQUE (AssetId, ProfileId, ProfileVersion),
        CONSTRAINT CK_MamProcessingJob_ProfileVersion CHECK (ProfileVersion > 0),
        CONSTRAINT CK_MamProcessingJob_AttemptCount CHECK (AttemptCount >= 0),
        CONSTRAINT CK_MamProcessingJob_State CHECK (State BETWEEN 0 AND 3)
    );
    CREATE INDEX IX_MamProcessingJob_State_Lease ON dbo.MamProcessingJob(State, LeaseExpiresAtUtc, CreatedAtUtc);
END;

IF OBJECT_ID(N'dbo.MamMediaDerivative', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamMediaDerivative
    (
        DerivativeId uniqueidentifier NOT NULL CONSTRAINT PK_MamMediaDerivative PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        ProfileId nvarchar(100) NOT NULL,
        ProfileVersion int NOT NULL,
        ObjectKey nvarchar(1024) NOT NULL,
        ContentType nvarchar(150) NOT NULL,
        Length bigint NOT NULL,
        Sha256 char(64) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT FK_MamMediaDerivative_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT UQ_MamMediaDerivative_Profile UNIQUE (AssetId, ProfileId, ProfileVersion),
        CONSTRAINT UQ_MamMediaDerivative_ObjectKey UNIQUE (ObjectKey),
        CONSTRAINT CK_MamMediaDerivative_ProfileVersion CHECK (ProfileVersion > 0),
        CONSTRAINT CK_MamMediaDerivative_Length CHECK (Length > 0)
    );
    CREATE INDEX IX_MamMediaDerivative_Asset ON dbo.MamMediaDerivative(AssetId, CreatedAtUtc);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0004_p04_processing')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0004_p04_processing');
END;

COMMIT TRANSACTION;
