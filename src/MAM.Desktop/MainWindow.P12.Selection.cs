namespace MAM.Desktop;

public partial class MainWindow
{
    // Set by discovery/library navigation when a specific asset is selected. Null preserves
    // the existing first-asset fallback used by the P04 development workspace.
    private Guid? _p12SelectedAssetId;
}
