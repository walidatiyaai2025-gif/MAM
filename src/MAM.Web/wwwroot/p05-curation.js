let p05Query='';
let p05Lifecycle='';
let p05Category='';
let p05Tag='';
let p05CollectionId='';
let p05Page=1;
let p05Grid=true;

loadLiveLibrary = p05LoadLibrary;

async function p05LoadLibrary(){
  const languageAtRequest=arabic;
  try{
    const params=new URLSearchParams({page:String(p05Page),pageSize:'50'});
    if(p05Query)params.set('query',p05Query);
    if(p05Lifecycle)params.set('lifecycle',p05Lifecycle);
    if(p05Category)params.set('category',p05Category);
    if(p05Tag)params.set('tag',p05Tag);
    if(p05CollectionId)params.set('collectionId',p05CollectionId);
    const [searchResponse,collectionsResponse,policyResponse]=await Promise.all([
      fetch(`/client-api/curation/search?${params}`,{headers:{Accept:'application/json'}}),
      fetch('/client-api/curation/collections',{headers:{Accept:'application/json'}}),
      fetch('/client-api/curation/policy',{headers:{Accept:'application/json'}})
    ]);
    if(route!=='library'||languageAtRequest!==arabic)return;
    if(!searchResponse.ok)return p05LibraryFailure(searchResponse.status);
    if(!collectionsResponse.ok)return p05LibraryFailure(collectionsResponse.status);
    if(!policyResponse.ok)return p05LibraryFailure(policyResponse.status);
    const result=await searchResponse.json();
    const collections=await collectionsResponse.json();
    const policy=await policyResponse.json();
    if(route!=='library'||languageAtRequest!==arabic)return;
    content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library',arabic?`${result.totalCount} أصل مطابق · نتائج مركزية`:`${result.totalCount} matching assets · authoritative results`,'P05 · SEARCH & CURATION')}
      ${p05SearchCard(result,collections)}
      ${p05CollectionsCard(collections,policy)}
      <div id="p05Results">${p05Results(result,collections)}</div>`;
    p05BindSearch(result);
    p05BindCollections();
    p05BindAssetActions(collections);
  }catch{
    if(route==='library'&&languageAtRequest===arabic)content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library','P05 · SEARCH & CURATION')}${state('error','API error',arabic?'تعذر تحميل البحث. يمكن إعادة المحاولة.':'Search could not be loaded. Retry is available.')}`;
  }
}

function p05SearchCard(result,collections){
  const lifecycleOptions=['',...(result.facets?.lifecycles||[]).map(x=>x.value)];
  const categoryOptions=['',...(result.facets?.categories||[]).map(x=>x.value)];
  return `<div class="card"><h3>${arabic?'البحث والمرشحات':'Search & facets'}</h3>
    <div class="toolbar">
      <input id="p05Query" value="${esc(p05Query)}" maxlength="300" placeholder="${arabic?'بحث عربي أو إنجليزي':'Arabic or English search'}" aria-label="${arabic?'بحث':'Search'}"/>
      <select id="p05Lifecycle" aria-label="Lifecycle">${lifecycleOptions.map(x=>`<option value="${esc(x)}" ${x===p05Lifecycle?'selected':''}>${esc(x||(arabic?'كل الحالات':'All lifecycle states'))}</option>`).join('')}</select>
      <select id="p05Category" aria-label="Category">${categoryOptions.map(x=>`<option value="${esc(x)}" ${x===p05Category?'selected':''}>${esc(x||(arabic?'كل التصنيفات':'All categories'))}</option>`).join('')}</select>
      <select id="p05Collection" aria-label="Collection"><option value="">${arabic?'كل المجموعات':'All collections'}</option>${(collections||[]).map(c=>`<option value="${esc(c.collectionId)}" ${c.collectionId===p05CollectionId?'selected':''}>${esc(arabic&&c.nameAr?c.nameAr:c.nameEn)} (${esc(c.memberCount)})</option>`).join('')}</select>
      <button id="p05Search" class="action">${arabic?'بحث':'Search'}</button>
      <button id="p05View" class="action">${p05Grid?(arabic?'عرض قائمة':'List view'):(arabic?'عرض شبكي':'Grid view')}</button>
      <button id="p05Reset" class="action">${arabic?'مسح المرشحات':'Reset'}</button>
    </div>
    <div class="toolbar">${(result.facets?.tags||[]).slice(0,12).map(t=>`<button class="action" data-p05-tag="${esc(t.value)}">${esc(t.value)} · ${esc(t.count)}</button>`).join('')}</div>
  </div>`;
}

