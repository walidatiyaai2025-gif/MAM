const pages={dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],asset:['Asset Details','تفاصيل الأصل'],ingest:['New Ingest','إدخال جديد'],upload:['Upload','رفع الملفات'],queue:['Processing Queue','قائمة المعالجة'],admin:['Administration','الإدارة'],settings:['Settings','الإعدادات']};
let arabic=false;
let route='dashboard';
const content=document.getElementById('content');
const title=document.getElementById('pageTitle');
const languageButton=document.getElementById('languageButton');
const nav=[...document.querySelectorAll('#nav button')];

const esc=value=>String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const lead=(h,p,badge='DEVELOPMENT DEMO')=>`<div class="lead"><span class="badge">${esc(badge)}</span><h2>${esc(h)}</h2><p>${esc(p)}</p></div>`;
const state=(kind,heading,detail)=>`<div class="card"><div class="state ${kind}"><strong>${esc(heading)}</strong><br>${esc(detail)}</div></div>`;
const states=()=>`<div class="card"><h3>${arabic?'حالات النظام':'System state treatments'}</h3><div class="state loading"><strong>Loading</strong><br>${arabic?'جاري تحميل بيانات العرض…':'Loading demo catalog data…'}</div><div class="state empty"><strong>Empty</strong><br>${arabic?'لا توجد نتائج مطابقة.':'No assets match the current filters.'}</div><div class="state error"><strong>API error</strong><br>${arabic?'تعذر الوصول إلى الخدمة المركزية.':'Central API is unreachable. Retry is available.'}</div><div class="state denied"><strong>Permission denied</strong><br>${arabic?'لا توجد صلاحية لهذا الإجراء.':'You do not have permission for this action.'}</div><div class="state degraded"><strong>Degraded</strong><br>${arabic?'النسخة الاحتياطية غير متاحة؛ الأصل ليس Protected.':'Backup is unavailable; assets are not marked Protected.'}</div></div>`;

function shellPage(){
  const views={
    dashboard:`${lead(arabic?'نظرة تشغيلية للأرشيف':'Operational view of the archive',arabic?'بيانات تجريبية واضحة بدون ادعاء اتصال إنتاجي.':'Clearly labeled demo data; no production connectivity is claimed in P01.')}<div class="grid three"><div class="card metric"><strong>1,248</strong><span>Demo assets</span><small>DEMO</small></div><div class="card metric"><strong>96.8%</strong><span>Demo protected</span><small>Primary + Backup verified</small></div><div class="card metric"><strong>14</strong><span>Processing</span><small>DEMO QUEUE</small></div></div>${states()}`,
    library:`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'الاتصال بواجهة API المركزية…':'Connecting to the authoritative Central API catalog…','CENTRAL API')} ${state('loading','Loading',arabic?'جاري تحميل الكتالوج المركزي…':'Loading authoritative catalog data…')}`,
    asset:`${lead(arabic?'تفاصيل الأصل':'Asset Details','DAA-2026-001248 · DEVELOPMENT DEMO')}<div class="grid two"><div class="card"><h3>Preview</h3><p>Video preview shell · 00:18:42 / 00:42:18</p></div><div class="card"><h3>Protection</h3><p>Primary verified ✓<br>Backup verified ✓<br>SHA-256 match ✓<br>State: Protected</p></div></div><div class="card"><h3>Metadata</h3><p>Title · event date · category · tags · preservation notes</p></div>`,
    ingest:`${lead(arabic?'إدخال جديد':'New Ingest',arabic?'اختر مسار الإدخال المناسب.':'Choose an ingest path.')}<div class="grid two"><div class="card"><h3>${arabic?'التسجيل من الشريط':'Tape Capture'}</h3><p><strong>Windows-only capability.</strong> ${arabic?'استخدم تطبيق Windows للتسجيل من الأجهزة المعتمدة.':'Use the Windows application for certified hardware capture.'}</p></div><div class="card"><h3>${arabic?'رفع الملفات':'Upload Files'}</h3><p>Temporary local selection before central upload.</p><button class="action" data-go="upload">${arabic?'فتح مساحة الرفع':'Open upload workspace'}</button></div></div>`,
    upload:`${lead(arabic?'رفع الملفات':'Upload Workspace','Local selection/cache is temporary; authoritative storage remains server-side.')}<div class="card"><h3>File selection area</h3><p>Drop files here or browse · Demo</p></div><div class="card"><h3>Preflight</h3><p>Extension · size · name · path · network readiness</p></div>`,
    queue:`${lead(arabic?'قائمة المعالجة':'Processing Queue','Success, failure, retry and degraded states are explicitly represented.')}<div class="list"><div class="row"><b>JOB-9031</b><span>Proxy generation</span><span>DAA-2026-001246</span><b>Running 68%</b></div><div class="row"><b>JOB-9029</b><span>Technical metadata</span><span>DAA-2026-001244</span><b>Retry available</b></div><div class="row"><b>JOB-9028</b><span>Backup verification</span><span>DAA-2026-001243</span><b>Degraded</b></div></div>`,
    admin:`${lead(arabic?'الإدارة':'Administration','P01 shell surface; authoritative identity and permissions are delivered through the Central API boundary.')}<div class="grid three"><div class="card metric"><strong>24</strong><span>Users</span><small>DEMO</small></div><div class="card metric"><strong>6</strong><span>Roles</span><small>DEMO</small></div><div class="card metric"><strong>3</strong><span>Capture stations</span><small>DEMO</small></div></div><div class="state denied"><strong>Permission denied</strong><br>This action requires the System Administrator role.</div>`,
    settings:`${lead(arabic?'الإعدادات':'Settings','Reviewable environment settings shell; secrets are never displayed.')}<div class="grid two"><div class="card"><h3>Language & appearance</h3><p>English / العربية<br>Navy + Gold<br>LTR / RTL</p></div><div class="card"><h3>Central services</h3><p>Central API: configured by deployment<br>SQL credentials: server-only<br>Secrets: hidden</p></div></div>`
  };
  return views[route];
}

