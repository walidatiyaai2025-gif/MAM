using Microsoft.Data.Sqlite;
using MAM.Infrastructure.Configuration;

namespace MAM.Infrastructure.Demo;

public sealed class DemoSqliteDatabase
{
    private readonly string _path;
    private readonly string _connectionString;
    private int _initialized;

    public DemoSqliteDatabase(MamSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.Equals(settings.Environment.Name, "Demo", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(settings.Database.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DemoSqliteDatabase is restricted to the Demo/Sqlite deployment profile.");

        _path = System.IO.Path.GetFullPath(settings.Database.SqlitePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = Math.Max(5, settings.Database.CommandTimeoutSeconds)
        }.ToString();
    }

    public string Path => _path;

    public async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        return connection;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) == 1) return;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await SeedAsync(connection, cancellationToken);
        Volatile.Write(ref _initialized, 1);
    }

    public static string ToDb(DateTimeOffset value) => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    public static DateTimeOffset FromDb(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var now = ToDb(DateTimeOffset.UtcNow);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO DemoUser(UserId,UserName,DisplayName,ExternalSubject,IsEnabled,Version,RolesJson)
            VALUES
            ('11111111-1111-1111-1111-111111111111','admin','Demo Administrator','demo-admin',1,1,'["Administrator"]'),
            ('22222222-2222-2222-2222-222222222222','editor','Demo Catalog Editor','demo-editor',1,1,'["CatalogEditor"]'),
            ('33333333-3333-3333-3333-333333333333','viewer','Demo Viewer','demo-viewer',1,1,'["Viewer"]');

            INSERT OR IGNORE INTO DemoPolicy(PolicyKey,Category,DisplayNameEn,DisplayNameAr,PayloadJson,SecretRef,Version,RequiresRestart,IsEnabled,UpdatedAtUtc)
            VALUES('demo.mode','System','Offline Demo Mode','وضع العرض بدون إنترنت','{"offline":true,"database":"SQLite","host":"demomam.da.gov.kw"}',NULL,1,0,1,$now);

            INSERT OR IGNORE INTO DemoDictionary(DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc)
            VALUES
            ('mediaType','video','Video','فيديو',1,1,$now),
            ('mediaType','audio','Audio','صوت',1,1,$now),
            ('mediaType','image','Image','صورة',1,1,$now),
            ('mediaType','document','Document','مستند',1,1,$now);

            INSERT OR IGNORE INTO DemoCategory(CategoryId,ParentCategoryId,NameEn,NameAr,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc)
            VALUES('00000000-0000-0000-0000-000000000001',NULL,'Uncategorized','غير مصنف',1,-1000,1,$now,$now);
            """;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS DemoAsset(
            AssetId TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            TitleAr TEXT NULL,
            Lifecycle TEXT NOT NULL DEFAULT 'Draft',
            Version INTEGER NOT NULL DEFAULT 1,
            EventDate TEXT NULL,
            Category TEXT NULL,
            TagsJson TEXT NOT NULL DEFAULT '[]',
            PreservationNotes TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            OriginalFileName TEXT NULL,
            OriginalObjectKey TEXT NULL,
            OriginalLength INTEGER NULL,
            OriginalSha256 TEXT NULL,
            MediaKind TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_DemoAsset_Updated ON DemoAsset(UpdatedAtUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_DemoAsset_OriginalSha ON DemoAsset(OriginalSha256);

        CREATE TABLE IF NOT EXISTS DemoAudit(
            AuditEventId TEXT PRIMARY KEY,
            OccurredAtUtc TEXT NOT NULL,
            ActorId TEXT NOT NULL,
            Action TEXT NOT NULL,
            EntityType TEXT NOT NULL,
            EntityId TEXT NOT NULL,
            Outcome TEXT NOT NULL,
            Detail TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_DemoAudit_Occurred ON DemoAudit(OccurredAtUtc DESC);

        CREATE TABLE IF NOT EXISTS DemoCollection(
            CollectionId TEXT PRIMARY KEY,
            NameEn TEXT NOT NULL,
            NameAr TEXT NULL,
            Version INTEGER NOT NULL DEFAULT 1,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS DemoCollectionAsset(
            CollectionId TEXT NOT NULL,
            AssetId TEXT NOT NULL,
            PRIMARY KEY(CollectionId,AssetId),
            FOREIGN KEY(CollectionId) REFERENCES DemoCollection(CollectionId) ON DELETE CASCADE,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoUploadSession(
            SessionId TEXT PRIMARY KEY,
            AssetId TEXT NOT NULL,
            Title TEXT NOT NULL,
            OriginalFileName TEXT NOT NULL,
            ExpectedLength INTEGER NOT NULL,
            ExpectedSha256 TEXT NOT NULL,
            ChunkSizeBytes INTEGER NOT NULL,
            ReceivedLength INTEGER NOT NULL DEFAULT 0,
            State INTEGER NOT NULL DEFAULT 0,
            IsQuarantined INTEGER NOT NULL DEFAULT 0,
            TempPath TEXT NOT NULL,
            PrimaryObjectKey TEXT NULL,
            Error TEXT NULL,
            ExpiresAtUtc TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoCategory(
            CategoryId TEXT PRIMARY KEY,
            ParentCategoryId TEXT NULL,
            NameEn TEXT NOT NULL,
            NameAr TEXT NULL,
            IsSystem INTEGER NOT NULL DEFAULT 0,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            Version INTEGER NOT NULL DEFAULT 1,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            FOREIGN KEY(ParentCategoryId) REFERENCES DemoCategory(CategoryId) ON DELETE RESTRICT
        );
        CREATE TABLE IF NOT EXISTS DemoAssetCategory(
            AssetId TEXT PRIMARY KEY,
            CategoryId TEXT NOT NULL,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE,
            FOREIGN KEY(CategoryId) REFERENCES DemoCategory(CategoryId) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS DemoAssetText(
            AssetId TEXT NOT NULL,
            SourceKind TEXT NOT NULL,
            Language TEXT NULL,
            TextValue TEXT NOT NULL,
            ContentSha256 TEXT NULL,
            SegmentsJson TEXT NOT NULL DEFAULT '[]',
            UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(AssetId,SourceKind),
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS DemoExtractionStatus(
            AssetId TEXT NOT NULL,
            ExtractionKind TEXT NOT NULL,
            State TEXT NOT NULL,
            ProgressPercent INTEGER NOT NULL,
            Detail TEXT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            CompletedAtUtc TEXT NULL,
            PRIMARY KEY(AssetId,ExtractionKind),
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoReferenceSubject(
            SubjectId TEXT PRIMARY KEY,
            NameEn TEXT NOT NULL,
            NameAr TEXT NULL,
            DescriptionEn TEXT NULL,
            DescriptionAr TEXT NULL,
            TagsJson TEXT NOT NULL DEFAULT '[]',
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS DemoReferenceAsset(
            SubjectId TEXT NOT NULL,
            AssetId TEXT NOT NULL,
            PRIMARY KEY(SubjectId,AssetId),
            FOREIGN KEY(SubjectId) REFERENCES DemoReferenceSubject(SubjectId) ON DELETE CASCADE,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS DemoAssetReferenceTag(
            AssetId TEXT NOT NULL,
            SubjectId TEXT NOT NULL,
            Confidence REAL NULL,
            DetectionSource TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(AssetId,SubjectId),
            FOREIGN KEY(SubjectId) REFERENCES DemoReferenceSubject(SubjectId) ON DELETE CASCADE,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoMediaPermission(
            RoleName TEXT NOT NULL,
            MediaKind TEXT NOT NULL,
            CanView INTEGER NOT NULL,
            CanUpload INTEGER NOT NULL,
            CanEdit INTEGER NOT NULL,
            CanProcess INTEGER NOT NULL,
            CanDownload INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(RoleName,MediaKind)
        );

        CREATE TABLE IF NOT EXISTS DemoProcessingJob(
            JobId TEXT PRIMARY KEY,
            AssetId TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            ProfileVersion INTEGER NOT NULL,
            State INTEGER NOT NULL,
            AttemptCount INTEGER NOT NULL DEFAULT 0,
            LastError TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            CompletedAtUtc TEXT NULL,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoProtection(
            AssetId TEXT PRIMARY KEY,
            PrimaryObjectKey TEXT NOT NULL,
            BackupObjectKey TEXT NOT NULL,
            ExpectedLength INTEGER NOT NULL,
            ExpectedSha256 TEXT NOT NULL,
            State INTEGER NOT NULL,
            AttemptCount INTEGER NOT NULL DEFAULT 1,
            VerifiedAtUtc TEXT NULL,
            LastIntegrityCheckAtUtc TEXT NULL,
            LastError TEXT NULL,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS DemoPolicy(
            PolicyKey TEXT PRIMARY KEY,
            Category TEXT NOT NULL,
            DisplayNameEn TEXT NOT NULL,
            DisplayNameAr TEXT NOT NULL,
            PayloadJson TEXT NOT NULL,
            SecretRef TEXT NULL,
            Version INTEGER NOT NULL,
            RequiresRestart INTEGER NOT NULL,
            IsEnabled INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS DemoUser(
            UserId TEXT PRIMARY KEY,
            UserName TEXT NOT NULL UNIQUE,
            DisplayName TEXT NOT NULL,
            ExternalSubject TEXT NULL,
            IsEnabled INTEGER NOT NULL,
            Version INTEGER NOT NULL,
            RolesJson TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS DemoDictionary(
            DictionaryKey TEXT NOT NULL,
            EntryKey TEXT NOT NULL,
            LabelEn TEXT NOT NULL,
            LabelAr TEXT NOT NULL,
            IsEnabled INTEGER NOT NULL,
            Version INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY(DictionaryKey,EntryKey)
        );
        """;
}
