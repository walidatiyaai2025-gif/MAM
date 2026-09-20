using System;
using System.Windows;

namespace MAM.Desktop;

public partial class MainWindow
{
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
            DomainPasswordBox.Password = string.Empty;
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

    private void CompleteAuthentication(string authenticatedUser)
    {
        LoginStatusText.Text = string.Empty;
        SessionBadgeText.Text =
            $"● Production · {DesktopProductionTransport.AuthenticationLabel} · {authenticatedUser}";
        LoginLayer.Visibility = Visibility.Collapsed;
        ShellLayer.Visibility = Visibility.Visible;
        ShowPage(_currentRoute);
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

    private static string FriendlyAuthenticationError(Exception ex)
    {
        var message = ex.Message;
        if (message.Length > 320)
            message = message[..320] + "…";
        return message;
    }
}
