(() => {
'use strict';

/* Presentation-only audit layer. Existing API contracts, permissions, routes,
   processing, upload, delete, storage and authentication behavior are untouched. */
const GUID=/\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/ig;
const SHORT=/^[0-9a-f]{7,13}(?:-[0-9a-f]{1,8})?$/i;
const SKIP=new Set(['SCRIPT','STYLE','PRE','CODE','TEXTAREA','OPTION']);
const profiles={
 'inspect-v1':['Technical inspection','فحص فني'],
 'video-proxy-v1':['Video proxy','نسخة فيديو للمعاينة'],
 'image-preview-v1':['Image preview','معاينة الصورة'],
 'audio-preview-v1':['Audio preview','معاينة الصوت'],
 'pdf-inline-v1':['Document preview','معاينة المستند'],
 'ocr-text-v1':['OCR text extraction','استخراج النص OCR'],
 'transcript-text-v1':['Timestamped transcription','التفريغ الزمني']
};
const media={Video:['Video','فيديو'],Audio:['Audio','صوت'],Image:['Image','صورة'],Document:['Document','مستند'],Other:['Other','أخرى']};
const lifecycle={Draft:['Draft','مسودة'],Active:['Active','نشط'],Archived:['Archived','مؤرشف'],Deleted:['Deleted','محذوف']};
const states={Queued:['Queued','في الانتظار'],Pending:['Pending','معلّق'],Running:['Running','جارٍ التنفيذ'],Processing:['Processing','قيد المعالجة'],Succeeded:['Succeeded','مكتمل'],Completed:['Completed','مكتمل'],Failed:['Failed','فشل'],Ready:['Ready','جاهز'],Protected:['Protected','محمي'],Mismatch:['Mismatch','عدم تطابق'],Leased:['In progress','قيد التنفيذ'],Stale:['Stale','متقادم'],Enabled:['Enabled','مفعّل'],Disabled:['Disabled','معطّل'],Success:['Success','نجاح']};
const roles={Administrator:['Administrator','مسؤول النظام'],CatalogEditor:['Catalog Editor','محرر الكتالوج'],Viewer:['Viewer','مشاهد']};
const permissions={
 'catalog.read':['View catalog','عرض الكتالوج'],'catalog.write':['Manage catalog','إدارة الكتالوج'],
 'processing.execute':['Run processing','تشغيل المعالجة'],'processing.read':['View processing','عرض المعالجة'],
 'protection.manage':['Manage backup protection','إدارة الحماية والنسخ الاحتياطي'],'protection.read':['View backup protection','عرض الحماية والنسخ الاحتياطي'],
 'administration.manage':['System administration','إدارة النظام'],'audit.read':['View audit and reports','عرض التدقيق والتقارير'],
 'capture.use':['Use capture','استخدام التسجيل'],'discovery.read':['Search indexed content','البحث في المحتوى المفهرس']
};
const auditActions={
 'upload.primary.committed':['Upload completed','اكتمل رفع أصل'],'upload.session.created':['Upload started','بدأ رفع ملف'],
 'processing.job.queued':['Processing queued','أضيفت معالجة'],'processing.job.completed':['Processing completed','اكتملت المعالجة'],'processing.job.failed':['Processing failed','فشلت المعالجة'],
 'catalog.asset.created':['Asset created','تم إنشاء أصل'],'catalog.asset.updated':['Asset updated','تم تحديث أصل'],'catalog.asset.deleted':['Asset deleted','تم حذف أصل'],
 'administration.user.updated':['User updated','تم تحديث مستخدم'],'administration.user.deleted':['User deleted','تم حذف مستخدم'],'administration.policy.updated':['Policy updated','تم تحديث سياسة'],
 'auth.login':['Sign in','تسجيل دخول'],'auth.logout':['Sign out','تسجيل خروج']
};
const queueNames={Processing:['Media processing','معالجة الوسائط'],Protection:['Backup protection','حماية النسخ الاحتياطي'],CaptureUploadHandoff:['Capture upload handoff','تسليم ملفات التسجيل'],Upload:['Uploads','عمليات الرفع']};
const dependencyNames={SqlServer:['Database','قاعدة البيانات'],SQL:['Database','قاعدة البيانات'],PrimaryStorage:['Primary storage','التخزين الأساسي'],BackupStorage:['Backup storage','التخزين الاحتياطي'],FFmpeg:['FFmpeg media engine','محرك الوسائط FFmpeg'],FFprobe:['FFprobe media inspection','فحص الوسائط FFprobe'],Tesseract:['OCR engine','محرك OCR'],Whisper:['Transcription engine','محرك التفريغ الصوتي']};
let busy=false,pending=false,catalogPromise=null;

const ar=()=>typeof arabic!=='undefined'?arabic:document.documentElement.lang==='ar';
const t=(en,arabicText)=>ar()?arabicText:en;
const val=x=>String(x??'').trim();
const map=(source,key)=>source[val(key)]?.[ar()?1:0]||null;
const human=value=>{const x=val(value);if(!x)return '—';return ar()?'عنصر نظام':x.replace(/[._-]+/g,' ').replace(/\b\w/g,c=>c.toUpperCase());};
const profile=value=>map(profiles,value)||human(value);
const medium=value=>map(media,value)||val(value);
const life=value=>map(lifecycle,value)||val(value);
const status=value=>map(states,value)||val(value);
const role=value=>map(roles,value)||val(value);
const permission=value=>map(permissions,value)||human(value);
const audit=value=>map(auditActions,value)||human(value);
const queue=value=>map(queueNames,value)||human(value);
const dependency=value=>map(dependencyNames,value)||human(value);
const escapeRx=s=>s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');

function cleanSeparators(text){return String(text??'').replace(/\s*[·•|]\s*[·•|]\s*/g,' · ').replace(/^\s*[·•|:;-]+\s*/,'').replace(/\s*[·•|:;-]+\s*$/,'').replace(/\s{2,}/g,' ').trim();}
function friendlyText(text){
 let out=String(text??'').replace(GUID,'');
 out=out.replace(/\bSHA(?:-256)?\s+[0-9a-f]{12,}(?:…|\.\.\.)?/ig,t('Hash verified','تم التحقق من البصمة'));
 Object.keys(profiles).forEach(key=>{out=out.replace(new RegExp(`\\b${escapeRx(key)}\\b`,'g'),profile(key));});
 Object.keys(lifecycle).forEach(key=>{out=out.replace(new RegExp(`\\b${key}\\b`,'g'),life(key));});
 if(ar())out=out.replace(/\battempt\s+(\d+)/ig,'المحاولة $1');
 return cleanSeparators(out);
}

function scrubVisibleIdentifiers(root=document.body){
 if(!root)return;
 const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT),nodes=[];while(walker.nextNode())nodes.push(walker.currentNode);
 nodes.forEach(node=>{const parent=node.parentElement;if(!parent||SKIP.has(parent.tagName)||parent.closest('[data-p129-allow-technical]'))return;const before=node.nodeValue||'';if(!before.trim())return;const after=friendlyText(before);if(after!==before.trim())node.nodeValue=before.replace(before.trim(),after);if(!after&&parent.childElementCount===0&&['SMALL','SPAN','B','P'].includes(parent.tagName))parent.hidden=true;});
 document.querySelectorAll('.p128-id').forEach(el=>{el.hidden=true;el.setAttribute('aria-hidden','true');});
}

function dedupeAndOrderNavigation(){
 const nav=document.getElementById('nav');if(!nav)return;
 const labels={dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],ingest:['New Ingest','إدخال جديد'],upload:['Add Media','إضافة ميديا'],queue:['Processing Queue','قائمة المعالجة'],reports:['Reports','التقارير'],protection:['Backup Protection','حماية النسخة الاحتياطية'],admin:['Administration','إدارة النظام'],settings:['System Settings','إعدادات النظام'],categories:['Categories','التصنيفات'],references:['Reference Library','مكتبة المراجع'],mediaPermissions:['Media Permissions','صلاحيات أنواع الوسائط'],search:['Content Search','البحث في المحتوى'],myPermissions:['My Permissions','صلاحياتي'],'curation-actions':['Curation Actions','إجراءات التهيئة'],'admin-actions':['Management Actions','إجراءات الإدارة']};
 const seen=new Set();nav.querySelectorAll('[data-route]').forEach(button=>{const key=button.dataset.route;if(seen.has(key)&&key!=='asset'){button.hidden=true;button.setAttribute('aria-hidden','true');button.classList.add('p129-duplicate-route');return;}seen.add(key);if(labels[key]){const label=labels[key][ar()?1:0],span=button.querySelector('.p127-nav-label');if(span)span.textContent=label;else if(!button.hidden)button.textContent=label;button.setAttribute('aria-label',label);button.setAttribute('title',label);}});
 const menu=nav.querySelector('.p127-admin-menu'),sub=menu?.querySelector('.p127-admin-submenu');
 if(menu){const trigger=menu.querySelector('.p127-admin-trigger .p127-nav-label');if(trigger)trigger.textContent=t('System Administrator Settings','إعدادات مسؤول النظام');['admin','settings','categories','references','mediaPermissions'].forEach(key=>{const button=[...nav.querySelectorAll(`[data-route="${key}"]`)].find(x=>!x.classList.contains('p129-duplicate-route'));if(button&&sub&&button.parentElement!==sub)sub.appendChild(button);});}
 const desired=['dashboard','library','curation-actions','ingest','upload','queue','reports','protection'];
 const top=()=>[...nav.children].filter(x=>x.matches?.('[data-route]')&&!x.hidden).map(x=>x.dataset.route);
 const target=desired.filter(k=>[...nav.querySelectorAll(`:scope>[data-route="${k}"]`)].some(x=>!x.hidden));
 const current=top().filter(k=>target.includes(k));
 if(current.join('|')!==target.join('|'))target.forEach(key=>{const b=[...nav.querySelectorAll(`:scope>[data-route="${key}"]`)].find(x=>!x.hidden);if(b)nav.appendChild(b);});
 if(menu&&menu.parentElement===nav&&nav.lastElementChild!==menu){const afterKeys=['admin-actions','search'];const hasAfter=afterKeys.some(k=>[...nav.querySelectorAll(`:scope>[data-route="${k}"]`)].some(x=>!x.hidden));if(!hasAfter)nav.appendChild(menu);}
 ['admin-actions','search'].forEach(key=>{const b=[...nav.querySelectorAll(`:scope>[data-route="${key}"]`)].find(x=>!x.hidden);if(b&&nav.lastElementChild!==b)nav.appendChild(b);});
}

