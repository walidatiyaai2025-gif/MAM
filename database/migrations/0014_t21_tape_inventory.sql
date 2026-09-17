SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamTapeCounter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTapeCounter
    (
        CounterKey nvarchar(50) NOT NULL CONSTRAINT PK_MamTapeCounter PRIMARY KEY,
        NextValue bigint NOT NULL,
        CONSTRAINT CK_MamTapeCounter_NextValue CHECK (NextValue >= 1)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamTapeCounter WHERE CounterKey=N'TAPE')
    INSERT dbo.MamTapeCounter(CounterKey, NextValue) VALUES(N'TAPE', 1);

IF OBJECT_ID(N'dbo.MamTape', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamTape
    (
        TapeId uniqueidentifier NOT NULL CONSTRAINT PK_MamTape PRIMARY KEY,
        TapeNumber bigint NOT NULL,
        TapeCode AS (N'TAPE-' + RIGHT(REPLICATE(N'0', 6) + CONVERT(nvarchar(20), TapeNumber), 6)) PERSISTED,
        Title nvarchar(300) NOT NULL,
        Description nvarchar(2000) NULL,
        LegacyNumber nvarchar(200) NULL,
        TapeFormat nvarchar(100) NULL,
        PhysicalCondition nvarchar(100) NULL,
        DigitizationStatus nvarchar(50) NOT NULL CONSTRAINT DF_MamTape_DigitizationStatus DEFAULT N'NotDigitized',
        Room nvarchar(100) NULL,
        Cabinet nvarchar(100) NULL,
        Shelf nvarchar(100) NULL,
        Bin nvarchar(100) NULL,
        OwnerDepartment nvarchar(200) NULL,
        Notes nvarchar(2000) NULL,
        Version bigint NOT NULL CONSTRAINT DF_MamTape_Version DEFAULT 1,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTape_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamTape_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_MamTape_TapeNumber UNIQUE(TapeNumber),
        CONSTRAINT CK_MamTape_TapeNumber CHECK (TapeNumber BETWEEN 1 AND 999999),
        CONSTRAINT CK_MamTape_Title CHECK (LEN(LTRIM(RTRIM(Title))) BETWEEN 1 AND 300),
        CONSTRAINT CK_MamTape_Version CHECK (Version >= 1),
        CONSTRAINT CK_MamTape_DigitizationStatus CHECK (DigitizationStatus IN
        (
            N'NotDigitized', N'SentForDigitization', N'PartiallyDigitized', N'DigitizedFileReceived',
            N'Uploaded', N'QcPending', N'QcApproved', N'Completed'
        ))
    );

    CREATE UNIQUE INDEX UX_MamTape_TapeCode ON dbo.MamTape(TapeCode);
    CREATE INDEX IX_MamTape_UpdatedAtUtc ON dbo.MamTape(UpdatedAtUtc DESC, TapeId);
    CREATE INDEX IX_MamTape_LegacyNumber ON dbo.MamTape(LegacyNumber) WHERE LegacyNumber IS NOT NULL;
    CREATE INDEX IX_MamTape_DigitizationStatus ON dbo.MamTape(DigitizationStatus, UpdatedAtUtc DESC);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0014_t21_tape_inventory')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0014_t21_tape_inventory');

COMMIT TRANSACTION;
