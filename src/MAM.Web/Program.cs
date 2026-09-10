using MAM.Application.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var build = BuildInfo.Current;

app.MapGet("/", () => Results.Content($$"""
<!doctype html>
<html lang="en" dir="ltr">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Diwan Al Amiri Media Asset Management</title></head>
<body style="font-family:Segoe UI,Arial,sans-serif;margin:0;background:#F5F7FA;color:#111827">
  <main style="min-height:100vh;display:grid;place-items:center;padding:24px">
    <section style="width:min(760px,100%);box-sizing:border-box;background:white;border-top:4px solid #B58A2A;padding:clamp(20px,5vw,32px);box-shadow:0 12px 36px rgba(7,24,46,.12)">
      <div style="color:#B58A2A;font-weight:700">P00 FOUNDATION</div>
      <h1 style="color:#0A2342">Diwan Al Amiri Media Asset Management</h1>
      <p lang="ar" dir="rtl" style="color:#0A2342;font-size:1.05rem">نظام إدارة الأصول الإعلامية - الديوان الأميري</p>
      <p>This Web surface is a client of the governed Central API boundary; it does not load database or permanent-storage credentials/configuration.</p>
      <p><strong>Environment:</strong> {{build.EnvironmentName}} · <strong>Version:</strong> {{build.Version}}</p>
      <p style="font-size:.85rem;color:#4B5563;overflow-wrap:anywhere"><strong>SHA:</strong> {{build.CommitSha}} · <strong>Build:</strong> {{build.BuildNumber}} · <strong>UTC:</strong> {{build.BuildTimestampUtc}}</p>
    </section>
  </main>
</body>
</html>
""", "text/html; charset=utf-8"));
app.MapGet("/version", () => Results.Ok(build));
app.Run();
