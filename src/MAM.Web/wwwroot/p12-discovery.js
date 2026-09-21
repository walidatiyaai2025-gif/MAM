let p12SelectedAssetId='';
let p12CategoryCache=[];
let p12ReferenceCache=[];

pages.search=['Content Search','البحث في المحتوى'];
pages.categories=['Categories','التصنيفات'];
pages.references=['Reference Library','مكتبة المراجع'];
pages.mediaPermissions=['Media Permissions','صلاحيات أنواع الوسائط'];

(function p12AddNavigation(){
  const navHost=document.getElementById('nav');
  if(!navHost)return;
  const additions=[
    ['search','Content Search','البحث في المحتوى'],
    ['categories','Categories','التصنيفات'],
    ['references','Reference Library','مكتبة المراجع'],
    ['mediaPermissions','Media Permissions','صلاحيات أنواع الوسائط']
  ];
  additions.forEach(([key,en,ar])=>{
    if(navHost.querySelector(`[data-route="${key}"]`))return;
    const button=document.createElement('button');
    button.dataset.route=key;
    button.textContent=arabic?ar:en;
    button.addEventListener('click',()=>{route=key;render();});
    navHost.appendChild(button);
    nav.push(button);
  });
})();

const p12PreviousShellPage=shellPage;
shellPage=function(){
  if(route==='search')return `${lead(arabic?'البحث في المحتوى':'Content Search',arabic?'ابحث داخل العناوين والبيانات الوصفية وOCR والتفريغ الصوتي والوسوم المرجعية.':'Search titles, metadata, OCR, timestamped transcripts and reference tags.','P12 · INDEXED DISCOVERY')}<div id="p12SearchHost">${state('loading','Loading',arabic?'جاري تحميل خيارات البحث…':'Loading search options…')}</div>`;
  if(route==='categories')return `${lead(arabic?'إدارة التصنيفات':'Category Management',arabic?'تصنيفات هرمية اختيارية بلا حد ثابت لمستوى التفرع؛ غير المصنف محفوظ تلقائيًا.':'Optional hierarchical categories with automatic Uncategorized fallback.','P12 · CATEGORIES')}<div id="p12CategoriesHost">${state('loading','Loading',arabic?'جاري تحميل التصنيفات…':'Loading categories…')}</div>`;
  if(route==='references')return `${lead(arabic?'مكتبة المراجع':'Reference Library',arabic?'أنشئ أشخاصًا أو كيانات مرجعية واربط صورًا مرجعية من مكتبة الوسائط.':'Create reference people/entities and attach image assets as visual references.','P12 · REFERENCES')}<div id="p12ReferencesHost">${state('loading','Loading',arabic?'جاري تحميل المراجع…':'Loading reference library…')}</div>`;
  if(route==='mediaPermissions')return `${lead(arabic?'صلاحيات أنواع الوسائط':'Media Type Permissions',arabic?'تحكم في العرض والرفع والتحرير والمعالجة والتنزيل حسب الدور ونوع الوسائط.':'Control view, upload, edit, process and download by role and media type.','P12 · RBAC')}<div id="p12PermissionsHost">${state('loading','Loading',arabic?'جاري تحميل الصلاحيات…':'Loading media permissions…')}</div>`;
  return p12PreviousShellPage();
};

const p12PreviousRender=render;
render=function(){
  p12PreviousRender();
  if(route==='search')void p12LoadSearch();
  if(route==='categories')void p12LoadCategories();
  if(route==='references')void p12LoadReferences();
  if(route==='mediaPermissions')void p12LoadMediaPermissions();
  if(route==='dashboard')void p12AugmentDashboard();
};

async function p12Json(url,options={}){
  const response=await fetch(url,{cache:'no-store',headers:{Accept:'application/json',...(options.headers||{})},...options});
  if(!response.ok){
    let payload={};
    try{payload=await response.json();}catch{}
    const correlationId=response.headers.get('X-Correlation-ID')||payload?.correlationId||'';
    const detail=payload?.detail||payload?.technicalDetail||payload?.error||`HTTP ${response.status}`;
    const error=new Error(correlationId?`${detail} · ID ${correlationId}`:detail);
    error.status=response.status;
    error.payload=payload;
    error.correlationId=correlationId;
    throw error;
  }
  if(response.status===204)return null;
  return await response.json().catch(()=>null);
}

