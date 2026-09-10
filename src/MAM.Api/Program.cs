using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var mamSettings = MamSettingsLoader.Load(configPath);
builder.Services.AddSingleton(mamSettings);

var app = builder.Build();
app.MapGet("/", () => Results.Ok(new
{
    product = mamSettings.Environment.DisplayNameEn,
    phase = "P00",
    environment = mamSettings.Environment.Name
}));
app.MapGet("/health/config", () => Results.Ok(new { status = "Healthy", site = mamSettings.Environment.SiteCode }));
app.MapGet("/version", () => Results.Ok(BuildInfo.Current));
app.Run();
