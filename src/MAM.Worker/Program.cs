using System.Text.Json;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Diagnostics;

var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var settings = MamSettingsLoader.Load(configPath);
Console.WriteLine(JsonSerializer.Serialize(new
{
    service = "MAM.Worker",
    phase = "P00",
    site = settings.Environment.SiteCode,
    build = BuildInfo.Current
}));
