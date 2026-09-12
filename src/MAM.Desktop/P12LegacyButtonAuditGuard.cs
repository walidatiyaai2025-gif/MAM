using System.Windows;
using System.Windows.Controls;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly bool P12LegacyButtonAuditGuardRegistered = RegisterP12LegacyButtonAuditGuard();

    private static bool RegisterP12LegacyButtonAuditGuard()
    {
        EventManager.RegisterClassHandler(typeof(Button), LoadedEvent, new RoutedEventHandler(P12LegacyButtonLoaded));
        return true;
    }

    private static void P12LegacyButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Content is not string label) return;
        if (!string.Equals(label, "Add to first collection", StringComparison.Ordinal) &&
            !string.Equals(label, "أضف لأول مجموعة", StringComparison.Ordinal)) return;

        // The old P05 shortcut swallowed failures and did not let the operator choose a collection.
        // It is deliberately suppressed in favor of the audited Curation Actions page, which exposes
        // explicit collection selection, Add and Remove controls with visible Central API outcomes.
        button.Visibility = Visibility.Collapsed;
        button.IsHitTestVisible = false;
        button.IsTabStop = false;
        button.ToolTip = "Use Curation Actions for audited collection membership changes.";
    }
}
