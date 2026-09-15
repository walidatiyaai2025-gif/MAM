SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.MamTextExtractionStatus', N'StartedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.MamTextExtractionStatus ADD StartedAtUtc datetime2(7) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0010_p127_enterprise_ux')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0010_p127_enterprise_ux');

COMMIT TRANSACTION;