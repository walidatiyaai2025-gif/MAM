(() => {
'use strict';

const sidebar = document.querySelector('.sidebar[data-mam-sidebar-v2="1"]');
const nav = document.getElementById('nav');
const shell = document.querySelector('.app-shell');
if (!sidebar || !nav || !shell) return;

const primaryRoutes = ['dashboard','library','curation-actions','upload','queue','reports','collections','tags','search'];
const labels = {
  dashboard:['Dashboard','لوحة التحكم'],
  library:['Media Library','مكتبة الوسائط'],
  'curation-actions':['Curation Actions','إجراءات التهيئة'],
  upload:['Add New','إضافة جديد'],
  queue:['Processing Queue','قائمة المعالجة'],
  reports:['Reports','التقارير'],
  collections:['Collections Management','إدارة المجموعات'],
  tags:['System Tags','علامات مميزة للنظام'],
  search:['Content Search','البحث في المحتوى']
};
const collapseKey='mam.sidebar.v2.collapsed';
let syncing=false;
let queued=false;

function currentRoute(){
  try{
    if(typeof route!=='undefined' && route) return String(route);
  }catch{}
  try{return new URLSearchParams(location.hash.replace(/^#/,'')).get('route')||'dashboard';}catch{return 'dashboard';}
}
function isArabic(){return document.documentElement.lang!=='en';}

function removeLegacyOwners(){
  sidebar.querySelectorAll('.p127-admin-menu,.p127-sidebar-toggle,.brand,.nonprod').forEach(node=>node.remove());
  shell.classList.remove('p127-sidebar-collapsed');
}

function ensurePrimaryStructure(){
  const utility=nav.querySelector('.mam-sidebar-hidden-routes');
  const canonical=new Map();
  primaryRoutes.forEach(key=>{
    const nodes=[...nav.querySelectorAll(`button[data-route="${CSS.escape(key)}"]`)];
    const keep=nodes.find(n=>n.classList.contains('mam-sidebar-item'))||nodes[0];
    if(!keep)return;
    canonical.set(key,keep);
    nodes.filter(n=>n!==keep).forEach(n=>n.remove());
    if(keep.parentElement!==nav) nav.insertBefore(keep,utility||null);
    keep.classList.add('mam-sidebar-item');
  });

  [...nav.querySelectorAll(':scope > button[data-route]')].forEach(button=>{
    if(primaryRoutes.includes(button.dataset.route)) return;
    if(utility) utility.appendChild(button);
    else button.hidden=true;
  });
}

function syncLabels(){
  const ar=isArabic();
  primaryRoutes.forEach(key=>{
    const button=nav.querySelector(`:scope > .mam-sidebar-item[data-route="${CSS.escape(key)}"]`);
    if(!button)return;
    const label=button.querySelector('.mam-sidebar-label');
    const value=labels[key]?.[ar?1:0]||key;
    if(label && label.textContent!==value) label.textContent=value;
    button.setAttribute('aria-label',value);
    button.setAttribute('title',value);
  });
  const title=sidebar.querySelector('.mam-sidebar-brandcopy strong');
  const subtitle=sidebar.querySelector('.mam-sidebar-brandcopy span');
  if(title) title.textContent=ar?'الديوان الأميري':'Diwan Al Amiri';
  if(subtitle) subtitle.textContent=ar?'نظام إدارة المحتوى المؤسسي':'Enterprise Content Management';
  const innovationTitle=sidebar.querySelector('.mam-sidebar-innovation-copy strong');
  const innovationSub=sidebar.querySelector('.mam-sidebar-innovation-copy span');
  if(innovationTitle) innovationTitle.textContent=ar?'نظام الإبداع':'Innovation System';
  if(innovationSub) innovationSub.textContent='Innovation';
}

function syncActive(){
  const key=currentRoute();
  nav.querySelectorAll(':scope > .mam-sidebar-item[data-route]').forEach(button=>{
    const active=button.dataset.route===key;
    button.classList.toggle('active',active);
    if(active) button.setAttribute('aria-current','page');
    else button.removeAttribute('aria-current');
  });
}

function sync(){
  if(syncing)return;
  syncing=true;
  try{
    removeLegacyOwners();
    ensurePrimaryStructure();
    syncLabels();
    syncActive();
  }finally{syncing=false}
}

function schedule(){
  if(queued)return;
  queued=true;
  requestAnimationFrame(()=>{queued=false;sync();});
}

const collapse=sidebar.querySelector('.mam-sidebar-collapse');
if(localStorage.getItem(collapseKey)==='1') shell.classList.add('mam-sidebar-collapsed');
else shell.classList.remove('mam-sidebar-collapsed');
collapse?.addEventListener('click',event=>{
  event.preventDefault();
  event.stopPropagation();
  shell.classList.toggle('mam-sidebar-collapsed');
  localStorage.setItem(collapseKey,shell.classList.contains('mam-sidebar-collapsed')?'1':'0');
});

window.addEventListener('hashchange',schedule);
window.addEventListener('pageshow',schedule);
document.getElementById('languageButton')?.addEventListener('click',()=>setTimeout(schedule,0));

const observer=new MutationObserver(schedule);
observer.observe(nav,{childList:true,subtree:true,attributes:true,characterData:true,attributeFilter:['class','hidden','aria-hidden','data-p1214-enabled']});

window.mamSidebarV2=Object.freeze({
  version:'sidebar-reference-v1',
  sync,
  routes:[...primaryRoutes],
  diagnose:()=>({
    route:currentRoute(),
    collapsed:shell.classList.contains('mam-sidebar-collapsed'),
    legacyAdminMenus:nav.querySelectorAll('.p127-admin-menu').length,
    primaryButtons:nav.querySelectorAll(':scope > .mam-sidebar-item[data-route]').length
  })
});

sync();
})();