using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MAM.Application.Discovery;

namespace MAM.Web;

public static class P12DiscoveryProxy
{
    public static void Map(WebApplication app, string? apiBase, string? developmentUser)
    {
        app.MapGet("/client-api/discovery/dashboard", (CancellationToken ct) => ForwardAsync(HttpMethod.Get, "api/v1/discovery/dashboard", null, apiBase, developmentUser, ct));
        app.MapGet("/client-api/discovery/my-media-capabilities", (CancellationToken ct) => ForwardAsync(HttpMethod.Get, "api/v1/discovery/my-media-capabilities", null, apiBase, developmentUser, ct));
        app.MapGet("/client-api/discovery/categories", (CancellationToken ct) => ForwardAsync(HttpMethod.Get, "api/v1/discovery/categories", null, apiBase, developmentUser, ct));
        app.MapPost("/client-api/discovery/categories", (CreateCategoryRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Post, "api/v1/discovery/categories", request, apiBase, developmentUser, ct));
        app.MapPut("/client-api/discovery/categories/{categoryId:guid}", (Guid categoryId, UpdateCategoryRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Put, $"api/v1/discovery/categories/{categoryId:D}", request, apiBase, developmentUser, ct));
        app.MapDelete("/client-api/discovery/categories/{categoryId:guid}", (Guid categoryId, CancellationToken ct) => ForwardAsync(HttpMethod.Delete, $"api/v1/discovery/categories/{categoryId:D}", null, apiBase, developmentUser, ct));

        app.MapGet("/client-api/discovery/assets/{assetId:guid}/category", (Guid assetId, CancellationToken ct) => ForwardAsync(HttpMethod.Get, $"api/v1/discovery/assets/{assetId:D}/category", null, apiBase, developmentUser, ct));
        app.MapPut("/client-api/discovery/assets/{assetId:guid}/category", (Guid assetId, AssignAssetCategoryRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Put, $"api/v1/discovery/assets/{assetId:D}/category", request, apiBase, developmentUser, ct));
        app.MapGet("/client-api/discovery/assets/{assetId:guid}/text/{sourceKind}", (Guid assetId, string sourceKind, CancellationToken ct) => ForwardAsync(HttpMethod.Get, $"api/v1/discovery/assets/{assetId:D}/text/{Uri.EscapeDataString(sourceKind)}", null, apiBase, developmentUser, ct));
        app.MapGet("/client-api/discovery/assets/{assetId:guid}/extraction-status", (Guid assetId, CancellationToken ct) => ForwardAsync(HttpMethod.Get, $"api/v1/discovery/assets/{assetId:D}/extraction-status", null, apiBase, developmentUser, ct));
        app.MapGet("/client-api/discovery/assets/{assetId:guid}/reference-tags", (Guid assetId, CancellationToken ct) => ForwardAsync(HttpMethod.Get, $"api/v1/discovery/assets/{assetId:D}/reference-tags", null, apiBase, developmentUser, ct));
        app.MapPost("/client-api/discovery/assets/{assetId:guid}/reference-tags", (Guid assetId, AddAssetReferenceTagRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Post, $"api/v1/discovery/assets/{assetId:D}/reference-tags", request, apiBase, developmentUser, ct));

        app.MapGet("/client-api/discovery/search", (string? query, int? page, int? pageSize, Guid? categoryId, string? mediaKind, CancellationToken ct) =>
        {
            var values = new List<string>
            {
                $"query={Uri.EscapeDataString(query ?? string.Empty)}",
                $"page={page ?? 1}",
                $"pageSize={pageSize ?? 50}"
            };
            if (categoryId is Guid category) values.Add($"categoryId={category:D}");
            if (!string.IsNullOrWhiteSpace(mediaKind)) values.Add($"mediaKind={Uri.EscapeDataString(mediaKind)}");
            return ForwardAsync(HttpMethod.Get, "api/v1/discovery/search?" + string.Join('&', values), null, apiBase, developmentUser, ct);
        });

        app.MapGet("/client-api/discovery/references", (CancellationToken ct) => ForwardAsync(HttpMethod.Get, "api/v1/discovery/references", null, apiBase, developmentUser, ct));
        app.MapPost("/client-api/discovery/references", (CreateReferenceSubjectRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Post, "api/v1/discovery/references", request, apiBase, developmentUser, ct));
        app.MapPost("/client-api/discovery/references/{subjectId:guid}/images", (Guid subjectId, AddReferenceImageRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Post, $"api/v1/discovery/references/{subjectId:D}/images", request, apiBase, developmentUser, ct));

        app.MapGet("/client-api/discovery/media-permissions", (CancellationToken ct) => ForwardAsync(HttpMethod.Get, "api/v1/discovery/media-permissions", null, apiBase, developmentUser, ct));
        app.MapPut("/client-api/discovery/media-permissions", (UpsertMediaPermissionRequest request, CancellationToken ct) => ForwardAsync(HttpMethod.Put, "api/v1/discovery/media-permissions", request, apiBase, developmentUser, ct));
    }

    private static async Task<IResult> ForwardAsync(HttpMethod method, string relativePath, object? body, string? apiBase, string? developmentUser, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var baseUri))
            return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);

        using var http = new HttpClient { BaseAddress = EnsureTrailingSlash(baseUri), Timeout = TimeSpan.FromMinutes(10) };
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.TryAddWithoutValidation("X-MAM-Client", "WebPortal");
        if (!string.IsNullOrWhiteSpace(developmentUser)) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", developmentUser.Trim());
        if (body is not null) request.Content = JsonContent.Create(body);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return Results.StatusCode((int)response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var mediaType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
            return Results.Content(payload, mediaType, Encoding.UTF8, (int)response.StatusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
