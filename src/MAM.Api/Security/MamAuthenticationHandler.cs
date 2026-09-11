using System.Security.Claims;
using System.Text.Encodings.Web;
using MAM.Application.Identity;
using MAM.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MAM.Api.Security;

public sealed class MamAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Mam";
    public const string DevelopmentUserHeader = "X-MAM-Dev-User";

    private readonly MamSettings _settings;

    public MamAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MamSettings settings)
        : base(options, logger, encoder)
    {
        _settings = settings;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(_settings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_settings.Auth.Mode, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(DevelopmentUserHeader, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = headerValue.ToString().Trim().ToLowerInvariant() switch
        {
            "admin" => CreateIdentity("dev-admin", "Development Administrator", MamRoles.Administrator),
            "editor" => CreateIdentity("dev-editor", "Development Catalog Editor", MamRoles.CatalogEditor),
            "viewer" => CreateIdentity("dev-viewer", "Development Viewer", MamRoles.Viewer),
            _ => null
        };

        if (identity is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown development identity."));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static ClaimsIdentity CreateIdentity(string userId, string displayName, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, role)
        };

        claims.AddRange(MamSecurity.PermissionsForRole(role)
            .Select(permission => new Claim(MamSecurity.PermissionClaimType, permission)));

        return new ClaimsIdentity(claims, Scheme);
    }
}
