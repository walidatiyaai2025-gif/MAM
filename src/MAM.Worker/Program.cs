using System.Text.Json;
using MAM.Application.Diagnostics;
using MAM.Infrastructure.Configuration;

var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var settings = MamSettingsLoader.Load(configPath);
var build = BuildInfo.Current.WithEnvironment(settings.Environment.Name);
Console.WriteLine(JsonSerializer.Serialize(new
{
    service = "MAM.Worker",
    phase = "P00",
    site = settings.Environment.SiteCode,
    build
}));
