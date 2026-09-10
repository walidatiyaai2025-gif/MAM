using System.Windows;
using MAM.Infrastructure.Diagnostics;

namespace MAM.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var build = BuildInfo.Current;
        var sha = build.CommitSha.Length > 12 ? build.CommitSha[..12] : build.CommitSha;
        BuildIdentityText.Text = $"{build.EnvironmentName} · v{build.Version} · SHA {sha} · Build {build.BuildNumber} · {build.BuildTimestampUtc}";
    }
}