function p05CollectionsCard(collections,policy){
  const collectionText=Array.isArray(collections)&&collections.length
    ? collections.slice(0,12).map(c=>`${esc(arabic&&c.nameAr?c.nameAr:c.nameEn)} (${esc(c.memberCount)})`).join(' · ')
    : (arabic?'لا توجد مجموعات بعد.':'No collections yet.');
  const policyText=policy?.savedFiltersSupported
    ? (arabic?'المرشحات المحفوظة مفعلة.':'Saved filters are enabled.')
    : (arabic?'المرشحات المحفوظة غير مفعلة حتى اعتماد السياسة رسميًا.':'Saved filters remain disabled until product policy is explicitly approved.');
  return `<div class="card"><h3>${arabic?'المجموعات والسياسة':'Collections & policy'}</h3>
    <div class="toolbar"><input id="p05CollectionEn" maxlength="200" placeholder="Collection name (English)"/><input id="p05CollectionAr" maxlength="200" placeholder="اسم المجموعة بالعربية"/><button id="p05CreateCollection" class="action">${arabic?'إنشاء مجموعة':'Create collection'}</button></div>
    <p>${collectionText}</p><p>${esc(policyText)}</p><div id="p05CollectionState" aria-live="polite"></div></div>`;
}

function p05Results(result,collections){
  const items=Array.isArray(result.items)?result.items:[];
  if(!items.length)return state('empty','Empty',arabic?'لا توجد نتائج تطابق البحث والمرشحات الحالية.':'No assets match the current search and filters.');
  if(p05Grid)return `<div class="grid three">${items.map(asset=>p05AssetCard(asset,collections)).join('')}</div>`;
  return `<div class="list">${items.map(asset=>`<div class="row"><b>${esc(String(asset.id).slice(0,13))}</b><span>${esc(arabic&&asset.titleAr?asset.titleAr:asset.title)}</span><span>v${esc(asset.version)} · ${esc(asset.lifecycle)} · ${esc(asset.category||'—')}</span><span><button class="action" data-p05-edit="${esc(asset.id)}">${arabic?'تعديل':'Edit'}</button></span></div>`).join('')}</div>`;
}

function p05AssetCard(asset,collections){
  const title=arabic&&asset.titleAr?asset.titleAr:asset.title;
  const tags=Array.isArray(asset.tags)?asset.tags.slice(0,8).join(' · '):'';
  const collectionAction=Array.isArray(collections)&&collections.length?`<button class="action" data-p05-add="${esc(asset.id)}" data-p05-collection="${esc(collections[0].collectionId)}" data-p05-version="${esc(collections[0].version)}">${arabic?'أضف لأول مجموعة':'Add to first collection'}</button>`:'';
  return `<div class="card"><h3>${esc(title)}</h3><p>${esc(asset.id)}<br>v${esc(asset.version)} · ${esc(asset.lifecycle)} · ${esc(asset.category||'—')}<br>${esc(tags)}</p><div class="toolbar"><button class="action" data-p05-edit="${esc(asset.id)}">${arabic?'تعديل البيانات':'Edit metadata'}</button>${collectionAction}</div></div>`;
}

