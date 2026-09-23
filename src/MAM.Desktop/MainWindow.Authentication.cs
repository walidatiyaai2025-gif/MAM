using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MAM.Desktop;

public partial class MainWindow
{
    private DispatcherTimer? _presenceHeartbeatTimer;

    private async void DomainSignIn_Click(object sender, RoutedEventArgs e)
    {
        var userName = DomainUserNameTextBox.Text?.Trim() ?? string.Empty;
        var password = DomainPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            LoginStatusText.Text = "Enter your Active Directory user name and password.";
            return;
        }

        SetLoginBusy(true, "Signing in with Active Directory…");
        try
        {
            var authenticatedUser = await DesktopProductionTransport.SignInWithActiveDirectoryAsync(
                userName,
                password);

            CompleteAuthentication(authenticatedUser);
        }
        catch (Exception ex)
        {
            LoginStatusText.Text = FriendlyAuthenticationError(ex);
        }
        finally
        {
            DomainPasswordBox.Clear();
            SetLoginBusy(false);
        }
    }

    private async void WindowsSso_Click(object sender, RoutedEventArgs e)
    {
        SetLoginBusy(true, "Signing in with Windows SSO…");
        try
        {
            var authenticatedUser = await DesktopProductionTransport.SignInWithWindowsAsync();
            CompleteAuthentication(authenticatedUser);
        }
        catch (Exception ex)
        {
            LoginStatusText.Text =
                FriendlyAuthenticationError(ex) +
                " You can sign in above with DA\\username and password without joining this PC to the domain.";
        }
        finally
        {
            SetLoginBusy(false);
        }
    }

    private async void CompleteAuthentication(string authenticatedUser)
    {
        LoginStatusText.Text = string.Empty;
        SessionBadgeText.Text =
            $"● Production · {DesktopProductionTransport.AuthenticationLabel} · {authenticatedUser}";
        LoginLayer.Visibility = Visibility.Collapsed;
        ShellLayer.Visibility = Visibility.Visible;
        StartProductionPresenceHeartbeat();
        await ApplyManagedDesktopNavigationAsync();
        ShowPage(_currentRoute);
    }

    private void StartProductionPresenceHeartbeat()
    {
        if (!DesktopProductionTransport.IsProduction)
            return;

        _presenceHeartbeatTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _presenceHeartbeatTimer.Tick -= PresenceHeartbeat_Tick;
        _presenceHeartbeatTimer.Tick += PresenceHeartbeat_Tick;
        _presenceHeartbeatTimer.Start();
        _ = SendProductionPresenceHeartbeatAsync();
    }

    private void StopProductionPresenceHeartbeat() => _presenceHeartbeatTimer?.Stop();

    private async void PresenceHeartbeat_Tick(object? sender, EventArgs e) =>
        await SendProductionPresenceHeartbeatAsync();

    private async Task SendProductionPresenceHeartbeatAsync()
    {
        try
        {
            await DesktopProductionTransport.SendPresenceHeartbeatAsync();
        }
        catch
        {
            // Presence telemetry must never interrupt the operator workflow.
        }
    }

    private void SetLoginBusy(bool busy, string? message = null)
    {
        DomainSignInButton.IsEnabled = !busy;
        WindowsSsoButton.IsEnabled = !busy;
        DomainUserNameTextBox.IsEnabled = !busy;
        DomainPasswordBox.IsEnabled = !busy;

        if (!string.IsNullOrWhiteSpace(message))
            LoginStatusText.Text = message;
    }

    private void OnProductionSessionInvalidated(string reason)
    {
        StopProductionPresenceHeartbeat();
        Dispatcher.Invoke(() =>
        {
            LoginStatusText.Text = reason;
            SessionBadgeText.Text = "● Production · Session expired";
            ShellLayer.Visibility = Visibility.Collapsed;
            LoginLayer.Visibility = Visibility.Visible;
            DomainPasswordBox.Clear();
            DomainUserNameTextBox.Focus();
        });
    }

    private static string FriendlyAuthenticationError(Exception ex)
    {
        var message = ex.Message;
        if (message.Length > 320)
            message = message[..320] + "…";
        return message;
    }
}
