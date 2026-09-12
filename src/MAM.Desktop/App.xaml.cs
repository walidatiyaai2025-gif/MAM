using System.Windows;

namespace MAM.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DesktopSetupConfiguration.Apply();
        base.OnStartup(e);
    }
}
