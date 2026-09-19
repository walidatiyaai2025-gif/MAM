(() => {
'use strict';

pages.capabilities=['Release Capabilities','وظائف النسخة الحالية'];

const capabilityCatalog=[
  {id:'shell',area:'experience',nameEn:'Premium application shell & responsive navigation',nameAr:'واجهة النظام والتنقل المتجاوب',surface:'Web · Desktop',completion:100,status:'closed',evidence:'P01 CLOSED',route:'dashboard'},
  {id:'bilingual',area:'experience',nameEn:'Arabic RTL / English LTR experience',nameAr:'دعم العربية RTL والإنجليزية LTR',surface:'Web · Desktop',completion:100,status:'closed',evidence:'P01 / P10 acceptance'},
  {id:'identity',area:'platform',nameEn:'Central identity, API and SQL catalog',nameAr:'الهوية المركزية وواجهة API وكتالوج SQL',surface:'API · SQL · Web · Desktop',completion:100,status:'closed',evidence:'P02 CLOSED'},
  {id:'auth',area:'administration',nameEn:'Active Directory / Windows SSO authentication',nameAr:'تسجيل الدخول عبر Active Directory وWindows SSO',surface:'Web · Server',completion:100,status:'engineering',evidence:'P08 engineering closed'},
  {id:'library',area:'media',nameEn:'Media Library and Asset Details',nameAr:'مكتبة الوسائط وتفاصيل الأصل الإعلامي',surface:'Web · Desktop · API',completion:100,status:'closed',evidence:'P133 / P135 acceptance',route:'library'},
  {id:'upload',area:'ingest',nameEn:'Durable resumable media upload',nameAr:'رفع الميديا القابل للاستكمال',surface:'Web · Desktop · API',completion:100,status:'closed',evidence:'P03 CLOSED',route:'upload'},
  {id:'bulk-folder',area:'ingest',nameEn:'Bulk folder import with category creation, duplicate skip and report',nameAr:'رفع مجلدات مجمّع مع إنشاء التصنيفات وتخطي المكرر والتقرير',surface:'Web · API',completion:100,status:'closed',evidence:'Bulk Folder Import Acceptance',route:'upload'},
  {id:'processing',area:'processing',nameEn:'Media inspection, proxies, thumbnails and previews',nameAr:'فحص الميديا والبروكسي والصور المصغرة والمعاينات',surface:'Worker · API · Web · Desktop',completion:100,status:'closed',evidence:'P04 CLOSED'},
  {id:'queue',area:'processing',nameEn:'Processing queue, retry and recovery',nameAr:'قائمة المعالجة وإعادة المحاولة والاستعادة',surface:'Web · Desktop · Worker',completion:100,status:'closed',evidence:'P04 / P09 acceptance',route:'queue'},
  {id:'search',area:'discovery',nameEn:'Unified text search, filters and discovery',nameAr:'البحث النصي والفلاتر والاكتشاف',surface:'Web · Desktop · API',completion:100,status:'closed',evidence:'P05 + P12 discovery acceptance'},
  {id:'ocr',area:'discovery',nameEn:'OCR, transcript and extracted-text indexing',nameAr:'OCR والنصوص المفرغة وفهرسة النص المستخرج',surface:'Worker · API · Web · Desktop',completion:100,status:'closed',evidence:'p12-ocr / p12-discovery'},
  {id:'visual-search',area:'discovery',nameEn:'Visual search and thumbnail evidence',nameAr:'البحث المرئي ومعاينات الصور',surface:'Web · API',completion:100,status:'closed',evidence:'p12-visual-search'},
  {id:'categories',area:'curation',nameEn:'Hierarchical category management',nameAr:'إدارة التصنيفات الهرمية',surface:'Web · Desktop · API',completion:100,status:'closed',evidence:'P12 hierarchical categories / Management Pages Acceptance',route:'categories'},
  {id:'collections',area:'curation',nameEn:'Collections management and membership',nameAr:'إدارة المجموعات والأصول داخلها',surface:'Web · API',completion:100,status:'closed',evidence:'P05 / Management Pages Acceptance',route:'collections'},
  {id:'tags',area:'curation',nameEn:'Tag dictionary, rename and safe deletion',nameAr:'قاموس الوسوم وإعادة التسمية والحذف الآمن',surface:'Web · API',completion:100,status:'closed',evidence:'P05 / Management Pages Acceptance',route:'tags'},
  {id:'references',area:'curation',nameEn:'Reference library and manual searchable tags',nameAr:'مكتبة المراجع والوسوم اليدوية القابلة للبحث',surface:'Web · Desktop · API',completion:100,status:'closed',evidence:'P12 reference library'},
  {id:'rbac',area:'administration',nameEn:'Role and media-kind permissions',nameAr:'صلاحيات الأدوار وأنواع الميديا',surface:'API · Web · Desktop',completion:100,status:'closed',evidence:'P08 + P12 media-kind RBAC'},
  {id:'admin',area:'administration',nameEn:'Users, roles, settings and policy administration',nameAr:'إدارة المستخدمين والأدوار والإعدادات والسياسات',surface:'Web · Desktop · API',completion:100,status:'engineering',evidence:'P08 engineering closed',route:'admin'},
  {id:'protection',area:'protection',nameEn:'Primary/Backup protection and checksum integrity',nameAr:'حماية التخزين الأساسي والاحتياطي والتحقق من البصمة',surface:'API · Worker · Storage',completion:100,status:'closed',evidence:'P06 CLOSED'},
  {id:'reports',area:'operations',nameEn:'Reports, monitoring, diagnostics and recovery',nameAr:'التقارير والمراقبة والتشخيص والاستعادة',surface:'Web · API · Server',completion:100,status:'engineering',evidence:'P09 engineering closed',route:'reports'},
  {id:'security',area:'operations',nameEn:'Security, performance, accessibility and scale gates',nameAr:'بوابات الأمان والأداء وإمكانية الوصول والتوسع',surface:'CI · API · Web · Desktop',completion:100,status:'engineering',evidence:'P10 engineering closed'},
  {id:'packaging',area:'release',nameEn:'Desktop/Server installers, upgrade preservation and UAT packaging',nameAr:'مثبتات الديسكتوب والسيرفر وحفظ البيانات أثناء الترقية وحزم UAT',surface:'Setup · Server · Desktop',completion:100,status:'engineering',evidence:'P11 + p12-setup acceptance'},
  {id:'offline-demo',area:'release',nameEn:'Offline Demo setup and clean-install runtime',nameAr:'نسخة Offline Demo واختبار التثبيت النظيف',surface:'Setup · Web',completion:100,status:'closed',evidence:'p12-setup acceptance'},
  {id:'version',area:'release',nameEn:'Version, commit SHA, build and environment identity',nameAr:'هوية الإصدار وCommit SHA ورقم البناء والبيئة',surface:'Web · Server · Setup',completion:100,status:'closed',evidence:'/version + build manifests'},
  {id:'tape-inventory',area:'phase2',nameEn:'Phase Two tape inventory foundation',nameAr:'أساس فهرس الشرائط للمرحلة الثانية',surface:'Web · Desktop · API · SQL',completion:100,status:'active',evidence:'T21 Tape Inventory Acceptance · tracker ACTIVE',href:'/tape-inventory.html'}
];

const ownerLastTotal=14;
const areaLabels={
  experience:['Experience','تجربة الاستخدام'],
  platform:['Core Platform','المنصة الأساسية'],
  media:['Media Library','مكتبة الوسائط'],
  ingest:['Ingest','الإدخال والرفع'],
  processing:['Processing','المعالجة'],
  discovery:['Discovery','البحث والاكتشاف'],
  curation:['Curation','التصنيف والتنظيم'],
  protection:['Protection','الحماية والنسخ الاحتياطي'],
  administration:['Administration','الإدارة والصلاحيات'],
  operations:['Operations','التشغيل والجودة'],
  release:['Release','الإصدار والتثبيت'],
  phase2:['Phase Two','المرحلة الثانية']
};
const statusLabels={
  closed:['Closed','مغلق'],
  engineering:['Engineering complete','مكتمل هندسيًا'],
  active:['Accepted implementation · tracker active','التنفيذ مقبول · التتبع ما زال نشطًا']
};
let capabilityVersion=null;
let capabilityQuery='';
let capabilityArea='all';
let capabilityStatus='all';

function capText(en,ar){return arabic?ar:en;}
function capEscape(value){return typeof esc==='function'?esc(value):String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));}

