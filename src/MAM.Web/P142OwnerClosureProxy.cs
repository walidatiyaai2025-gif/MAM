using System.Net.Http.Json;
using MAM.Application.Clients;

namespace MAM.Web;

public static class P142OwnerClosureProxy
{
    public static void Map(WebApplication app, string? apiBase, string? developmentUser)
    {
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            app.MapMethods("/client-api/admin/references/{**rest}", new[]{"GET","POST","PUT","DELETE"}, () => Results.Json(new { error="central_api_not_configured" },statusCode:503));
            return;
        }

        async Task<IResult> Forward(HttpContext context, string path, CancellationToken ct)
        {
            using var http = MamWebApiTransport.Create(apiUri, TimeSpan.FromMinutes(2));
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
            request.Headers.TryAddWithoutValidation("X-MAM-Client","WebPortal");
            if (!string.IsNullOrWhiteSpace(developmentUser)) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User",developmentUser);
            if (context.Request.ContentLength is > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                    request.Content.Headers.TryAddWithoutValidation("Content-Type",context.Request.ContentType);
            }
            using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            return Results.Text(text,response.Content.Headers.ContentType?.ToString()??"application/json",statusCode:(int)response.StatusCode);
        }

        app.MapGet("/client-api/admin/references", (HttpContext c,CancellationToken ct)=>Forward(c,"api/v1/admin/references",ct));
        app.MapPost("/client-api/admin/references", (HttpContext c,CancellationToken ct)=>Forward(c,"api/v1/admin/references",ct));
        app.MapPut("/client-api/admin/references/{subjectId:guid}", (Guid subjectId,HttpContext c,CancellationToken ct)=>Forward(c,$"api/v1/admin/references/{subjectId:D}",ct));
        app.MapDelete("/client-api/admin/references/{subjectId:guid}", (Guid subjectId,HttpContext c,CancellationToken ct)=>Forward(c,$"api/v1/admin/references/{subjectId:D}",ct));
    }
}