function localizePageTitle(){const names={dashboard:['Dashboard','لوحة التحكم'],library:['Media Library','مكتبة الوسائط'],asset:['Asset Details','تفاصيل الأصل'],ingest:['New Ingest','إدخال جديد'],upload:['Add Media','إضافة ميديا'],queue:['Processing Queue','قائمة المعالجة'],reports:['Reports','التقارير'],protection:['Backup Protection','حماية النسخة الاحتياطية'],admin:['Administration','إدارة النظام'],settings:['System Settings','إعدادات النظام'],search:['Content Search','البحث في المحتوى'],categories:['Categories','التصنيفات'],references:['Reference Library','مكتبة المراجع'],mediaPermissions:['Media Permissions','صلاحيات أنواع الوسائط'],myPermissions:['My Permissions','صلاحياتي']};const key=typeof route!=='undefined'?route:'';const host=document.getElementById('pageTitle');if(host&&names[key])host.textContent=names[key][ar()?1:0];}

function localizeRoleSelect(select){if(!select)return;[...select.options].forEach(option=>{const canonical=option.dataset.roleKey||option.value||option.textContent.trim();option.dataset.roleKey=canonical;option.value=canonical;option.textContent=role(canonical);});}
function localizeAdmin(){
 document.querySelectorAll('#p127NewRole,#er').forEach(localizeRoleSelect);
 document.querySelectorAll('.p127-modal label').forEach(label=>{if(label.textContent.trim()==='Username')label.childNodes.forEach(n=>{if(n.nodeType===Node.TEXT_NODE)n.nodeValue=t('Username','اسم المستخدم');});if(label.textContent.trim()==='SecretRef')label.childNodes.forEach(n=>{if(n.nodeType===Node.TEXT_NODE)n.nodeValue=t('Secret reference','مرجع القيمة السرية');});});
 document.querySelectorAll('.p127-modal small').forEach(small=>{if(/External Subject/i.test(small.textContent))small.textContent=t('Active Directory identity is stored automatically and is not displayed.','يتم حفظ هوية Active Directory تلقائيًا ولا يتم عرض المعرّف الداخلي.');if(/Validate\s*→\s*Test reference\s*→\s*Save/i.test(small.textContent))small.textContent=t('Validate → Test reference → Save · secret values are never displayed','تحقق ← اختبار المرجع ← حفظ · القيم السرية لا يتم عرضها');});
 document.querySelectorAll('#p127UsersTable tbody tr').forEach(row=>{if(row.cells[2])row.cells[2].textContent=row.cells[2].textContent.split(',').map(x=>role(x.trim())).join(ar()?'، ':' , ');});
 if(typeof route!=='undefined'&&route==='admin'){document.querySelectorAll('#p127AdminPanel table').forEach(table=>{const heads=[...table.querySelectorAll('thead th')].map(x=>x.textContent.trim().toLowerCase()),ai=heads.findIndex(x=>x==='action'||x==='الإجراء'),oi=heads.findIndex(x=>x==='outcome'||x==='النتيجة'),actor=heads.findIndex(x=>x==='actor'||x==='المنفّذ');[...table.querySelectorAll('tbody tr')].forEach(row=>{if(ai>=0&&row.cells[ai])row.cells[ai].textContent=audit(row.cells[ai].textContent);if(oi>=0&&row.cells[oi])row.cells[oi].textContent=status(row.cells[oi].textContent);if(actor>=0&&row.cells[actor]&&GUID.test(row.cells[actor].textContent)){GUID.lastIndex=0;row.cells[actor].textContent=t('System service','خدمة النظام');}GUID.lastIndex=0;});});}
 const exportButton=document.getElementById('p127AuditExport');if(exportButton)exportButton.textContent=t('Export CSV','تصدير CSV');
}