function ensureCapabilityNav(){
  const navHost=document.getElementById('nav');
  if(!navHost||navHost.querySelector('[data-route="capabilities"]'))return;
  const button=document.createElement('button');
  button.type='button';
  button.dataset.route='capabilities';
  button.textContent=capText('Release Capabilities','وظائف النسخة الحالية');
  button.addEventListener('click',()=>{
    if(window.mamNavigationRuntime?.activateRoute){window.mamNavigationRuntime.activateRoute('capabilities');return;}
    route='capabilities';render();
  });
  const adminMenu=navHost.querySelector('.p127-admin-menu');
  if(adminMenu)navHost.insertBefore(button,adminMenu);
  else navHost.appendChild(button);
  if(Array.isArray(nav))nav.push(button);
}

function injectCapabilityStyle(){
  if(document.getElementById('p139CapabilityStyle'))return;
  const style=document.createElement('style');
  style.id='p139CapabilityStyle';
  style.textContent=`
    .p139-shell{display:grid;gap:16px}
    .p139-hero{border:1px solid #e4e7ec;border-radius:18px;background:linear-gradient(135deg,#071f38 0%,#0c355b 68%,#87651d 140%);color:#fff;padding:22px;box-shadow:0 12px 32px rgba(7,31,56,.12)}
    .p139-hero-top{display:flex;justify-content:space-between;gap:20px;align-items:flex-start}.p139-hero h2{margin:0 0 7px;font-size:22px}.p139-hero p{margin:0;color:#d5deea;line-height:1.7}
    .p139-version{min-width:230px;border:1px solid rgba(255,255,255,.18);border-radius:13px;background:rgba(255,255,255,.08);padding:12px 14px;font-size:12px;line-height:1.8}.p139-version b{color:#f6d77b}.p139-mono{font-family:Consolas,monospace;word-break:break-all}
    .p139-summary{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px}.p139-metric{border:1px solid #e4e7ec;border-radius:14px;background:#fff;padding:15px;box-shadow:0 5px 18px rgba(16,24,40,.04)}.p139-metric strong{display:block;font-size:24px;color:#0a2342}.p139-metric span{display:block;margin-top:4px;color:#667085;font-size:12px}
    .p139-tools{display:grid;grid-template-columns:minmax(220px,1fr) 220px 220px;gap:10px}.p139-tools input,.p139-tools select{width:100%;min-height:42px;border:1px solid #d0d5dd;border-radius:10px;background:#fff;padding:9px 11px;color:#101828;font:inherit}
    .p139-table-card{border:1px solid #e4e7ec;border-radius:16px;background:#fff;overflow:hidden;box-shadow:0 7px 22px rgba(16,24,40,.04)}.p139-table-wrap{overflow:auto}.p139-table{width:100%;border-collapse:collapse;min-width:1060px}.p139-table th,.p139-table td{padding:13px 14px;border-bottom:1px solid #eef0f3;text-align:start;vertical-align:middle}.p139-table th{background:#f8fafc;color:#475467;font-size:11px;letter-spacing:.02em}.p139-table tbody tr:hover{background:#fffdf7}
    .p139-name{min-width:260px}.p139-name strong{display:block;color:#101828}.p139-name small{display:block;color:#667085;margin-top:4px}.p139-chip{display:inline-flex;align-items:center;gap:5px;border-radius:999px;padding:5px 8px;font-size:11px;font-weight:700;white-space:nowrap}.p139-chip.yes{background:#ecfdf3;color:#027a48}.p139-chip.closed{background:#ecfdf3;color:#027a48}.p139-chip.engineering{background:#eff8ff;color:#175cd3}.p139-chip.active{background:#fff7e6;color:#8a6116}
    .p139-progress-cell{min-width:150px}.p139-progress-line{display:flex;align-items:center;gap:8px}.p139-progress{height:8px;flex:1;background:#eef2f6;border-radius:999px;overflow:hidden}.p139-progress span{display:block;height:100%;background:linear-gradient(90deg,#0f6b54,#b58a2a);border-radius:inherit}.p139-progress-line b{font-size:12px;color:#344054;min-width:38px;text-align:end}
    .p139-evidence{max-width:290px;color:#667085;font-size:11px;line-height:1.55}.p139-open{border:1px solid #d0d5dd;background:#fff;color:#0a2342;border-radius:8px;padding:7px 10px;cursor:pointer;font-weight:700;font-size:11px}.p139-open:hover{border-color:#b58a2a;background:#fffaf0}
    .p139-note{border:1px solid #f4d58d;border-radius:12px;background:#fffbeb;color:#694b13;padding:12px 14px;font-size:12px;line-height:1.7}.p139-empty{padding:36px;text-align:center;color:#667085}
    @media(max-width:980px){.p139-summary{grid-template-columns:repeat(2,minmax(0,1fr))}.p139-tools{grid-template-columns:1fr}.p139-hero-top{flex-direction:column}.p139-version{min-width:0;width:100%}}
    @media(max-width:560px){.p139-summary{grid-template-columns:1fr}.p139-hero{padding:17px}}
  `;
  document.head.appendChild(style);
}

