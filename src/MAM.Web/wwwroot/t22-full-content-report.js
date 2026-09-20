(() => {
'use strict';
let arabic=true,data=null,session=null;
const $=id=>document.getElementById(id);
const esc=v=>String(v??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const t=(en,ar)=>arabic?ar:en;
async function json(url){const r=await fetch(url,{headers:{Accept:'application/json'},cache:'no-store'});const p=await r.json().catch(()=>null);if(!r.ok)throw new Error(p?.detail||`HTTP ${r.status}`);return p;}
function mediaName(kind){const names={Video:['Video','فيديو'],Audio:['Audio','صوت'],Image:['Images','صور'],Document:['Documents','وثائق'],Other:['Other','أخرى']};return names[kind]?.[arabic?1:0]||kind;}
function render(){
 if(!data)return;
 document.documentElement.lang=arabic?'ar':'en';document.documentElement.dir=arabic?'rtl':'ltr';
 $('languageButton').textContent=arabic?'English':'العربية';$('printButton').textContent=t('Print report','طباعة التقرير');$('backLink').textContent=t('Reports','التقارير');
 const generated=new Date(data.generatedAtUtc).toLocaleString(arabic?'ar-KW':'en-GB');
 $('reportHost').innerHTML=`
   <header class="report-head">
     <img src="/assets/branding/diwan-al-amiri-crest.png" alt="${esc(t('Diwan Al Amiri crest','شعار الديوان الأميري'))}" />
     <div><h1>${esc(t('Diwan Al Amiri','الديوان الأميري'))}</h1><p>Media Asset Management · MAM</p></div>
     <div class="report-meta"><div>${esc(t('Generated','تاريخ الإصدار'))}: ${esc(generated)}</div><div>${esc(t('Issued by','أصدره'))}: ${esc(data.generatedBy||session?.displayName||'—')}</div></div>
   </header>
   <section class="report-title"><h2>${esc(t('Full Content Report','تقرير المحتوى بالكامل'))}</h2><p>${esc(t('Authoritative count of current non-deleted media files by media type.','إحصاء رسمي للملفات الحالية غير المحذوفة حسب نوع الميديا.'))}</p></section>
   <div class="report-total">${esc(t('Total files','إجمالي الملفات'))}: ${esc(data.totalFiles)}</div>
   <table class="report-table"><thead><tr><th>${esc(t('Media type','نوع الميديا'))}</th><th>${esc(t('Uploaded files','عدد الملفات المرفوعة'))}</th></tr></thead>
   <tbody>${(data.byMediaType||[]).map(x=>`<tr><td>${esc(mediaName(x.mediaType))}</td><td><strong>${esc(x.fileCount)}</strong></td></tr>`).join('')}</tbody>
   <tfoot><tr><th>${esc(t('Total','الإجمالي'))}</th><th>${esc(data.totalFiles)}</th></tr></tfoot></table>
   <footer class="report-footer"><span>${esc(t('Official MAM content inventory report','تقرير رسمي من نظام إدارة الأصول الإعلامية'))}</span><span>${esc(generated)}</span></footer>`;
}
async function load(){try{[session,data]=await Promise.all([json('/client-api/session'),json('/client-api/reports/full-content')]);render();}catch(error){$('reportHost').innerHTML=`<div class="state error">${esc(error.message)}</div>`;}}
$('languageButton').addEventListener('click',()=>{arabic=!arabic;render();});
$('printButton').addEventListener('click',()=>window.print());
load();
})();