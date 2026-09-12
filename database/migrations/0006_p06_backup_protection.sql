SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamBackupProtection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamBackupProtection
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MamBackupProtection PRIMARY KEY,
        PrimaryTargetId nvarchar(100) NOT NULL,
        PrimaryObjectKey nvarchar(1024) NOT NULL,
        BackupTargetId nvarchar(100) NOT NULL,
        BackupObjectKey nvarchar(1024) NOT NULL,
        ExpectedLength bigint NOT NULL,
        ExpectedSha256 char(64) NOT NULL,
        State tinyint NOT NULL CONSTRAINT DF_MamBackupProtection_State DEFAULT (0),
        AttemptCount int NOT NULL CONSTRAINT DF_MamBackupProtection_AttemptCount DEFAULT (0),
        LastAttemptAtUtc datetime2(7) NULL,
        VerifiedAtUtc datetime2(7) NULL,
        LastIntegrityCheckAtUtc datetime2(7) NULL,
        LastError nvarchar(1000) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamBackupProtection_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamBackupProtection_Asset FOREIGN KEY (AssetId) REFERENCES dbo.MediaAsset(AssetId),
        CONSTRAINT CK_MamBackupProtection_ExpectedLength CHECK (ExpectedLength > 0),
        CONSTRAINT CK_MamBackupProtection_State CHECK (State IN (0,1,2,3)),
        CONSTRAINT CK_MamBackupProtection_AttemptCount CHECK (AttemptCount >= 0),
        CONSTRAINT UQ_MamBackupProtection_BackupObject UNIQUE (BackupTargetId, BackupObjectKey)
    );
    CREATE INDEX IX_MamBackupProtection_State ON dbo.MamBackupProtection(State, UpdatedAtUtc);
END;

IF OBJECT_ID(N'dbo.MamBackupJob', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamBackupJob
    (
        JobId uniqueidentifier NOT NULL CONSTRAINT PK_MamBackupJob PRIMARY KEY,
        AssetId uniqueidentifier NOT NULL,
        State tinyint NOT NULL CONSTRAINT DF_MamBackupJob_State DEFAULT (0),
        AttemptCount int NOT NULL CONSTRAINT DF_MamBackupJob_AttemptCount DEFAULT (0),
        AvailableAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamBackupJob_AvailableAtUtc DEFAULT SYSUTCDATETIME(),
        LeaseOwner nvarchar(200) NULL,
        LeaseExpiresAtUtc datetime2(7) NULL,
        LastHeartbeatAtUtc datetime2(7) NULL,
        LastError nvarchar(1000) NULL,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamBackupJob_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamBackupJob_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MamBackupJob_Protection FOREIGN KEY (AssetId) REFERENCES dbo.MamBackupProtection(AssetId),
        CONSTRAINT CK_MamBackupJob_State CHECK (State IN (0,1,2,3)),
        CONSTRAINT CK_MamBackupJob_AttemptCount CHECK (AttemptCount >= 0)
    );
    CREATE UNIQUE INDEX UX_MamBackupJob_ActiveAsset ON dbo.MamBackupJob(AssetId) WHERE State IN (0,1);
    CREATE INDEX IX_MamBackupJob_Lease ON dbo.MamBackupJob(State, AvailableAtUtc, LeaseExpiresAtUtc);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0006_p06_backup_protection')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0006_p06_backup_protection');
END;

COMMIT TRANSACTION;
