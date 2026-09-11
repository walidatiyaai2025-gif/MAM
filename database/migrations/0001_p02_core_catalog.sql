SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamSchemaVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamSchemaVersion
    (
        MigrationId nvarchar(100) NOT NULL CONSTRAINT PK_MamSchemaVersion PRIMARY KEY,
        AppliedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamSchemaVersion_AppliedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.MamUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamUser
    (
        UserId uniqueidentifier NOT NULL CONSTRAINT PK_MamUser PRIMARY KEY,
        ExternalSubject nvarchar(200) NULL,
        UserName nvarchar(200) NOT NULL,
        DisplayName nvarchar(300) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_MamUser_IsEnabled DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamUser_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamUser_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        Version bigint NOT NULL CONSTRAINT DF_MamUser_Version DEFAULT (1),
        RowVersion rowversion NOT NULL,
        CONSTRAINT UQ_MamUser_UserName UNIQUE (UserName)
    );
END;

IF OBJECT_ID(N'dbo.MamRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamRole
    (
        RoleId uniqueidentifier NOT NULL CONSTRAINT PK_MamRole PRIMARY KEY,
        RoleName nvarchar(100) NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamRole_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_MamRole_RoleName UNIQUE (RoleName)
    );
END;

IF OBJECT_ID(N'dbo.MamUserRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamUserRole
    (
        UserId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        AssignedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamUserRole_AssignedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MamUserRole PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_MamUserRole_User FOREIGN KEY (UserId) REFERENCES dbo.MamUser(UserId),
        CONSTRAINT FK_MamUserRole_Role FOREIGN KEY (RoleId) REFERENCES dbo.MamRole(RoleId)
    );
END;

IF OBJECT_ID(N'dbo.MediaAsset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MediaAsset
    (
        AssetId uniqueidentifier NOT NULL CONSTRAINT PK_MediaAsset PRIMARY KEY,
        Title nvarchar(300) NOT NULL,
        Lifecycle tinyint NOT NULL CONSTRAINT DF_MediaAsset_Lifecycle DEFAULT (0),
        Version bigint NOT NULL CONSTRAINT DF_MediaAsset_Version DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MediaAsset_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MediaAsset_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NULL,
        UpdatedByUserId uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_MediaAsset_Title_NotBlank CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT CK_MediaAsset_Lifecycle CHECK (Lifecycle IN (0, 1, 2)),
        CONSTRAINT FK_MediaAsset_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.MamUser(UserId),
        CONSTRAINT FK_MediaAsset_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES dbo.MamUser(UserId)
    );

    CREATE INDEX IX_MediaAsset_UpdatedAtUtc ON dbo.MediaAsset(UpdatedAtUtc DESC);
END;

IF OBJECT_ID(N'dbo.MamAuditEvent', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamAuditEvent
    (
        AuditEventId uniqueidentifier NOT NULL CONSTRAINT PK_MamAuditEvent PRIMARY KEY,
        OccurredAtUtc datetime2(7) NOT NULL,
        ActorId nvarchar(200) NOT NULL,
        Action nvarchar(200) NOT NULL,
        EntityType nvarchar(100) NOT NULL,
        EntityId nvarchar(200) NOT NULL,
        Outcome nvarchar(50) NOT NULL,
        Detail nvarchar(2000) NULL
    );

    CREATE INDEX IX_MamAuditEvent_OccurredAtUtc ON dbo.MamAuditEvent(OccurredAtUtc DESC);
    CREATE INDEX IX_MamAuditEvent_Entity ON dbo.MamAuditEvent(EntityType, EntityId, OccurredAtUtc DESC);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId = N'0001_p02_core_catalog')
BEGIN
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES (N'0001_p02_core_catalog');
END;

COMMIT TRANSACTION;
