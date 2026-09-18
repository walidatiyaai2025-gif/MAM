(() => {
'use strict';

pages.collections=['Collections','المجموعات'];
pages.tags=['Tags','الوسوم'];

let collectionCache=[];
let selectedCollectionId='';
let collectionAssetQuery='';
let tagCache=[];
let tagQuery='';

const h=v=>typeof esc==='function'?esc(v):String(v??'');
const text=(en,ar)=>arabic?ar:en;

function addAdminRoute(key,en,ar,icon){
  const navHost=document.getElementById('nav');
  if(!navHost||navHost.querySelector('[data-route="'+key+'"]'))return;
  const button=document.createElement('button');
  button.type='button';
  button.dataset.route=key;
  button.innerHTML='<span class="p127-nav-icon"><i class="bi '+icon+'"></i></span><span class="p127-nav-label">'+h(arabic?ar:en)+'</span>';
  button.addEventListener('click',()=>{route=key;render();});
  const submenu=document.querySelector('.p127-admin-submenu');
  (submenu||navHost).appendChild(button);
  if(Array.isArray(nav))nav.push(button);
}
addAdminRoute('collections','Collections','المجموعات','bi-folder2-open');
addAdminRoute('tags','Tags','الوسوم','bi-tags');

const previousShell=shellPage;
shellPage=function(){
  if(route==='collections')return lead(text('Collection Management','إدارة المجموعات'),text('Create, rename and delete collection definitions, then add or remove member assets without deleting media.','إنشاء وإعادة تسمية وحذف المجموعات وإدارة الأعضاء بدون حذف أي أصل إعلامي.'),'CURATION · COLLECTIONS')+'<div id="p138CollectionsHost">'+state('loading','Loading',text('Loading collections…','جاري تحميل المجموعات…'))+'</div>';
  if(route==='tags')return lead(text('Tag Management','إدارة الوسوم'),text('Authoritative tag dictionary with usage counts, propagated rename and safe deletion.','قاموس مركزي للوسوم مع عدادات الاستخدام وإعادة تسمية تنتشر على الأصول وحذف آمن.'),'CURATION · TAGS')+'<div id="p138TagsHost">'+state('loading','Loading',text('Loading tags…','جاري تحميل الوسوم…'))+'</div>';
  return previousShell();
};

const previousRender=render;
render=function(){
  previousRender();
  if(route==='collections')void loadCollections();
  if(route==='tags')void loadTags();
};

async function api(url,options={}){
  const response=await fetch(url,{headers:{Accept:'application/json',...(options.headers||{})},...options});
  if(!response.ok){
    let payload={};try{payload=await response.json();}catch{}
    const error=new Error(payload.detail||('HTTP '+response.status));
    error.status=response.status;error.payload=payload;throw error;
  }
  if(response.status===204)return null;
  return await response.json();
}
function failure(ex,fallback){
  const kind=ex?.status===401||ex?.status===403?'denied':ex?.status===503?'degraded':'error';
  return state(kind,kind==='denied'?'Permission denied':kind==='degraded'?'Degraded':'API error',ex?.payload?.detail||ex?.message||fallback);
}
async function confirmDialog(title,body,confirmText,danger=true){
  if(window.p127OpenModal)return await window.p127OpenModal({title,body,confirmText,danger});
  return window.confirm(body.replace(/<[^>]+>/g,' '))?{}:null;
}
function collectionName(x){return arabic&&x?.nameAr?x.nameAr:x?.nameEn||'—';}

async function loadCollections(){
  const host=document.getElementById('p138CollectionsHost');if(!host)return;
  try{
    collectionCache=await api('/client-api/curation/collections');
    if(route!=='collections')return;
    let selected=collectionCache.find(x=>x.collectionId===selectedCollectionId)||null;
    let members=null,candidates=null;
    if(selected){
      const mp=new URLSearchParams({collectionId:selected.collectionId,page:'1',pageSize:'100'});
      const cp=new URLSearchParams({page:'1',pageSize:'100'});
      if(collectionAssetQuery)cp.set('query',collectionAssetQuery);
      [members,candidates]=await Promise.all([
        api('/client-api/curation/search?'+mp.toString()),
        api('/client-api/curation/search?'+cp.toString())
      ]);
      if(route!=='collections')return;
      selected=collectionCache.find(x=>x.collectionId===selectedCollectionId)||selected;
    }
    const memberships=collectionCache.reduce((sum,x)=>sum+Number(x.memberCount||0),0);
    host.innerHTML=
      '<div class="grid three">'+
        metric(collectionCache.length,text('Collections','المجموعات'))+
        metric(memberships,text('Memberships','إجمالي العضويات'))+
        metric(collectionCache.filter(x=>Number(x.memberCount)===0).length,text('Empty','مجموعات فارغة'))+
      '</div>'+
      '<div class="card"><h3>'+h(text('Create collection','إنشاء مجموعة'))+'</h3>'+
        '<div class="toolbar"><input id="p138CollectionEn" maxlength="200" placeholder="Collection name (English)"/><input id="p138CollectionAr" maxlength="200" dir="rtl" placeholder="اسم المجموعة بالعربية"/><button id="p138CollectionCreate" class="action">'+h(text('Create','إضافة'))+'</button></div><div id="p138CollectionState"></div></div>'+
      '<div class="card"><h3>'+h(text('Collections','المجموعات'))+'</h3><div class="list">'+
        (collectionCache.map(x=>'<div class="row"><b>'+h(collectionName(x))+'</b><span>'+h(x.memberCount)+' '+h(text('assets','أصل'))+'</span><span>v'+h(x.version)+'</span><span><button class="action" data-c-open="'+h(x.collectionId)+'">'+h(text('Members','الأعضاء'))+'</button> <button class="action" data-c-edit="'+h(x.collectionId)+'">'+h(text('Edit','تعديل'))+'</button> <button class="action" data-c-delete="'+h(x.collectionId)+'">'+h(text('Delete','حذف'))+'</button></span></div>').join('')||'<p>'+h(text('No collections yet.','لا توجد مجموعات بعد.'))+'</p>')+
      '</div></div>'+
      (selected?membersMarkup(selected,members,candidates):'');
    document.getElementById('p138CollectionCreate')?.addEventListener('click',()=>void createCollection());
    host.querySelectorAll('[data-c-open]').forEach(b=>b.addEventListener('click',()=>{selectedCollectionId=b.dataset.cOpen||'';collectionAssetQuery='';void loadCollections();}));
    host.querySelectorAll('[data-c-edit]').forEach(b=>b.addEventListener('click',()=>void editCollection(b.dataset.cEdit)));
    host.querySelectorAll('[data-c-delete]').forEach(b=>b.addEventListener('click',()=>void deleteCollection(b.dataset.cDelete)));
    host.querySelectorAll('[data-c-remove]').forEach(b=>b.addEventListener('click',()=>void changeMember(selected,b.dataset.cRemove,false)));
    host.querySelectorAll('[data-c-add]').forEach(b=>b.addEventListener('click',()=>void changeMember(selected,b.dataset.cAdd,true)));
    document.getElementById('p138CollectionSearchBtn')?.addEventListener('click',()=>{collectionAssetQuery=document.getElementById('p138CollectionSearch')?.value.trim()||'';void loadCollections();});
    document.getElementById('p138CollectionClose')?.addEventListener('click',()=>{selectedCollectionId='';collectionAssetQuery='';void loadCollections();});
  }catch(ex){host.innerHTML=failure(ex,text('Collections could not be loaded.','تعذر تحميل المجموعات.'));}
}
function metric(value,label){return '<div class="card metric"><strong>'+h(value)+'</strong><span>'+h(label)+'</span></div>';}
function membersMarkup(collection,members,candidates){
  const ids=new Set((members?.items||[]).map(x=>x.id));
  const available=(candidates?.items||[]).filter(x=>!ids.has(x.id));
  return '<div class="card"><h3>'+h(collectionName(collection))+' · '+h(text('Membership management','إدارة الأعضاء'))+'</h3>'+
    '<div class="toolbar"><input id="p138CollectionSearch" value="'+h(collectionAssetQuery)+'" placeholder="'+h(text('Search assets to add','ابحث عن أصل لإضافته'))+'"/><button id="p138CollectionSearchBtn" class="action">'+h(text('Search','بحث'))+'</button><button id="p138CollectionClose" class="action">'+h(text('Close','إغلاق'))+'</button></div>'+
    '<h4>'+h(text('Current members','الأعضاء الحاليون'))+' ('+h(members?.totalCount||0)+')</h4><div class="list">'+
      ((members?.items||[]).map(a=>'<div class="row"><b>'+h(a.title)+'</b><span>'+h(a.id)+'</span><span>'+h(a.category||'—')+'</span><span><button class="action" data-c-remove="'+h(a.id)+'">'+h(text('Remove','إزالة'))+'</button></span></div>').join('')||'<p>'+h(text('This collection is empty.','المجموعة فارغة.'))+'</p>')+
    '</div><h4>'+h(text('Add assets','إضافة أصول'))+'</h4><div class="list">'+
      (available.slice(0,50).map(a=>'<div class="row"><b>'+h(a.title)+'</b><span>'+h(a.id)+'</span><span>'+h(a.category||'—')+'</span><span><button class="action" data-c-add="'+h(a.id)+'">'+h(text('Add','إضافة'))+'</button></span></div>').join('')||'<p>'+h(text('No matching assets are available to add.','لا توجد أصول مطابقة متاحة للإضافة.'))+'</p>')+
    '</div></div>';
}
async function createCollection(){
  const out=document.getElementById('p138CollectionState'),nameEn=document.getElementById('p138CollectionEn')?.value.trim()||'',nameAr=document.getElementById('p138CollectionAr')?.value.trim()||'';
  if(!nameEn){out.innerHTML=state('error','Validation',text('English collection name is required.','الاسم الإنجليزي مطلوب.'));return;}
  try{await api('/client-api/curation/collections',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({nameEn,nameAr:nameAr||null})});await loadCollections();}catch(ex){out.innerHTML=failure(ex,text('Collection could not be created.','تعذر إنشاء المجموعة.'));}
}
async function editCollection(id){
  const item=collectionCache.find(x=>x.collectionId===id);if(!item)return;
  const modal=await confirmDialog(text('Edit collection','تعديل المجموعة'),'<div class="p127-user-form"><div class="p127-field"><label>English</label><input id="p138EditCollectionEn" value="'+h(item.nameEn)+'"/></div><div class="p127-field"><label>العربية</label><input id="p138EditCollectionAr" dir="rtl" value="'+h(item.nameAr||'')+'"/></div></div>',text('Save','حفظ'),false);
  if(!modal||!modal.querySelector)return;
  const nameEn=modal.querySelector('#p138EditCollectionEn')?.value.trim()||'',nameAr=modal.querySelector('#p138EditCollectionAr')?.value.trim()||'';if(!nameEn)return;
  try{await api('/client-api/curation/collections/'+id,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({expectedVersion:item.version,nameEn,nameAr:nameAr||null})});await loadCollections();}catch(ex){document.getElementById('p138CollectionState').innerHTML=failure(ex,text('Collection could not be updated.','تعذر تعديل المجموعة.'));}
}
async function deleteCollection(id){
  const item=collectionCache.find(x=>x.collectionId===id);if(!item)return;
  const body='<p>'+h(arabic?'سيتم فك ارتباط '+item.memberCount+' أصل من المجموعة فقط، ولن يتم حذف أي أصل إعلامي.':item.memberCount+' memberships will be detached. No media asset will be deleted.')+'</p><strong>'+h(collectionName(item))+'</strong>';
  if(!await confirmDialog(text('Delete collection','حذف المجموعة'),body,text('Delete collection','حذف المجموعة'),true))return;
  try{await api('/client-api/curation/collections/'+id+'?expectedVersion='+encodeURIComponent(item.version),{method:'DELETE'});if(selectedCollectionId===id)selectedCollectionId='';await loadCollections();}catch(ex){document.getElementById('p138CollectionState').innerHTML=failure(ex,text('Collection could not be deleted.','تعذر حذف المجموعة.'));}
}
async function changeMember(collection,assetId,add){
  if(!collection)return;
  try{
    const url='/client-api/curation/collections/'+collection.collectionId+'/assets/'+assetId;
    if(add)await api(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({expectedVersion:collection.version})});
    else await api(url+'?expectedVersion='+encodeURIComponent(collection.version),{method:'DELETE'});
    await loadCollections();
  }catch(ex){document.getElementById('p138CollectionState').innerHTML=failure(ex,text('Collection membership could not be updated.','تعذر تحديث عضوية المجموعة.'));}
}