function p05BindSearch(){
  document.getElementById('p05Search')?.addEventListener('click',()=>{
    p05Query=document.getElementById('p05Query')?.value.trim()||'';
    p05Lifecycle=document.getElementById('p05Lifecycle')?.value||'';
    p05Category=document.getElementById('p05Category')?.value||'';
    p05CollectionId=document.getElementById('p05Collection')?.value||'';
    p05Page=1;void p05LoadLibrary();
  });
  document.getElementById('p05View')?.addEventListener('click',()=>{p05Grid=!p05Grid;void p05LoadLibrary();});
  document.getElementById('p05Reset')?.addEventListener('click',()=>{p05Query='';p05Lifecycle='';p05Category='';p05Tag='';p05CollectionId='';p05Page=1;void p05LoadLibrary();});
  content.querySelectorAll('[data-p05-tag]').forEach(button=>button.addEventListener('click',()=>{p05Tag=button.dataset.p05Tag||'';p05Page=1;void p05LoadLibrary();}));
}

function p05BindCollections(){
  document.getElementById('p05CreateCollection')?.addEventListener('click',async()=>{
    const output=document.getElementById('p05CollectionState');
    const nameEn=document.getElementById('p05CollectionEn')?.value.trim()||'';
    const nameAr=document.getElementById('p05CollectionAr')?.value.trim()||'';
    if(!nameEn){if(output)output.innerHTML=state('error','Validation',arabic?'الاسم الإنجليزي مطلوب.':'English collection name is required.');return;}
    try{
      const response=await fetch('/client-api/curation/collections',{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({nameEn,nameAr})});
      if(!response.ok){if(output)p05InlineFailure(output,response.status);return;}
      await p05LoadLibrary();
    }catch{if(output)output.innerHTML=state('error','API error',arabic?'تعذر إنشاء المجموعة.':'Collection creation failed.');}
  });
}

