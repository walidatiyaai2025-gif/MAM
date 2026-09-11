using System.Net;
using MAM.Application.Clients;
using MAM.Application.Metadata;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MAM.P02.ClientAcceptance.Checks <api-base-url>");
    return 2;
}

var baseUri = new Uri(args[0].TrimEnd('/') + "/", UriKind.Absolute);
using var desktopHttp = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
using var webHttp = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
using var viewerHttp = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };

var desktop = new MamCatalogApiClient(desktopHttp, "WindowsDesktop", "editor");
var web = new MamCatalogApiClient(webHttp, "WebPortal", "editor");
var viewer = new MamCatalogApiClient(viewerHttp, "WindowsDesktopViewer", "viewer");

var health = await desktop.GetCatalogHealthAsync();
if (!health.IsReady || !string.Equals(health.Provider, "SqlServer", StringComparison.Ordinal))
    throw new InvalidOperationException($"Expected SQL Server readiness, got {health.Provider}: {health.Detail}");

var schemas = await viewer.ListMetadataSchemasAsync();
if (!schemas.Any(schema => schema.Key == BuiltInMetadataSchemaRegistry.CoreMediaSchemaKey && schema.Fields.Any(field => field.Key == "title" && field.Required)))
    throw new InvalidOperationException("Web/Desktop clients could not observe the P02 metadata schema baseline through the Central API.");

var created = await desktop.CreateAssetAsync("P02 Desktop Created Asset");
if (created.Version != 1) throw new InvalidOperationException("Desktop client create did not return version 1.");

var webView = (await web.ListAssetsAsync()).SingleOrDefault(asset => asset.Id == created.Id);
if (webView is null || webView.Title != created.Title)
    throw new InvalidOperationException("Web client did not observe Desktop-created authoritative catalog state.");

try
{
    await web.UpdateTitleAsync(created.Id, "Stale Web Update", 0);
    throw new InvalidOperationException("Stale Web update unexpectedly succeeded.");
}
catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
}

var updated = await web.UpdateTitleAsync(created.Id, "P02 Web Updated Asset", created.Version);
if (updated.Version != 2) throw new InvalidOperationException("Web client update did not advance optimistic version to 2.");

var desktopView = (await desktop.ListAssetsAsync()).SingleOrDefault(asset => asset.Id == created.Id);
if (desktopView is null || desktopView.Title != "P02 Web Updated Asset" || desktopView.Version != 2)
    throw new InvalidOperationException("Desktop client did not observe the Web-updated authoritative catalog state.");

Console.WriteLine("PASS: WindowsDesktop and WebPortal client identities observed the same SQL-backed authoritative state through the Central API.");
Console.WriteLine("PASS: optimistic concurrency and metadata schema discovery are exercised through the shared client contract.");
return 0;
