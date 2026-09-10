using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var mamSettings = MamSettingsLoader.Load(configPath);
var app = builder.Build();

app.MapGet("/", () => Results.Content($$"""
<!doctype html>
<html lang="en" dir="ltr">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{mamSettings.Environment.DisplayNameEn}}</title></head>
<body style="font-family:Segoe UI,Arial,sans-serif;margin:0;background:#F5F7FA;color:#111827">
  <main style="min-height:100vh;display:grid;place-items:center;padding:24px">
    <section style="max-width:760px;background:white;border-top:4px solid #B58A2A;padding:32px;box-shadow:0 12px 36px rgba(7,24,46,.12)">
      <div style="color:#B58A2A;font-weight:700">P00 FOUNDATION</div>
      <h1 style="color:#0A2342">{{mamSettings.Environment.DisplayNameEn}}</h1>
      <p>Central API/server, SQL Server catalog, Primary + independently verified Backup Storage, Windows tape capture, and Windows/Web upload are the governed product target.</p>
      <p><strong>Environment:</strong> {{mamSettings.Environment.Name}} · <strong>Version:</strong> {{BuildInfo.Current.Version}}</p>
    </section>
  </main>
</body>
</html>
""", "text/html; charset=utf-8"));
app.MapGet("/version", () => Results.Ok(BuildInfo.Current));
app.Run();