function render(){
  document.documentElement.dir=arabic?'rtl':'ltr';
  document.documentElement.lang=arabic?'ar':'en';
  title.textContent=pages[route][arabic?1:0];
  languageButton.textContent=arabic?'English':'العربية';
  nav.forEach(button=>{
    button.classList.toggle('active',button.dataset.route===route);
    const page=pages[button.dataset.route];
    if(page)button.textContent=page[arabic?1:0];
  });
  content.innerHTML=shellPage();
  content.querySelectorAll('[data-go]').forEach(button=>button.addEventListener('click',()=>{route=button.dataset.go;render();}));
  if(route==='library')void loadLiveLibrary();
}

async function loadLiveLibrary(){
  const languageAtRequest=arabic;
  try{
    const response=await fetch('/client-api/catalog/assets',{headers:{'Accept':'application/json'}});
    if(route!=='library'||languageAtRequest!==arabic)return;
    if(response.status===401||response.status===403){
      content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'حالة الصلاحيات من الخدمة المركزية.':'Authorization state from the Central API.','CENTRAL API')}${state('denied','Permission denied',arabic?'لا توجد صلاحية لقراءة الكتالوج المركزي.':'The current identity cannot read the authoritative catalog.')}`;
      return;
    }
    if(response.status===503){
      content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'حالة الاعتماد المركزي.':'Central dependency state.','CENTRAL API')}${state('degraded','Degraded',arabic?'الكتالوج المركزي غير جاهز حاليًا.':'The authoritative catalog dependency is currently unavailable.')}`;
      return;
    }
    if(!response.ok)throw new Error(`HTTP ${response.status}`);
    const assets=await response.json();
    if(route!=='library'||languageAtRequest!==arabic)return;
    if(!Array.isArray(assets)||assets.length===0){
      content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'متصل بالكتالوج المركزي.':'Connected to the authoritative Central API catalog.','CENTRAL API LIVE')}${catalogCreateCard()}${state('empty','Empty',arabic?'لا توجد أصول في الكتالوج المركزي.':'No assets are present in the authoritative catalog.')}`;
      bindCatalogCreate();
      return;
    }
    const rows=assets.slice(0,100).map(asset=>`<div class="row"><b>${esc(String(asset.id).slice(0,13))}</b><span>${esc(asset.title)}</span><span>v${esc(asset.version)} · ${esc(asset.lifecycle)}</span><b>${arabic?'مركزي':'Central'}</b></div>`).join('');
    content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'بيانات مباشرة من واجهة API المركزية.':'Live authoritative data through the Central API.','CENTRAL API LIVE')}${catalogCreateCard()}<div class="toolbar">⌕ ${arabic?'بحث ومرشحات · اتصال مركزي':'Search and filters · Central API'}</div><div class="list">${rows}</div>`;
    bindCatalogCreate();
  }catch{
    if(route!=='library'||languageAtRequest!==arabic)return;
    content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?'حالة الاتصال بالخدمة المركزية.':'Central API connection state.','CENTRAL API')}${state('error','API error',arabic?'تعذر الوصول إلى واجهة API المركزية. يمكن إعادة المحاولة.':'Central API is unreachable. Retry is available.')}`;
  }
}

function catalogCreateCard(){
  return `<div class="card"><h3>${arabic?'إضافة أصل للكتالوج':'Create catalog asset'}</h3><p>${arabic?'يتم الحفظ عبر واجهة API المركزية فقط.':'The write is sent only through the Central API.'}</p><div class="toolbar"><input id="catalogTitle" maxlength="300" placeholder="${arabic?'عنوان الأصل':'Asset title'}" aria-label="${arabic?'عنوان الأصل':'Asset title'}"/><button id="catalogCreate" class="action">${arabic?'إنشاء':'Create'}</button></div><div id="catalogWriteState" aria-live="polite"></div></div>`;
}

function bindCatalogCreate(){
  const button=document.getElementById('catalogCreate');
  const input=document.getElementById('catalogTitle');
  const writeState=document.getElementById('catalogWriteState');
  if(!button||!input||!writeState)return;
  button.addEventListener('click',async()=>{
    const assetTitle=input.value.trim();
    if(!assetTitle){writeState.innerHTML=`<div class="state error"><strong>API error</strong><br>${arabic?'العنوان مطلوب.':'Title is required.'}</div>`;return;}
    button.disabled=true;
    writeState.innerHTML=`<div class="state loading"><strong>Loading</strong><br>${arabic?'جاري الحفظ…':'Saving through the Central API…'}</div>`;
    try{
      const response=await fetch('/client-api/catalog/assets',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},body:JSON.stringify({title:assetTitle})});
      if(response.status===401||response.status===403){writeState.innerHTML=`<div class="state denied"><strong>Permission denied</strong><br>${arabic?'لا توجد صلاحية للإنشاء.':'The current identity cannot create catalog assets.'}</div>`;return;}
      if(response.status===503){writeState.innerHTML=`<div class="state degraded"><strong>Degraded</strong><br>${arabic?'الخدمة المركزية غير جاهزة.':'The authoritative service is currently degraded.'}</div>`;return;}
      if(!response.ok)throw new Error(`HTTP ${response.status}`);
      await loadLiveLibrary();
    }catch{
      writeState.innerHTML=`<div class="state error"><strong>API error</strong><br>${arabic?'فشل الحفظ عبر الخدمة المركزية.':'The Central API write failed.'}</div>`;
    }finally{button.disabled=false;}
  });
}

nav.forEach(button=>button.addEventListener('click',()=>{route=button.dataset.route;render();}));
languageButton.addEventListener('click',()=>{arabic=!arabic;render();});
fetch('/version').then(response=>response.ok?response.json():Promise.reject()).then(version=>{document.getElementById('buildIdentity').textContent=`${version.version||'0.1.0'} · ${version.environmentName||'Development'}`;}).catch(()=>{document.getElementById('buildIdentity').textContent='P02 · VERSION UNAVAILABLE';});
render();
