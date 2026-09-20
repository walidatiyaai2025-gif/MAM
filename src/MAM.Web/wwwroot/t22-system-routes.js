(() => {
'use strict';
pages.tapes=['Tape Management','إدارة الأشرطة'];
pages.systemFunctions=['System Functions','وظائف النظام'];

const originalShellPage=shellPage;
shellPage=function(){
  if(route==='tapes'){
    return `${lead(arabic?'إدارة الأشرطة':'Tape Management',arabic?'إدارة الأشرطة والبحث والباركود والطباعة من مصدر مركزي واحد.':'Tape inventory, search, barcode and printing from one authoritative workspace.','T2.2 · TAPE MANAGEMENT')}<div class="card" style="padding:0;overflow:hidden"><iframe id="t22TapeFrame" title="${arabic?'إدارة الأشرطة':'Tape Management'}" src="/tape-inventory.html?embedded=1" style="width:100%;height:calc(100vh - 190px);min-height:720px;border:0;background:#f4f7fb"></iframe></div>`;
  }
  if(route==='systemFunctions'){
    return `${lead(arabic?'وظائف النظام':'System Functions',arabic?'التحكم المركزي في تفعيل أو تعطيل الوظائف المدارة.':'Authoritative controls for managed product functions.','SYSTEM FUNCTIONS')}<div id="t22SystemFunctionsHost">${state('loading','Loading',arabic?'جاري تحميل وظائف النظام…':'Loading system functions…')}</div>`;
  }
  return originalShellPage();
};

const originalRender=render;
render=function(){
  originalRender();
  if(route==='systemFunctions')void loadSystemFunctions();
  void applyT22NavigationAccess();
};

async function fetchJson(url,options={}){
  const response=await fetch(url,{cache:'no-store',...options,headers:{Accept:'application/json',...(options.body?{'Content-Type':'application/json'}:{}),...(options.headers||{})}});
  const payload=await response.json().catch(()=>null);
  if(!response.ok){const e=new Error(payload?.detail||`HTTP ${response.status}`);e.status=response.status;throw e;}
  return payload;
}

let accessSerial=0;
async function applyT22NavigationAccess(){
  const serial=++accessSerial;
  try{
    const [session,effective]=await Promise.all([
      fetchJson('/client-api/session'),
      fetchJson('/client-api/system-functions/effective').catch(()=>[])
    ]);
    if(serial!==accessSerial)return;
    const perms=new Set(session.permissions||[]);
    const flags=new Map((effective||[]).map(x=>[x.functionKey,!!x.isEnabled]));
    const tapeVisible=perms.has('tape.view')&&flags.get('tape.management')!==false;
    const systemVisible=perms.has('system-functions.view');
    document.querySelectorAll('#nav [data-route="tapes"]').forEach(x=>{x.hidden=!tapeVisible;x.setAttribute('aria-hidden',String(!tapeVisible));});
    document.querySelectorAll('#nav [data-route="systemFunctions"]').forEach(x=>{x.hidden=!systemVisible;x.setAttribute('aria-hidden',String(!systemVisible));});
    if(route==='tapes'&&!tapeVisible){route='dashboard';originalRender();}
    if(route==='systemFunctions'&&!systemVisible){route='dashboard';originalRender();}
  }catch{
    document.querySelectorAll('#nav [data-route="tapes"],#nav [data-route="systemFunctions"]').forEach(x=>x.hidden=true);
  }
}

async function loadSystemFunctions(){
  const host=document.getElementById('t22SystemFunctionsHost');if(!host)return;
  try{
    const [rows,session]=await Promise.all([fetchJson('/client-api/system-functions/'),fetchJson('/client-api/session')]);
    if(route!=='systemFunctions'||!host.isConnected)return;
    const canManage=(session.permissions||[]).includes('system-functions.manage');
    host.innerHTML=`<div class="card"><h3>${arabic?'الوظائف المدارة':'Managed functions'}</h3><p>${arabic?'التعطيل هنا يطبق في الواجهة والـ API، وليس إخفاءً شكليًا فقط.':'Disabling a function here is enforced by both the UI and Central API.'}</p><div class="list">${rows.map(row=>`<div class="row"><b>${row.isEnabled?'ON':'OFF'}</b><span><strong>${escapeT22(arabic?row.nameAr:row.nameEn)}</strong><br><small>${escapeT22(row.functionKey)}</small></span><span>v${escapeT22(row.version)}</span><span>${canManage?`<button class="action" data-t22-function="${escapeT22(row.functionKey)}" data-version="${escapeT22(row.version)}" data-enabled="${row.isEnabled?'true':'false'}">${row.isEnabled?(arabic?'تعطيل':'Disable'):(arabic?'تفعيل':'Enable')}</button>`:''}</span></div>`).join('')}</div><div id="t22FunctionState"></div></div>`;
    host.querySelectorAll('[data-t22-function]').forEach(button=>button.addEventListener('click',()=>void updateFunction(button)));
  }catch(error){host.innerHTML=state('error','API error',error.message);}
}

async function updateFunction(button){
  const host=document.getElementById('t22FunctionState');
  const key=button.dataset.t22Function,version=Number(button.dataset.version),next=button.dataset.enabled!=='true';
  try{
    await fetchJson(`/client-api/system-functions/${encodeURIComponent(key)}`,{method:'PUT',body:JSON.stringify({isEnabled:next,expectedVersion:version})});
    if(host)host.innerHTML=state('empty',arabic?'تم الحفظ':'Saved',arabic?'تم تحديث وظيفة النظام.':'System function updated.');
    await loadSystemFunctions();
    await applyT22NavigationAccess();
  }catch(error){if(host)host.innerHTML=state('error','API error',error.message);}
}

function escapeT22(value){return String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));}

requestAnimationFrame(()=>void applyT22NavigationAccess());
})();