function filteredCapabilities(){
  const q=capabilityQuery.trim().toLowerCase();
  return capabilityCatalog.filter(item=>{
    if(capabilityArea!=='all'&&item.area!==capabilityArea)return false;
    if(capabilityStatus!=='all'&&item.status!==capabilityStatus)return false;
    if(!q)return true;
    const bag=[item.nameEn,item.nameAr,item.surface,item.evidence,areaLabels[item.area]?.join(' ')].join(' ').toLowerCase();
    return bag.includes(q);
  });
}

function completionAverage(items=capabilityCatalog){
  if(!items.length)return 0;
  return Math.round(items.reduce((sum,item)=>sum+Number(item.completion||0),0)/items.length);
}

function capabilityMarkup(){
  const items=filteredCapabilities();
  const complete=capabilityCatalog.filter(x=>x.completion===100).length;
  const overall=completionAverage();
  const version=capabilityVersion||{};
  const shortSha=String(version.commitSha||'').slice(0,12)||capText('loading…','جارٍ التحميل…');
  const versionText=version.version||'—';
  const buildText=version.buildNumber||'—';
  const environment=version.environmentName||'—';
  const areaOptions=Object.entries(areaLabels).map(([key,label])=>`<option value="${key}" ${capabilityArea===key?'selected':''}>${capEscape(label[arabic?1:0])}</option>`).join('');
  const statusOptions=Object.entries(statusLabels).map(([key,label])=>`<option value="${key}" ${capabilityStatus===key?'selected':''}>${capEscape(label[arabic?1:0])}</option>`).join('');
  const rows=items.map(item=>{
    const status=statusLabels[item.status]||[item.status,item.status];
    const area=areaLabels[item.area]||[item.area,item.area];
    const action=item.route
      ? `<button class="p139-open" type="button" data-cap-route="${capEscape(item.route)}">${capEscape(capText('Open','فتح'))}</button>`
      : item.href
        ? `<a class="p139-open" href="${capEscape(item.href)}">${capEscape(capText('Open','فتح'))}</a>`
        : '—';
    return `<tr>
      <td class="p139-name"><strong>${capEscape(arabic?item.nameAr:item.nameEn)}</strong><small>${capEscape(item.surface)}</small></td>
      <td>${capEscape(area[arabic?1:0])}</td>
      <td><span class="p139-chip yes">✓ ${capEscape(capText('Present','موجودة'))}</span></td>
      <td class="p139-progress-cell"><div class="p139-progress-line"><div class="p139-progress"><span style="width:${Math.max(0,Math.min(100,item.completion))}%"></span></div><b>${item.completion}%</b></div></td>
      <td><span class="p139-chip ${capEscape(item.status)}">${capEscape(status[arabic?1:0])}</span></td>
      <td class="p139-evidence">${capEscape(item.evidence)}</td>
      <td>${action}</td>
    </tr>`;
  }).join('');

  return `<div class="p139-shell">
    <section class="p139-hero">
      <div class="p139-hero-top">
        <div>
          <h2>${capEscape(capText('Current Release Capability Matrix','مصفوفة وظائف النسخة الحالية'))}</h2>
          <p>${capEscape(capText('A version-bound inventory of the capabilities actually shipped in this build, with engineering completion and acceptance evidence.','حصر مرتبط بالإصدار للوظائف الموجودة فعليًا في هذا البناء، مع نسبة الاكتمال الهندسي ودليل القبول.'))}</p>
        </div>
        <div class="p139-version">
          <div><b>${capEscape(capText('Version','الإصدار'))}:</b> ${capEscape(versionText)}</div>
          <div><b>SHA:</b> <span class="p139-mono">${capEscape(shortSha)}</span></div>
          <div><b>${capEscape(capText('Build','البناء'))}:</b> ${capEscape(buildText)}</div>
          <div><b>${capEscape(capText('Environment','البيئة'))}:</b> ${capEscape(environment)}</div>
        </div>
      </div>
    </section>
    <section class="p139-summary">
      <div class="p139-metric"><strong>${capabilityCatalog.length}</strong><span>${capEscape(capText('Capabilities in this release','وظائف في النسخة الحالية'))}</span></div>
      <div class="p139-metric"><strong>${capabilityCatalog.length}</strong><span>${capEscape(capText('Present in the build','موجودة في البناء'))}</span></div>
      <div class="p139-metric"><strong>${complete}</strong><span>${capEscape(capText('100% engineering-complete','مكتملة هندسيًا 100%'))}</span></div>
      <div class="p139-metric"><strong>${overall}%</strong><span>${capEscape(capText('Average engineering completion','متوسط الاكتمال الهندسي'))}</span></div>
    </section>
    <div class="p139-note"><strong>${capEscape(capText('Production evidence is separate:','إثباتات الإنتاج منفصلة:'))}</strong> ${capEscape(capText(`${ownerLastTotal} owner/site evidence items remain OWNER_LAST in the repository tracker and are not subtracted from software engineering completion.`,`يوجد ${ownerLastTotal} بند إثبات خاص بالموقع/المالك بحالة OWNER_LAST في سجل المشروع، ولا يتم خصمها من اكتمال البرمجيات الهندسي.`))}</div>
    <section class="p139-tools">
      <input id="p139Search" type="search" value="${capEscape(capabilityQuery)}" placeholder="${capEscape(capText('Search capabilities, surfaces or evidence…','ابحث في الوظائف أو الأسطح أو أدلة القبول…'))}">
      <select id="p139Area"><option value="all">${capEscape(capText('All areas','كل المجالات'))}</option>${areaOptions}</select>
      <select id="p139Status"><option value="all">${capEscape(capText('All statuses','كل الحالات'))}</option>${statusOptions}</select>
    </section>
    <section class="p139-table-card">
      <div class="p139-table-wrap">
        <table class="p139-table">
          <thead><tr>
            <th>${capEscape(capText('Capability','الوظيفة'))}</th>
            <th>${capEscape(capText('Area','المجال'))}</th>
            <th>${capEscape(capText('Exists','الوجود'))}</th>
            <th>${capEscape(capText('Completion','الاكتمال'))}</th>
            <th>${capEscape(capText('Status','الحالة'))}</th>
            <th>${capEscape(capText('Acceptance evidence','دليل القبول'))}</th>
            <th>${capEscape(capText('Action','فتح'))}</th>
          </tr></thead>
          <tbody>${rows}</tbody>
        </table>
        ${items.length?'':`<div class="p139-empty">${capEscape(capText('No capabilities match the current filters.','لا توجد وظائف مطابقة للفلاتر الحالية.'))}</div>`}
      </div>
    </section>
  </div>`;
}

