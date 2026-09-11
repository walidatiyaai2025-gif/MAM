using MAM.Application.Branding;
using MAM.Application.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var build = BuildInfo.Current;

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/version", () => Results.Ok(build));
app.MapGet(BrandTokens.CrestRuntimePath, () =>
{
    if (!DiwanCrestData.HasApprovedFingerprint()) return Results.Problem("Brand asset fingerprint validation failed.", statusCode: StatusCodes.Status500InternalServerError);
    return Results.File(DiwanCrestData.Bytes.ToArray(), "image/png");
});
app.MapFallbackToFile("index.html");

app.Run();