async function p12LoadCategoryCache(){
  const rows=await p12Json('/client-api/discovery/categories');
  p12CategoryCache=Array.isArray(rows)?rows:[];
  return p12CategoryCache;
}

function p12CategoryName(item){return arabic&&item?.nameAr?item.nameAr:item?.nameEn||'—';}
function p12Indent(category,categories){
  let depth=0,current=category,guard=0;
  while(current?.parentCategoryId&&guard++<30){depth++;current=categories.find(x=>x.categoryId===current.parentCategoryId);}
  return `${'— '.repeat(depth)}${p12CategoryName(category)}`;
}
function p12Time(ms){
  if(ms===null||ms===undefined)return '—';
  const total=Math.max(0,Math.floor(Number(ms)/1000));
  const h=Math.floor(total/3600),m=Math.floor((total%3600)/60),s=total%60;
  return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
}

async function p12AugmentDashboard(){
  const languageAtRequest=arabic;
  try{
    const stats=await p12Json('/client-api/discovery/dashboard');
    if(route!=='dashboard'||languageAtRequest!==arabic)return;
    const grids=content.querySelectorAll('.grid.three');
    const target=grids.length?grids[0]:content;
    if(document.getElementById('p12CategoryMetric'))return;
    const card=document.createElement('div');card.className='card metric';card.id='p12CategoryMetric';
    card.innerHTML=`<strong>${esc(stats.categoryCount)}</strong><span>${arabic?'التصنيفات':'Categories'}</span><small>${arabic?`${esc(stats.uncategorizedAssetCount)} غير مصنف`:`${esc(stats.uncategorizedAssetCount)} uncategorized`}</small>`;
    target.appendChild(card);
    const indexed=document.createElement('div');indexed.className='card metric';indexed.innerHTML=`<strong>${esc(stats.indexedAssetCount)}</strong><span>${arabic?'أصول مفهرسة':'Indexed assets'}</span><small>${esc(stats.transcriptCount)} ${arabic?'تفريغ':'transcripts'} · ${esc(stats.ocrCount)} OCR</small>`;target.appendChild(indexed);
  }catch{}
}

async function p12LoadSearch(){
  const host=document.getElementById('p12SearchHost');if(!host)return;
  try{
    const categories=await p12LoadCategoryCache();
    if(route!=='search')return;
    host.innerHTML=`<div class="card"><h3>${arabic?'بحث نصي شامل':'Full-text discovery'}</h3><div class="toolbar">
      <input id="p12SearchQuery" maxlength="300" placeholder="${arabic?'اكتب كلمة أو جملة للبحث داخل كل المحتوى':'Enter text to search all indexed content'}" aria-label="${arabic?'نص البحث':'Search text'}"/>
      <select id="p12SearchKind"><option value="">${arabic?'كل أنواع الوسائط':'All media types'}</option>${['Video','Audio','Image','Document','Other'].map(x=>`<option value="${x}">${esc(x)}</option>`).join('')}</select>
      <select id="p12SearchCategory"><option value="">${arabic?'كل التصنيفات':'All categories'}</option>${categories.map(c=>`<option value="${esc(c.categoryId)}">${esc(p12Indent(c,categories))}</option>`).join('')}</select>
      <button id="p12SearchButton" class="action">${arabic?'بحث':'Search'}</button></div><div id="p12SearchResults"></div></div>`;
    const run=()=>void p12RunSearch();
    document.getElementById('p12SearchButton')?.addEventListener('click',run);
    document.getElementById('p12SearchQuery')?.addEventListener('keydown',event=>{if(event.key==='Enter')run();});
  }catch(ex){host.innerHTML=p12Failure(ex,arabic?'تعذر تحميل البحث.':'Search configuration could not be loaded.');}
}

