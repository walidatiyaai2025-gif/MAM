namespace MAM.Application.Discovery;

public sealed record MediaCapabilitySnapshot(
    string MediaKind,
    bool CanView,
    bool CanUpload,
    bool CanEdit,
    bool CanProcess,
    bool CanDownload);
