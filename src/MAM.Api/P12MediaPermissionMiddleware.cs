using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using MAM.Application.Discovery;
using MAM.Application.Uploads;

namespace MAM.Api;

public static class P12MediaPermissionMiddleware
{
    private static readonly Regex ProcessingAssetRoute = new(
        @"/processing/assets/(?<asset>[0-9a-fA-F-]{36})(?<tail>/.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static void Use(WebApplication app, string configuredApiBasePath)
    {
        var prefix = $"{configuredApiBasePath.TrimEnd('/')}/v1";
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var discovery = context.RequestServices.GetService<IDiscoveryService>();
            if (discovery is null)
            {
                await next();
                return;
            }

            var relative = context.Request.Path.Value?[prefix.Length..] ?? string.Empty;
            if (HttpMethods.IsPost(context.Request.Method) && string.Equals(relative, "/uploads/sessions", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.EnableBuffering();
                CreateUploadSessionRequest? request = null;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<CreateUploadSessionRequest>(context.Request.Body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, context.RequestAborted);
                }
                catch (JsonException)
                {
                    // Preserve the existing endpoint's request validation behavior.
                }
                finally
                {
                    context.Request.Body.Position = 0;
                }

                if (request is not null)
                {
                    var mediaKind = MediaKinds.FromFileName(request.OriginalFileName);
                    if (!await AuthorizeAsync(context, discovery, mediaKind, "upload")) return;
                }
            }

            var match = ProcessingAssetRoute.Match(relative);
            if (match.Success && Guid.TryParse(match.Groups["asset"].Value, out var assetId))
            {
                var tail = match.Groups["tail"].Value;
                var action = HttpMethods.IsPost(context.Request.Method) && string.Equals(tail, "/jobs", StringComparison.OrdinalIgnoreCase)
                    ? "process"
                    : HttpMethods.IsGet(context.Request.Method) && (tail.Contains("/content", StringComparison.OrdinalIgnoreCase) || tail.Contains("/preview/original", StringComparison.OrdinalIgnoreCase))
                        ? "download"
                        : HttpMethods.IsGet(context.Request.Method)
                            ? "view"
                            : null;
                if (action is not null)
                {
                    string mediaKind;
                    try { mediaKind = await discovery.GetAssetMediaKindAsync(assetId, context.RequestAborted); }
                    catch (DiscoveryRequestException ex)
                    {
                        context.Response.StatusCode = ex.StatusCode;
                        await context.Response.WriteAsJsonAsync(new { error = ex.Code, detail = ex.Message }, context.RequestAborted);
                        return;
                    }
                    if (!await AuthorizeAsync(context, discovery, mediaKind, action)) return;
                }
            }

            await next();
        });
    }

    private static async Task<bool> AuthorizeAsync(HttpContext context, IDiscoveryService discovery, string mediaKind, string action)
    {
        var health = await discovery.GetHealthAsync(context.RequestAborted);
        if (!health.IsReady)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "discovery_unavailable", detail = health.Detail }, context.RequestAborted);
            return false;
        }

        var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (await discovery.IsMediaActionAllowedAsync(roles, mediaKind, action, context.RequestAborted)) return true;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "media_type_permission_denied",
            detail = $"The current role is not permitted to {action} {mediaKind} media.",
            mediaKind,
            action
        }, context.RequestAborted);
        return false;
    }
}
