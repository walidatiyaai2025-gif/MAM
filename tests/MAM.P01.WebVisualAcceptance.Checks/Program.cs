using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var output=Path.GetFullPath(args.Length>0?args[0]:"artifacts/p01-visual");
var baseUrl=(args.Length>1?args[1]:"http://127.0.0.1:5080").TrimEnd('/');
Directory.CreateDirectory(output);
var profile=Path.Combine(Path.GetTempPath(),$"mam-ar-{Guid.NewGuid():N}");
Directory.CreateDirectory(profile);
var browser=StartBrowser(profile);
try{
 using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
 var port=await WaitPort(profile,browser);
 using var tr=await http.PutAsync($"http://127.0.0.1:{port}/json/new?about:blank",null);
 tr.EnsureSuccessStatusCode();
 using var td=JsonDocument.Parse(await tr.Content.ReadAsStringAsync());
 var ws=td.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!;
 using var socket=new ClientWebSocket(); await socket.ConnectAsync(new Uri(ws),CancellationToken.None);
 var id=0; await Call(socket,++id,"Page.enable"); await Call(socket,++id,"Runtime.enable");

 var captures=new[]{
  new Shot("dashboard-ar-1920.png",1920,1080,"dashboard"),new Shot("dashboard-ar-1366.png",1366,900,"dashboard"),
  new Shot("dashboard-ar-1024.png",1024,900,"dashboard"),new Shot("mobile-ar-390.png",390,844,"dashboard"),
  new Shot("library-ar-1440.png",1440,1000,"library"),new Shot("search-ar-1440.png",1440,1000,"search")};
 foreach(var s in captures){
  await Call(socket,++id,"Emulation.setDeviceMetricsOverride",new{width=s.W,height=s.H,deviceScaleFactor=1,mobile=false,screenWidth=s.W,screenHeight=s.H});
  var extra=s.Route=="search"?"&q=Diwan&searched=1":"";
  await Call(socket,++id,"Page.navigate",new{url=$"{baseUrl}/?lang=en&qa=p131#route={s.Route}{extra}"}); id=await Settle(socket,id);
  var v=(await Eval(socket,++id,"(()=>{const u=new URL(location.href),sb=document.querySelector('.sidebar')?.getBoundingClientRect(),mn=document.querySelector('.app-shell>main')?.getBoundingClientRect(),cs=[...document.querySelectorAll('[data-p128-language],#languageButton')];return{w:innerWidth,h:innerHeight,sw:Math.max(document.documentElement.scrollWidth,document.body.scrollWidth),lang:document.documentElement.lang,dir:document.documentElement.dir,ql:u.searchParams.get('lang'),r:new URLSearchParams(u.hash.slice(1)).get('route'),t:document.querySelector('#pageTitle')?.textContent?.trim()||'',txt:document.body.innerText||'',sb:sb?.left??0,mn:mn?.left??0,lc:cs.filter(e=>!e.hidden&&e.getAttribute('aria-hidden')!=='true'&&getComputedStyle(e).display!=='none').length}})()"));
  if(v.GetProperty("w").GetInt32()!=s.W||v.GetProperty("h").GetInt32()!=s.H||v.GetProperty("sw").GetInt32()>s.W) throw new Exception($"Viewport/overflow failed: {s.Name}");
  if(S(v,"lang")!="ar"||S(v,"dir")!="rtl"||S(v,"ql")!="ar"||S(v,"r")!=s.Route||v.GetProperty("lc").GetInt32()!=0) throw new Exception($"Arabic-only state failed: {s.Name} {v.GetRawText()}");
  if(s.W>800&&v.GetProperty("sb").GetDouble()<=v.GetProperty("mn").GetDouble()) throw new Exception($"RTL shell failed: {s.Name}");
  if(string.IsNullOrWhiteSpace(S(v,"t"))||S(v,"txt").Length<20) throw new Exception($"Rendered content failed: {s.Name}");
  var png=Convert.FromBase64String(S(await Call(socket,++id,"Page.captureScreenshot",new{format="png",fromSurface=true,captureBeyondViewport=false}),"data"));
  CheckPng(png,s.W,s.H,s.Name); await File.WriteAllBytesAsync(Path.Combine(output,s.Name),png); Console.WriteLine($"PASS: {s.Name} ar/rtl");
 }

 var routes=new[]{"dashboard","library","asset","curation-actions","ingest","upload","queue","reports","protection","admin","settings","categories","references","mediaPermissions","admin-actions","search","myPermissions"};
 var csv=new StringBuilder("surface,route,language,direction,status\r\n");
 foreach(var r in routes){
  var ex=r=="asset"?"&asset=00000000-0000-0000-0000-000000000001":"";
  await Call(socket,++id,"Page.navigate",new{url=$"{baseUrl}/?lang=en&qa=p131#route={r}{ex}"}); id=await Settle(socket,id);
  var v=await Eval(socket,++id,"(()=>{const u=new URL(location.href),h=new URLSearchParams(u.hash.slice(1)),cs=[...document.querySelectorAll('[data-p128-language],#languageButton')];return{lang:document.documentElement.lang,dir:document.documentElement.dir,ql:u.searchParams.get('lang'),r:h.get('route'),t:document.querySelector('#pageTitle')?.textContent?.trim()||'',txt:document.body.innerText||'',lc:cs.filter(e=>!e.hidden&&e.getAttribute('aria-hidden')!=='true'&&getComputedStyle(e).display!=='none').length,sw:Math.max(document.documentElement.scrollWidth,document.body.scrollWidth),w:innerWidth}})()" );
  if(S(v,"lang")!="ar"||S(v,"dir")!="rtl"||S(v,"ql")!="ar"||S(v,"r")!=r||v.GetProperty("lc").GetInt32()!=0||v.GetProperty("sw").GetInt32()>v.GetProperty("w").GetInt32()) throw new Exception($"Arabic route audit failed {r}: {v.GetRawText()}");
  foreach(var x in new[]{"Dashboard","Media Library","Content Search","System Settings","Central services"}) if(S(v,"txt").Contains(x,StringComparison.OrdinalIgnoreCase)) throw new Exception($"English chrome '{x}' on {r}");
  csv.AppendLine($"app,{r},ar,rtl,PASS"); Console.WriteLine($"PASS: route={r} ar/rtl");
 }

 await Call(socket,++id,"Page.navigate",new{url=$"{baseUrl}/?lang=en&qa=p131&tenant=diwan#route=library&q=Diwan&page=2&view=list&sort=title"}); id=await Settle(socket,id);
 var policy=await Eval(socket,++id,"(async()=>{const b=document.querySelector('[data-p128-language]')||document.querySelector('#languageButton');if(b)b.click();await new Promise(r=>setTimeout(r,250));const u=new URL(location.href),h=new URLSearchParams(u.hash.slice(1));return{lang:document.documentElement.lang,dir:document.documentElement.dir,ql:u.searchParams.get('lang'),tenant:u.searchParams.get('tenant'),route:h.get('route'),q:h.get('q'),page:h.get('page'),view:h.get('view'),sort:h.get('sort')}})()",true);
 if(S(policy,"lang")!="ar"||S(policy,"dir")!="rtl"||S(policy,"ql")!="ar"||S(policy,"tenant")!="diwan"||S(policy,"route")!="library"||S(policy,"q")!="Diwan"||S(policy,"page")!="2"||S(policy,"view")!="list"||S(policy,"sort")!="title") throw new Exception($"Arabic-only deep-link policy failed: {policy.GetRawText()}");
 var nav=await Eval(socket,++id,"(async()=>{const b=[...document.querySelectorAll('#nav [data-route=\"reports\"]')].find(x=>!x.hidden);if(!b)return{ok:false};b.click();await new Promise(r=>setTimeout(r,180));const h=new URLSearchParams(location.hash.slice(1));return{ok:true,r:h.get('route'),t:document.querySelector('#pageTitle')?.textContent?.trim()||'',lang:document.documentElement.lang,dir:document.documentElement.dir}})()",true);
 if(!nav.GetProperty("ok").GetBoolean()||S(nav,"r")!="reports"||S(nav,"t")!="التقارير"||S(nav,"lang")!="ar"||S(nav,"dir")!="rtl") throw new Exception($"Arabic navigation failed: {nav.GetRawText()}");
 await File.WriteAllTextAsync(Path.Combine(output,"route-language-matrix.csv"),csv.ToString(),new UTF8Encoding(false));
 Console.WriteLine("PASS: Arabic-only Web acceptance complete.");
 return 0;
}finally{try{if(!browser.HasExited)browser.Kill(true);}catch{} browser.Dispose(); try{Directory.Delete(profile,true);}catch{}}