function p05BindAssetActions(collections){
  content.querySelectorAll('[data-p05-edit]').forEach(button=>button.addEventListener('click',()=>void p05OpenEditor(button.dataset.p05Edit)));
  content.querySelectorAll('[data-p05-add]').forEach(button=>button.addEventListener('click',async()=>{
    try{
      const response=await fetch(`/client-api/curation/collections/${button.dataset.p05Collection}/assets/${button.dataset.p05Add}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({expectedVersion:Number(button.dataset.p05Version)})});
      if(response.ok)await p05LoadLibrary();
    }catch{}
  }));
}

async function p05OpenEditor(assetId){
  try{
    const response=await fetch(`/client-api/curation/assets/${assetId}/metadata`,{headers:{Accept:'application/json'}});
    if(!response.ok)return p05LibraryFailure(response.status);
    const metadata=await response.json();
    content.innerHTML=`${lead(arabic?'تهيئة البيانات الوصفية':'Metadata Curation',`${esc(assetId)} · v${esc(metadata.version)} · ${esc(metadata.lifecycle)}`,'P05 · CURATION')}
      <div class="grid two"><div class="card"><h3>${arabic?'العنوان الإنجليزي':'English title'}</h3><input id="p05EditTitle" maxlength="300" value="${esc(metadata.titleEn)}"/></div><div class="card"><h3>${arabic?'العنوان العربي':'Arabic title'}</h3><input id="p05EditTitleAr" maxlength="300" value="${esc(metadata.titleAr||'')}" dir="rtl"/></div></div>
      <div class="grid two"><div class="card"><h3>${arabic?'التصنيف':'Category'}</h3><input id="p05EditCategory" maxlength="120" value="${esc(metadata.category||'')}"/></div><div class="card"><h3>${arabic?'الوسوم':'Tags'}</h3><input id="p05EditTags" maxlength="1000" value="${esc((metadata.tags||[]).join(', '))}"/></div></div>
      <div class="card"><h3>${arabic?'ملاحظات الحفظ':'Preservation notes'}</h3><textarea id="p05EditNotes" maxlength="2000" style="width:100%;min-height:110px">${esc(metadata.preservationNotes||'')}</textarea></div>
      <div class="card"><div class="toolbar"><button id="p05SaveMeta" class="action">${arabic?'حفظ البيانات':'Save metadata'}</button><button id="p05Lifecycle" class="action">${metadata.lifecycle==='Archived'?(arabic?'استعادة':'Restore'):(arabic?'أرشفة':'Archive')}</button><button id="p05Back" class="action">${arabic?'رجوع للمكتبة':'Back to library'}</button></div><div id="p05EditState" aria-live="polite"></div></div>`;
    document.getElementById('p05Back')?.addEventListener('click',()=>void p05LoadLibrary());
    document.getElementById('p05SaveMeta')?.addEventListener('click',()=>void p05SaveMetadata(assetId,metadata));
    document.getElementById('p05Lifecycle')?.addEventListener('click',()=>void p05SetLifecycle(assetId,metadata));
  }catch{content.innerHTML=`${lead(arabic?'تهيئة البيانات':'Metadata Curation','P05')}${state('error','API error',arabic?'تعذر تحميل البيانات الوصفية.':'Metadata could not be loaded.')}`;}
}

async function p05SaveMetadata(assetId,metadata){
  const output=document.getElementById('p05EditState');
  const body={expectedVersion:metadata.version,schemaKey:'core-media-v1',titleEn:document.getElementById('p05EditTitle')?.value.trim()||'',titleAr:document.getElementById('p05EditTitleAr')?.value.trim()||'',eventDate:metadata.eventDate,category:document.getElementById('p05EditCategory')?.value.trim()||'',tags:(document.getElementById('p05EditTags')?.value||'').split(',').map(x=>x.trim()).filter(Boolean),preservationNotes:document.getElementById('p05EditNotes')?.value.trim()||''};
  try{
    const response=await fetch(`/client-api/curation/assets/${assetId}/metadata`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify(body)});
    if(!response.ok){if(output)p05InlineFailure(output,response.status);return;}
    await p05OpenEditor(assetId);
  }catch{if(output)output.innerHTML=state('error','API error',arabic?'تعذر حفظ البيانات.':'Metadata save failed.');}
}

async function p05SetLifecycle(assetId,metadata){
  const output=document.getElementById('p05EditState');
  const action=metadata.lifecycle==='Archived'?'restore':'archive';
  try{
    const response=await fetch(`/client-api/curation/assets/${assetId}/${action}`,{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({expectedVersion:metadata.version})});
    if(!response.ok){if(output)p05InlineFailure(output,response.status);return;}
    await p05LoadLibrary();
  }catch{if(output)output.innerHTML=state('error','API error',arabic?'تعذر تغيير حالة الأصل.':'Lifecycle update failed.');}
}

function p05InlineFailure(host,statusCode){
  if(statusCode===401||statusCode===403){host.innerHTML=state('denied','Permission denied',arabic?'لا توجد صلاحية لهذا الإجراء.':'You do not have permission for this curation action.');return;}
  if(statusCode===409){host.innerHTML=state('error','Version conflict',arabic?'تم تعديل السجل من مستخدم آخر. حدّث ثم أعد المحاولة.':'The record changed elsewhere. Refresh before retrying.');return;}
  if(statusCode===503){host.innerHTML=state('degraded','Degraded',arabic?'خدمة التهيئة غير جاهزة.':'The curation service is degraded.');return;}
  host.innerHTML=state('error','API error',`HTTP ${statusCode}`);
}

function p05LibraryFailure(statusCode){
  if(route!=='library')return;
  if(statusCode===401||statusCode===403){content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library','P05')}${state('denied','Permission denied',arabic?'لا توجد صلاحية للبحث أو التهيئة.':'Permission denied for search or curation.')}`;return;}
  if(statusCode===503){content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library','P05')}${state('degraded','Degraded',arabic?'خدمة البحث والتهيئة غير جاهزة.':'The search/curation service is degraded.')}`;return;}
  content.innerHTML=`${lead(arabic?'مكتبة الوسائط':'Media Library','P05')}${state('error','API error',`HTTP ${statusCode}`)}`;
}

if(route==='library')void p05LoadLibrary();
