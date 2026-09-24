(() => {
'use strict';

const tr=(en,ar)=>(document.documentElement.lang==='ar'||window.arabic)?ar:en;
const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const content=document.getElementById('content');
let menuSnapshot=[];
let enhancePending=false;

function popup(message,kind='success',title=''){
  if(window.MamPopup?.notify){window.MamPopup.notify(message,kind,title);return;}
  if(window.mamToast){window.mamToast(kind,title||tr('Completed','تم التنفيذ'),message);return;}
  alert(message);
}

async function api(url,options={}){
  const response=await fetch(url,{cache:'no-store',headers:{Accept:'application/json',...(options.headers||{})},...options});
  if(!response.ok){
    let body={};try{body=await response.json();}catch{}
    const err=new Error(body.detail||body.error||`HTTP ${response.status}`);
    err.status=response.status;err.body=body;throw err;
  }
  return response.status===204?null:response.json();
}

function setRoute(key){
  try{
    route=key;
    const url=new URL(location.href);
    const state=new URLSearchParams(url.hash.replace(/^#/,''));
    state.set('route',key);url.hash=state.toString();
    history.replaceState(history.state,'',url.href);
    if(typeof render==='function')render();
  }catch{location.hash=`#route=${encodeURIComponent(key)}`;}
}

function canonicalizeSettings(){
  if(typeof route==='undefined'||route!=='settings')return;
  if(window.mamAdminTabs?.select?.('web-menu'))return;
  try{
    localStorage.setItem('mam.p127.adminTab','web-menu');
    localStorage.removeItem('mam.p142.adminTab');
  }catch{}
  setRoute('admin');
}

function menuEditor(items,desktop){
  const rows=items.filter(x=>desktop?String(x.navigationKey).startsWith('desktop-'):!String(x.navigationKey).startsWith('desktop-'));
  const title=desktop?tr('Desktop application menu management','إدارة قائمة تطبيق سطح المكتب'):tr('Web menu management','إدارة قائمة الويب');
  const detail=desktop
    ?tr('Control which Desktop sections are visible, their Arabic/English labels and ordering. Changes are applied after Desktop sign-in/refresh.','تحكم في ظهور أقسام تطبيق سطح المكتب ومسمياتها العربية والإنجليزية وترتيبها. يتم تطبيق التغييرات بعد تسجيل الدخول أو تحديث التطبيق.')
    :tr('Control the Web sidebar visibility, labels and order. Permissions remain enforced by the server.','تحكم في ظهور عناصر القائمة الجانبية للويب ومسمياتها وترتيبها. تظل الصلاحيات مفروضة من الخادم.');
  return `<section class="p142-admin-editor" data-p142-menu="${desktop?'desktop':'web'}">
    <header><div><h3><i class="bi ${desktop?'bi-pc-display':'bi-window-sidebar'}"></i> ${esc(title)}</h3><p>${esc(detail)}</p></div>
      <button type="button" class="action p142-save-menu"><i class="bi bi-floppy"></i> ${esc(tr('Save menu','حفظ القائمة'))}</button></header>
    <div class="p142-menu-list">${rows.sort((a,b)=>(a.parentKey||'').localeCompare(b.parentKey||'')||Number(a.sortOrder)-Number(b.sortOrder)).map(item=>`
      <div class="p142-menu-row" data-key="${esc(item.navigationKey)}">
        <div><strong>${esc((window.arabic?item.labelAr:item.labelEn)||item.navigationKey)}</strong><small>${esc(item.navigationKey)}</small></div>
        <label><span>${esc(tr('Visible','ظاهر'))}</span><input data-f="enabled" type="checkbox" ${item.isEnabled!==false?'checked':''}></label>
        <label><span>${esc(tr('Arabic label','الاسم العربي'))}</span><input data-f="ar" dir="rtl" maxlength="100" value="${esc(item.labelAr||'')}"></label>
        <label><span>${esc(tr('English label','الاسم الإنجليزي'))}</span><input data-f="en" dir="ltr" maxlength="100" value="${esc(item.labelEn||'')}"></label>
        <label><span>${esc(tr('Order','الترتيب'))}</span><input data-f="order" type="number" min="0" max="9999" value="${Number(item.sortOrder)||0}"></label>
      </div>`).join('')}</div>
  </section>`;
}

async function loadMenuTab(desktop){
  const panel=document.getElementById('p127AdminPanel');if(!panel)return;
  panel.innerHTML=`<div class="state loading"><strong>${esc(tr('Loading menu configuration…','جاري تحميل إعدادات القائمة…'))}</strong></div>`;
  try{
    const payload=await api('/client-api/admin/navigation');
    menuSnapshot=Array.isArray(payload?.items)?payload.items:[];
    panel.innerHTML=menuEditor(menuSnapshot,desktop);
    panel.querySelector('.p142-save-menu')?.addEventListener('click',async e=>{
      const button=e.currentTarget;button.disabled=true;
      try{
        const updates=[...panel.querySelectorAll('.p142-menu-row')].map(row=>({
          navigationKey:row.dataset.key,
          labelEn:row.querySelector('[data-f="en"]').value.trim(),
          labelAr:row.querySelector('[data-f="ar"]').value.trim(),
          isEnabled:row.querySelector('[data-f="enabled"]').checked,
          sortOrder:Number(row.querySelector('[data-f="order"]').value||0)
        }));
        if(updates.some(x=>!x.labelEn||!x.labelAr))throw new Error(tr('Arabic and English labels are required.','الاسم العربي والإنجليزي مطلوبان.'));
        await api('/client-api/admin/navigation',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({items:updates})});
        popup(tr('Menu configuration saved.','تم حفظ إعدادات القائمة.'),'success');
        try{window.mamNavigationPreferences?.reload?.();}catch{}
      }catch(err){popup(err.message,'error',tr('Save failed','فشل الحفظ'));}finally{button.disabled=false;}
    });
  }catch(err){panel.innerHTML='';popup(err.message,'error',tr('Menu configuration failed','تعذر تحميل إعدادات القائمة'));}
}

async function permissionTab(){
  const panel=document.getElementById('p127AdminPanel');if(!panel)return;
  panel.innerHTML=`<section class="p142-permissions">
    <div class="card"><h3><i class="bi bi-shield-check"></i> ${esc(tr('Permission matrix','مصفوفة الصلاحيات'))}</h3>
    <p>${esc(tr('Media, deletion, operational and tape-management permissions are managed from the same authoritative matrix.','يتم إدارة صلاحيات الميديا والحذف والتشغيل وإدارة الأشرطة من نفس مصفوفة الصلاحيات الموثوقة.'))}</p></div>
    <div id="p142PermissionMatrixHost"><div class="state loading"><strong>${esc(tr('Loading permission matrix…','جاري تحميل مصفوفة الصلاحيات…'))}</strong></div></div>
  </section>`;
  const host=document.getElementById('p142PermissionMatrixHost');
  if(!host)return;
  if(window.mamPermissionMatrix?.load)return window.mamPermissionMatrix.load(host);
  host.innerHTML=`<div class="state error"><strong>${esc(tr('Permission matrix module is unavailable.','وحدة مصفوفة الصلاحيات غير متاحة.'))}</strong></div>`;
}

async function referencesTab(){
  const panel=document.getElementById('p127AdminPanel');if(!panel)return;
  panel.innerHTML=`<div class="state loading"><strong>${esc(tr('Loading references…','جاري تحميل المراجع…'))}</strong></div>`;
  try{
    const rows=await api('/client-api/admin/references');
    panel.innerHTML=`<section class="p142-reference-admin"><header><div><h3><i class="bi bi-person-bounding-box"></i> ${esc(tr('Reference management','إدارة المراجع'))}</h3><p>${esc(tr('Create, edit and delete reference subjects used by discovery and media organization.','إضافة وتعديل وحذف المراجع المستخدمة في البحث وتنظيم الوسائط.'))}</p></div><button class="action" data-ref-add><i class="bi bi-plus-lg"></i> ${esc(tr('Add reference','إضافة مرجع'))}</button></header>
      <div class="p142-reference-list">${(rows||[]).map(r=>`<article data-ref-id="${esc(r.subjectId)}"><div><strong>${esc((window.arabic?r.nameAr:r.nameEn)||r.nameEn)}</strong><small>${esc(r.tagsText||'')}</small><small>${Number(r.imageCount||0)} ${esc(tr('reference images','صور مرجعية'))} · ${Number(r.taggedAssetCount||0)} ${esc(tr('tagged media','وسائط مرتبطة'))}</small></div><div><button class="action" data-ref-edit>${esc(tr('Edit','تعديل'))}</button><button class="action danger" data-ref-delete>${esc(tr('Delete','حذف'))}</button></div></article>`).join('')||`<div class="state empty"><strong>${esc(tr('No references','لا توجد مراجع'))}</strong></div>`}</div>
    </section>`;
    const openEditor=async row=>{
      const modal=await window.p127OpenModal({title:row?tr('Edit reference','تعديل المرجع'):tr('Add reference','إضافة مرجع'),confirmText:tr('Save','حفظ'),body:`<div class="p127-user-form"><div class="p127-field"><label>English</label><input id="rEn" maxlength="200" value="${esc(row?.nameEn||'')}"></div><div class="p127-field"><label>العربية</label><input id="rAr" dir="rtl" maxlength="200" value="${esc(row?.nameAr||'')}"></div><div class="p127-field full"><label>${esc(tr('Tags','الوسوم'))}</label><input id="rTags" maxlength="1000" value="${esc(row?.tagsText||'')}"></div><div class="p127-field full"><label>${esc(tr('Description','الوصف'))}</label><textarea id="rDesc" rows="4">${esc((window.arabic?row?.descriptionAr:row?.descriptionEn)||'')}</textarea></div></div>`});
      if(!modal)return;
      const body={nameEn:modal.querySelector('#rEn').value.trim(),nameAr:modal.querySelector('#rAr').value.trim()||null,tagsText:modal.querySelector('#rTags').value.trim()||null,descriptionEn:window.arabic?row?.descriptionEn||null:modal.querySelector('#rDesc').value.trim()||null,descriptionAr:window.arabic?modal.querySelector('#rDesc').value.trim()||null:row?.descriptionAr||null,isActive:true};
      if(!body.nameEn){popup(tr('English name is required.','الاسم الإنجليزي مطلوب.'),'error');return;}
      await api(row?`/client-api/admin/references/${encodeURIComponent(row.subjectId)}`:'/client-api/admin/references',{method:row?'PUT':'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
      popup(tr('Reference saved.','تم حفظ المرجع.'),'success');await referencesTab();
    };
    panel.querySelector('[data-ref-add]')?.addEventListener('click',()=>void openEditor(null));
    panel.querySelectorAll('[data-ref-edit]').forEach(b=>b.addEventListener('click',()=>{const id=b.closest('[data-ref-id]').dataset.refId;void openEditor(rows.find(x=>x.subjectId===id));}));
    panel.querySelectorAll('[data-ref-delete]').forEach(b=>b.addEventListener('click',async()=>{const id=b.closest('[data-ref-id]').dataset.refId;const row=rows.find(x=>x.subjectId===id);const modal=await window.p127OpenModal({title:tr('Delete reference','حذف المرجع'),danger:true,confirmText:tr('Delete','حذف'),body:`<p>${esc(tr('Reference links will be removed; media assets themselves will not be deleted.','سيتم حذف روابط المرجع فقط ولن يتم حذف ملفات الميديا.'))}</p><strong>${esc(row?.nameEn||id)}</strong>`});if(!modal)return;try{await api(`/client-api/admin/references/${encodeURIComponent(id)}`,{method:'DELETE'});popup(tr('Reference deleted.','تم حذف المرجع.'),'success');await referencesTab();}catch(err){popup(err.message,'error');}}));
  }catch(err){panel.innerHTML='';popup(err.message,'error',tr('Reference management failed','تعذر تحميل إدارة المراجع'));}
}

function renderCustomTab(key){
  if(key==='web-menu')return loadMenuTab(false);
  if(key==='desktop-menu')return loadMenuTab(true);
  if(key==='permissions')return permissionTab();
  if(key==='references-admin')return referencesTab();
  return false;
}

function headerSearch(){
  const form=document.getElementById('p126SidebarSearch');if(!form||form.dataset.p142Search)return;
  const candidate=form.querySelector('#p126GlobalSearchInput,input[type="search"],input');
  if(!candidate)return;
  if(candidate.id==='p126GlobalSearchInput'){form.dataset.p142Search='native-fixed';return;}
  form.dataset.p142Search='1';
  const execute=()=>{
    const value=candidate.value.trim();if(value.length<2){popup(tr('Enter at least two searchable characters.','أدخل حرفين على الأقل للبحث.'),'error');return;}
    localStorage.setItem('mam.p142.headerQuery',value);setRoute('search');
    let tries=0;const fill=()=>{const q=document.getElementById('mamUnifiedQuery');if(q){q.value=value;document.querySelector('[data-mam-search-mode="text"]')?.click();document.getElementById('mamUnifiedRun')?.click();return;}if(++tries<30)setTimeout(fill,100);};setTimeout(fill,0);
  };
  candidate.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();execute();}});
  form.querySelector('button[type="submit"]')?.addEventListener('click',e=>{e.preventDefault();execute();},true);
}

function curationGroups(){
  content?.classList.remove('p142-curation-two-column');
  if(typeof route==='undefined'||route!=='curation-actions')return;
  content?.querySelectorAll('[data-p142-groups]').forEach(x=>x.remove());
}
async function openGroupsModal(){
  try{
    const rows=await api('/client-api/curation/collections');
    const modal=await window.p127OpenModal({title:tr('Group management','إدارة المجموعات'),confirmText:tr('Close','إغلاق'),body:`<div class="p142-group-create"><input id="gEn" placeholder="English name"><input id="gAr" dir="rtl" placeholder="الاسم العربي"><button type="button" class="action" id="gAdd">${esc(tr('Add group','إضافة مجموعة'))}</button></div><div class="p142-group-list">${rows.map(g=>`<article data-gid="${esc(g.collectionId)}"><strong>${esc((window.arabic?g.nameAr:g.nameEn)||g.nameEn)}</strong><span>${Number(g.memberCount||0)}</span><button type="button" class="action danger" data-gdel>${esc(tr('Delete','حذف'))}</button></article>`).join('')}</div>`});
    if(!modal)return;
    modal.querySelector('#gAdd')?.addEventListener('click',async()=>{const nameEn=modal.querySelector('#gEn').value.trim();const nameAr=modal.querySelector('#gAr').value.trim();if(!nameEn){popup(tr('English name is required.','الاسم الإنجليزي مطلوب.'),'error');return;}try{await api('/client-api/curation/collections',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({nameEn,nameAr:nameAr||null})});popup(tr('Group added.','تمت إضافة المجموعة.'),'success');modal.closest('.p127-modal-backdrop,.mam-modal-backdrop')?.remove();void openGroupsModal();}catch(err){popup(err.message,'error');}});
    modal.querySelectorAll('[data-gdel]').forEach(b=>b.addEventListener('click',async()=>{const id=b.closest('[data-gid]').dataset.gid;const row=rows.find(x=>x.collectionId===id);try{await api(`/client-api/curation/collections/${encodeURIComponent(id)}?expectedVersion=${encodeURIComponent(row?.version??1)}`,{method:'DELETE'});popup(tr('Group deleted.','تم حذف المجموعة.'),'success');b.closest('article')?.remove();}catch(err){popup(err.message,'error');}}));
  }catch(err){popup(err.message,'error');}
}

function queueCounter(){
  if(typeof route==='undefined'||route!=='queue')return;
  const host=document.getElementById('p04QueueState');if(!host||host.dataset.p142Count==='1')return;
  fetch('/client-api/processing/jobs?limit=100',{cache:'no-store',headers:{Accept:'application/json'}}).then(r=>r.ok?r.json():Promise.reject()).then(jobs=>{
    if(!Array.isArray(jobs)||!host.isConnected)return;
    const counts={all:jobs.length,active:jobs.filter(x=>Number(x.state)===0||Number(x.state)===1).length,done:jobs.filter(x=>Number(x.state)===2).length,failed:jobs.filter(x=>Number(x.state)===3).length};
    const bar=document.createElement('div');bar.className='p142-queue-counts';bar.innerHTML=`<span><strong>${counts.all}</strong>${esc(tr('Total','الإجمالي'))}</span><span><strong>${counts.active}</strong>${esc(tr('Active','نشطة'))}</span><span><strong>${counts.done}</strong>${esc(tr('Completed','مكتملة'))}</span><span><strong>${counts.failed}</strong>${esc(tr('Failed','فاشلة'))}</span>`;host.prepend(bar);host.dataset.p142Count='1';
  }).catch(()=>{});
}

function downloadIcons(){
  document.querySelectorAll('a.mam-desktop-download,[data-mam-desktop-download],a[href*="Mac-Uploader"],a[href*="Desktop-Setup"]').forEach(a=>{
    if(!a.querySelector('i'))a.insertAdjacentHTML('afterbegin','<i class="bi bi-download"></i>');
    a.classList.add('p142-download-button');
  });
}

function popupAudit(){
  document.querySelectorAll('#content .state.error,#content .state.denied,#content .state.degraded,#content [role="alert"],#content [aria-live="polite"] .state:not(.loading)').forEach(node=>{
    if(node.dataset.p142Popup==='1')return;
    const msg=node.textContent?.replace(/\s+/g,' ').trim();if(!msg||msg.length>900)return;
    node.dataset.p142Popup='1';
    const error=node.classList.contains('error')||node.classList.contains('denied')||node.classList.contains('degraded');
    popup(msg,error?'error':'success');
  });
}

function schedule(){
  if(enhancePending)return;enhancePending=true;
  requestAnimationFrame(()=>{enhancePending=false;canonicalizeSettings();headerSearch();curationGroups();queueCounter();downloadIcons();popupAudit();});
}
new MutationObserver(schedule).observe(document.body,{childList:true,subtree:true,characterData:true});
window.addEventListener('hashchange',schedule);
schedule();
window.mamOwnerClosure=Object.freeze({
  version:'p142-owner-closure-2',
  enhance:schedule,
  loadAdminTab:renderCustomTab
});
window.mamAdminTabs?.reload?.();
})();