async function p12RunSearch(){
  const output=document.getElementById('p12SearchResults');if(!output)return;
  const query=document.getElementById('p12SearchQuery')?.value.trim()||'';
  if(query.length<2){output.innerHTML=state('error',arabic?'تحقق':'Validation',arabic?'أدخل حرفين على الأقل.':'Enter at least two searchable characters.');return;}
  const params=new URLSearchParams({query,page:'1',pageSize:'100'});
  const kind=document.getElementById('p12SearchKind')?.value||'';if(kind)params.set('mediaKind',kind);
  const category=document.getElementById('p12SearchCategory')?.value||'';if(category)params.set('categoryId',category);
  output.innerHTML=state('loading','Loading',arabic?'جاري البحث في الفهرس…':'Searching the authoritative text index…');
  try{
    const result=await p12Json(`/client-api/discovery/search?${params}`);
    const items=Array.isArray(result.items)?result.items:[];
    if(!items.length){output.innerHTML=state('empty','Empty',arabic?'لا توجد وسائط مرتبطة بالنص المطلوب.':'No related media was found.');return;}
    output.innerHTML=`<div class="list">${items.map(item=>`<div class="row"><b>${esc(item.mediaKind)}</b><span><strong>${esc(item.title)}</strong><br>${esc(item.snippet||'')}</span><span>${esc(arabic&&item.categoryNameAr?item.categoryNameAr:item.categoryNameEn)}<br>${item.startMs!==null&&item.startMs!==undefined?`${p12Time(item.startMs)} → ${p12Time(item.endMs)}`:item.pageNumber?`${arabic?'صفحة':'Page'} ${esc(item.pageNumber)}`:esc(item.matchedSource)}</span><span><button class="action" data-p12-open-asset="${esc(item.assetId)}">${arabic?'فتح الأصل':'Open asset'}</button><br>${(item.referenceTags||[]).map(tag=>`<span class="badge">${esc(tag)}</span>`).join(' ')}</span></div>`).join('')}</div><p>${arabic?'النتائج':'Results'}: ${esc(result.totalCount)}</p>`;
    output.querySelectorAll('[data-p12-open-asset]').forEach(button=>button.addEventListener('click',()=>{p12SelectedAssetId=button.dataset.p12OpenAsset||'';route='asset';render();}));
  }catch(ex){output.innerHTML=p12Failure(ex,arabic?'فشل البحث.':'Search failed.');}
}