function bindCapabilityPage(){
  const search=document.getElementById('p139Search');
  const area=document.getElementById('p139Area');
  const status=document.getElementById('p139Status');
  search?.addEventListener('input',()=>{capabilityQuery=search.value;refreshCapabilityBody();});
  area?.addEventListener('change',()=>{capabilityArea=area.value;refreshCapabilityBody();});
  status?.addEventListener('change',()=>{capabilityStatus=status.value;refreshCapabilityBody();});
  document.querySelectorAll('[data-cap-route]').forEach(button=>button.addEventListener('click',()=>{
    const key=button.dataset.capRoute;
    if(window.mamNavigationRuntime?.activateRoute){window.mamNavigationRuntime.activateRoute(key);return;}
    if(pages[key]){route=key;render();}
  }));
}

function refreshCapabilityBody(){
  if(route!=='capabilities')return;
  const host=document.getElementById('p139CapabilityHost');
  if(!host)return;
  host.innerHTML=capabilityMarkup();
  bindCapabilityPage();
}

async function loadCapabilityVersion(){
  try{
    const response=await fetch('/version',{cache:'no-store',headers:{Accept:'application/json'}});
    if(response.ok)capabilityVersion=await response.json();
  }catch{}
  refreshCapabilityBody();
}

ensureCapabilityNav();
injectCapabilityStyle();

const previousShellPage=shellPage;
shellPage=function(){
  if(route==='capabilities'){
    return lead(
      capText('Release Capabilities','وظائف النسخة الحالية'),
      capText('Review what is shipped, whether it exists in this release, and its engineering completion percentage.','راجع الوظائف الموجودة في هذه النسخة، وحالة وجود كل وظيفة، ونسبة اكتمالها الهندسي.'),
      capText('VERSION CAPABILITY MATRIX','مصفوفة وظائف الإصدار')
    )+'<div id="p139CapabilityHost">'+capabilityMarkup()+'</div>';
  }
  return previousShellPage();
};

const previousRenderCapabilities=render;
render=function(){
  previousRenderCapabilities();
  if(route==='capabilities'){
    ensureCapabilityNav();
    refreshCapabilityBody();
    void loadCapabilityVersion();
  }
};

window.mamCapabilityMatrix=Object.freeze({
  version:'p139.1',
  count:capabilityCatalog.length,
  ownerLastEvidenceCount:ownerLastTotal,
  items:capabilityCatalog.map(item=>({...item}))
});
})();