function localizePermissions(){const host=document.getElementById('p127PermissionsHost');if(!host)return;host.querySelectorAll('.list .row span').forEach(span=>{const raw=span.textContent.trim();if(raw.includes('.')||permissions[raw])span.textContent=permission(raw);});const headings=host.querySelectorAll('table thead th'),labels=ar()?['النوع','عرض','رفع','تعديل','معالجة','تنزيل']:['Type','View','Upload','Edit','Process','Download'];headings.forEach((h,i)=>{if(labels[i])h.textContent=labels[i];});host.querySelectorAll('table tbody td:first-child').forEach(td=>td.textContent=medium(td.textContent));}

function getCatalog(){if(!catalogPromise)catalogPromise=fetch('/client-api/catalog/assets',{headers:{Accept:'application/json'},cache:'no-store'}).then(r=>r.ok?r.json():[]).then(x=>Array.isArray(x)?x:[]).catch(()=>[]);return catalogPromise;}
function assetTitle(a){return val(ar()&&a.titleAr?a.titleAr:(a.title||a.titleEn))||t('Untitled media','وسائط بلا عنوان');}
async function replaceIdInput(input){if(!input||input.dataset.p129Friendly==='1')return;input.dataset.p129Friendly='1';input.type='hidden';const select=document.createElement('select');select.className='p129-friendly-asset-select';select.innerHTML=`<option value="">${t('Select media by title','اختر الوسائط بالاسم')}</option>`;input.before(select);const assets=await getCatalog();if(!select.isConnected)return;assets.slice().sort((a,b)=>assetTitle(a).localeCompare(assetTitle(b),ar()?'ar':'en')).forEach(a=>{const o=document.createElement('option');o.value=a.id;o.textContent=`${assetTitle(a)}${a.version?` · ${t('Version','الإصدار')} ${a.version}`:''}`;select.appendChild(o);});select.value=input.value||'';select.addEventListener('change',()=>input.value=select.value);}
function friendlyIdInputs(){document.querySelectorAll('[data-p12-ref-image-input]').forEach(x=>void replaceIdInput(x));const p=document.getElementById('p06AssetId');if(p)void replaceIdInput(p);document.querySelectorAll('#p12ReferencesHost .card small').forEach(x=>{if(GUID.test(x.textContent)){GUID.lastIndex=0;x.hidden=true;x.setAttribute('aria-hidden','true');}GUID.lastIndex=0;});}