static Process StartBrowser(string p){var c=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe")}.FirstOrDefault(File.Exists)??throw new Exception("Chromium not found");return Process.Start(new ProcessStartInfo{FileName=c,Arguments=$"--headless --disable-gpu --no-sandbox --no-first-run --remote-debugging-port=0 --user-data-dir=\"{p}\" about:blank",UseShellExecute=false,CreateNoWindow=true})??throw new Exception("Chromium start failed");}
static async Task<int> WaitPort(string p,Process b){var f=Path.Combine(p,"DevToolsActivePort");for(var i=0;i<450;i++){if(File.Exists(f)){var a=await File.ReadAllLinesAsync(f);if(a.Length>0&&int.TryParse(a[0],out var n))return n;}if(b.HasExited)throw new Exception("Chromium exited");await Task.Delay(100);}throw new Exception("DevTools timeout");}
static async Task<int> Settle(ClientWebSocket s,int id){await Call(s,++id,"Runtime.evaluate",new{expression="new Promise(r=>{const d=()=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(r,500)));document.readyState==='complete'?d():addEventListener('load',d,{once:true})})",awaitPromise=true,returnByValue=true});return id;}
static async Task<JsonElement> Eval(ClientWebSocket s,int id,string e,bool ap=false){var x=await Call(s,id,"Runtime.evaluate",new{expression=e,awaitPromise=ap,returnByValue=true});return x.GetProperty("result").GetProperty("value");}
static string S(JsonElement e,string k)=>e.TryGetProperty(k,out var v)?v.GetString()??"":"";
static async Task<JsonElement> Call(ClientWebSocket s,int id,string m,object? p=null){var b=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string,object?>{{"id",id},{"method",m},{"params",p??new{}}}));await s.SendAsync(b,WebSocketMessageType.Text,true,CancellationToken.None);while(true){var j=await Recv(s);using var d=JsonDocument.Parse(j);var r=d.RootElement;if(!r.TryGetProperty("id",out var x)||x.GetInt32()!=id)continue;if(r.TryGetProperty("error",out var er))throw new Exception($"CDP {m}: {er.GetRawText()}");return r.TryGetProperty("result",out var z)?z.Clone():default;}}
static async Task<string> Recv(ClientWebSocket s){using var ms=new MemoryStream();var b=new byte[16384];while(true){var r=await s.ReceiveAsync(b,CancellationToken.None);if(r.MessageType==WebSocketMessageType.Close)throw new Exception("CDP closed");ms.Write(b,0,r.Count);if(r.EndOfMessage)break;}return Encoding.UTF8.GetString(ms.ToArray());}
static void CheckPng(byte[] p,int w,int h,string n){if(p.Length<10000||p[0]!=0x89||R(p,16)!=w||R(p,20)!=h)throw new Exception($"PNG evidence invalid {n}");}
static int R(byte[] b,int o)=>(b[o]<<24)|(b[o+1]<<16)|(b[o+2]<<8)|b[o+3];
internal sealed record Shot(string Name,int W,int H,string Route);
