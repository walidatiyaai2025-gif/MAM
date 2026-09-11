namespace MAM.Domain.Assets;

public sealed class MediaAsset
{
    private MediaAsset(AssetId id, string title, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Title = NormalizeTitle(title);
        Lifecycle = AssetLifecycleState.Draft;
        Version = 1;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public AssetId Id { get; }
    public string Title { get; private set; }
    public AssetLifecycleState Lifecycle { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static MediaAsset Create(string title, DateTimeOffset createdAtUtc) =>
        new(AssetId.New(), title, createdAtUtc);

    public static MediaAsset Create(AssetId id, string title, DateTimeOffset createdAtUtc) =>
        new(id, title, createdAtUtc);

    public bool TryRename(string title, long expectedVersion, DateTimeOffset updatedAtUtc)
    {
        if (expectedVersion != Version)
        {
            return false;
        }

        Title = NormalizeTitle(title);
        Version++;
        UpdatedAtUtc = updatedAtUtc;
        return true;
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Asset title is required.", nameof(title));
        }

        if (normalized.Length > 300)
        {
            throw new ArgumentException("Asset title cannot exceed 300 characters.", nameof(title));
        }

        return normalized;
    }
}