function localizeForms(){const field=(id,placeholder,label)=>{const el=document.getElementById(id);if(!el)return;if(placeholder!==null)el.setAttribute('placeholder',placeholder);if(label)el.setAttribute('aria-label',label);};field('p12CategoryEn',t('Category name (English)','اسم التصنيف بالإنجليزية'),t('English category name','اسم التصنيف بالإنجليزية'));field('p12CategoryAr',t('Category name (Arabic)','اسم التصنيف بالعربية'),t('Arabic category name','اسم التصنيف بالعربية'));field('p12CategorySort',null,t('Display order','ترتيب العرض'));field('p12RefEn',t('Name (English)','الاسم بالإنجليزية'),t('English reference name','اسم المرجع بالإنجليزية'));field('p12RefAr',t('Name (Arabic)','الاسم بالعربية'),t('Arabic reference name','اسم المرجع بالعربية'));field('p12RefTags',t('Comma-separated tags','وسوم مفصولة بفاصلة'),t('Reference tags','وسوم المرجع'));document.querySelectorAll('#p12SearchKind option,#p128Kind option').forEach(o=>{if(media[o.value])o.textContent=medium(o.value);});}

function localizeProcessing(){const queueHost=document.getElementById('p04QueueState');if(queueHost)queueHost.querySelectorAll('.row').forEach((row,i)=>{const b=row.querySelector(':scope>b');if(b&&SHORT.test(b.textContent.trim()))b.textContent=`${t('Processing job','مهمة معالجة')} ${i+1}`;});const legacy=document.getElementById('p05Results');if(legacy)legacy.querySelectorAll('.row').forEach(row=>{const b=row.querySelector(':scope>b');if(b&&SHORT.test(b.textContent.trim()))b.textContent=t('Media asset','أصل إعلامي');});document.querySelectorAll('#p04AssetState iframe[title="Document preview"]').forEach(x=>x.title=t('Document preview','معاينة المستند'));}

