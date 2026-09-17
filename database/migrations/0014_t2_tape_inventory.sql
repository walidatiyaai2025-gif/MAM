SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.inv_counters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inv_counters
    (
        CounterKey nvarchar(64) NOT NULL CONSTRAINT PK_inv_counters PRIMARY KEY,
        LastValue bigint NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_inv_counters_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_inv_counters_LastValue CHECK (LastValue >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.inv_counters WHERE CounterKey=N'tape')
    INSERT dbo.inv_counters(CounterKey,LastValue) VALUES(N'tape',0);

IF OBJECT_ID(N'dbo.inv_tape_formats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inv_tape_formats
    (
        Code nvarchar(64) NOT NULL CONSTRAINT PK_inv_tape_formats PRIMARY KEY,
        NameEn nvarchar(128) NOT NULL,
        NameAr nvarchar(128) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_inv_tape_formats_IsActive DEFAULT 1,
        SortOrder int NOT NULL CONSTRAINT DF_inv_tape_formats_SortOrder DEFAULT 0,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_inv_tape_formats_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy nvarchar(256) NOT NULL CONSTRAINT DF_inv_tape_formats_UpdatedBy DEFAULT N'system'
    );
END;

MERGE dbo.inv_tape_formats AS target
USING (VALUES
    (N'HDCAM',N'HDCAM',N'HDCAM',10),
    (N'BETACAM',N'Betacam',N'Betacam',20),
    (N'BETACAM_SP',N'Betacam SP',N'Betacam SP',30),
    (N'DIGITAL_BETACAM',N'Digital Betacam',N'Digital Betacam',40)
) AS source(Code,NameEn,NameAr,SortOrder)
ON target.Code=source.Code
WHEN NOT MATCHED THEN
    INSERT(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedBy)
    VALUES(source.Code,source.NameEn,source.NameAr,1,source.SortOrder,N'migration-0014');

IF OBJECT_ID(N'dbo.inv_tapes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inv_tapes
    (
        TapeId uniqueidentifier NOT NULL CONSTRAINT PK_inv_tapes PRIMARY KEY,
        TapeCode nvarchar(32) NOT NULL,
        LegacyNumber nvarchar(128) NULL,
        Title nvarchar(512) NULL,
        Description nvarchar(max) NULL,
        TapeFormatCode nvarchar(64) NULL,
        PhysicalCondition nvarchar(32) NULL,
        DigitizationStatus nvarchar(32) NOT NULL CONSTRAINT DF_inv_tapes_DigitizationStatus DEFAULT N'NotDigitized',
        OwnerDepartment nvarchar(256) NULL,
        DurationSeconds int NULL,
        RecordingDate date NULL,
        Room nvarchar(128) NULL,
        Cabinet nvarchar(128) NULL,
        Shelf nvarchar(128) NULL,
        Bin nvarchar(128) NULL,
        Notes nvarchar(max) NULL,
        Version int NOT NULL CONSTRAINT DF_inv_tapes_Version DEFAULT 1,
        CreatedAtUtc datetime2(7) NOT NULL,
        CreatedBy nvarchar(256) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        UpdatedBy nvarchar(256) NOT NULL,
        CONSTRAINT UQ_inv_tapes_TapeCode UNIQUE(TapeCode),
        CONSTRAINT FK_inv_tapes_TapeFormat FOREIGN KEY(TapeFormatCode) REFERENCES dbo.inv_tape_formats(Code),
        CONSTRAINT CK_inv_tapes_Duration CHECK (DurationSeconds IS NULL OR DurationSeconds >= 0),
        CONSTRAINT CK_inv_tapes_Version CHECK (Version > 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.inv_tapes') AND name=N'IX_inv_tapes_LegacyNumber')
    CREATE INDEX IX_inv_tapes_LegacyNumber ON dbo.inv_tapes(LegacyNumber) WHERE LegacyNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.inv_tapes') AND name=N'IX_inv_tapes_UpdatedAtUtc')
    CREATE INDEX IX_inv_tapes_UpdatedAtUtc ON dbo.inv_tapes(UpdatedAtUtc DESC,TapeId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.inv_tapes') AND name=N'IX_inv_tapes_TapeFormatCode')
    CREATE INDEX IX_inv_tapes_TapeFormatCode ON dbo.inv_tapes(TapeFormatCode,TapeId);

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0014_t2_tape_inventory')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0014_t2_tape_inventory');

COMMIT TRANSACTION;
