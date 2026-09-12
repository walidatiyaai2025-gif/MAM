using System.Windows;

namespace MAM.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DesktopSetupConfiguration.Apply();
        base.OnStartup(e);
    }
}