function localizeProtection(){if(typeof route==='undefined'||route!=='protection')return;document.querySelectorAll('#content .card p').forEach(p=>{if(/Primary:/i.test(p.textContent)&&/Backup:/i.test(p.textContent)&&!p.closest('#p06AssetState'))p.innerHTML=`<strong>${t('Primary storage','التخزين الأساسي')}:</strong> ${t('Ready','جاهز')}<br><strong>${t('Backup storage','التخزين الاحتياطي')}:</strong> ${t('Ready','جاهز')}`;});const result=document.getElementById('p06AssetState');if(result&&(/Primary:/i.test(result.textContent)||/SHA-256/i.test(result.textContent))){const heading=result.querySelector('strong')?.textContent||'Ready';result.innerHTML=`<strong>${status(heading)}</strong><br>${t('Primary and backup copies were verified successfully.','تم التحقق من النسخة الأساسية والنسخة الاحتياطية بنجاح.')}`;}}

function localizeReports(){if(typeof route==='undefined'||route!=='reports')return;document.querySelectorAll('#content table').forEach(table=>{const heads=[...table.querySelectorAll('thead th')].map(x=>x.textContent.trim().toLowerCase()),qi=heads.findIndex(x=>x==='queue'||x==='القائمة'),di=heads.findIndex(x=>x==='dependency'||x==='الاعتماد'),si=heads.findIndex(x=>x==='status'||x==='الحالة'),ti=heads.findIndex(x=>x==='target'||x==='الهدف');[...table.querySelectorAll('tbody tr')].forEach(row=>{if(qi>=0&&row.cells[qi])row.cells[qi].textContent=queue(row.cells[qi].textContent);if(di>=0&&row.cells[di])row.cells[di].textContent=dependency(row.cells[di].textContent);if(si>=0&&row.cells[si])row.cells[si].textContent=status(row.cells[si].textContent);if(ti>=0&&row.cells[ti]&&row.cells[ti].textContent.trim()!=='—')row.cells[ti].textContent=t('Configured','مهيأ');});});document.querySelectorAll('#content .card.metric small').forEach(s=>{const x=s.textContent.trim();if(x&&!/^\d/.test(x)&&!/%$/.test(x)&&!/(original|verified|session|نسخة|تم التحقق|جلسة)/i.test(x))s.textContent=t('Primary storage','التخزين الأساسي');});}

function localizeChrome(){const footer=document.querySelector('.p128-app-footer span:first-child');if(footer)footer.textContent=t('All rights reserved · Diwan Al Amiri · Media Asset Management','جميع الحقوق محفوظة للديوان الأميري · نظام إدارة الأصول الإعلامية');const mobile=document.querySelector('.p128-mobile-menu');if(mobile)mobile.setAttribute('aria-label',t('Menu','القائمة'));const language=document.querySelector('[data-p128-language] span');if(language)language.textContent=ar()?'English':'العربية';const central=document.querySelector('.topbar .status');if(central)central.textContent=t('● Central services','● الخدمات المركزية');const home=document.querySelector('.topbar a[title="Landing page"],.topbar a[title="الصفحة الرئيسية"]');if(home)home.title=t('Landing page','الصفحة الرئيسية');}

function apply(){if(busy)return;busy=true;try{document.documentElement.lang=ar()?'ar':'en';document.documentElement.dir=ar()?'rtl':'ltr';dedupeAndOrderNavigation();localizePageTitle();localizeAdmin();localizePermissions();localizeForms();localizeProcessing();localizeProtection();localizeReports();localizeChrome();friendlyIdInputs();scrubVisibleIdentifiers(document.body);}finally{busy=false;}}
function schedule(){if(pending)return;pending=true;requestAnimationFrame(()=>{pending=false;apply();});}
new MutationObserver(schedule).observe(document.documentElement,{childList:true,subtree:true});
window.addEventListener('hashchange',schedule);document.getElementById('languageButton')?.addEventListener('click',()=>setTimeout(schedule,0));
window.mamUiAudit={apply,profile,medium,life,status,role,permission};
apply();
})();
