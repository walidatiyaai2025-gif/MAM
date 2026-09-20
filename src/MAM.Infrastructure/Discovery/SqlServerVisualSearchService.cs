using System.Globalization;
using System.Security.Cryptography;
using MAM.Application.Discovery;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Discovery;

public sealed class SqlServerVisualSearchService : IVisualSearchService
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IVisualEmbeddingProvider _provider;
    private readonly string _derivativesPrefix;

    public SqlServerVisualSearchService(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IVisualEmbeddingProvider provider,
        MamSettings settings)
    {
        _connections = connections;
        _primary = primary;
        _provider = provider;
        _derivativesPrefix = CleanPrefix(settings.Storage.Primary.DerivativesPrefix, "derivatives");
    }

    public async Task<VisualSearchHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var provider = _provider.Health;
        if (!provider.IsReady)
            return new VisualSearchHealth(false, provider.Provider, provider.ModelId, provider.ModelVersion, provider.Dimensions, provider.Detail);
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0012_p12_visual_segment_image_search';", connection)
            { CommandTimeout = _connections.CommandTimeoutSeconds };
            var ready = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
            return new VisualSearchHealth(ready, provider.Provider, provider.ModelId, provider.ModelVersion, provider.Dimensions,
                ready ? "Visual segment index and local image search are ready." : "Visual search migration 0012 is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new VisualSearchHealth(false, provider.Provider, provider.ModelId, provider.ModelVersion, provider.Dimensions,
                $"Visual search dependency unavailable: {ex.GetType().Name}.");
        }
    }

    public async Task<IReadOnlyList<VisualSegmentSnapshot>> ListSegmentsAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default)
    {
        sourceKind = NormalizeSource(sourceKind);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT t.SegmentIndex,t.StartMs,t.EndMs,t.PageNumber,t.Text,
                   v.SegmentId,v.CaptureMs,v.VisualState,v.ThumbnailObjectKey,v.ThumbnailContentType,v.ThumbnailLength,v.ThumbnailSha256,v.LastError,v.UpdatedAtUtc
            FROM dbo.MamAssetTextSegment t
            LEFT JOIN dbo.MamVisualSegment v ON v.AssetId=t.AssetId AND v.SourceKind=t.SourceKind AND v.SegmentIndex=t.SegmentIndex
            WHERE t.AssetId=@AssetId AND t.SourceKind=@SourceKind
            ORDER BY t.SegmentIndex;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        command.Parameters.AddWithValue("@SourceKind", sourceKind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<VisualSegmentSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var index = reader.GetInt32(0);
            var state = reader.IsDBNull(7) ? VisualStates.Pending : reader.GetString(7);
            rows.Add(new VisualSegmentSnapshot(
                reader.IsDBNull(5) ? VisualSegmentIdentity.Create(assetId, sourceKind, index) : reader.GetGuid(5),
                assetId, sourceKind, index,
                reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(6) ? null : reader.GetInt64(6), state, string.Equals(state, VisualStates.Ready, StringComparison.Ordinal) && !reader.IsDBNull(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : Utc(reader.GetDateTime(13))));
        }
        return rows;
    }

    public async Task RegisterSegmentsAsync(Guid assetId, string sourceKind, IReadOnlyList<TextSegmentSnapshot> segments, bool visualEligible, string? unavailableReason, CancellationToken cancellationToken = default)
    {
        sourceKind = NormalizeSource(sourceKind);
        if (segments is null) throw new VisualSearchRequestException("segments_required", "Visual segment registration requires text segments.");
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var resetAt = DateTime.UtcNow;
        const string resetSql = """
            UPDATE vi SET IsActive=0,UpdatedAtUtc=@Now
            FROM dbo.MamVisualIndex vi
            INNER JOIN dbo.MamVisualSegment vs ON vs.SegmentId=vi.SegmentId
            WHERE vs.AssetId=@AssetId AND vs.SourceKind=@SourceKind AND vi.IsActive=1;

            UPDATE dbo.MamVisualSegment
            SET VisualState=N'Unavailable',LastError=N'Visual rebuild pending.',UpdatedAtUtc=@Now
            WHERE AssetId=@AssetId AND SourceKind=@SourceKind;
            """;
        await using (var reset = new SqlCommand(resetSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            reset.Parameters.AddWithValue("@Now", resetAt);
            reset.Parameters.AddWithValue("@AssetId", assetId);
            reset.Parameters.AddWithValue("@SourceKind", sourceKind);
            await reset.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var segment in segments.OrderBy(item => item.SegmentIndex))
        {
            var id = VisualSegmentIdentity.Create(assetId, sourceKind, segment.SegmentIndex);
            const string sql = """
                MERGE dbo.MamVisualSegment AS target
                USING (SELECT @AssetId AssetId,@SourceKind SourceKind,@SegmentIndex SegmentIndex) AS source
                ON target.AssetId=source.AssetId AND target.SourceKind=source.SourceKind AND target.SegmentIndex=source.SegmentIndex
                WHEN MATCHED THEN UPDATE SET StartMs=@StartMs,EndMs=@EndMs,PageNumber=@PageNumber,
                    VisualState=@State,LastError=@Error,UpdatedAtUtc=@Now
                WHEN NOT MATCHED THEN INSERT(SegmentId,AssetId,SourceKind,SegmentIndex,StartMs,EndMs,PageNumber,VisualState,LastError,UpdatedAtUtc)
                    VALUES(@SegmentId,@AssetId,@SourceKind,@SegmentIndex,@StartMs,@EndMs,@PageNumber,@State,@Error,@Now);
                """;
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            command.Parameters.AddWithValue("@SegmentId", id); command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind);
            command.Parameters.AddWithValue("@SegmentIndex", segment.SegmentIndex); command.Parameters.AddWithValue("@StartMs", (object?)segment.StartMs ?? DBNull.Value);
            command.Parameters.AddWithValue("@EndMs", (object?)segment.EndMs ?? DBNull.Value); command.Parameters.AddWithValue("@PageNumber", (object?)segment.PageNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@State", visualEligible ? VisualStates.Pending : VisualStates.Unavailable);
            command.Parameters.AddWithValue("@Error", visualEligible ? DBNull.Value : (object?)Short(unavailableReason ?? "No visual frame is available for this segment.", 1000));
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VisualSegmentSnapshot> UpsertSegmentThumbnailAsync(Guid assetId, string sourceKind, int segmentIndex, long captureMs, Stream thumbnail, string contentType, CancellationToken cancellationToken = default)
    {
        sourceKind = NormalizeSource(sourceKind);
        contentType = NormalizeImageContentType(contentType);
        var segmentId = VisualSegmentIdentity.Create(assetId, sourceKind, segmentIndex);
        var bytes = await ReadBoundedAsync(thumbnail, 12L * 1024 * 1024, cancellationToken);
        if (bytes.Length == 0) throw new VisualSearchRequestException("thumbnail_empty", "Generated segment thumbnail is empty.", 422);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var key = $"{_derivativesPrefix}/{assetId:N}/visual/{sourceKind}/{segmentId:N}.jpg";
        await using (var storageStream = new MemoryStream(bytes, writable: false))
            await _primary.WriteAsync(key, storageStream, sha, cancellationToken);
        VisualEmbeddingDescriptor embedding;
        await using (var embedStream = new MemoryStream(bytes, writable: false))
            embedding = await _provider.EmbedAsync(embedStream, $"{segmentId:N}.jpg", contentType, cancellationToken);
        ValidateEmbedding(embedding);
        var vector = Serialize(embedding.Values);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string segmentSql = """
            UPDATE dbo.MamVisualSegment SET CaptureMs=@CaptureMs,ThumbnailObjectKey=@ObjectKey,ThumbnailContentType=@ContentType,
                ThumbnailLength=@Length,ThumbnailSha256=@Sha,VisualState=N'Ready',LastError=NULL,UpdatedAtUtc=@Now
            WHERE SegmentId=@SegmentId AND AssetId=@AssetId AND SourceKind=@SourceKind AND SegmentIndex=@SegmentIndex;
            """;
        await using (var command = new SqlCommand(segmentSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@CaptureMs", captureMs); command.Parameters.AddWithValue("@ObjectKey", key); command.Parameters.AddWithValue("@ContentType", contentType);
            command.Parameters.AddWithValue("@Length", bytes.LongLength); command.Parameters.AddWithValue("@Sha", sha); command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
            command.Parameters.AddWithValue("@SegmentId", segmentId); command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind); command.Parameters.AddWithValue("@SegmentIndex", segmentIndex);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new VisualSearchRequestException("visual_segment_not_found", "The transcript segment is unavailable for thumbnail indexing.", 404);
        }
        await UpsertIndexAsync(connection, transaction, assetId, segmentId, "Segment", sourceKind, embedding, vector, sha, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await ListSegmentsAsync(assetId, sourceKind, cancellationToken)).Single(item => item.SegmentId == segmentId);
    }

    public async Task IndexAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var original = await ReadOriginalAsync(assetId, cancellationToken) ?? throw new VisualSearchRequestException("primary_original_not_found", "Primary original is unavailable.", 404);
        if (!string.Equals(original.MediaKind, MediaKinds.Image, StringComparison.OrdinalIgnoreCase))
            throw new VisualSearchRequestException("visual_asset_type_not_supported", "Asset-level visual indexing currently supports image originals; video is indexed by transcript segment frames.", 415);
        var verification = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
        if (!verification.Exists || !verification.ChecksumMatches || verification.Length != original.Length)
            throw new VisualSearchRequestException("primary_original_verification_failed", "Primary original verification failed before visual indexing.", 503);
        VisualEmbeddingDescriptor embedding;
        await using (var stream = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            embedding = await _provider.EmbedAsync(stream, original.FileName, ContentType(original.FileName), cancellationToken);
        ValidateEmbedding(embedding);
        var payload = Serialize(embedding.Values);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await UpsertIndexAsync(connection, transaction, assetId, null, "Asset", "original", embedding, payload, original.Sha256, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VisualThumbnailPayload?> OpenThumbnailAsync(Guid assetId, Guid segmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT ThumbnailObjectKey,ThumbnailContentType,ThumbnailLength,ThumbnailSha256 FROM dbo.MamVisualSegment WHERE AssetId=@AssetId AND SegmentId=@SegmentId AND VisualState=N'Ready';";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SegmentId", segmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        var key = reader.GetString(0); var type = reader.GetString(1); var length = reader.GetInt64(2); var sha = reader.GetString(3).Trim();
        await reader.DisposeAsync();
        var verification = await _primary.VerifyAsync(key, sha, cancellationToken);
        if (!verification.Exists || !verification.ChecksumMatches || verification.Length != length)
            throw new VisualSearchRequestException("thumbnail_verification_failed", "Segment thumbnail verification failed before delivery.", 503);
        return new VisualThumbnailPayload(await _primary.OpenReadAsync(key, cancellationToken), type, $"segment-{segmentId:N}.jpg", length, sha);
    }

    public async Task<VisualSearchResult> SearchAsync(Stream queryImage, string? fileName, string? contentType, int limit = 30, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var health = await GetHealthAsync(cancellationToken);
        if (!health.IsReady) throw new VisualSearchRequestException("visual_search_unavailable", health.Detail, 503);
        var query = await _provider.EmbedAsync(queryImage, fileName, contentType, cancellationToken);
        ValidateEmbedding(query);
        var rows = new List<VisualSearchHit>();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT vi.AssetId,a.Title,tm.MediaType,o.OriginalFileName,vi.SegmentId,vi.SourceKind,
                   vs.SegmentIndex,vs.StartMs,vs.EndMs,vs.PageNumber,vs.ThumbnailObjectKey,
                   vi.VectorPayload,vi.Provider,vi.ModelId,vi.ModelVersion,vi.UpdatedAtUtc
            FROM dbo.MamVisualIndex vi
            JOIN dbo.MediaAsset a ON a.AssetId=vi.AssetId
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=vi.AssetId
            LEFT JOIN dbo.MamMediaOriginal o ON o.AssetId=vi.AssetId
            LEFT JOIN dbo.MamVisualSegment vs ON vs.SegmentId=vi.SegmentId
            WHERE vi.IsActive=1 AND vi.Provider=@Provider AND vi.ModelId=@ModelId AND vi.ModelVersion=@ModelVersion AND vi.Dimensions=@Dimensions;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Provider", query.Provider); command.Parameters.AddWithValue("@ModelId", query.ModelId);
        command.Parameters.AddWithValue("@ModelVersion", query.ModelVersion); command.Parameters.AddWithValue("@Dimensions", query.Dimensions);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var candidate = Deserialize((byte[])reader[11], query.Dimensions);
            var score = Similarity(query.Values, candidate);
            var mediaKind = reader.IsDBNull(2) ? MediaKinds.FromFileName(reader.IsDBNull(3) ? null : reader.GetString(3)) : NormalizeMediaKind(reader.GetString(2));
            rows.Add(new VisualSearchHit(reader.GetGuid(0), reader.GetString(1), mediaKind, reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetInt32(9),
                score, !reader.IsDBNull(10), reader.GetString(12), reader.GetString(13), reader.GetInt32(14), Utc(reader.GetDateTime(15))));
        }
        var ordered = rows
            .Where(item => item.Score >= VisualSearchPolicy.MinimumScore)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .Take(limit)
            .ToArray();

        return new VisualSearchResult(
            ordered,
            query.Provider,
            query.ModelId,
            query.ModelVersion,
            query.Dimensions,
            limit,
            VisualSearchPolicy.MinimumScore);
    }

    private async Task UpsertIndexAsync(SqlConnection connection, SqlTransaction transaction, Guid assetId, Guid? segmentId, string scope, string sourceKind,
        VisualEmbeddingDescriptor embedding, byte[] vector, string sourceSha, CancellationToken cancellationToken)
    {
        const string deactivate = "UPDATE dbo.MamVisualIndex SET IsActive=0,UpdatedAtUtc=@Now WHERE AssetId=@AssetId AND ((@SegmentId IS NULL AND SegmentId IS NULL) OR SegmentId=@SegmentId) AND IndexScope=@Scope AND IsActive=1 AND (Provider<>@Provider OR ModelId<>@ModelId OR ModelVersion<>@ModelVersion OR Dimensions<>@Dimensions);";
        await using (var command = new SqlCommand(deactivate, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow); command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SegmentId", (object?)segmentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Scope", scope); command.Parameters.AddWithValue("@Provider", embedding.Provider); command.Parameters.AddWithValue("@ModelId", embedding.ModelId);
            command.Parameters.AddWithValue("@ModelVersion", embedding.ModelVersion); command.Parameters.AddWithValue("@Dimensions", embedding.Dimensions); await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string upsert = """
            MERGE dbo.MamVisualIndex AS target
            USING (SELECT @AssetId AssetId,@SegmentId SegmentId,@Scope IndexScope,@Provider Provider,@ModelId ModelId,@ModelVersion ModelVersion) source
            ON target.AssetId=source.AssetId AND ((target.SegmentId IS NULL AND source.SegmentId IS NULL) OR target.SegmentId=source.SegmentId)
               AND target.IndexScope=source.IndexScope AND target.Provider=source.Provider AND target.ModelId=source.ModelId AND target.ModelVersion=source.ModelVersion
            WHEN MATCHED THEN UPDATE SET SourceKind=@SourceKind,Dimensions=@Dimensions,VectorPayload=@Vector,SourceSha256=@Sha,IsActive=1,UpdatedAtUtc=@Now
            WHEN NOT MATCHED THEN INSERT(VisualIndexId,AssetId,SegmentId,IndexScope,SourceKind,Provider,ModelId,ModelVersion,Dimensions,VectorPayload,SourceSha256,IsActive,CreatedAtUtc,UpdatedAtUtc)
                VALUES(@Id,@AssetId,@SegmentId,@Scope,@SourceKind,@Provider,@ModelId,@ModelVersion,@Dimensions,@Vector,@Sha,1,@Now,@Now);
            """;
        await using var merge = new SqlCommand(upsert, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        merge.Parameters.AddWithValue("@Id", Guid.NewGuid()); merge.Parameters.AddWithValue("@AssetId", assetId); merge.Parameters.AddWithValue("@SegmentId", (object?)segmentId ?? DBNull.Value);
        merge.Parameters.AddWithValue("@Scope", scope); merge.Parameters.AddWithValue("@SourceKind", sourceKind); merge.Parameters.AddWithValue("@Provider", embedding.Provider); merge.Parameters.AddWithValue("@ModelId", embedding.ModelId);
        merge.Parameters.AddWithValue("@ModelVersion", embedding.ModelVersion); merge.Parameters.AddWithValue("@Dimensions", embedding.Dimensions); merge.Parameters.AddWithValue("@Vector", vector); merge.Parameters.AddWithValue("@Sha", sourceSha); merge.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        await merge.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<OriginalRecord?> ReadOriginalAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT o.ObjectKey,o.OriginalFileName,o.Length,o.Sha256,tm.MediaType FROM dbo.MamMediaOriginal o LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=o.AssetId WHERE o.AssetId=@AssetId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var fileName = reader.GetString(1);
        return new OriginalRecord(reader.GetString(0), fileName, reader.GetInt64(2), reader.GetString(3).Trim(), reader.IsDBNull(4) ? MediaKinds.FromFileName(fileName) : NormalizeMediaKind(reader.GetString(4)));
    }

    private static void ValidateEmbedding(VisualEmbeddingDescriptor embedding)
    {
        if (embedding.Dimensions <= 0 || embedding.Dimensions > 8192 || embedding.Values.Count != embedding.Dimensions)
            throw new VisualSearchRequestException("visual_embedding_invalid", "Visual provider returned an invalid embedding.", 503);
        if (embedding.Values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
            throw new VisualSearchRequestException("visual_embedding_invalid", "Visual provider returned a non-finite embedding.", 503);
    }

    private static byte[] Serialize(IReadOnlyList<float> values)
    {
        var floats = values as float[] ?? values.ToArray(); var bytes = new byte[floats.Length * sizeof(float)]; Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length); return bytes;
    }
    private static float[] Deserialize(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float)) throw new VisualSearchRequestException("visual_index_corrupt", "Stored visual index dimensions do not match its vector payload.", 503);
        var values = new float[dimensions]; Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length); return values;
    }
    private static double Similarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count) return 0; double dot=0,ls=0,rs=0; for(var i=0;i<left.Count;i++){dot+=left[i]*right[i];ls+=left[i]*left[i];rs+=right[i]*right[i];}
        if(ls<=1e-12||rs<=1e-12)return 0; var cosine=dot/(Math.Sqrt(ls)*Math.Sqrt(rs)); return Math.Clamp((cosine+1d)/2d,0d,1d);
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream source,long max,CancellationToken ct)
    {
        await using var memory=new MemoryStream();var buffer=new byte[64*1024];long total=0;while(true){var read=await source.ReadAsync(buffer.AsMemory(0,buffer.Length),ct);if(read==0)break;total+=read;if(total>max)throw new VisualSearchRequestException("thumbnail_too_large","Generated thumbnail exceeds the configured visual derivative limit.",413);await memory.WriteAsync(buffer.AsMemory(0,read),ct);}return memory.ToArray();
    }
    private static string NormalizeSource(string? value){var v=value?.Trim().ToLowerInvariant();if(string.IsNullOrWhiteSpace(v)||v.Length>40)throw new VisualSearchRequestException("visual_source_invalid","Visual source kind is required and must be at most 40 characters.");return v;}
    private static string NormalizeImageContentType(string? value){var v=value?.Split(';',2)[0].Trim().ToLowerInvariant();return v is "image/jpeg" or "image/png" or "image/bmp" or "image/gif" or "image/tiff" or "image/webp"?v:throw new VisualSearchRequestException("image_type_not_supported","Thumbnail content type is not supported.",415);}
    private static string NormalizeMediaKind(string? kind)=>kind?.Trim().ToLowerInvariant() switch{"video"=>MediaKinds.Video,"audio"=>MediaKinds.Audio,"image"=>MediaKinds.Image,"document"=>MediaKinds.Document,_=>MediaKinds.Other};
    private static string ContentType(string name)=>Path.GetExtension(name).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".bmp"=>"image/bmp",".gif"=>"image/gif",".tif" or ".tiff"=>"image/tiff",".webp"=>"image/webp",_=>"application/octet-stream"};
    private static string CleanPrefix(string? value,string fallback){var v=string.IsNullOrWhiteSpace(value)?fallback:value.Trim().Trim('/','\\');return v.Length==0?fallback:v;}
    private static string Short(string value,int max)=>string.IsNullOrWhiteSpace(value)?string.Empty:value.Trim()[..Math.Min(value.Trim().Length,max)];
    private static DateTimeOffset Utc(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));
    private sealed record OriginalRecord(string ObjectKey,string FileName,long Length,string Sha256,string MediaKind);
}