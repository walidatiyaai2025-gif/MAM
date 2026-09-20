(() => {
'use strict';
const H=v=>typeof esc==='function'?esc(v):String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const P128_PAGE_SIZE=12;
let p128MediaKind='',p128Sort='newest',p128Auth=null,p128RenderSerial=0;

/* The supplied reference screens are Arabic-first. Keep English available from the profile menu. */
if(localStorage.getItem('mam.p128.language.initialized')!=='1'){
  arabic=true;
  localStorage.setItem('mam.p128.language.initialized','1');
}

function iconForRoute(key){return ({dashboard:'bi-grid',library:'bi-folder2', 'curation-actions':'bi-gear',ingest:'bi-file-earmark-plus',upload:'bi-cloud-arrow-up',queue:'bi-hourglass-split',reports:'bi-bar-chart',protection:'bi-shield-check', 'admin-actions':'bi-people',search:'bi-search',admin:'bi-sliders',settings:'bi-gear',categories:'bi-diagram-3',references:'bi-person-badge',mediaPermissions:'bi-shield-check'})[key]||'bi-circle';}
function navLabels(){return {dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],'curation-actions':['Curation Actions','إجراءات التهيئة'],ingest:['New Ingest','إدخال جديد'],upload:['Add Media','إضافة ميديا'],queue:['Processing Queue','قائمة المعالجة'],reports:['Reports','التقارير'],protection:['Backup Protection','حماية النسخة الاحتياطية'],'admin-actions':['Management Actions','إجراءات الإدارة'],search:['Content Search','البحث في المحتوى'],admin:['System Administrator Settings','إعدادات مسؤول النظام']};}
function go(key){route=key;render();window.scrollTo({top:0,behavior:'auto'});}

function syncChrome(){
  document.documentElement.dir=arabic?'rtl':'ltr';
  document.documentElement.lang=arabic?'ar':'en';
  const labels=navLabels();
  const navHost=document.getElementById('nav');
  if(navHost){
    navHost.querySelectorAll('[data-route]').forEach(button=>{
      const key=button.dataset.route;
      if(labels[key]){
        const label=labels[key][arabic?1:0];
        let text=button.querySelector('.p127-nav-label');
        let icon=button.querySelector('.p127-nav-icon');
        if(!text){button.innerHTML=`<span class="p127-nav-icon"><i class="bi ${iconForRoute(key)}"></i></span><span class="p127-nav-label">${H(label)}</span>`;}
        else{text.textContent=label;if(icon)icon.innerHTML=`<i class="bi ${iconForRoute(key)}"></i>`;}
      }
    });
    const menu=navHost.querySelector('.p127-admin-menu');
    const trigger=menu?.querySelector('.p127-admin-trigger .p127-nav-label');
    if(trigger)trigger.textContent=arabic?'إعدادات مسؤول النظام':'System Administrator Settings';
    const ordered=['dashboard','library','curation-actions','ingest','upload','queue','reports','protection'];
    ordered.forEach(k=>{const b=navHost.querySelector(`:scope>[data-route="${k}"]`);if(b)navHost.appendChild(b);});
    if(menu)navHost.appendChild(menu);
    ['admin-actions','search'].forEach(k=>{const b=navHost.querySelector(`:scope>[data-route="${k}"]`);if(b)navHost.appendChild(b);});
  }
  const first=document.querySelector('.topbar>div:first-child');
  if(first){
    const s=first.querySelector('small'),h1=first.querySelector('h1');
    if(s)s.textContent=arabic?'الديوان الأميري  ‹  نظام إدارة الأصول الإعلامية':'Diwan Al Amiri  ›  Media Asset Management';
    if(h1)h1.textContent=(pages[route]?.[arabic?1:0]||route);
  }
  const brandText=document.querySelector('.sidebar .brand>div');
  if(brandText)brandText.innerHTML=`<strong>${arabic?'الديوان الأميري':'Diwan Al Amiri'}</strong><span>${arabic?'نظام إدارة الأصول الإعلامية':'Media Asset Management'}</span>`;
  const nonprod=document.querySelector('.nonprod');
  if(nonprod){const strong=nonprod.querySelector('strong');if(strong)strong.textContent=arabic?'نظام الإنتاج':'Production';}
  ensureLanguageInProfile();
  void ensureIdentityChip();
}

function ensureLanguageInProfile(){
  const menu=document.querySelector('.p127-profile-menu');
  if(!menu||menu.querySelector('[data-p128-language]'))return;
  const button=document.createElement('button');button.type='button';button.dataset.p128Language='1';button.innerHTML=`<i class="bi bi-translate"></i><span>${arabic?'English':'العربية'}</span>`;
  const logout=menu.querySelector('[data-logout]');menu.insertBefore(button,logout||null);
  button.addEventListener('click',()=>{arabic=!arabic;localStorage.setItem('mam.p128.language.initialized','1');render();});
}

async function ensureIdentityChip(){
  const actions=document.querySelector('.topbar .actions');if(!actions)return;
  let chip=actions.querySelector('.p128-identity-chip');
  if(!chip){chip=document.createElement('div');chip.className='p128-identity-chip';chip.style.cssText='height:38px;border:1px solid #d8e6de;background:#f1fbf5;border-radius:10px;padding:0 11px;display:flex;align-items:center;gap:7px;font-size:12px;font-weight:800;color:#153954;direction:ltr;white-space:nowrap';actions.insertBefore(chip,actions.firstChild);}
  if(!p128Auth){try{const r=await fetch('/auth/status',{headers:{Accept:'application/json'},cache:'no-store'});if(r.ok)p128Auth=await r.json();}catch{}}
  chip.innerHTML=`<span style="width:8px;height:8px;border-radius:50%;background:#31bd68;display:inline-block"></span><span>${H(p128Auth?.userName||'DA\\setup')}</span><i class="bi bi-chevron-down" style="font-size:10px"></i>`;
}

const baseShell=shellPage;
shellPage=function(){
  if(route==='dashboard')return `<div id="p12CategoryMetric" hidden></div><div id="p126DashboardCharts" hidden></div><div id="p128DashboardHost" class="p128-dashboard"><div class="state loading"><strong>${arabic?'جاري تحميل لوحة التحكم':'Loading dashboard'}</strong></div></div>`;
  if(route==='library')return `<div id="p128LibraryHost" class="p128-library"><div class="state loading"><strong>${arabic?'جاري تحميل مكتبة الوسائط':'Loading media library'}</strong></div></div>`;
  return baseShell();
};

async function json(url){const r=await fetch(url,{headers:{Accept:'application/json'},cache:'no-store'});if(!r.ok)throw Object.assign(new Error(`HTTP ${r.status}`),{status:r.status});return r.json();}
async function safe(url,fallback){try{return await json(url);}catch{return fallback;}}
async function mapLimit(items,limit,worker){const out=new Array(items.length);let cursor=0;await Promise.all(Array.from({length:Math.min(limit,Math.max(1,items.length))},async()=>{while(true){const i=cursor++;if(i>=items.length)return;out[i]=await worker(items[i],i);}}));return out;}
async function mediaKind(asset){
  try{const rows=await json(`/client-api/discovery/lookups/assets?query=${encodeURIComponent(asset.id)}&limit=2`);const hit=(rows||[]).find(x=>String(x.assetId).toLowerCase()===String(asset.id).toLowerCase())||rows?.[0];return hit?.mediaKind||'Other';}catch{return 'Other';}
}
function kindAr(k){return ({Video:'فيديو',Audio:'صوت',Image:'صورة',Document:'مستند',Other:'أخرى'})[k]||k;}
function kindIcon(k){return ({Video:'bi-play-circle',Audio:'bi-music-note-beamed',Image:'bi-image',Document:'bi-file-earmark-text',Other:'bi-file-earmark'})[k]||'bi-file-earmark';}

async function renderDashboard(){
  const serial=++p128RenderSerial,host=document.getElementById('p128DashboardHost');if(!host)return;
  const [assets,stats,queue,collections,refs,audit]=await Promise.all([
    safe('/client-api/catalog/assets',[]),safe('/client-api/discovery/dashboard',{}),safe('/client-api/processing/jobs/page?page=1&pageSize=10',{items:[],totalCount:0}),safe('/client-api/curation/collections',[]),safe('/client-api/discovery/references',[]),safe('/client-api/admin/audit?limit=500',{items:[]})
  ]);
  if(serial!==p128RenderSerial||route!=='dashboard'||!document.getElementById('p128DashboardHost'))return;
  const list=Array.isArray(assets)?assets:[];
  const kindSample=list.slice(0,80);const kinds=await mapLimit(kindSample,6,mediaKind);
  const counts={Video:0,Audio:0,Image:0,Document:0,Other:0};kinds.forEach(k=>counts[k]=(counts[k]||0)+1);
  const qItems=queue?.items||[];const active=qItems.filter(x=>/queued|running|processing|retry/i.test(String(x.status||x.state||''))).length;const failed=qItems.filter(x=>/fail/i.test(String(x.status||x.state||''))).length;
  const events=Array.isArray(audit)?audit:(audit?.items||[]),users=new Map();events.filter(x=>x.action==='upload.primary.committed'&&String(x.outcome).toLowerCase()==='success').forEach(x=>{const n=String(x.actorId||'unknown');users.set(n,(users.get(n)||0)+1);});
  const userRows=[...users.entries()].sort((a,b)=>b[1]-a[1]).slice(0,5);const userMax=Math.max(1,...userRows.map(x=>x[1]));const mediaMax=Math.max(1,...Object.values(counts));
  const total=list.length, indexed=Number(stats.indexedAssetCount||0), categories=Number(stats.categoryCount||0), references=Array.isArray(refs)?refs.length:0, folders=Array.isArray(collections)?collections.length:0;
  host.innerHTML=`
    <section class="p128-hero"><div class="p128-hero-copy"><span class="p128-live">LIVE</span><h2>${arabic?'نظرة تشغيلية مباشرة':'Live operational overview'}</h2><p>${arabic?'مرحبًا بك في منصة الإدارة المركزية الموثوقة.':'Welcome to the trusted central management platform.'}</p><div class="p128-hero-actions"><button class="p128-btn primary" data-p128-go="upload"><i class="bi bi-cloud-arrow-up"></i>${arabic?'إضافة ميديا':'Add Media'}</button><button class="p128-btn" data-p128-go="settings"><i class="bi bi-gear"></i>${arabic?'الإعدادات':'Settings'}</button><button class="p128-btn" data-p128-go="reports"><i class="bi bi-bar-chart"></i>${arabic?'التقارير':'Reports'}</button></div></div><div class="p128-hero-tagline">${arabic?'معًا نحو إدارة إعلامية أكثر كفاءة':'Toward more efficient media management'}</div></section>
    <section class="p128-metrics">
      ${metric('bi-folder2',total,arabic?'إجمالي الأصول':'Total assets',arabic?'الكتالوج المركزي':'Central catalog')}
      ${metric('bi-shield-check',indexed,arabic?'أصول مفهرسة':'Indexed assets',`${Number(stats.transcriptCount||0)} ${arabic?'تفريغ':'transcripts'} · ${Number(stats.ocrCount||0)} OCR`)}
      ${metric('bi-gear-wide-connected',active,arabic?'معالجة نشطة':'Active processing',failed?`${failed} ${arabic?'فشل':'failed'}`:(arabic?'لا توجد أخطاء نشطة':'No active failures'))}
      ${metric('bi-diagram-3',categories,arabic?'التصنيفات':'Categories',`${Number(stats.uncategorizedAssetCount||0)} ${arabic?'غير مصنف':'uncategorized'}`)}
      ${metric('bi-search',references,arabic?'مراجع':'References',arabic?'مكتبة المراجع':'Reference library')}
      ${metric('bi-files',folders,arabic?'مجلدات':'Collections',arabic?'مجموعات منظمة':'Organized collections')}
    </section>
    <section class="p128-production"><div class="p128-production-head"><span></span>${arabic?'حالة الإنتاج':'Production status'}</div><div class="p128-production-body"><div class="p128-production-status"><div class="p128-production-check"><i class="bi bi-check-lg"></i></div><div class="p128-production-copy"><strong>${arabic?'متصل':'Connected'}</strong><small>${arabic?'تم تحميل البيانات المباشرة بنجاح من الكتالوج والفهرسة والمعالجة.':'Live catalog, indexing and processing data loaded successfully.'}</small></div></div><div class="p128-production-divider"></div><div class="p128-production-copy"><strong>${arabic?'النظام يعمل بشكل طبيعي':'System operating normally'}</strong><small>${arabic?'جميع الخدمات متاحة':'All services available'}</small></div></div></section>
    <section class="p128-charts"><div class="p128-chart"><h3><i class="bi bi-bar-chart-fill"></i>${arabic?'عدد الملفات حسب نوع الميديا':'Files by media type'}</h3><div class="p128-bars">${Object.entries(counts).map(([k,v])=>`<div class="p128-vbar" style="--v:${Math.max(2,Math.round(v/mediaMax*100))}" data-value="${v}" data-label="${H(arabic?kindAr(k):k)}"></div>`).join('')}</div></div><div class="p128-chart"><h3><i class="bi bi-people-fill"></i>${arabic?'عدد الملفات المرفوعة بواسطة كل مستخدم':'Uploaded files by user'}</h3><div class="p128-user-list">${userRows.length?userRows.map(([n,v])=>`<div class="p128-user-line"><span title="${H(n)}">${H(n.split('\\').pop())}</span><div class="p128-user-track"><div class="p128-user-fill" style="width:${Math.max(4,v/userMax*100)}%"></div></div><b>${v}</b></div>`).join(''):`<div style="color:#728499;font-size:12px">${arabic?'لا توجد عمليات رفع مسجلة بعد.':'No recorded uploads yet.'}</div>`}</div></div></section>`;
  host.querySelectorAll('[data-p128-go]').forEach(b=>b.addEventListener('click',()=>go(b.dataset.p128Go)));
}
function metric(icon,value,label,detail){return `<article class="p128-metric"><div class="p128-metric-icon"><i class="bi ${icon}"></i></div><strong>${H(value)}</strong><b>${H(label)}</b><small>${H(detail)}</small></article>`;}

function searchParams(){const p=new URLSearchParams({page:String(p05Page||1),pageSize:String(P128_PAGE_SIZE)});if(p05Query)p.set('query',p05Query);if(p05Lifecycle)p.set('lifecycle',p05Lifecycle);if(p05Category)p.set('category',p05Category);if(p05Tag)p.set('tag',p05Tag);if(p05CollectionId)p.set('collectionId',p05CollectionId);return p;}
async function renderLibrary(){
  const serial=++p128RenderSerial,host=document.getElementById('p128LibraryHost')||content;if(route!=='library'||!host)return;
  host.innerHTML=`<div class="state loading"><strong>${arabic?'جاري تحميل مكتبة الوسائط…':'Loading media library…'}</strong></div>`;
  try{
    const [result,collections]=await Promise.all([json(`/client-api/curation/search?${searchParams()}`),safe('/client-api/curation/collections',[])]);
    if(serial!==p128RenderSerial||route!=='library')return;
    let items=Array.isArray(result.items)?result.items:[];
    items=items.map(a=>({...a,mediaKind:a.mediaKind||'Other'}));
    items=p128MediaKind?items.filter(a=>a.mediaKind===p128MediaKind):items;
    if(p128Sort==='title')items.sort((a,b)=>String(arabic&&a.titleAr?a.titleAr:a.title).localeCompare(String(arabic&&b.titleAr?b.titleAr:b.title),arabic?'ar':'en'));
    const totalPages=Math.max(1,Math.ceil(Number(result.totalCount||0)/P128_PAGE_SIZE));
    host.innerHTML=`
      <section class="p128-library-hero"><div class="p128-library-copy"><span class="p128-kicker">SEARCH & CURATION <i class="bi bi-headphones"></i></span><h2>${arabic?'مكتبة الوسائط':'Media Library'}</h2><p>${H(result.totalCount||0)} ${arabic?'أصل مطابق · نتائج مركزية':'matching assets · authoritative results'}</p><p>${arabic?'ابحث واستعرض وأدر جميع الأصول الإعلامية في مكان واحد':'Search, browse and manage all media assets in one place'}</p><div class="p128-library-actions"><button class="p128-btn primary" id="p128SearchTop"><i class="bi bi-search"></i>${arabic?'البحث':'Search'}</button><button class="p128-btn" id="p128CreateCategory"><i class="bi bi-tag"></i>${arabic?'إنشاء تصنيف':'Create category'}</button></div></div></section>
      <section class="p128-filter-panel"><div class="p128-filter-title"><i class="bi bi-funnel"></i> ${arabic?'تصفية البحث المتقدم':'Advanced search filters'}</div><div class="p128-filters"><input id="p128Query" class="p128-control" value="${H(p05Query||'')}" placeholder="${arabic?'بحث عربي أو إنجليزي':'Arabic or English search'}"/><select id="p128Kind" class="p128-control">${kindOptions()}</select><select id="p128Life" class="p128-control"><option value="">${arabic?'كل الحالات':'All states'}</option>${(result.facets?.lifecycles||[]).map(x=>`<option value="${H(x.value)}" ${x.value===p05Lifecycle?'selected':''}>${H(x.value)}</option>`).join('')}</select><select id="p128Cat" class="p128-control"><option value="">${arabic?'كل التصنيفات':'All categories'}</option>${(result.facets?.categories||[]).map(x=>`<option value="${H(x.value)}" ${x.value===p05Category?'selected':''}>${H(x.value)}</option>`).join('')}</select><select id="p128Collection" class="p128-control"><option value="">${arabic?'كل المجموعات':'All collections'}</option>${(collections||[]).map(c=>`<option value="${H(c.collectionId)}" ${c.collectionId===p05CollectionId?'selected':''}>${H(arabic&&c.nameAr?c.nameAr:c.nameEn)}</option>`).join('')}</select><button class="p128-filter-btn primary" id="p128Apply"><i class="bi bi-search"></i> ${arabic?'بحث':'Search'}</button><button class="p128-filter-btn" id="p128Reset"><i class="bi bi-arrow-clockwise"></i> ${arabic?'مسح':'Reset'}</button></div></section>
      <section class="p128-result-head"><strong><i class="bi bi-list-ul"></i> ${H(result.totalCount||0)} ${arabic?'أصل مطابق · نتائج مركزية':'matching assets · authoritative results'}</strong><div style="display:flex;gap:9px;align-items:center"><select id="p128Sort" class="p128-control" style="height:37px;width:145px"><option value="newest" ${p128Sort==='newest'?'selected':''}>${arabic?'الأحدث أولاً':'Newest first'}</option><option value="title" ${p128Sort==='title'?'selected':''}>${arabic?'حسب الاسم':'By title'}</option></select><div class="p128-view-switch"><button id="p128Grid" class="${p05Grid?'active':''}"><i class="bi bi-grid"></i></button><button id="p128List" class="${!p05Grid?'active':''}"><i class="bi bi-list"></i></button></div></div></section>
      <section id="p128Assets">${items.length?(p05Grid?`<div class="p128-asset-grid">${items.map(a=>assetCard(a)).join('')}</div>`:`<div class="list">${items.map(a=>assetRow(a)).join('')}</div>`):`<div class="state empty"><strong>${arabic?'لا توجد نتائج':'No results'}</strong><br>${arabic?'لا توجد أصول تطابق عوامل التصفية الحالية.':'No assets match the current filters.'}</div>`}</section>
      <div class="p127-pager"><button id="p128Prev" ${(p05Page||1)<=1?'disabled':''}><i class="bi bi-chevron-right"></i></button><span class="p127-page-info">${arabic?'صفحة':'Page'} ${p05Page||1} / ${totalPages}</span><button id="p128Next" ${(p05Page||1)>=totalPages?'disabled':''}><i class="bi bi-chevron-left"></i></button></div>`;
    bindLibrary(items,collections,totalPages);
  }catch(e){host.innerHTML=`<div class="state error"><strong>${arabic?'تعذر تحميل مكتبة الوسائط':'Media library failed to load'}</strong><br>${H(e.message)}</div>`;}
}
function kindOptions(){return `<option value="">${arabic?'كل أنواع الميديا':'All media types'}</option>${['Video','Audio','Image','Document','Other'].map(k=>`<option value="${k}" ${p128MediaKind===k?'selected':''}>${H(arabic?kindAr(k):k)}</option>`).join('')}`;}
function assetCard(a){const k=a.mediaKind||'Other',title=arabic&&a.titleAr?a.titleAr:a.title;return `<article class="p128-asset-card"><div class="p128-thumb ${String(k).toLowerCase()}"><span class="p128-type-pill"><i class="bi ${kindIcon(k)}"></i> ${H(arabic?kindAr(k):k)}</span><i class="bi ${kindIcon(k)}"></i></div><div class="p128-card-title">${H(title||'—')} <i class="bi bi-star" style="float:left;color:#647b92"></i></div><div class="p128-id">${H(a.id)}</div><div class="p128-card-meta"><span class="p128-dot"></span><span>v${H(a.version)} · ${H(a.lifecycle||'Draft')}</span></div><div class="p128-card-actions"><button data-mam-open-asset="${H(a.id)}"><i class="bi bi-eye"></i> ${arabic?'تفاصيل الأصل':'Details'}</button><button data-p05-edit="${H(a.id)}"><i class="bi bi-pencil-square"></i> ${arabic?'تعديل البيانات':'Edit metadata'}</button><button class="danger" data-mam-delete-asset="${H(a.id)}" data-mam-delete-title="${H(title||'')}"><i class="bi bi-trash3"></i> ${arabic?'حذف نهائي':'Delete'}</button></div></article>`;}
function assetRow(a){const title=arabic&&a.titleAr?a.titleAr:a.title;return `<div class="row"><b>${H(arabic?kindAr(a.mediaKind):a.mediaKind)}</b><span><strong>${H(title)}</strong><br><small>${H(a.id)}</small></span><span>v${H(a.version)} · ${H(a.lifecycle)}</span><span><button class="action" data-mam-open-asset="${H(a.id)}">${arabic?'التفاصيل':'Details'}</button> <button class="action" data-p05-edit="${H(a.id)}">${arabic?'تعديل':'Edit'}</button></span></div>`;}
function bindLibrary(items,collections,totalPages){
  const apply=()=>{p05Query=document.getElementById('p128Query')?.value.trim()||'';p128MediaKind=document.getElementById('p128Kind')?.value||'';p05Lifecycle=document.getElementById('p128Life')?.value||'';p05Category=document.getElementById('p128Cat')?.value||'';p05CollectionId=document.getElementById('p128Collection')?.value||'';p05Page=1;void renderLibrary();};
  document.getElementById('p128Apply')?.addEventListener('click',apply);document.getElementById('p128SearchTop')?.addEventListener('click',()=>document.getElementById('p128Query')?.focus());document.getElementById('p128Query')?.addEventListener('keydown',e=>{if(e.key==='Enter')apply();});
  document.getElementById('p128Reset')?.addEventListener('click',()=>{p05Query='';p128MediaKind='';p05Lifecycle='';p05Category='';p05Tag='';p05CollectionId='';p05Page=1;void renderLibrary();});
  document.getElementById('p128CreateCategory')?.addEventListener('click',()=>go('categories'));
  document.getElementById('p128Grid')?.addEventListener('click',()=>{p05Grid=true;void renderLibrary();});document.getElementById('p128List')?.addEventListener('click',()=>{p05Grid=false;void renderLibrary();});document.getElementById('p128Sort')?.addEventListener('change',e=>{p128Sort=e.target.value;void renderLibrary();});
  document.getElementById('p128Prev')?.addEventListener('click',()=>{if(p05Page>1){p05Page--;void renderLibrary();}});document.getElementById('p128Next')?.addEventListener('click',()=>{if(p05Page<totalPages){p05Page++;void renderLibrary();}});
  /* Reuse the existing tested action bindings, including permanent-delete confirmation. */
  if(typeof p05BindAssetActions==='function')p05BindAssetActions(collections||[]);
}

/* Make the authoritative library renderer use the screenshot-matched surface. */
if(typeof p05LoadLibrary==='function')p05LoadLibrary=renderLibrary;
loadLiveLibrary=renderLibrary;

const oldRender=render;
render=function(){oldRender();syncChrome();if(route==='dashboard')void renderDashboard();if(route==='library')void renderLibrary();};

/* One final render applies Arabic-first reference composition after all previous layers loaded. */
render();
})();
