(() => {
'use strict';
let arabic=true;
let tape=null,barcode=null,formats=[],departments=[],session=null,effective=[];
const $=id=>document.getElementById(id);
const esc=v=>String(v??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const t=(en,ar)=>arabic?ar:en;
async function json(url,options={}){
  const r=await fetch(url,{cache:'no-store',...options,headers:{Accept:'application/json',...(options.body?{'Content-Type':'application/json'}:{}),...(options.headers||{})}});
  const p=await r.json().catch(()=>null);if(!r.ok)throw new Error(p?.detail||`HTTP ${r.status}`);return p;
}
function field(label,value,wide=false){return `<div class="report-field ${wide?'wide':''}"><label>${esc(label)}</label><div>${esc(value||'—')}</div></div>`;}
function fmt(code){const x=formats.find(r=>String(r.code).toLowerCase()===String(code||'').toLowerCase());return x?(arabic?x.nameAr:x.nameEn):(code||'—');}
function dept(code){const x=departments.find(r=>String(r.code).toLowerCase()===String(code||'').toLowerCase());return x?(arabic?x.nameAr:x.nameEn):(code||'—');}
function date(v){if(!v)return '—';try{return new Date(v).toLocaleDateString(arabic?'ar-KW':'en-GB');}catch{return v;}}
function duration(seconds){if(seconds==null)return '—';const s=Number(seconds),h=Math.floor(s/3600),m=Math.floor((s%3600)/60),r=s%60;return [h,m,r].map(x=>String(x).padStart(2,'0')).join(':');}
function tapeLocation(){return [tape.room,tape.cabinet,tape.shelf,tape.bin].filter(Boolean).join(' / ')||'—';}
function render(){
  if(!tape||!barcode)return;
  document.documentElement.lang=arabic?'ar':'en';document.documentElement.dir=arabic?'rtl':'ltr';
  $('languageButton').textContent=arabic?'English':'العربية';$('printButton').textContent=t('Print report','طباعة التقرير');$('backLink').textContent=t('Tape Management','إدارة الأشرطة');
  const flags=new Map((effective||[]).map(x=>[x.functionKey,!!x.isEnabled]));
  $('printButton').hidden=!(session?.permissions||[]).includes('tape.print')||flags.get('tape.printing')===false;
  const generated=new Date().toLocaleString(arabic?'ar-KW':'en-GB');
  $('reportHost').innerHTML=`
    <header class="report-head">
      <img src="/assets/branding/diwan-al-amiri-crest.png" alt="${esc(t('Diwan Al Amiri crest','شعار الديوان الأميري'))}" />
      <div><h1>${esc(t('Diwan Al Amiri','الديوان الأميري'))}</h1><p>Media Asset Management · MAM</p></div>
      <div class="report-meta"><div>${esc(t('Generated','تاريخ الإصدار'))}: ${esc(generated)}</div><div>${esc(t('Issued by','أصدره'))}: ${esc(session?.displayName||'—')}</div></div>
    </header>
    <section class="report-title"><h2>${esc(t('Official Tape Report','تقرير رسمي عن الشريط'))}</h2><p>${esc(tape.tapeCode)} · ${esc(tape.title||t('Untitled','بدون عنوان'))}</p></section>
    <section class="report-barcode"><strong>${esc(tape.tapeCode)}</strong>${window.mamCode128.svg(barcode.payload,{height:90,ariaLabel:tape.tapeCode})}<div class="name">${esc(tape.title||t('Untitled','بدون عنوان'))}</div></section>
    <section class="report-grid">
      ${field(t('Tape number','رقم الشريط'),tape.tapeCode)}
      ${field(t('Tape name','اسم الشريط'),tape.title)}
      ${field(t('Legacy number','الرقم القديم'),tape.legacyNumber)}
      ${field(t('Tape format','نوع الشريط'),fmt(tape.tapeFormatCode))}
      ${field(t('Department','الإدارة'),dept(tape.ownerDepartment))}
      ${field(t('Physical condition','الحالة المادية'),tape.physicalCondition)}
      ${field(t('Digitization status','حالة الرقمنة'),tape.digitizationStatus)}
      ${field(t('Recording date','تاريخ التسجيل'),date(tape.recordingDate))}
      ${field(t('Duration','المدة'),duration(tape.durationSeconds))}
      ${field(t('Location','الموقع'),tapeLocation())}
      ${field(t('Room','الغرفة'),tape.room)}
      ${field(t('Cabinet','الخزانة'),tape.cabinet)}
      ${field(t('Shelf','الرف'),tape.shelf)}
      ${field(t('Bin','الصندوق'),tape.bin)}
      ${field(t('Description','الوصف'),tape.description,true)}
      ${field(t('Notes','الملاحظات'),tape.notes,true)}
      ${field(t('Created at','تاريخ الإنشاء'),new Date(tape.createdAtUtc).toLocaleString(arabic?'ar-KW':'en-GB'))}
      ${field(t('Created by','أنشأه'),tape.createdBy)}
      ${field(t('Updated at','آخر تحديث'),new Date(tape.updatedAtUtc).toLocaleString(arabic?'ar-KW':'en-GB'))}
      ${field(t('Updated by','آخر تعديل بواسطة'),tape.updatedBy)}
    </section>
    <footer class="report-footer"><span>${esc(t('Official MAM tape inventory report','تقرير رسمي من نظام إدارة الأصول الإعلامية'))}</span><span>${esc(tape.tapeCode)}</span></footer>`;
}
async function load(){
  const id=new URLSearchParams(window.location.search).get('id');if(!id){$('reportHost').innerHTML='<div class="state error">Tape id is required.</div>';return;}
  try{
    [session,tape,formats,departments,effective]=await Promise.all([
      json('/client-api/session'),json(`/client-api/tapes/${encodeURIComponent(id)}`),
      json('/client-api/tapes/formats/list'),json('/client-api/tapes/departments/list'),
      json('/client-api/system-functions/effective').catch(()=>[])
    ]);
    barcode=await json(`/client-api/tapes/${encodeURIComponent(id)}/barcode`);
    render();
  }catch(error){$('reportHost').innerHTML=`<div class="state error">${esc(error.message)}</div>`;}
}
$('languageButton').addEventListener('click',()=>{arabic=!arabic;render();});
$('printButton').addEventListener('click',async()=>{
  if(!tape||$('printButton').hidden)return;
  try{await json(`/client-api/tapes/${encodeURIComponent(tape.tapeId)}/print-events`,{method:'POST',body:JSON.stringify({kind:'report',labelType:null,widthMm:210,heightMm:297})});}catch{}
  window.print();
});
load();
})();