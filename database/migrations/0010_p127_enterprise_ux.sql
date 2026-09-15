SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.MamTextExtractionStatus', N'StartedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.MamTextExtractionStatus ADD StartedAtUtc datetime2(7) NULL;
END;

IF OBJECT_ID(N'dbo.MamTextExtractionStatus', N'U') IS NOT NULL
BEGIN
    EXEC(N'
    UPDATE es
       SET StartedAtUtc = COALESCE(j.CreatedAtUtc, es.UpdatedAtUtc)
    FROM dbo.MamTextExtractionStatus es
    OUTER APPLY
    (
        SELECT TOP (1) p.CreatedAtUtc
        FROM dbo.MamProcessingJob p
        WHERE p.AssetId = es.AssetId
          AND ((es.ExtractionKind = N''transcript'' AND p.ProfileId = N''transcript-text-v1'')
            OR (es.ExtractionKind = N''ocr'' AND p.ProfileId = N''ocr-text-v1''))
        ORDER BY p.CreatedAtUtc DESC
    ) j
    WHERE es.StartedAtUtc IS NULL;
    ');
END;

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_MamTextExtractionStatus_P127StartedAt
ON dbo.MamTextExtractionStatus
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE target
       SET StartedAtUtc = COALESCE(i.UpdatedAtUtc, SYSUTCDATETIME())
    FROM dbo.MamTextExtractionStatus target
    JOIN inserted i
      ON i.AssetId=target.AssetId AND i.ExtractionKind=target.ExtractionKind
    LEFT JOIN deleted d
      ON d.AssetId=i.AssetId AND d.ExtractionKind=i.ExtractionKind
    WHERE i.State=N''Running''
      AND (target.StartedAtUtc IS NULL OR d.CompletedAtUtc IS NOT NULL);
END;
');

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0010_p127_enterprise_ux')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0010_p127_enterprise_ux');

COMMIT TRANSACTION;
