using System.Globalization;
using System.Text;
using MAM.Application.Auditing;
using MAM.Application.BulkImport;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Uploads;
using MAM.Infrastructure.Configuration;

namespace MAM.Infrastructure.BulkImport;

public sealed class BulkImportCoordinator(
    IBulkImportStateStore store,
    IDurableUploadService uploads,
    IDiscoveryService discovery,
    IMediaProcessingService processing,
    IAuditSink audit,
    MamSettings settings) : IBulkImportService
{
    private const int MaxFilesPerSession = 5000;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mxf", ".mov", ".mp4", ".mkv", ".avi", ".webm", ".m4v" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wma" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".rtf", ".txt", ".odt" };

    public async Task<BulkImportSessionSnapshot> CreateSessionAsync(
        CreateBulkImportSessionRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = NormalizeActor(actorId);
        var root = NormalizeRoot(request.RootFolderName);
        var files = request.Files ?? Array.Empty<BulkImportFileDescriptor>();
        if (files.Count == 0) throw Error("bulk_import_empty", "Select a folder containing at least one file.");
        if (files.Count > MaxFilesPerSession) throw Error("bulk_import_too_large", $"A bulk import session is limited to {MaxFilesPerSession} files.", 413);

        var normalized = files.Select(file => NormalizeDescriptor(root, file)).ToArray();
        if (normalized.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw Error("duplicate_relative_path", "The selected folder contains duplicate relative paths.");

        var categories = await EnsureCategoriesAsync(normalized.Where(x => x.Supported).Select(x => x.CategoryName), actor, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var session = new BulkImportSessionRow(
            sessionId, root, BulkImportSessionState.Preparing, normalized.Length,
            normalized.Sum(x => Math.Max(0L, x.Length)), actor, now, now, null);

        var items = normalized.Select(file =>
        {
            var state = !file.Supported
                ? BulkImportItemState.Unsupported
                : file.Length <= 0 || file.Sha256 is null
                    ? BulkImportItemState.Failed
                    : BulkImportItemState.Pending;
            var reason = state switch
            {
                BulkImportItemState.Unsupported => "unsupported_file_type",
                BulkImportItemState.Failed when file.Length <= 0 => "invalid_length",
                BulkImportItemState.Failed => "sha256_required",
                _ => null
            };
            var detail = reason switch
            {
                "unsupported_file_type" => $"Extension '{Path.GetExtension(file.FileName)}' is not allowed by the bulk-import policy.",
                "invalid_length" => "File length must be greater than zero.",
                "sha256_required" => "A valid SHA-256 digest is required before upload.",
                _ => null
            };
            return new BulkImportItemRow(
                Guid.NewGuid(), sessionId, file.RelativePath, file.FileName, file.CategoryName,
                categories.TryGetValue(file.CategoryName, out var category) ? category.CategoryId : null,
                file.Length, file.Sha256, null, null, state, reason, detail, now);
        }).ToArray();

        await store.CreateAsync(session, items, cancellationToken);

        foreach (var item in items.Where(x => x.State == BulkImportItemState.Pending))
            await PrepareUploadAsync(item, actor, cancellationToken);

        await RecalculateSessionAsync(sessionId, actor, cancellationToken);
        await audit.AppendAsync(NewAudit(actor, "bulk-import.session.created", "BulkImportSession", sessionId.ToString("D"), "Success",
            $"root={root};files={files.Count};bytes={session.TotalBytes}"), cancellationToken);
        return await GetSessionAsync(sessionId, actor, cancellationToken);
    }

    public async Task<BulkImportSessionSnapshot> GetSessionAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        return await ToSnapshotAsync(data.Session, data.Items, cancellationToken);
    }

    public async Task<IReadOnlyList<BulkImportSessionSummary>> ListRecentAsync(string actorId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var rows = await store.ListRecentAsync(actor, Math.Clamp(limit, 1, 100), cancellationToken);
        var result = new List<BulkImportSessionSummary>(rows.Count);
        foreach (var row in rows)
        {
            var data = await store.GetAsync(row.SessionId, cancellationToken);
            if (data is null) continue;
            var snapshot = await ToSnapshotAsync(data.Value.Session, data.Value.Items, cancellationToken);
            result.Add(ToSummary(snapshot));
        }
        return result;
    }

    public async Task<BulkImportItemSnapshot> BeginItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        if (data.Session.State == BulkImportSessionState.Cancelled) throw Error("bulk_import_cancelled", "The bulk import session is cancelled.", 409);
        var item = RequireItem(data.Items, itemId);

        if (IsSuccessTerminal(item.State) || item.State == BulkImportItemState.Unsupported || item.State == BulkImportItemState.Cancelled)
            return await ToItemSnapshotAsync(item, cancellationToken);

        if (item.UploadSessionId is Guid uploadId)
        {
            try
            {
                var upload = await uploads.GetSessionAsync(uploadId, cancellationToken);
                if (upload.State == UploadSessionState.Completed)
                {
                    item = await CompleteExistingUploadAsync(item, upload.Session.AssetId, actor, null, cancellationToken);
                    await RecalculateSessionAsync(sessionId, actor, cancellationToken);
                    return await ToItemSnapshotAsync(item, cancellationToken);
                }
                if (upload.State == UploadSessionState.Receiving)
                {
                    item = item with { State = BulkImportItemState.Uploading, ReasonCode = null, Detail = null, UpdatedAtUtc = DateTimeOffset.UtcNow };
                    await store.UpdateItemAsync(item, cancellationToken);
                    await RecalculateSessionAsync(sessionId, actor, cancellationToken);
                    return await ToItemSnapshotAsync(item, cancellationToken);
                }
            }
            catch (UploadRequestException)
            {
                // The standard upload session may have expired or been cleaned up. Re-create it below.
            }
        }

        item = await PrepareUploadAsync(item with { State = BulkImportItemState.Pending }, actor, cancellationToken);
        if (item.State == BulkImportItemState.Pending)
        {
            item = item with { State = BulkImportItemState.Uploading, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await store.UpdateItemAsync(item, cancellationToken);
        }
        await RecalculateSessionAsync(sessionId, actor, cancellationToken);
        return await ToItemSnapshotAsync(item, cancellationToken);
    }

    public async Task<BulkImportItemSnapshot> FinalizeItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        var item = RequireItem(data.Items, itemId);
        if (IsSuccessTerminal(item.State)) return await ToItemSnapshotAsync(item, cancellationToken);
        if (item.UploadSessionId is not Guid uploadId) throw Error("bulk_upload_session_missing", "This item has no resumable upload session.", 409);

        try
        {
            var finalized = await uploads.FinalizeAsync(uploadId, actor, cancellationToken);
            if (!string.Equals(finalized.Sha256, item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw Error("bulk_finalize_sha_mismatch", "The finalized upload digest does not match the bulk-import manifest.", 409);
            item = await CompleteExistingUploadAsync(item, finalized.AssetId, actor, null, cancellationToken);
        }
        catch (UploadRequestException ex) when (ex.Code == "duplicate_detected" && ex.ExistingAssetId is Guid existing)
        {
            item = await ResolveDuplicateAsync(item, existing, actor, cancellationToken);
        }
        catch (UploadRequestException ex)
        {
            item = item with
            {
                State = BulkImportItemState.Failed,
                ReasonCode = ex.Code,
                Detail = Truncate(ex.Message, 1000),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await store.UpdateItemAsync(item, cancellationToken);
        }

        await RecalculateSessionAsync(sessionId, actor, cancellationToken);
        return await ToItemSnapshotAsync(item, cancellationToken);
    }

    public async Task<BulkImportItemSnapshot> FailItemAsync(Guid sessionId, Guid itemId, BulkImportFailureRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        var item = RequireItem(data.Items, itemId);
        if (IsSuccessTerminal(item.State) || item.State is BulkImportItemState.Unsupported or BulkImportItemState.Cancelled)
            return await ToItemSnapshotAsync(item, cancellationToken);

        item = item with
        {
            State = BulkImportItemState.Failed,
            ReasonCode = NormalizeCode(request?.Code),
            Detail = Truncate(request?.Detail ?? "Client upload interrupted before completion.", 1000),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await store.UpdateItemAsync(item, cancellationToken);
        await RecalculateSessionAsync(sessionId, actor, cancellationToken);
        return await ToItemSnapshotAsync(item, cancellationToken);
    }

    public Task<BulkImportItemSnapshot> RetryItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default) =>
        BeginItemAsync(sessionId, itemId, actorId, cancellationToken);

    public async Task<BulkImportSessionSnapshot> CancelAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default)
    {
        var actor = NormalizeActor(actorId);
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in data.Items.Where(x => x.State is BulkImportItemState.Pending or BulkImportItemState.Uploading))
        {
            await store.UpdateItemAsync(item with
            {
                State = BulkImportItemState.Cancelled,
                ReasonCode = "cancelled",
                Detail = "Bulk import was cancelled by the user.",
                UpdatedAtUtc = now
            }, cancellationToken);
        }

        await store.UpdateSessionAsync(data.Session with
        {
            State = BulkImportSessionState.Cancelled,
            UpdatedAtUtc = now,
            CompletedAtUtc = now
        }, cancellationToken);
        await audit.AppendAsync(NewAudit(actor, "bulk-import.session.cancelled", "BulkImportSession", sessionId.ToString("D"), "Success"), cancellationToken);
        return await GetSessionAsync(sessionId, actor, cancellationToken);
    }

    public async Task<BulkImportReport> GetReportAsync(Guid sessionId, string format, string actorId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSessionAsync(sessionId, actorId, cancellationToken);
        var requested = (format ?? "txt").Trim().ToLowerInvariant();
        return requested switch
        {
            "csv" => new BulkImportReport($"MAM-Bulk-Import-{sessionId:D}.csv", "text/csv; charset=utf-8", BuildCsv(snapshot)),
            "txt" or "text" => new BulkImportReport($"MAM-Bulk-Import-{sessionId:D}.txt", "text/plain; charset=utf-8", BuildText(snapshot)),
            _ => throw Error("report_format_invalid", "Bulk import report format must be txt or csv.")
        };
    }

    private async Task<BulkImportItemRow> PrepareUploadAsync(BulkImportItemRow item, string actor, CancellationToken cancellationToken)
    {
        if (item.CategoryId is null || item.ExpectedSha256 is null || item.ExpectedLength <= 0) return item;
        try
        {
            var upload = await uploads.CreateSessionAsync(new CreateUploadSessionRequest(
                TitleFromFile(item.FileName), item.FileName, item.ExpectedLength, item.ExpectedSha256), actor, cancellationToken);
            item = item with
            {
                UploadSessionId = upload.Session.SessionId,
                AssetId = null,
                State = BulkImportItemState.Pending,
                ReasonCode = null,
                Detail = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await store.UpdateItemAsync(item, cancellationToken);
            return item;
        }
        catch (UploadRequestException ex) when (ex.Code == "duplicate_detected" && ex.ExistingAssetId is Guid existing)
        {
            return await ResolveDuplicateAsync(item, existing, actor, cancellationToken);
        }
        catch (UploadRequestException ex)
        {
            item = item with
            {
                State = ex.Code is "file_type_not_allowed" or "file_quarantined" ? BulkImportItemState.Unsupported : BulkImportItemState.Failed,
                ReasonCode = ex.Code,
                Detail = Truncate(ex.Message, 1000),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await store.UpdateItemAsync(item, cancellationToken);
            return item;
        }
    }

    private async Task<BulkImportItemRow> ResolveDuplicateAsync(BulkImportItemRow item, Guid existingAssetId, string actor, CancellationToken cancellationToken)
    {
        if (item.CategoryId is not Guid targetCategoryId) throw Error("bulk_category_missing", "Bulk import category could not be resolved.", 503);
        var current = await discovery.GetAssetCategoryAsync(existingAssetId, cancellationToken);
        var changed = current.Category.CategoryId != targetCategoryId;
        if (changed) await discovery.AssignAssetCategoryAsync(existingAssetId, targetCategoryId, actor, cancellationToken);

        item = item with
        {
            AssetId = existingAssetId,
            State = changed ? BulkImportItemState.Linked : BulkImportItemState.AlreadyExists,
            ReasonCode = changed ? "duplicate_category_updated" : "duplicate_detected",
            Detail = changed
                ? $"Existing asset was not re-uploaded. Category changed from '{current.Category.NameEn}' to '{item.CategoryName}'."
                : "Existing authoritative asset with the same SHA-256 is already assigned to this category.",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await store.UpdateItemAsync(item, cancellationToken);
        await audit.AppendAsync(NewAudit(actor, "bulk-import.duplicate.resolved", "MediaAsset", existingAssetId.ToString("D"), "Success",
            $"state={item.State};category={item.CategoryName};sha256={item.ExpectedSha256}"), cancellationToken);
        return item;
    }

    private async Task<BulkImportItemRow> CompleteExistingUploadAsync(BulkImportItemRow item, Guid assetId, string actor, string? detail, CancellationToken cancellationToken)
    {
        if (item.CategoryId is not Guid categoryId) throw Error("bulk_category_missing", "Bulk import category could not be resolved.", 503);
        await discovery.AssignAssetCategoryAsync(assetId, categoryId, actor, cancellationToken);
        var processingDetail = await QueueProcessingAsync(assetId, item.FileName, actor, cancellationToken);
        item = item with
        {
            AssetId = assetId,
            State = BulkImportItemState.Uploaded,
            ReasonCode = null,
            Detail = detail ?? processingDetail,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await store.UpdateItemAsync(item, cancellationToken);
        await audit.AppendAsync(NewAudit(actor, "bulk-import.item.uploaded", "MediaAsset", assetId.ToString("D"), "Success",
            $"path={item.RelativePath};category={item.CategoryName};sha256={item.ExpectedSha256}"), cancellationToken);
        return item;
    }

    private async Task<string?> QueueProcessingAsync(Guid assetId, string fileName, string actor, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        var profiles = new List<string>();
        if (VideoExtensions.Contains(extension) || AudioExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.TranscriptText);
        }
        else if (ImageExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }
        else if (DocumentExtensions.Contains(extension))
        {
            if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)) profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }

        try
        {
            foreach (var profile in profiles) await processing.EnqueueAsync(assetId, profile, actor, cancellationToken);
            return profiles.Count == 0 ? null : $"Automatic processing queued: {string.Join(", ", profiles)}.";
        }
        catch (ProcessingRequestException ex)
        {
            return $"Upload completed, but automatic processing queueing failed: {ex.Code} - {ex.Message}";
        }
    }

    private async Task<Dictionary<string, CategorySnapshot>> EnsureCategoriesAsync(IEnumerable<string> names, string actor, CancellationToken cancellationToken)
    {
        var existing = (await discovery.ListCategoriesAsync(cancellationToken))
            .Where(x => x.ParentCategoryId is null)
            .GroupBy(x => x.NameEn.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.ContainsKey(name)) continue;
            try
            {
                var created = await discovery.CreateCategoryAsync(new CreateCategoryRequest(null, name, null, 0), actor, cancellationToken);
                existing[name] = created;
            }
            catch (DiscoveryRequestException ex) when (ex.Code == "category_duplicate")
            {
                var refreshed = await discovery.ListCategoriesAsync(cancellationToken);
                var match = refreshed.FirstOrDefault(x => x.ParentCategoryId is null && string.Equals(x.NameEn.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (match is null) throw Error("category_create_conflict", $"Category '{name}' was created concurrently but could not be re-read.", 503);
                existing[name] = match;
            }
        }
        return existing;
    }

    private async Task RecalculateSessionAsync(Guid sessionId, string actor, CancellationToken cancellationToken)
    {
        var data = await GetOwnedAsync(sessionId, actor, cancellationToken);
        if (data.Session.State == BulkImportSessionState.Cancelled) return;
        var terminal = data.Items.Count(x => IsTerminal(x.State));
        var errors = data.Items.Count(x => x.State is BulkImportItemState.Failed or BulkImportItemState.Unsupported or BulkImportItemState.Cancelled);
        var now = DateTimeOffset.UtcNow;
        var state = terminal == data.Items.Count
            ? errors == 0 ? BulkImportSessionState.Completed : BulkImportSessionState.CompletedWithErrors
            : data.Items.Any(x => x.State == BulkImportItemState.Uploading)
                ? BulkImportSessionState.Running
                : BulkImportSessionState.Ready;
        await store.UpdateSessionAsync(data.Session with
        {
            State = state,
            UpdatedAtUtc = now,
            CompletedAtUtc = terminal == data.Items.Count ? now : null
        }, cancellationToken);
    }

    private async Task<(BulkImportSessionRow Session, IReadOnlyList<BulkImportItemRow> Items)> GetOwnedAsync(Guid sessionId, string actor, CancellationToken cancellationToken)
    {
        var data = await store.GetAsync(sessionId, cancellationToken)
            ?? throw Error("bulk_import_not_found", "Bulk import session was not found.", 404);
        if (!string.Equals(data.Value.Session.CreatedBy, actor, StringComparison.Ordinal))
            throw Error("bulk_import_forbidden", "This bulk import session belongs to another user.", 403);
        return data.Value;
    }

    private async Task<BulkImportSessionSnapshot> ToSnapshotAsync(BulkImportSessionRow session, IReadOnlyList<BulkImportItemRow> rows, CancellationToken cancellationToken)
    {
        var items = new List<BulkImportItemSnapshot>(rows.Count);
        foreach (var row in rows) items.Add(await ToItemSnapshotAsync(row, cancellationToken));
        var processed = items.Count(x => IsTerminal(x.State));
        return new BulkImportSessionSnapshot(
            session.SessionId, session.RootFolderName, session.State, session.TotalFiles, processed,
            items.Count(x => x.State == BulkImportItemState.Uploaded),
            items.Count(x => x.State == BulkImportItemState.AlreadyExists),
            items.Count(x => x.State == BulkImportItemState.Linked),
            items.Count(x => x.State == BulkImportItemState.Failed),
            items.Count(x => x.State == BulkImportItemState.Unsupported),
            items.Count(x => x.State == BulkImportItemState.Cancelled),
            session.TotalBytes, session.CreatedAtUtc, session.UpdatedAtUtc, session.CompletedAtUtc, items);
    }

    private async Task<BulkImportItemSnapshot> ToItemSnapshotAsync(BulkImportItemRow row, CancellationToken cancellationToken)
    {
        long received = 0;
        if (row.UploadSessionId is Guid uploadId && row.State is BulkImportItemState.Pending or BulkImportItemState.Uploading)
        {
            try { received = (await uploads.GetSessionAsync(uploadId, cancellationToken)).ReceivedLength; }
            catch (UploadRequestException) { received = 0; }
        }
        else if (IsTerminal(row.State) && row.State != BulkImportItemState.Failed)
        {
            received = row.ExpectedLength;
        }

        return new BulkImportItemSnapshot(
            row.ItemId, row.RelativePath, row.FileName, row.CategoryName, row.CategoryId, row.ExpectedLength,
            row.ExpectedSha256, row.UploadSessionId, row.AssetId, row.State, row.ReasonCode, row.Detail, received);
    }

    private static BulkImportSessionSummary ToSummary(BulkImportSessionSnapshot x) => new(
        x.SessionId, x.RootFolderName, x.State, x.TotalFiles, x.ProcessedFiles, x.Uploaded, x.AlreadyExists,
        x.Linked, x.Failed, x.Unsupported, x.TotalBytes, x.CreatedAtUtc, x.UpdatedAtUtc, x.CompletedAtUtc);

    private static BulkImportItemRow RequireItem(IReadOnlyList<BulkImportItemRow> items, Guid itemId) =>
        items.FirstOrDefault(x => x.ItemId == itemId) ?? throw Error("bulk_import_item_not_found", "Bulk import item was not found.", 404);

    private static bool IsSuccessTerminal(BulkImportItemState state) =>
        state is BulkImportItemState.Uploaded or BulkImportItemState.AlreadyExists or BulkImportItemState.Linked;

    private static bool IsTerminal(BulkImportItemState state) =>
        IsSuccessTerminal(state) || state is BulkImportItemState.Failed or BulkImportItemState.Unsupported or BulkImportItemState.Cancelled;

    private NormalizedDescriptor NormalizeDescriptor(string root, BulkImportFileDescriptor file)
    {
        if (file is null) throw Error("bulk_item_invalid", "Bulk import manifest contains an empty item.");
        var relative = (file.RelativePath ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        if (relative.Length == 0 || relative.Length > 1000) throw Error("relative_path_invalid", "Every selected file must have a valid relative path up to 1000 characters.");
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or "..")) throw Error("relative_path_invalid", $"Relative path '{relative}' is invalid.");
        var fileName = segments[^1];
        if (fileName.Length == 0 || fileName.Length > 260) throw Error("file_name_invalid", "File names are limited to 260 characters.");
        var category = segments.Length > 1 ? segments[0] : root;
        if (category.Length == 0 || category.Length > 200) throw Error("category_name_invalid", "Folder-derived category names are limited to 200 characters.");
        var supported = settings.Upload.AllowedExtensions.Any(x => string.Equals(x, Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase));
        var sha = NormalizeSha(file.Sha256);
        return new NormalizedDescriptor(relative, fileName, category, file.Length, sha, supported);
    }

    private static string NormalizeRoot(string? value)
    {
        var root = (value ?? string.Empty).Trim().TrimEnd('.', ' ');
        if (root.Length == 0) throw Error("root_folder_required", "The selected root folder name is required.");
        if (root.Length > 200) throw Error("root_folder_too_long", "The selected root folder name is limited to 200 characters.");
        if (root is "." or "..") throw Error("root_folder_invalid", "The selected root folder name is invalid.");
        return root;
    }

    private static string? NormalizeSha(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sha = value.Trim().ToLowerInvariant();
        if (sha.Length != 64 || sha.Any(ch => !Uri.IsHexDigit(ch))) return null;
        return sha;
    }

    private static string NormalizeActor(string? actorId)
    {
        var value = string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim();
        return value.Length <= 200 ? value : value[..200];
    }

    private static string NormalizeCode(string? value)
    {
        var code = string.IsNullOrWhiteSpace(value) ? "client_upload_failed" : value.Trim().ToLowerInvariant();
        return code.Length <= 100 ? code : code[..100];
    }

    private static string TitleFromFile(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (title.Length == 0) title = fileName;
        return title.Length <= 300 ? title : title[..300];
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

    private static AuditEvent NewAudit(string actor, string action, string entityType, string entityId, string outcome, string? detail = null) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, entityType, entityId, outcome, detail);

    private static BulkImportRequestException Error(string code, string message, int status = 400) => new(code, message, status);

    private static string BuildText(BulkImportSessionSnapshot snapshot)
    {
        var b = new StringBuilder();
        b.AppendLine("MAM BULK FOLDER IMPORT REPORT");
        b.AppendLine($"Session: {snapshot.SessionId:D}");
        b.AppendLine($"Root folder: {snapshot.RootFolderName}");
        b.AppendLine($"State: {snapshot.State}");
        b.AppendLine($"Created UTC: {snapshot.CreatedAtUtc:O}");
        b.AppendLine($"Completed UTC: {(snapshot.CompletedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-")}");
        b.AppendLine();
        b.AppendLine("SUMMARY");
        b.AppendLine($"Files: {snapshot.TotalFiles}");
        b.AppendLine($"Uploaded: {snapshot.Uploaded}");
        b.AppendLine($"Already existed: {snapshot.AlreadyExists}");
        b.AppendLine($"Existing assets reclassified: {snapshot.Linked}");
        b.AppendLine($"Failed: {snapshot.Failed}");
        b.AppendLine($"Unsupported: {snapshot.Unsupported}");
        b.AppendLine($"Cancelled: {snapshot.Cancelled}");
        b.AppendLine();
        b.AppendLine("FILES");
        foreach (var item in snapshot.Items)
        {
            b.AppendLine();
            b.AppendLine($"[{item.State.ToString().ToUpperInvariant()}]");
            b.AppendLine(item.RelativePath);
            b.AppendLine($"Category: {item.CategoryName}");
            if (item.AssetId is Guid assetId) b.AppendLine($"Asset ID: {assetId:D}");
            if (!string.IsNullOrWhiteSpace(item.ReasonCode)) b.AppendLine($"Reason: {item.ReasonCode}");
            if (!string.IsNullOrWhiteSpace(item.Detail)) b.AppendLine($"Detail: {item.Detail}");
        }
        return b.ToString();
    }

    private static string BuildCsv(BulkImportSessionSnapshot snapshot)
    {
        var b = new StringBuilder();
        b.AppendLine("relative_path,file_name,category,state,asset_id,sha256,reason,detail");
        foreach (var item in snapshot.Items)
            b.AppendLine(string.Join(",", new[]
            {
                Csv(item.RelativePath), Csv(item.FileName), Csv(item.CategoryName), Csv(item.State.ToString()),
                Csv(item.AssetId?.ToString("D")), Csv(item.ExpectedSha256), Csv(item.ReasonCode), Csv(item.Detail)
            }));
        return b.ToString();
    }

    private static string Csv(string? value) => $""{(value ?? string.Empty).Replace(""", """")}"";

    private sealed record NormalizedDescriptor(string RelativePath, string FileName, string CategoryName, long Length, string? Sha256, bool Supported);
}
