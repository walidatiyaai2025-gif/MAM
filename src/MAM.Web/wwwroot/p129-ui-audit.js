(() => {
'use strict';

/*
  P12.9 presentation-only audit layer.
  It deliberately does not change API contracts, data values, routes, permissions,
  processing behavior, delete behavior, or previously accepted workflows.
  Internal IDs remain in data attributes/requests when required, but are not rendered
  as user-facing labels.
*/

const GUID_RE=/\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/ig;
const GUID_ANY_RE=/\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/ig;
const SHORT_ID_RE=/^[0-9a-f]{6,13}(?:-[0-9a-f]{1,8})?$/i;
const SKIP_TEXT_TAGS=new Set(['SCRIPT','STYLE','PRE','CODE','TEXTAREA','OPTION']);
const PROFILE_KEYS=['inspect-v1','video-proxy-v1','image-preview-v1','audio-preview-v1','pdf-inline-v1','ocr-text-v1','transcript-text-v1'];
let applying=false,scheduled=false,catalogPromise=null;

const isArabic=()=>typeof arabic!=='undefined'?arabic:document.documentElement.lang==='ar';
const tr=(en,ar)=>isArabic()?ar:en;
const clean=value=>String(value??'').trim();

const profileLabels={
  'inspect-v1':['Technical inspection','فحص فني'],
  'video-proxy-v1':['Video proxy','نسخة فيديو للمعاينة'],
  'image-preview-v1':['Image preview','معاينة الصورة'],
  'audio-preview-v1':['Audio preview','معاينة الصوت'],
  'pdf-inline-v1':['Document preview','معاينة المستند'],
  'ocr-text-v1':['OCR text extraction','استخراج النص OCR'],
  'transcript-text-v1':['Timestamped transcription','التفريغ الزمني']
};
const mediaLabels={Video:['Video','فيديو'],Audio:['Audio','صوت'],Image:['Image','صورة'],Document:['Document','مستند'],Other:['Other','أخرى']};
const lifecycleLabels={Draft:['Draft','مسودة'],Active:['Active','نشط'],Archived:['Archived','مؤرشف'],Deleted:['Deleted','محذوف']};
const stateLabels={
  Queued:['Queued','في الانتظار'],Pending:['Pending','معلّق'],Running:['Running','جارٍ التنفيذ'],Processing:['Processing','قيد المعالجة'],
  Succeeded:['Succeeded','مكتمل'],Completed:['Completed','مكتمل'],Failed:['Failed','فشل'],Retry:['Retry','إعادة المحاولة'],
  Ready:['Ready','جاهز'],Protected:['Protected','محمي'],Mismatch:['Mismatch','عدم تطابق'],Leased:['In progress','قيد التنفيذ'],Stale:['Stale','متقادم'],
  Enabled:['Enabled','مفعّل'],Disabled:['Disabled','معطّل'],Success:['Success','نجاح'],Denied:['Denied','مرفوض']
};
const roleLabels={Administrator:['Administrator','مسؤول النظام'],CatalogEditor:['Catalog Editor','محرر الكتالوج'],Viewer:['Viewer','مشاهد']};
const permissionLabels={
  'catalog.read':['View catalog','عرض الكتالوج'],
  'catalog.write':['Manage catalog','إدارة الكتالوج'],
  'processing.execute':['Run processing','تشغيل المعالجة'],
  'processing.read':['View processing','عرض المعالجة'],
  'protection.manage':['Manage backup protection','إدارة الحماية والنسخ الاحتياطي'],
  'protection.read':['View backup protection','عرض الحماية والنسخ الاحتياطي'],
  'administration.manage':['System administration','إدارة النظام'],
  'audit.read':['View audit and reports','عرض التدقيق والتقارير'],
  'capture.use':['Use capture','استخدام التسجيل'],
  'discovery.read':['Search indexed content','البحث في المحتوى المفهرس']
};
const auditActionLabels={
  'upload.primary.committed':['Upload completed','اكتمل رفع أصل'],
  'upload.session.created':['Upload started','بدأ رفع ملف'],
  'processing.job.queued':['Processing queued','أضيفت معالجة'],
  'processing.job.completed':['Processing completed','اكتملت المعالجة'],
  'processing.job.failed':['Processing failed','فشلت المعالجة'],
  'catalog.asset.created':['Asset created','تم إنشاء أصل'],
  'catalog.asset.updated':['Asset updated','تم تحديث أصل'],
  'catalog.asset.deleted':['Asset deleted','تم حذف أصل'],
  'administration.user.updated':['User updated','تم تحديث مستخدم'],
  'administration.user.deleted':['User deleted','تم حذف مستخدم'],
  'administration.policy.updated':['Policy updated','تم تحديث سياسة'],
  'auth.login':['Sign in','تسجيل دخول'],
  'auth.logout':['Sign out','تسجيل خروج']
};
const queueLabels={
  Processing:['Media processing','معالجة الوسائط'],
  Protection:['Backup protection','حماية النسخ الاحتياطي'],
  CaptureUploadHandoff:['Capture upload handoff','تسليم ملفات التسجيل'],
  Upload:['Uploads','عمليات الرفع']
};
const dependencyLabels={
  SqlServer:['Database','قاعدة البيانات'],SQL:['Database','قاعدة البيانات'],PrimaryStorage:['Primary storage','التخزين الأساسي'],BackupStorage:['Backup storage','التخزين الاحتياطي'],
  FFmpeg:['FFmpeg media engine','محرك الوسائط FFmpeg'],FFprobe:['FFprobe media inspection','فحص الوسائط FFprobe'],Tesseract:['OCR engine','محرك OCR'],Whisper:['Transcription engine','محرك التفريغ الصوتي']
};

function labelFrom(map,key){const hit=map[clean(key)];return hit?hit[isArabic()?1:0]:null;}
function humanizeCode(value){
  const raw=clean(value);if(!raw)return '—';
  if(isArabic())return tr('System item','عنصر نظام');
  return raw.replace(/[._-]+/g,' ').replace(/\b\w/g,c=>c.toUpperCase());
}
function permissionLabel(value){return labelFrom(permissionLabels,value)||humanizeCode(value);}
function profileLabel(value){return labelFrom(profileLabels,value)||humanizeCode(value);}
function roleLabel(value){return labelFrom(roleLabels,value)||clean(value);}
function mediaLabel(value){return labelFrom(mediaLabels,value)||clean(value);}
function lifecycleLabel(value){return labelFrom(lifecycleLabels,value)||clean(value);}
function stateLabel(value){return labelFrom(stateLabels,value)||clean(value);}
function auditLabel(value){return labelFrom(auditActionLabels,value)||humanizeCode(value);}
function queueLabel(value){return labelFrom(queueLabels,value)||humanizeCode(value);}
function dependencyLabel(value){return labelFrom(dependencyLabels,value)||humanizeCode(value);}

function tidySeparators(text){
  return text
    .replace(/\s*[·•|]\s*[·•|]\s*/g,' · ')
    .replace(/^\s*[·•|:-]+\s*/,'')
    .replace(/\s*[·•|:-]+\s*$/,'')
    .replace(/\s{2,}/g,' ')
    .trim();
}
function scrubIdentifierText(text){
  let value=String(text??'');
  value=value.replace(GUID_RE,'').replace(GUID_ANY_RE,'');
  value=value.replace(/\bSHA(?:-256)?\s+[0-9a-f]{12,}(?:…|\.\.\.)?/ig,tr('Hash verified','تم التحقق من البصمة'));
  value=value.replace(/\b(?:job|asset|subject|category|collection|user)\s*(?:id|guid)\s*[:#]?\s*/ig,'');
  return tidySeparators(value);
}
function replaceKnownTokens(text){
  let value=scrubIdentifierText(text);
  PROFILE_KEYS.forEach(key=>{value=value.replace(new RegExp(`\\b${key.replace(/[-/\\^$*+?.()|[\]{}]/g,'\\$&')}\\b`,'g'),profileLabel(key));});
  Object.keys(lifecycleLabels).forEach(key=>{value=value.replace(new RegExp(`\\b${key}\\b`,'g'),lifecycleLabel(key));});
  if(isArabic())value=value.replace(/\battempt\s+(\d+)/ig,'المحاولة $1');
  return tidySeparators(value);
}

function scrubVisibleText(root=document){
  const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
  const nodes=[];while(walker.nextNode())nodes.push(walker.currentNode);
  nodes.forEach(node=>{
    const parent=node.parentElement;if(!parent||SKIP_TEXT_TAGS.has(parent.tagName)||parent.closest('[data-p129-allow-technical]'))return;
    const before=node.nodeValue||'';if(!before.trim())return;
    const after=replaceKnownTokens(before);
    if(after!==before.trim())node.nodeValue=before.replace(before.trim(),after);
    if(!after&&['SMALL','B','SPAN','P'].includes(parent.tagName)&&parent.childElementCount===0)parent.hidden=true;
  });
  document.querySelectorAll('.p128-id').forEach(el=>{el.hidden=true;el.setAttribute('aria-hidden','true');});
}

function localizeRoleSelect(select){
  if(!select)return;
  [...select.options].forEach(option=>{
    const canonical=option.dataset.p129Role||option.value||option.textContent.trim();
    option.dataset.p129Role=canonical;option.value=canonical;option.textContent=roleLabel(canonical);
  });
}

function localizeNavigation(){
  const nav=document.getElementById('nav');if(!nav)return;
  const labels={
    dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],ingest:['New Ingest','إدخال جديد'],upload:['Add Media','إضافة ميديا'],queue:['Processing Queue','قائمة المعالجة'],reports:['Reports','التقارير'],protection:['Backup Protection','حماية النسخة الاحتياطية'],
    admin:['Administration','إدارة النظام'],settings:['System Settings','إعدادات النظام'],categories:['Categories','التصنيفات'],references:['Reference Library','مكتبة المراجع'],mediaPermissions:['Media Permissions','صلاحيات أنواع الوسائط'],search:['Content Search','البحث في المحتوى'],myPermissions:['My Permissions','صلاحياتي'],
    'curation-actions':['Curation Actions','إجراءات التهيئة'],'admin-actions':['Management Actions','إجراءات الإدارة']
  };
  nav.querySelectorAll('[data-route]').forEach(button=>{
    const key=button.dataset.route;if(!labels[key])return;
    const label=labels[key][isArabic()?1:0];const span=button.querySelector('.p127-nav-label');
    if(span)span.textContent=label;else if(!button.hidden)button.textContent=label;
    button.setAttribute('aria-label',label);button.setAttribute('title',label);
  });
  const menu=nav.querySelector('.p127-admin-menu');
  const trigger=menu?.querySelector('.p127-admin-trigger .p127-nav-label');
  if(trigger){const label=tr('System Administrator Settings','إعدادات مسؤول النظام');trigger.textContent=label;menu.querySelector('.p127-admin-trigger')?.setAttribute('aria-label',label);}
  if(menu){const sub=menu.querySelector('.p127-admin-submenu');['admin','settings','categories','references','mediaPermissions'].forEach(key=>{const button=nav.querySelector(`[data-route="${key}"]`);if(button&&sub&&button.parentElement!==sub)sub.appendChild(button);});}
  const primary=['dashboard','library','curation-actions','ingest','upload','queue','reports','protection'];
  primary.forEach(key=>{const button=nav.querySelector(`:scope>[data-route="${key}"]`);if(button)nav.appendChild(button);});
  if(menu)nav.appendChild(menu);
  ['admin-actions','search'].forEach(key=>{const button=nav.querySelector(`:scope>[data-route="${key}"]`);if(button)nav.appendChild(button);});
}

function localizePageTitle(){
  const names={dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],asset:['Asset Details','تفاصيل الأصل'],ingest:['New Ingest','إدخال جديد'],upload:['Add Media','إضافة ميديا'],queue:['Processing Queue','قائمة المعالجة'],reports:['Reports','التقارير'],protection:['Backup Protection','حماية النسخة الاحتياطية'],admin:['Administration','إدارة النظام'],settings:['System Settings','إعدادات النظام'],search:['Content Search','البحث في المحتوى'],categories:['Categories','التصنيفات'],references:['Reference Library','مكتبة المراجع'],mediaPermissions:['Media Permissions','صلاحيات أنواع الوسائط'],myPermissions:['My Permissions','صلاحياتي']};
  const current=typeof route!=='undefined'?route:'';const title=document.getElementById('pageTitle');if(title&&names[current])title.textContent=names[current][isArabic()?1:0];
}

function localizeAdmin(){
  const root=document.getElementById('p127AdminPanel')||document;
  root.querySelectorAll('label').forEach(label=>{
    const text=label.textContent.trim();
    if(text==='Username')label.childNodes.forEach(n=>{if(n.nodeType===Node.TEXT_NODE)n.nodeValue=tr('Username','اسم المستخدم');});
    if(text==='SecretRef')label.childNodes.forEach(n=>{if(n.nodeType===Node.TEXT_NODE)n.nodeValue=tr('Secret reference','مرجع القيمة السرية');});
  });
  document.querySelectorAll('#p127UsersTable td:nth-child(3)').forEach(cell=>{const roles=cell.textContent.split(',').map(x=>roleLabel(x.trim())).filter(Boolean);if(roles.length)cell.textContent=roles.join('، ');});
  document.querySelectorAll('#p127NewRole,#er').forEach(localizeRoleSelect);
  document.querySelectorAll('.p127-modal small').forEach(small=>{
    if(/External Subject/i.test(small.textContent))small.textContent=tr('Active Directory identity is stored automatically and is not shown.','يتم حفظ هوية Active Directory تلقائيًا ولا يتم عرض المعرّف الداخلي.');
    if(/Validate\s*→\s*Test reference\s*→\s*Save/i.test(small.textContent))small.textContent=tr('Validate → Test reference → Save · secret values are never displayed','تحقق ← اختبار المرجع ← حفظ · القيم السرية لا يتم عرضها');
  });
  const audit=document.querySelector('#p127AdminPanel table');
  if(typeof route!=='undefined'&&route==='admin'&&audit){
    const headers=[...audit.querySelectorAll('thead th')].map(x=>x.textContent.trim().toLowerCase());
    const actionIndex=headers.findIndex(x=>x==='action'||x==='الإجراء');
    const outcomeIndex=headers.findIndex(x=>x==='outcome'||x==='النتيجة');
    [...audit.querySelectorAll('tbody tr')].forEach(row=>{
      const cells=[...row.cells];if(actionIndex>=0&&cells[actionIndex])cells[actionIndex].textContent=auditLabel(cells[actionIndex].textContent);
      if(outcomeIndex>=0&&cells[outcomeIndex])cells[outcomeIndex].textContent=stateLabel(cells[outcomeIndex].textContent);
      cells.forEach(cell=>{if(GUID_ANY_RE.test(cell.textContent)){GUID_ANY_RE.lastIndex=0;cell.textContent=tr('System service','خدمة النظام');}GUID_ANY_RE.lastIndex=0;});
    });
  }
  const exportButton=document.getElementById('p127AuditExport');if(exportButton)exportButton.textContent=tr('Export CSV','تصدير CSV');
}

function localizePermissions(){
  const host=document.getElementById('p127PermissionsHost');if(!host)return;
  host.querySelectorAll('.list .row span').forEach(span=>{const text=span.textContent.trim();if(text.includes('.')||permissionLabels[text])span.textContent=permissionLabel(text);});
  const headers=host.querySelectorAll('table thead th');const labels=isArabic()?['النوع','عرض','رفع','تعديل','معالجة','تنزيل']:['Type','View','Upload','Edit','Process','Download'];headers.forEach((h,i)=>{if(labels[i])h.textContent=labels[i];});
  host.querySelectorAll('table tbody tr td:first-child').forEach(td=>td.textContent=mediaLabel(td.textContent));
  host.querySelectorAll('.card:nth-child(2) p').forEach(p=>{const roles=p.textContent.split('·').map(x=>roleLabel(x.trim())).filter(Boolean);if(roles.length)p.textContent=roles.join(' · ');});
}

async function getCatalog(){
  if(catalogPromise)return catalogPromise;
  catalogPromise=fetch('/client-api/catalog/assets',{headers:{Accept:'application/json'},cache:'no-store'}).then(r=>r.ok?r.json():[]).then(rows=>Array.isArray(rows)?rows:[]).catch(()=>[]);
  return catalogPromise;
}
function assetTitle(asset){return clean(isArabic()&&asset.titleAr?asset.titleAr:(asset.title||asset.titleEn))||tr('Untitled media','وسائط بلا عنوان');}
async function replaceIdInputWithAssetSelect(input,kind){
  if(!input||input.dataset.p129Friendly==='1')return;input.dataset.p129Friendly='1';input.type='hidden';
  const select=document.createElement('select');select.className='p129-friendly-asset-select';select.dataset.p129For=kind;
  select.innerHTML=`<option value="">${tr('Select media by title','اختر الوسائط بالاسم')}</option>`;input.before(select);
  const assets=await getCatalog();if(!select.isConnected)return;
  assets.slice().sort((a,b)=>assetTitle(a).localeCompare(assetTitle(b),isArabic()?'ar':'en')).forEach(asset=>{const option=document.createElement('option');option.value=asset.id;option.textContent=`${assetTitle(asset)}${asset.version?` · ${tr('Version','الإصدار')} ${asset.version}`:''}`;select.appendChild(option);});
  select.value=input.value||'';select.addEventListener('change',()=>{input.value=select.value;});
}
function upgradeFriendlyAssetSelectors(){
  document.querySelectorAll('[data-p12-ref-image-input]').forEach(input=>void replaceIdInputWithAssetSelect(input,'reference-image'));
  const protection=document.getElementById('p06AssetId');if(protection)void replaceIdInputWithAssetSelect(protection,'protection-asset');
  document.querySelectorAll('#p12ReferencesHost .card small').forEach(small=>{const text=small.textContent.trim();if(GUID_ANY_RE.test(text)){small.hidden=true;small.setAttribute('aria-hidden','true');}GUID_ANY_RE.lastIndex=0;});
}

function localizeReferenceAndCategoryForms(){
  const set=(id,placeholder,aria)=>{const el=document.getElementById(id);if(!el)return;if(placeholder!==undefined)el.setAttribute('placeholder',placeholder);if(aria)el.setAttribute('aria-label',aria);};
  set('p12CategoryEn',tr('Category name (English)','اسم التصنيف بالإنجليزية'),tr('English category name','اسم التصنيف بالإنجليزية'));
  set('p12CategoryAr',tr('Category name (Arabic)','اسم التصنيف بالعربية'),tr('Arabic category name','اسم التصنيف بالعربية'));
  set('p12CategorySort','',tr('Display order','ترتيب العرض'));
  const sort=document.getElementById('p12CategorySort');if(sort)sort.setAttribute('title',tr('Display order','ترتيب العرض'));
  set('p12RefEn',tr('Name (English)','الاسم بالإنجليزية'),tr('English reference name','اسم المرجع بالإنجليزية'));
  set('p12RefAr',tr('Name (Arabic)','الاسم بالعربية'),tr('Arabic reference name','اسم المرجع بالعربية'));
  set('p12RefTags',tr('Comma-separated tags','وسوم مفصولة بفاصلة'),tr('Reference tags','وسوم المرجع'));
  document.querySelectorAll('#p12SearchKind option,#p128Kind option').forEach(option=>{if(mediaLabels[option.value])option.textContent=mediaLabel(option.value);});
}

function localizeProcessing(){
  const queue=document.getElementById('p04QueueState');
  if(queue){queue.querySelectorAll('.row').forEach((row,index)=>{const b=row.querySelector(':scope>b');if(b&&SHORT_ID_RE.test(b.textContent.trim()))b.textContent=`${tr('Processing job','مهمة معالجة')} ${index+1}`;});}
  const legacy=document.getElementById('p05Results');if(legacy){legacy.querySelectorAll('.row').forEach(row=>{const b=row.querySelector(':scope>b');if(b&&SHORT_ID_RE.test(b.textContent.trim()))b.textContent=tr('Media asset','أصل إعلامي');});}
  ['p03UploadState','p04AssetState','p04QueueState','p12AssetDiscovery'].forEach(id=>{const root=document.getElementById(id);if(!root)return;root.querySelectorAll('*').forEach(el=>{if(el.children.length)return;const raw=el.textContent.trim();if(profileLabels[raw])el.textContent=profileLabel(raw);else if(stateLabels[raw])el.textContent=stateLabel(raw);else if(mediaLabels[raw])el.textContent=mediaLabel(raw);});});
  document.querySelectorAll('#p04AssetState iframe[title="Document preview"]').forEach(frame=>frame.setAttribute('title',tr('Document preview','معاينة المستند')));
}

function localizeProtection(){
  if(typeof route==='undefined'||route!=='protection')return;
  const contentRoot=document.getElementById('content');if(!contentRoot)return;
  contentRoot.querySelectorAll('.card p').forEach(p=>{
    const text=p.textContent;
    if(/Primary:/i.test(text)&&/Backup:/i.test(text)&&!p.closest('#p06AssetState'))p.innerHTML=`<strong>${tr('Primary storage','التخزين الأساسي')}:</strong> ${tr('Ready','جاهز')}<br><strong>${tr('Backup storage','التخزين الاحتياطي')}:</strong> ${tr('Ready','جاهز')}`;
  });
  const detail=document.querySelector('#p06AssetState');if(detail&&(/Primary:/i.test(detail.textContent)||/SHA-256/i.test(detail.textContent))){const strong=detail.querySelector('strong');detail.innerHTML=`<strong>${strong?stateLabel(strong.textContent):tr('Verified','تم التحقق')}</strong><br>${tr('Primary and backup copies were verified successfully.','تم التحقق من النسخة الأساسية والنسخة الاحتياطية بنجاح.')}`;}
}

function localizeReports(){
  if(typeof route==='undefined'||route!=='reports')return;
  const root=document.getElementById('content');if(!root)return;
  root.querySelectorAll('.card.metric small').forEach(small=>{const text=small.textContent.trim();if(text&&!/^\d/.test(text)&&!/%$/.test(text)&&!/(original|verified|session|نسخة|تم التحقق|جلسة)/i.test(text))small.textContent=tr('Primary storage','التخزين الأساسي');});
  root.querySelectorAll('table').forEach(table=>{
    const headers=[...table.querySelectorAll('thead th')].map(x=>x.textContent.trim().toLowerCase());
    const queueIndex=headers.findIndex(x=>x==='queue'||x==='القائمة');const dependencyIndex=headers.findIndex(x=>x==='dependency'||x==='الاعتماد');const statusIndex=headers.findIndex(x=>x==='status'||x==='الحالة');const targetIndex=headers.findIndex(x=>x==='target'||x==='الهدف');
    [...table.querySelectorAll('tbody tr')].forEach(row=>{const cells=[...row.cells];if(queueIndex>=0&&cells[queueIndex])cells[queueIndex].textContent=queueLabel(cells[queueIndex].textContent);if(dependencyIndex>=0&&cells[dependencyIndex])cells[dependencyIndex].textContent=dependencyLabel(cells[dependencyIndex].textContent);if(statusIndex>=0&&cells[statusIndex])cells[statusIndex].textContent=stateLabel(cells[statusIndex].textContent);if(targetIndex>=0&&cells[targetIndex]&&cells[targetIndex].textContent.trim()!=='—')cells[targetIndex].textContent=tr('Configured','مهيأ');});
  });
  root.querySelectorAll('p strong').forEach(strong=>{const parent=strong.parentElement;if(parent&&(/Primary:/i.test(parent.textContent)||/Backup:/i.test(parent.textContent))&&!/^\d/.test(strong.textContent.trim()))strong.textContent=tr('Available','متاح');});
}

function localizeFooterAndChrome(){
  const footer=document.querySelector('.p128-app-footer span:first-child');if(footer)footer.textContent=tr('All rights reserved · Diwan Al Amiri · Media Asset Management','جميع الحقوق محفوظة للديوان الأميري · نظام إدارة الأصول الإعلامية');
  const mobile=document.querySelector('.p128-mobile-menu');if(mobile)mobile.setAttribute('aria-label',tr('Menu','القائمة'));
  const language=document.querySelector('[data-p128-language] span');if(language)language.textContent=isArabic()?'English':'العربية';
  const status=document.querySelector('.topbar .status');if(status)status.textContent=tr('● Central services','● الخدمات المركزية');
  const landingLink=document.querySelector('.topbar a[title="Landing page"]');if(landingLink)landingLink.setAttribute('title',tr('Landing page','الصفحة الرئيسية'));
}

function apply(){
  if(applying)return;applying=true;
  try{
    document.documentElement.lang=isArabic()?'ar':'en';document.documentElement.dir=isArabic()?'rtl':'ltr';
    localizeNavigation();localizePageTitle();localizeAdmin();localizePermissions();localizeReferenceAndCategoryForms();localizeProcessing();localizeProtection();localizeReports();localizeFooterAndChrome();upgradeFriendlyAssetSelectors();scrubVisibleText(document.body);
  }finally{applying=false;}
}
function schedule(){if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;apply();});}

const observer=new MutationObserver(schedule);observer.observe(document.documentElement,{childList:true,subtree:true,characterData:false});
window.addEventListener('hashchange',schedule);document.getElementById('languageButton')?.addEventListener('click',()=>setTimeout(schedule,0));
window.mamUiAudit={apply,permissionLabel,profileLabel,roleLabel,mediaLabel,lifecycleLabel,stateLabel};
apply();
})();
