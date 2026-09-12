SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamAdminPolicy', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAdminPolicy
    (
        PolicyKey nvarchar(160) NOT NULL CONSTRAINT PK_MamAdminPolicy PRIMARY KEY,
        Category nvarchar(50) NOT NULL,
        DisplayNameEn nvarchar(200) NOT NULL,
        DisplayNameAr nvarchar(200) NOT NULL,
        PayloadJson nvarchar(max) NOT NULL,
        SecretRef nvarchar(300) NULL,
        Version bigint NOT NULL CONSTRAINT DF_MamAdminPolicy_Version DEFAULT (1),
        RequiresRestart bit NOT NULL CONSTRAINT DF_MamAdminPolicy_RequiresRestart DEFAULT (0),
        IsEnabled bit NOT NULL CONSTRAINT DF_MamAdminPolicy_IsEnabled DEFAULT (1),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAdminPolicy_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_MamAdminPolicy_PayloadJson CHECK (ISJSON(PayloadJson) = 1),
        CONSTRAINT CK_MamAdminPolicy_Version CHECK (Version > 0)
    );
    CREATE INDEX IX_MamAdminPolicy_Category ON dbo.MamAdminPolicy(Category, PolicyKey);
END;

IF OBJECT_ID(N'dbo.MamAdminDictionaryEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAdminDictionaryEntry
    (
        DictionaryKey nvarchar(120) NOT NULL,
        EntryKey nvarchar(120) NOT NULL,
        LabelEn nvarchar(300) NOT NULL,
        LabelAr nvarchar(300) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_MamAdminDictionaryEntry_IsEnabled DEFAULT (1),
        Version bigint NOT NULL CONSTRAINT DF_MamAdminDictionaryEntry_Version DEFAULT (1),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamAdminDictionaryEntry_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_MamAdminDictionaryEntry PRIMARY KEY (DictionaryKey, EntryKey),
        CONSTRAINT CK_MamAdminDictionaryEntry_Version CHECK (Version > 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamRole WHERE RoleName = N'Administrator')
    INSERT dbo.MamRole(RoleId, RoleName) VALUES(NEWID(), N'Administrator');
IF NOT EXISTS (SELECT 1 FROM dbo.MamRole WHERE RoleName = N'CatalogEditor')
    INSERT dbo.MamRole(RoleId, RoleName) VALUES(NEWID(), N'CatalogEditor');
IF NOT EXISTS (SELECT 1 FROM dbo.MamRole WHERE RoleName = N'Viewer')
    INSERT dbo.MamRole(RoleId, RoleName) VALUES(NEWID(), N'Viewer');

IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'identity.authorization')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'identity.authorization', N'Identity', N'Identity & authorization', N'الهوية والصلاحيات', N'{"provisioningAuthority":"ExternalIdP","localPasswordsAllowed":false,"defaultRole":"Viewer"}', NULL, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'metadata.core')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'metadata.core', N'Metadata', N'Metadata dictionary policy', N'سياسة قواميس البيانات', N'{"schemaKey":"core-media-v1","dictionaryMode":"Managed"}', NULL, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'capture.approved')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'capture.approved', N'Capture', N'Capture station policy', N'سياسة محطات التسجيل', N'{"stationPolicyId":"approved-only","providerMode":"ApprovedOnly","simulatorAllowedProduction":false}', NULL, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'processing.default')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'processing.default', N'Processing', N'Processing profile policy', N'سياسة معالجة الوسائط', N'{"profileId":"enterprise-default","version":1,"enabled":true}', NULL, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'storage.primary')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'storage.primary', N'Storage', N'Primary Storage reference', N'مرجع التخزين الأساسي', N'{"targetId":"Primary","mode":"ServerManaged","credentialHandling":"SecretReferenceOnly"}', N'env:MAM_SECRET_PRIMARY_STORAGE', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'storage.backup')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'storage.backup', N'Storage', N'Backup Storage reference', N'مرجع التخزين الاحتياطي', N'{"targetId":"Backup","mode":"ServerManaged","credentialHandling":"SecretReferenceOnly"}', N'env:MAM_SECRET_BACKUP_STORAGE', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'auth.production')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'auth.production', N'Auth', N'Production authentication reference', N'مرجع مصادقة الإنتاج', N'{"mode":"External","credentialHandling":"SecretReferenceOnly"}', N'env:MAM_SECRET_AUTH_PRODUCTION', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'retention.default')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'retention.default', N'Retention', N'Retention & delete policy', N'سياسة الاحتفاظ والحذف', N'{"retentionDays":3650,"deleteMode":"OwnerApprovedDelete","legalHoldRequired":true}', NULL, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'branding.diwan')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'branding.diwan', N'Branding', N'Diwan Al Amiri branding', N'هوية الديوان الأميري', N'{"crestSha256":"bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb","navy":"#0A2342","gold":"#B58A2A","identityLocked":true}', NULL, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'notification.operations')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart, IsEnabled)
    VALUES(N'notification.operations', N'Notification', N'Operations notification policy', N'سياسة تنبيهات التشغيل', N'{"channel":"Operations","destinationRef":"OWNER_LAST","enabled":false}', NULL, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminPolicy WHERE PolicyKey = N'system.runtime')
    INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, RequiresRestart)
    VALUES(N'system.runtime', N'System', N'Runtime system settings', N'إعدادات تشغيل النظام', N'{"maintenanceMode":false,"configurationAuthority":"CentralAPI"}', NULL, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminDictionaryEntry WHERE DictionaryKey = N'category' AND EntryKey = N'ceremony')
    INSERT dbo.MamAdminDictionaryEntry(DictionaryKey, EntryKey, LabelEn, LabelAr) VALUES(N'category', N'ceremony', N'Ceremony', N'مناسبة رسمية');
IF NOT EXISTS (SELECT 1 FROM dbo.MamAdminDictionaryEntry WHERE DictionaryKey = N'category' AND EntryKey = N'interview')
    INSERT dbo.MamAdminDictionaryEntry(DictionaryKey, EntryKey, LabelEn, LabelAr) VALUES(N'category', N'interview', N'Interview', N'مقابلة');

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0007_p08_administration')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0007_p08_administration');

COMMIT TRANSACTION;