async function loadTags(){
  const host=document.getElementById('p138TagsHost');if(!host)return;
  try{
    tagCache=await api('/client-api/curation/tags'+(tagQuery?'?query='+encodeURIComponent(tagQuery):''));
    if(route!=='tags')return;
    const assignments=tagCache.reduce((sum,x)=>sum+Number(x.assetCount||0),0);
    host.innerHTML=
      '<div class="grid three">'+metric(tagCache.length,text('Tags','الوسوم'))+metric(assignments,text('Assignments','مرات الاستخدام'))+metric(tagCache.filter(x=>Number(x.assetCount)===0).length,text('Unused','غير مستخدم'))+'</div>'+
      '<div class="card"><h3>'+h(text('Create tag','إضافة وسم'))+'</h3><div class="toolbar"><input id="p138TagName" maxlength="120" placeholder="'+h(text('Tag name','اسم الوسم'))+'"/><button id="p138TagCreate" class="action">'+h(text('Create','إضافة'))+'</button></div><div id="p138TagState"></div></div>'+
      '<div class="card"><h3>'+h(text('Tag dictionary','قاموس الوسوم'))+'</h3><div class="toolbar"><input id="p138TagSearch" value="'+h(tagQuery)+'" placeholder="'+h(text('Search tags','بحث في الوسوم'))+'"/><button id="p138TagSearchBtn" class="action">'+h(text('Search','بحث'))+'</button><button id="p138TagClear" class="action">'+h(text('Clear','مسح'))+'</button></div><div class="list">'+
      (tagCache.map(x=>'<div class="row"><b>'+h(x.name)+'</b><span>'+h(x.assetCount)+' '+h(text('assets','أصل'))+'</span><span>v'+h(x.version)+'</span><span><button class="action" data-t-edit="'+h(x.tagId)+'">'+h(text('Rename','إعادة تسمية'))+'</button> <button class="action" data-t-assets="'+h(x.name)+'">'+h(text('View assets','عرض الأصول'))+'</button> <button class="action" data-t-delete="'+h(x.tagId)+'">'+h(text('Delete','حذف'))+'</button></span></div>').join('')||'<p>'+h(text('No matching tags.','لا توجد وسوم مطابقة.'))+'</p>')+
      '</div></div>';
    document.getElementById('p138TagCreate')?.addEventListener('click',()=>void createTag());
    document.getElementById('p138TagSearchBtn')?.addEventListener('click',()=>{tagQuery=document.getElementById('p138TagSearch')?.value.trim()||'';void loadTags();});
    document.getElementById('p138TagClear')?.addEventListener('click',()=>{tagQuery='';void loadTags();});
    host.querySelectorAll('[data-t-edit]').forEach(b=>b.addEventListener('click',()=>void editTag(b.dataset.tEdit)));
    host.querySelectorAll('[data-t-delete]').forEach(b=>b.addEventListener('click',()=>void deleteTag(b.dataset.tDelete)));
    host.querySelectorAll('[data-t-assets]').forEach(b=>b.addEventListener('click',()=>{if(typeof p05Tag!=='undefined')p05Tag=b.dataset.tAssets||'';route='library';render();}));
  }catch(ex){host.innerHTML=failure(ex,text('Tags could not be loaded.','تعذر تحميل الوسوم.'));}
}
async function createTag(){
  const out=document.getElementById('p138TagState'),name=document.getElementById('p138TagName')?.value.trim()||'';
  if(!name){out.innerHTML=state('error','Validation',text('Tag name is required.','اسم الوسم مطلوب.'));return;}
  try{await api('/client-api/curation/tags',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name})});await loadTags();}catch(ex){out.innerHTML=failure(ex,text('Tag could not be created.','تعذر إنشاء الوسم.'));}
}
async function editTag(id){
  const item=tagCache.find(x=>x.tagId===id);if(!item)return;
  const body='<div class="p127-field"><label>'+h(text('New name','الاسم الجديد'))+'</label><input id="p138EditTagName" maxlength="120" value="'+h(item.name)+'"/></div><p>'+h(text('The new name will propagate to every asset using this tag.','سيتم تطبيق الاسم الجديد على كل الأصول التي تستخدم هذا الوسم.'))+'</p>';
  const modal=await confirmDialog(text('Rename tag','إعادة تسمية الوسم'),body,text('Save','حفظ'),false);if(!modal||!modal.querySelector)return;
  const name=modal.querySelector('#p138EditTagName')?.value.trim()||'';if(!name)return;
  try{await api('/client-api/curation/tags/'+id,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({expectedVersion:item.version,name})});await loadTags();}catch(ex){document.getElementById('p138TagState').innerHTML=failure(ex,text('Tag could not be renamed.','تعذر تعديل الوسم.'));}
}
async function deleteTag(id){
  const item=tagCache.find(x=>x.tagId===id);if(!item)return;const inUse=Number(item.assetCount)>0;
  const message=inUse?(arabic?'الوسم مستخدم على '+item.assetCount+' أصل. سيتم إزالته من هذه الأصول ثم حذف تعريفه.':'This tag is used by '+item.assetCount+' assets. It will be removed from those assets before deletion.'):text('This tag is unused and can be deleted safely.','الوسم غير مستخدم ويمكن حذفه بأمان.');
  if(!await confirmDialog(text('Delete tag','حذف الوسم'),'<p>'+h(message)+'</p><strong>'+h(item.name)+'</strong>',text('Delete','حذف'),true))return;
  try{await api('/client-api/curation/tags/'+id+'?expectedVersion='+encodeURIComponent(item.version)+'&removeFromAssets='+(inUse?'true':'false'),{method:'DELETE'});await loadTags();}catch(ex){document.getElementById('p138TagState').innerHTML=failure(ex,text('Tag could not be deleted.','تعذر حذف الوسم.'));}
}

window.mamManagementPages=Object.freeze({version:'p138-management-1',loadCollections,loadTags});
})();