async function p12LoadCategories(){
  const host=document.getElementById('p12CategoriesHost');if(!host)return;
  try{
    const categories=await p12LoadCategoryCache();if(route!=='categories')return;
    const options=`<option value="">${arabic?'بدون أب - مستوى رئيسي':'No parent — root category'}</option>${categories.filter(c=>!c.isSystem).map(c=>`<option value="${esc(c.categoryId)}">${esc(p12Indent(c,categories))}</option>`).join('')}`;
    host.innerHTML=`<div class="card"><h3>${arabic?'إضافة أو تعديل تصنيف':'Create or edit category'}</h3><input type="hidden" id="p12CategoryId"/><input type="hidden" id="p12CategoryVersion"/>
      <div class="toolbar"><input id="p12CategoryEn" maxlength="200" placeholder="Category name (English)"/><input id="p12CategoryAr" maxlength="200" placeholder="اسم التصنيف بالعربية" dir="rtl"/><select id="p12CategoryParent">${options}</select><input id="p12CategorySort" type="number" value="0" style="width:90px"/><button id="p12CategorySave" class="action">${arabic?'حفظ':'Save'}</button><button id="p12CategoryClear" class="action">${arabic?'جديد':'New'}</button></div><div id="p12CategoryState"></div></div>
      <div class="card"><h3>${arabic?'شجرة التصنيفات':'Category tree'}</h3><div class="list">${categories.map(c=>`<div class="row"><b>${esc(p12Indent(c,categories))}</b><span>${esc(c.assetCount)} ${arabic?'أصل':'assets'} · ${esc(c.childCount)} ${arabic?'فرعي':'children'}</span><span>v${esc(c.version)}</span><span>${c.isSystem?`<span class="badge">${arabic?'نظام':'System'}</span>`:`<button class="action" data-p12-cat-edit="${esc(c.categoryId)}">${arabic?'تعديل':'Edit'}</button> <button class="action" data-p12-cat-delete="${esc(c.categoryId)}">${arabic?'حذف':'Delete'}</button>`}</span></div>`).join('')}</div></div>`;
    document.getElementById('p12CategorySave')?.addEventListener('click',()=>void p12SaveCategory());
    document.getElementById('p12CategoryClear')?.addEventListener('click',p12ClearCategoryEditor);
    host.querySelectorAll('[data-p12-cat-edit]').forEach(button=>button.addEventListener('click',()=>p12EditCategory(button.dataset.p12CatEdit)));
    host.querySelectorAll('[data-p12-cat-delete]').forEach(button=>button.addEventListener('click',()=>void p12DeleteCategory(button.dataset.p12CatDelete)));
  }catch(ex){host.innerHTML=p12Failure(ex,arabic?'تعذر تحميل التصنيفات.':'Categories could not be loaded.');}
}
function p12ClearCategoryEditor(){['p12CategoryId','p12CategoryVersion','p12CategoryEn','p12CategoryAr'].forEach(id=>{const el=document.getElementById(id);if(el)el.value='';});const p=document.getElementById('p12CategoryParent');if(p)p.value='';const s=document.getElementById('p12CategorySort');if(s)s.value='0';}
function p12EditCategory(id){const c=p12CategoryCache.find(x=>x.categoryId===id);if(!c)return;document.getElementById('p12CategoryId').value=c.categoryId;document.getElementById('p12CategoryVersion').value=c.version;document.getElementById('p12CategoryEn').value=c.nameEn||'';document.getElementById('p12CategoryAr').value=c.nameAr||'';document.getElementById('p12CategoryParent').value=c.parentCategoryId||'';document.getElementById('p12CategorySort').value=c.sortOrder||0;window.scrollTo({top:0,behavior:'smooth'});}
async function p12SaveCategory(){
  const output=document.getElementById('p12CategoryState');const id=document.getElementById('p12CategoryId')?.value||'';const nameEn=document.getElementById('p12CategoryEn')?.value.trim()||'';const nameAr=document.getElementById('p12CategoryAr')?.value.trim()||'';const parent=document.getElementById('p12CategoryParent')?.value||'';const sortOrder=Number(document.getElementById('p12CategorySort')?.value||0);if(!nameEn){if(output)output.innerHTML=state('error','Validation',arabic?'الاسم الإنجليزي مطلوب.':'English category name is required.');return;}
  try{const body={parentCategoryId:parent||null,nameEn,nameAr:nameAr||null,sortOrder};if(id){body.expectedVersion=Number(document.getElementById('p12CategoryVersion')?.value||0);await p12Json(`/client-api/discovery/categories/${id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});}else await p12Json('/client-api/discovery/categories',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});await p12LoadCategories();}catch(ex){if(output)output.innerHTML=p12Failure(ex,arabic?'تعذر حفظ التصنيف.':'Category could not be saved.');}
}
async function p12DeleteCategory(id){try{await p12Json(`/client-api/discovery/categories/${id}`,{method:'DELETE'});await p12LoadCategories();}catch(ex){const output=document.getElementById('p12CategoryState');if(output)output.innerHTML=p12Failure(ex,arabic?'لا يمكن حذف التصنيف قبل نقل أصوله أو التصنيفات الفرعية.':'Move assets and child categories before deleting this category.');}}

async function p12LoadReferences(){
  const host=document.getElementById('p12ReferencesHost');if(!host)return;
  try{p12ReferenceCache=await p12Json('/client-api/discovery/references');if(route!=='references')return;host.innerHTML=`<div class="card"><h3>${arabic?'إضافة مرجع':'Add reference subject'}</h3><div class="toolbar"><input id="p12RefEn" maxlength="200" placeholder="Name (English)"/><input id="p12RefAr" maxlength="200" placeholder="الاسم بالعربية" dir="rtl"/><input id="p12RefTags" maxlength="1000" placeholder="${arabic?'وسوم مفصولة بفاصلة':'Comma-separated tags'}"/><button id="p12RefCreate" class="action">${arabic?'إضافة':'Add'}</button></div><div id="p12RefState"></div></div>
    <div class="grid three">${p12ReferenceCache.map(r=>`<div class="card"><h3>${esc(arabic&&r.nameAr?r.nameAr:r.nameEn)}</h3><p>${esc((r.tags||[]).join(' · '))}</p><p>${arabic?'صور مرجعية':'Reference images'}: ${esc((r.referenceAssetIds||[]).length)}</p><div class="toolbar"><input data-p12-ref-image-input="${esc(r.subjectId)}" placeholder="Image Asset ID"/><button class="action" data-p12-ref-image-add="${esc(r.subjectId)}">${arabic?'ربط صورة':'Attach image'}</button></div><small>${(r.referenceAssetIds||[]).map(x=>esc(x)).join('<br>')}</small></div>`).join('')}</div>`;document.getElementById('p12RefCreate')?.addEventListener('click',()=>void p12CreateReference());host.querySelectorAll('[data-p12-ref-image-add]').forEach(button=>button.addEventListener('click',()=>void p12AddReferenceImage(button.dataset.p12RefImageAdd)));}catch(ex){host.innerHTML=p12Failure(ex,arabic?'تعذر تحميل مكتبة المراجع.':'Reference library could not be loaded.');}
}
async function p12CreateReference(){const output=document.getElementById('p12RefState');const nameEn=document.getElementById('p12RefEn')?.value.trim()||'';if(!nameEn){if(output)output.innerHTML=state('error','Validation',arabic?'الاسم الإنجليزي مطلوب.':'English name is required.');return;}const nameAr=document.getElementById('p12RefAr')?.value.trim()||'';const tags=(document.getElementById('p12RefTags')?.value||'').split(',').map(x=>x.trim()).filter(Boolean);try{await p12Json('/client-api/discovery/references',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({nameEn,nameAr:nameAr||null,descriptionEn:null,descriptionAr:null,tags})});await p12LoadReferences();}catch(ex){if(output)output.innerHTML=p12Failure(ex,arabic?'تعذر إضافة المرجع.':'Reference subject could not be created.');}}
async function p12AddReferenceImage(subjectId){const input=document.querySelector(`[data-p12-ref-image-input="${subjectId}"]`);const assetId=input?.value.trim()||'';if(!assetId)return;try{await p12Json(`/client-api/discovery/references/${subjectId}/images`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({assetId})});await p12LoadReferences();}catch(ex){const output=document.getElementById('p12RefState');if(output)output.innerHTML=p12Failure(ex,arabic?'يجب أن يكون الأصل صورة صالحة.':'The referenced asset must be a valid image asset.');}}

async function p12LoadMediaPermissions(){
  const host=document.getElementById('p12PermissionsHost');if(!host)return;
  try{const rows=await p12Json('/client-api/discovery/media-permissions');if(route!=='mediaPermissions')return;host.innerHTML=`<div class="card"><h3>${arabic?'مصفوفة الصلاحيات':'Permission matrix'}</h3><div class="list">${rows.map((p,i)=>`<div class="row"><b>${esc(p.roleName)}</b><span>${esc(p.mediaKind)}</span><span>${['View','Upload','Edit','Process','Download'].map(key=>{const prop='can'+key;return `<label style="margin-inline-end:8px"><input type="checkbox" data-p12-perm="${i}|${prop}" ${p[prop]?'checked':''}/> ${arabic?{View:'عرض',Upload:'رفع',Edit:'تعديل',Process:'معالجة',Download:'تنزيل'}[key]:key}</label>`;}).join('')}</span><span><button class="action" data-p12-perm-save="${i}">${arabic?'حفظ':'Save'}</button></span></div>`).join('')}</div><div id="p12PermissionState"></div></div>`;host.querySelectorAll('[data-p12-perm-save]').forEach(button=>button.addEventListener('click',()=>void p12SavePermission(rows,Number(button.dataset.p12PermSave))));}catch(ex){host.innerHTML=p12Failure(ex,arabic?'يلزم حساب إداري لإدارة هذه الصلاحيات.':'An administrator identity is required to manage these permissions.');}
}
async function p12SavePermission(rows,index){const row=rows[index];if(!row)return;const box=prop=>document.querySelector(`[data-p12-perm="${index}|${prop}"]`)?.checked===true;const body={roleName:row.roleName,mediaKind:row.mediaKind,canView:box('canView'),canUpload:box('canUpload'),canEdit:box('canEdit'),canProcess:box('canProcess'),canDownload:box('canDownload')};const output=document.getElementById('p12PermissionState');try{await p12Json('/client-api/discovery/media-permissions',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(output)output.innerHTML=state('empty',arabic?'تم الحفظ':'Saved',arabic?'تم تحديث صلاحيات نوع الوسائط.':'Media-type permissions were updated.');}catch(ex){if(output)output.innerHTML=p12Failure(ex,arabic?'تعذر حفظ الصلاحيات.':'Permissions could not be saved.');}}

async function p12AttachAssetDiscovery(assetId,technical){
  const host=document.getElementById('p12AssetDiscovery');if(!host)return;
  try{
    const requests=[p12LoadCategoryCache(),p12Json(`/client-api/discovery/assets/${assetId}/category`),p12Json(`/client-api/discovery/assets/${assetId}/extraction-status`),p12Json(`/client-api/discovery/assets/${assetId}/reference-tags`),p12Json('/client-api/discovery/references')];
    const [categories,assetCategory,statuses,tags,references]=await Promise.all(requests);
    let transcript=null,ocr=null;
    try{transcript=await p12Json(`/client-api/discovery/assets/${assetId}/text/transcript`);}catch(ex){if(ex.status!==404)throw ex;}
    try{ocr=await p12Json(`/client-api/discovery/assets/${assetId}/text/ocr`);}catch(ex){if(ex.status!==404)throw ex;}
    if(route!=='asset')return;
    const detected=p12DetectedMetadata(technical);
    host.innerHTML=`<div class="card"><div class="toolbar"><button class="action" data-p12-tab="metadata">${arabic?'البيانات والتصنيف':'Metadata & Category'}</button><button class="action" data-p12-tab="transcript">${arabic?'التفريغ الزمني':'Transcript Timeline'}</button><button class="action" data-p12-tab="ocr">OCR</button><button class="action" data-p12-tab="references">${arabic?'الوسوم المرجعية':'Reference Tags'}</button></div><div id="p12AssetTab"></div></div>`;
    const showTab=tab=>{
      const body=document.getElementById('p12AssetTab');if(!body)return;
      if(tab==='metadata')body.innerHTML=`<h3>${arabic?'التصنيف والبيانات المكتشفة':'Category & detected metadata'}</h3><div class="toolbar"><select id="p12AssetCategory">${categories.map(c=>`<option value="${esc(c.categoryId)}" ${c.categoryId===assetCategory.category.categoryId?'selected':''}>${esc(p12Indent(c,categories))}</option>`).join('')}</select><button id="p12AssetCategorySave" class="action">${arabic?'حفظ التصنيف':'Save category'}</button></div><div id="p12AssetCategoryState"></div>${detected}`;
      if(tab==='transcript')body.innerHTML=p12TextTab(transcript,statuses,'transcript');
      if(tab==='ocr')body.innerHTML=p12TextTab(ocr,statuses,'ocr');
      if(tab==='references')body.innerHTML=`<h3>${arabic?'وسوم الأشخاص والكيانات المرجعية':'Reference people/entity tags'}</h3><div class="toolbar"><select id="p12AssetReference"><option value="">${arabic?'اختر مرجعًا':'Select reference subject'}</option>${references.map(r=>`<option value="${esc(r.subjectId)}">${esc(arabic&&r.nameAr?r.nameAr:r.nameEn)}</option>`).join('')}</select><button id="p12AssetReferenceAdd" class="action">${arabic?'إضافة وسم':'Add tag'}</button></div><div id="p12AssetReferenceState"></div><div class="toolbar">${(tags||[]).map(t=>`<span class="badge">${esc(arabic&&t.nameAr?t.nameAr:t.nameEn)}${t.confidence!==null&&t.confidence!==undefined?` · ${Math.round(Number(t.confidence)*100)}%`:''}</span>`).join(' ')||esc(arabic?'لا توجد وسوم مرجعية.':'No reference tags yet.')}</div>`;
      document.getElementById('p12AssetCategorySave')?.addEventListener('click',()=>void p12SaveAssetCategory(assetId));
      document.getElementById('p12AssetReferenceAdd')?.addEventListener('click',()=>void p12AddAssetReference(assetId));
    };
    host.querySelectorAll('[data-p12-tab]').forEach(button=>button.addEventListener('click',()=>showTab(button.dataset.p12Tab)));
    showTab(technical?.mediaType==='Video'||technical?.mediaType==='Audio'?'transcript':technical?.mediaType==='Document'||technical?.mediaType==='Image'?'ocr':'metadata');
  }catch(ex){host.innerHTML=p12Failure(ex,arabic?'تعذر تحميل بيانات الاكتشاف والفهرسة.':'Discovery/index data could not be loaded.');}
}
function p12TextTab(text,statuses,kind){const status=(statuses||[]).find(x=>x.extractionKind===kind);const progress=status?`<div class="state ${status.state==='Failed'?'error':status.state==='Succeeded'?'empty':'loading'}"><strong>${esc(status.state)} · ${esc(status.progressPercent)}%</strong><br>${esc(status.detail||'')}</div>`:'';if(!text)return `<h3>${kind==='transcript'?(arabic?'التفريغ الزمني':'Transcript Timeline'):'OCR'}</h3>${progress}<p>${arabic?'لا يوجد نص مفهرس بعد. استخدم زر المعالجة أعلى الصفحة.':'No indexed text yet. Queue the relevant processing action above.'}</p>`;const segments=Array.isArray(text.segments)?text.segments:[];return `<h3>${kind==='transcript'?(arabic?'التفريغ الزمني':'Transcript Timeline'):'OCR'}</h3>${progress}<div class="list">${segments.map(s=>`<div class="row"><b>${kind==='transcript'?`${p12Time(s.startMs)} → ${p12Time(s.endMs)}`:`${arabic?'صفحة':'Page'} ${esc(s.pageNumber||1)}`}</b><span style="grid-column:span 3">${esc(s.text)}</span></div>`).join('')||`<p>${esc(text.text||'')}</p>`}</div>`;}
function p12DetectedMetadata(technical){if(!technical?.rawJson)return `<p>${arabic?'لا توجد بيانات مضمنة مكتشفة بعد.':'No embedded metadata has been detected yet.'}</p>`;try{const raw=JSON.parse(technical.rawJson);const tags=raw?.format?.tags||{};const entries=Object.entries(tags).filter(([,v])=>v!==null&&v!==undefined&&String(v).trim()).slice(0,20);if(!entries.length)return `<p>${arabic?'لم توجد حقول مضمّنة إضافية.':'No additional embedded fields were found.'}</p>`;return `<div class="list">${entries.map(([k,v])=>`<div class="row"><b>${esc(k)}</b><span style="grid-column:span 3">${esc(v)}</span></div>`).join('')}</div>`;}catch{return `<p>${arabic?'تعذر تحليل البيانات الفنية المضمنة.':'Embedded technical metadata could not be parsed.'}</p>`;}}
async function p12SaveAssetCategory(assetId){const output=document.getElementById('p12AssetCategoryState');const categoryId=document.getElementById('p12AssetCategory')?.value||null;try{await p12Json(`/client-api/discovery/assets/${assetId}/category`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({categoryId})});if(output)output.innerHTML=state('empty',arabic?'تم الحفظ':'Saved',arabic?'تم تحديث تصنيف الأصل.':'Asset category was updated.');}catch(ex){if(output)output.innerHTML=p12Failure(ex,arabic?'تعذر حفظ التصنيف.':'Category assignment failed.');}}
async function p12AddAssetReference(assetId){const output=document.getElementById('p12AssetReferenceState');const subjectId=document.getElementById('p12AssetReference')?.value||'';if(!subjectId)return;try{await p12Json(`/client-api/discovery/assets/${assetId}/reference-tags`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({subjectId,confidence:null,detectionSource:'manual'})});if(output)output.innerHTML=state('empty',arabic?'تمت الإضافة':'Tagged',arabic?'تمت إضافة الوسم المرجعي وفهرسته.':'Reference tag was added and indexed.');}catch(ex){if(output)output.innerHTML=p12Failure(ex,arabic?'تعذر إضافة الوسم.':'Reference tag could not be added.');}}

async function p12DecorateProcessingQueue(host,jobs){
  const items=[...host.querySelectorAll('[data-p12-progress]')];
  const cache=new Map();
  for(const element of items){const [assetId,profileId]=(element.dataset.p12Progress||'').split('|');const kind=profileId==='ocr-text-v1'?'ocr':profileId==='transcript-text-v1'?'transcript':'';if(!kind)continue;try{if(!cache.has(assetId))cache.set(assetId,await p12Json(`/client-api/discovery/assets/${assetId}/extraction-status`));const status=(cache.get(assetId)||[]).find(x=>x.extractionKind===kind);if(status)element.innerHTML=`<br><strong>${esc(status.progressPercent)}%</strong> · ${esc(status.state)}`;}catch{}}
}

function p12Failure(ex,fallback){
  const detail=ex?.payload?.detail||ex?.message||fallback;
  const status=Number(ex?.status||0);
  const kind=status===401||status===403?'denied':status===429||status>=500?'degraded':'error';
  const heading=status===401||status===403
    ? (arabic?'لا توجد صلاحية':'Permission denied')
    : status===409
      ? (arabic?'تعارض في البيانات':'Conflict')
      : status===400||status===422
        ? (arabic?'تحقق من البيانات':'Validation')
        : status===404
          ? (arabic?'غير موجود':'Not found')
          : kind==='degraded'
            ? (arabic?'الخدمة غير متاحة مؤقتًا':'Service temporarily unavailable')
            : (arabic?'تعذر تنفيذ الإجراء':'Action failed');
  return state(kind,heading,detail);
}
