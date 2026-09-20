(() => {
'use strict';

const API='/client-api/tapes';
if(new URLSearchParams(location.search).get('embedded')==='1')document.body.classList.add('embedded');
let arabic=true;
let permissions=new Set();
let features=new Map();
let formats=[];
let departments=[];
let items=[];
let total=0;

const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const t=(en,ar)=>arabic?ar:en;
const can=p=>permissions.has(p);
const enabled=k=>features.get(k)!==false;
const nullIfBlank=v=>{const x=String(v??'').trim();return x||null;};

async function json(url,options={}){
  const response=await fetch(url,{cache:'no-store',...options,headers:{Accept:'application/json',...(options.body?{'Content-Type':'application/json'}:{}),...(options.headers||{})}});
  const payload=await response.json().catch(()=>null);
  if(!response.ok){
    const error=new Error(payload?.detail||`HTTP ${response.status}`);
    error.status=response.status;error.payload=payload;throw error;
  }
  return payload;
}

function state(message,kind=''){
  const host=$('t22Status');if(!host)return;
  host.innerHTML=message?`<div class="state ${kind}">${esc(message)}</div>`:'';
}

function updateStaticText(){
  document.documentElement.lang=arabic?'ar':'en';
  document.documentElement.dir=arabic?'rtl':'ltr';
  $('t22Language').textContent=arabic?'English':'العربية';
  $('t22Back').textContent=t('Back to MAM','العودة للنظام');
  $('t22Title').textContent=t('Tape Management','إدارة الأشرطة');
  $('t22Subtitle').textContent=t('Physical tape catalog, barcode search, labels and printable reports. Digitization is performed outside MAM.','إدارة وفهرسة الأشرطة المادية والبحث والباركود والطباعة. الرقمنة تتم خارج MAM.');
  $('t22Create').textContent=t('Add tape','إضافة شريط');
  $('t22SearchLabel').textContent=t('Text search','بحث نصي');
  $('t22ScanLabel').textContent=t('Barcode scan','مسح الباركود');
  $('t22FormatFilterLabel').textContent=t('Format','النوع');
  $('t22DepartmentFilterLabel').textContent=t('Department','الإدارة');
  $('t22StatusFilterLabel').textContent=t('Digitization status','حالة الرقمنة');
  $('t22Search').textContent=t('Search','بحث');
  $('t22ScanButton').textContent=t('Open scanned tape','فتح نتيجة المسح');
  $('t22Reset').textContent=t('Clear filters','مسح الفلاتر');
  $('t22ManageFormats').textContent=t('Manage formats','إدارة الأنواع');
  $('t22ManageDepartments').textContent=t('Manage departments','إدارة الإدارات');
  $('t22ResultsTitle').textContent=t('Tapes','الأشرطة');
  $('t22Query').placeholder=t('Tape code, name, legacy number, department, location…','رقم الشريط، الاسم، الرقم القديم، الإدارة، الموقع...');
  $('t22Scan').placeholder=t('Scan barcode or paste the full value','امسح الباركود أو الصق القيمة');
  renderFilterOptions();
  renderRows();
}

function displayFormat(code){
  const row=formats.find(x=>String(x.code).toLowerCase()===String(code||'').toLowerCase());
  return row?(arabic?row.nameAr:row.nameEn):(code||'—');
}
function displayDepartment(code){
  const row=departments.find(x=>String(x.code).toLowerCase()===String(code||'').toLowerCase());
  return row?(arabic?row.nameAr:row.nameEn):(code||'—');
}
function displayStatus(value){
  const map={
    NotDigitized:['Not digitized','غير مرقمن'],
    SentForDigitization:['Sent for digitization','مرسل للرقمنة'],
    Digitized:['Digitized','تمت الرقمنة'],
    QcPending:['QC pending','بانتظار فحص الجودة'],
    Approved:['Approved','معتمد'],
    Rejected:['Rejected','مرفوض']
  };
  return map[value]?.[arabic?1:0]||value||'—';
}
function displayCondition(value){
  const map={Good:['Good','جيدة'],Fair:['Fair','متوسطة'],Poor:['Poor','ضعيفة'],Damaged:['Damaged','تالفة']};
  return map[value]?.[arabic?1:0]||value||'—';
}

async function bootstrap(){
  state(t('Loading tape management…','جاري تحميل إدارة الأشرطة…'),'loading');
  try{
    const [session,effective]=await Promise.all([
      json('/client-api/session'),
      json('/client-api/system-functions/effective').catch(()=>[])
    ]);
    permissions=new Set(session.permissions||[]);
    features=new Map((effective||[]).map(x=>[x.functionKey,!!x.isEnabled]));
    if(!can('tape.view'))throw Object.assign(new Error(t('You do not have permission to view tape management.','لا توجد صلاحية لعرض إدارة الأشرطة.')),{status:403});
    if(!enabled('tape.management'))throw Object.assign(new Error(t('Tape Management is disabled in System Functions.','إدارة الأشرطة معطلة من وظائف النظام.')),{status:409});
    await Promise.all([loadFormats(),loadDepartments()]);
    bind();
    updateStaticText();
    await loadList();
    const initialScan=new URLSearchParams(location.search).get('scan');
    if(initialScan){$('t22Scan').value=initialScan;await resolveScan();}
    else state('');
  }catch(error){
    state(error.message,'error');
    disableAll();
  }
}

function disableAll(){
  document.querySelectorAll('button,input,select').forEach(x=>x.disabled=true);
}

async function loadFormats(){
  formats=await json(`${API}/formats/list?includeInactive=${can('tape.formats.manage')?'true':'false'}`);
}
async function loadDepartments(){
  departments=await json(`${API}/departments/list?includeInactive=${can('tape.departments.manage')?'true':'false'}`);
}

function renderFilterOptions(){
  const currentFormat=$('t22FormatFilter')?.value||'';
  const currentDepartment=$('t22DepartmentFilter')?.value||'';
  const currentStatus=$('t22DigitizationFilter')?.value||'';
  if($('t22FormatFilter'))$('t22FormatFilter').innerHTML=`<option value="">${esc(t('All','الكل'))}</option>${formats.filter(x=>x.isActive).map(x=>`<option value="${esc(x.code)}">${esc(arabic?x.nameAr:x.nameEn)}</option>`).join('')}`;
  if($('t22DepartmentFilter'))$('t22DepartmentFilter').innerHTML=`<option value="">${esc(t('All','الكل'))}</option>${departments.filter(x=>x.isActive).map(x=>`<option value="${esc(x.code)}">${esc(arabic?x.nameAr:x.nameEn)}</option>`).join('')}`;
  const statuses=['NotDigitized','SentForDigitization','Digitized','QcPending','Approved','Rejected'];
  if($('t22DigitizationFilter'))$('t22DigitizationFilter').innerHTML=`<option value="">${esc(t('All','الكل'))}</option>${statuses.map(x=>`<option value="${x}">${esc(displayStatus(x))}</option>`).join('')}`;
  if([...$('t22FormatFilter')?.options||[]].some(x=>x.value===currentFormat))$('t22FormatFilter').value=currentFormat;
  if([...$('t22DepartmentFilter')?.options||[]].some(x=>x.value===currentDepartment))$('t22DepartmentFilter').value=currentDepartment;
  if([...$('t22DigitizationFilter')?.options||[]].some(x=>x.value===currentStatus))$('t22DigitizationFilter').value=currentStatus;
}

async function loadList(){
  state(t('Loading tapes…','جاري تحميل الأشرطة…'),'loading');
  try{
    const q=$('t22Query')?.value.trim()||'';
    const page=q
      ?await json(`${API}/search?query=${encodeURIComponent(q)}&limit=500`)
      :await json(`${API}?limit=500`);
    items=Array.isArray(page.items)?page.items:[];
    total=Number(page.total??items.length);
    renderRows();
    state('');
  }catch(error){state(error.message,'error');}
}

function filtered(){
  const f=$('t22FormatFilter')?.value||'';
  const d=$('t22DepartmentFilter')?.value||'';
  const s=$('t22DigitizationFilter')?.value||'';
  return items.filter(x=>(!f||x.tapeFormatCode===f)&&(!d||x.ownerDepartment===d)&&(!s||x.digitizationStatus===s));
}

function renderRows(){
  const host=$('t22Rows');if(!host)return;
  $('t22Count').textContent=String(filtered().length);
  $('t22Create').hidden=!can('tape.create');
  $('t22ManageFormats').hidden=!can('tape.formats.manage');
  $('t22ManageDepartments').hidden=!can('tape.departments.manage');

  const rows=filtered();
  if(!rows.length){
    host.innerHTML=`<div class="state">${esc(t('No tapes match the current filters.','لا توجد أشرطة مطابقة للفلاتر الحالية.'))}</div>`;
    return;
  }
  host.innerHTML=rows.map(x=>{
    const location=[x.room,x.cabinet,x.shelf,x.bin].filter(Boolean).join(' / ')||'—';
    return `<article class="t22-row" data-id="${esc(x.tapeId)}">
      <div><div class="t22-code">${esc(x.tapeCode)}</div><h3>${esc(x.title||t('Untitled','بدون عنوان'))}</h3><span class="badge">${esc(displayFormat(x.tapeFormatCode))}</span><span class="badge">${esc(displayStatus(x.digitizationStatus))}</span></div>
      <div class="t22-meta">
        <div><strong>${esc(t('Department','الإدارة'))}:</strong> ${esc(displayDepartment(x.ownerDepartment))}</div>
        <div><strong>${esc(t('Legacy no.','الرقم القديم'))}:</strong> ${esc(x.legacyNumber||'—')} · <strong>${esc(t('Condition','الحالة'))}:</strong> ${esc(displayCondition(x.physicalCondition))}</div>
        <div><strong>${esc(t('Location','الموقع'))}:</strong> ${esc(location)}</div>
      </div>
      <div class="t22-row-actions">
        <button type="button" data-view="${esc(x.tapeId)}">${esc(t('View','عرض'))}</button>
        ${can('tape.edit')?`<button type="button" data-edit="${esc(x.tapeId)}">${esc(t('Edit','تعديل'))}</button>`:''}
        ${can('tape.print')&&enabled('tape.printing')?`<button type="button" data-label="${esc(x.tapeId)}">${esc(t('Print barcode','طباعة الباركود'))}</button><button type="button" data-report="${esc(x.tapeId)}">${esc(t('Tape report','تقرير الشريط'))}</button>`:''}
        ${can('tape.delete')?`<button type="button" class="danger" data-delete="${esc(x.tapeId)}">${esc(t('Delete','حذف'))}</button>`:''}
      </div>
    </article>`;
  }).join('');

  host.querySelectorAll('[data-view]').forEach(b=>b.addEventListener('click',()=>openTape(items.find(x=>x.tapeId===b.dataset.view),true)));
  host.querySelectorAll('[data-edit]').forEach(b=>b.addEventListener('click',()=>openTape(items.find(x=>x.tapeId===b.dataset.edit),false)));
  host.querySelectorAll('[data-label]').forEach(b=>b.addEventListener('click',()=>openLabel(items.find(x=>x.tapeId===b.dataset.label))));
  host.querySelectorAll('[data-report]').forEach(b=>b.addEventListener('click',()=>window.open(`/tape-report.html?id=${encodeURIComponent(b.dataset.report)}`,'_blank','noopener')));
  host.querySelectorAll('[data-delete]').forEach(b=>b.addEventListener('click',()=>deleteTape(items.find(x=>x.tapeId===b.dataset.delete))));
}

function bind(){
  $('t22Language').addEventListener('click',()=>{arabic=!arabic;updateStaticText();});
  $('t22Search').addEventListener('click',()=>void loadList());
  $('t22Query').addEventListener('keydown',e=>{if(e.key==='Enter')void loadList();});
  $('t22ScanButton').addEventListener('click',()=>void resolveScan());
  $('t22Scan').addEventListener('keydown',e=>{if(e.key==='Enter')void resolveScan();});
  ['t22FormatFilter','t22DepartmentFilter','t22DigitizationFilter'].forEach(id=>$(id).addEventListener('change',renderRows));
  $('t22Reset').addEventListener('click',()=>{$('t22Query').value='';$('t22Scan').value='';$('t22FormatFilter').value='';$('t22DepartmentFilter').value='';$('t22DigitizationFilter').value='';void loadList();});
  $('t22Create').addEventListener('click',()=>openTape(null,false));
  $('t22ManageFormats').addEventListener('click',()=>openFormatManager());
  $('t22ManageDepartments').addEventListener('click',()=>openDepartmentManager());
}

async function resolveScan(){
  const value=$('t22Scan').value.trim();
  if(!value){state(t('Scan a barcode or enter a tape code first.','امسح الباركود أو أدخل رقم الشريط أولًا.'),'error');return;}
  state(t('Resolving barcode…','جاري قراءة الباركود…'),'loading');
  try{
    const tape=await json(`${API}/resolve/${encodeURIComponent(value)}`);
    state('');
    openTape(tape,true);
  }catch(error){state(error.message,'error');}
}

function modal(title,body,actions=''){
  const host=$('t22ModalHost');
  host.innerHTML=`<div class="t22-modal-backdrop"><section class="t22-modal" role="dialog" aria-modal="true"><header class="t22-modal-head"><h2>${esc(title)}</h2><button type="button" data-close>×</button></header><div class="t22-modal-body">${body}</div><footer class="t22-modal-actions">${actions}<button type="button" data-close>${esc(t('Close','إغلاق'))}</button></footer></section></div>`;
  host.querySelectorAll('[data-close]').forEach(b=>b.addEventListener('click',()=>host.innerHTML=''));
  host.querySelector('.t22-modal-backdrop').addEventListener('click',e=>{if(e.target===e.currentTarget)host.innerHTML='';});
  return host.querySelector('.t22-modal');
}

function optionRows(rows,value,label){
  return `<option value="">${esc(t('Unknown / not set','غير معروف / غير محدد'))}</option>${rows.filter(x=>x.isActive||x.code===value).map(x=>`<option value="${esc(x.code)}" ${x.code===value?'selected':''}>${esc(label(x))}</option>`).join('')}`;
}

function tapeForm(tape,readOnly){
  const v=(key,def='')=>tape?.[key]??def;
  const ro=readOnly?'disabled':'';
  const conditions=['','Good','Fair','Poor','Damaged'];
  const statuses=['NotDigitized','SentForDigitization','Digitized','QcPending','Approved','Rejected'];
  return `<div class="t22-form">
    <label><span>${esc(t('Tape number','رقم الشريط'))}</span><input value="${esc(v('tapeCode',t('Allocated automatically','يخصص تلقائيًا')))}" disabled /></label>
    <label><span>${esc(t('Tape name','اسم الشريط'))}</span><input id="tfTitle" maxlength="512" value="${esc(v('title'))}" ${ro}/></label>
    <label><span>${esc(t('Legacy number','الرقم القديم'))}</span><input id="tfLegacy" maxlength="128" value="${esc(v('legacyNumber'))}" ${ro}/></label>
    <label><span>${esc(t('Tape format','نوع الشريط'))}</span><select id="tfFormat" ${ro}>${optionRows(formats,v('tapeFormatCode'),x=>arabic?x.nameAr:x.nameEn)}</select></label>
    <label><span>${esc(t('Department','الإدارة'))}</span><select id="tfDepartment" ${ro}>${optionRows(departments,v('ownerDepartment'),x=>arabic?x.nameAr:x.nameEn)}</select></label>
    <label><span>${esc(t('Physical condition','الحالة المادية'))}</span><select id="tfCondition" ${ro}>${conditions.map(x=>`<option value="${x}" ${x===v('physicalCondition')?'selected':''}>${esc(x?displayCondition(x):t('Unknown','غير معروف'))}</option>`).join('')}</select></label>
    ${tape?`<label><span>${esc(t('Digitization status','حالة الرقمنة'))}</span><select id="tfDigitization" ${ro}>${statuses.map(x=>`<option value="${x}" ${x===v('digitizationStatus')?'selected':''}>${esc(displayStatus(x))}</option>`).join('')}</select></label>`:''}
    <label><span>${esc(t('Duration (seconds)','المدة بالثواني'))}</span><input id="tfDuration" type="number" min="0" value="${esc(v('durationSeconds'))}" ${ro}/></label>
    <label><span>${esc(t('Recording date','تاريخ التسجيل'))}</span><input id="tfDate" type="date" value="${esc(v('recordingDate'))}" ${ro}/></label>
    <label><span>${esc(t('Room','الغرفة'))}</span><input id="tfRoom" maxlength="128" value="${esc(v('room'))}" ${ro}/></label>
    <label><span>${esc(t('Cabinet','الخزانة'))}</span><input id="tfCabinet" maxlength="128" value="${esc(v('cabinet'))}" ${ro}/></label>
    <label><span>${esc(t('Shelf','الرف'))}</span><input id="tfShelf" maxlength="128" value="${esc(v('shelf'))}" ${ro}/></label>
    <label><span>${esc(t('Bin','الصندوق'))}</span><input id="tfBin" maxlength="128" value="${esc(v('bin'))}" ${ro}/></label>
    <label class="wide"><span>${esc(t('Description','الوصف'))}</span><textarea id="tfDescription" maxlength="4000" ${ro}>${esc(v('description'))}</textarea></label>
    <label class="wide"><span>${esc(t('Notes','الملاحظات'))}</span><textarea id="tfNotes" maxlength="4000" ${ro}>${esc(v('notes'))}</textarea></label>
  </div>
  <div id="tfState"></div>`;
}

function openTape(tape,readOnly){
  if(!tape&&readOnly)return;
  const title=tape?(readOnly?t('Tape details','تفاصيل الشريط'):t('Edit tape','تعديل الشريط')):t('Register tape','إضافة شريط');
  const actions=readOnly
    ?`${can('tape.edit')?`<button type="button" data-switch-edit>${esc(t('Edit','تعديل'))}</button>`:''}${can('tape.print')&&tape&&enabled('tape.printing')?`<button type="button" data-form-label class="primary">${esc(t('Print barcode','طباعة الباركود'))}</button><button type="button" data-form-report>${esc(t('Tape report','تقرير الشريط'))}</button>`:''}`
    :`<button type="button" class="primary" data-save>${esc(t('Save','حفظ'))}</button>`;
  const root=modal(title,tapeForm(tape,readOnly),actions);
  root.querySelector('[data-switch-edit]')?.addEventListener('click',()=>openTape(tape,false));
  root.querySelector('[data-form-label]')?.addEventListener('click',()=>openLabel(tape));
  root.querySelector('[data-form-report]')?.addEventListener('click',()=>window.open(`/tape-report.html?id=${encodeURIComponent(tape.tapeId)}`,'_blank','noopener'));
  root.querySelector('[data-save]')?.addEventListener('click',()=>void saveTape(tape,root));
}

function formValue(root,id){return root.querySelector('#'+id)?.value??'';}
async function saveTape(tape,root){
  const output=root.querySelector('#tfState');
  const duration=formValue(root,'tfDuration');
  const body={
    legacyNumber:nullIfBlank(formValue(root,'tfLegacy')),
    title:nullIfBlank(formValue(root,'tfTitle')),
    description:nullIfBlank(formValue(root,'tfDescription')),
    tapeFormatCode:nullIfBlank(formValue(root,'tfFormat')),
    physicalCondition:nullIfBlank(formValue(root,'tfCondition')),
    ownerDepartment:nullIfBlank(formValue(root,'tfDepartment')),
    durationSeconds:duration===''?null:Number(duration),
    recordingDate:nullIfBlank(formValue(root,'tfDate')),
    room:nullIfBlank(formValue(root,'tfRoom')),
    cabinet:nullIfBlank(formValue(root,'tfCabinet')),
    shelf:nullIfBlank(formValue(root,'tfShelf')),
    bin:nullIfBlank(formValue(root,'tfBin')),
    notes:nullIfBlank(formValue(root,'tfNotes'))
  };
  if(tape){body.digitizationStatus=formValue(root,'tfDigitization')||tape.digitizationStatus;body.expectedVersion=tape.version;}
  output.innerHTML=`<div class="state loading">${esc(t('Saving…','جاري الحفظ…'))}</div>`;
  try{
    const saved=tape
      ?await json(`${API}/${encodeURIComponent(tape.tapeId)}`,{method:'PUT',body:JSON.stringify(body)})
      :await json(API,{method:'POST',body:JSON.stringify(body)});
    $('t22ModalHost').innerHTML='';
    await Promise.all([loadDepartments(),loadList()]);
    state(tape?t('Tape updated.','تم تحديث الشريط.'):t(`Tape ${saved.tapeCode} created.`,`تم إنشاء الشريط ${saved.tapeCode}.`),'ok');
  }catch(error){output.innerHTML=`<div class="state error">${esc(error.message)}</div>`;}
}

async function deleteTape(tape){
  if(!tape||!confirm(t(`Delete ${tape.tapeCode} permanently?`,`حذف ${tape.tapeCode} نهائيًا؟`)))return;
  try{
    await json(`${API}/${encodeURIComponent(tape.tapeId)}?expectedVersion=${encodeURIComponent(tape.version)}`,{method:'DELETE'});
    await loadList();state(t('Tape deleted.','تم حذف الشريط.'),'ok');
  }catch(error){state(error.message,'error');}
}

function labelPresetOptions(){
  return [
    ['50x25','50 × 25 mm',50,25],
    ['60x30','60 × 30 mm',60,30],
    ['70x40','70 × 40 mm',70,40],
    ['100x50','100 × 50 mm',100,50],
    ['custom',t('Custom','مخصص'),60,30]
  ];
}

async function openLabel(tape){
  if(!tape)return;
  try{
    const descriptor=await json(`${API}/${encodeURIComponent(tape.tapeId)}/barcode`);
    const options=labelPresetOptions();
    const root=modal(t('Print barcode label','طباعة ملصق الباركود'),`
      <div class="t22-label-controls">
        <label><span>${esc(t('Label type','نوع الملصق'))}</span><select id="lbType"><option value="Tape">Tape Label</option><option value="Case">Case Label</option></select></label>
        <label><span>${esc(t('Size','المقاس'))}</span><select id="lbPreset">${options.map(x=>`<option value="${x[0]}">${esc(x[1])}</option>`).join('')}</select></label>
        <label><span>${esc(t('Width mm','العرض mm'))}</span><input id="lbWidth" type="number" min="20" max="500" step="1" value="50"/></label>
        <label><span>${esc(t('Height mm','الارتفاع mm'))}</span><input id="lbHeight" type="number" min="15" max="500" step="1" value="25"/></label>
      </div>
      <div class="t22-label-preview" id="lbPreview"></div>
      <p><strong>${esc(descriptor.tapeCode)}</strong> · ${esc(descriptor.tapeName)}</p>
      <small>${esc(t('Barcode payload includes the durable tape number plus the URI-encoded tape name.','قيمة الباركود تحتوي رقم الشريط الثابت مع اسم الشريط بترميز URI صالح لـ Code 128.'))}</small>
      <div id="lbState"></div>`,
      `<button type="button" class="primary" data-print-label>${esc(t('Print','طباعة'))}</button>`
    );
    const refresh=()=>{
      const preset=root.querySelector('#lbPreset').value;
      const match=options.find(x=>x[0]===preset);
      if(match&&preset!=='custom'){root.querySelector('#lbWidth').value=match[2];root.querySelector('#lbHeight').value=match[3];}
      const w=Number(root.querySelector('#lbWidth').value||50),h=Number(root.querySelector('#lbHeight').value||25);
      const preview=root.querySelector('#lbPreview');
      preview.style.width=`${Math.min(100,w)}mm`;preview.style.minHeight=`${Math.min(55,h)}mm`;
      preview.innerHTML=`<strong>${esc(root.querySelector('#lbType').value)} · ${esc(descriptor.tapeCode)}</strong>${window.mamCode128.svg(descriptor.payload,{height:70,ariaLabel:descriptor.tapeCode})}<small>${esc(descriptor.tapeName)}</small>`;
    };
    root.querySelector('#lbPreset').addEventListener('change',refresh);
    root.querySelector('#lbType').addEventListener('change',refresh);
    root.querySelector('#lbWidth').addEventListener('input',()=>{root.querySelector('#lbPreset').value='custom';refresh();});
    root.querySelector('#lbHeight').addEventListener('input',()=>{root.querySelector('#lbPreset').value='custom';refresh();});
    root.querySelector('[data-print-label]').addEventListener('click',()=>void printLabel(tape,descriptor,root));
    refresh();
  }catch(error){state(error.message,'error');}
}

async function printLabel(tape,descriptor,root){
  const width=Number(root.querySelector('#lbWidth').value),height=Number(root.querySelector('#lbHeight').value),labelType=root.querySelector('#lbType').value;
  if(!(width>0&&width<=500&&height>0&&height<=500)){root.querySelector('#lbState').innerHTML=`<div class="state error">${esc(t('Invalid label dimensions.','مقاس الملصق غير صالح.'))}</div>`;return;}
  const printWindow=window.open('','_blank','noopener');
  if(!printWindow){root.querySelector('#lbState').innerHTML=`<div class="state error">${esc(t('Allow pop-ups to print labels.','اسمح بالنوافذ المنبثقة لطباعة الملصق.'))}</div>`;return;}
  try{
    await json(`${API}/${encodeURIComponent(tape.tapeId)}/print-events`,{method:'POST',body:JSON.stringify({kind:'label',labelType,widthMm:width,heightMm:height})});
  }catch{}
  const dir=arabic?'rtl':'ltr';
  printWindow.document.write(`<!doctype html><html dir="${dir}"><head><meta charset="utf-8"><title>${esc(tape.tapeCode)}</title><style>@page{size:${width}mm ${height}mm;margin:0}*{box-sizing:border-box}html,body{width:${width}mm;height:${height}mm;margin:0;font-family:Arial,sans-serif}.label{width:100%;height:100%;padding:2.5mm;display:flex;flex-direction:column;justify-content:center;gap:1.2mm;overflow:hidden}.head{font-weight:800;font-size:10pt;color:#000;white-space:nowrap}.name{font-size:8pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.kind{font-size:6.5pt;color:#444}svg{min-height:10mm;max-height:45%}</style></head><body><div class="label"><div class="kind">${esc(labelType)} · MAM</div><div class="head">${esc(tape.tapeCode)}</div>${window.mamCode128.svg(descriptor.payload,{height:70,ariaLabel:tape.tapeCode})}<div class="name">${esc(descriptor.tapeName)}</div></div><script>window.onload=()=>{window.print();setTimeout(()=>window.close(),300)}<\/script></body></html>`);
  printWindow.document.close();
}

function openFormatManager(){
  const rows=formats.slice().sort((a,b)=>a.sortOrder-b.sortOrder);
  const root=modal(t('Tape format management','إدارة أنواع الأشرطة'),`
    <div class="t22-form"><label><span>Code</span><input id="fmCode"/></label><label><span>English</span><input id="fmEn"/></label><label><span>العربية</span><input id="fmAr" dir="rtl"/></label><label><span>${esc(t('Order','الترتيب'))}</span><input id="fmSort" type="number" value="100"/></label><label><span>${esc(t('Active','نشط'))}</span><select id="fmActive"><option value="true">${esc(t('Yes','نعم'))}</option><option value="false">${esc(t('No','لا'))}</option></select></label></div>
    <button type="button" class="primary" data-add-format>${esc(t('Add / update format','إضافة / تحديث النوع'))}</button><div id="fmState"></div>
    <table class="t22-admin-table"><thead><tr><th>Code</th><th>English</th><th>العربية</th><th>${esc(t('Active','نشط'))}</th><th></th></tr></thead><tbody>${rows.map(x=>`<tr><td>${esc(x.code)}</td><td>${esc(x.nameEn)}</td><td>${esc(x.nameAr)}</td><td>${x.isActive?'✓':'—'}</td><td><button type="button" data-edit-format="${esc(x.code)}">${esc(t('Edit','تعديل'))}</button></td></tr>`).join('')}</tbody></table>`);
  const fill=x=>{root.querySelector('#fmCode').value=x.code;root.querySelector('#fmCode').disabled=true;root.querySelector('#fmEn').value=x.nameEn;root.querySelector('#fmAr').value=x.nameAr;root.querySelector('#fmSort').value=x.sortOrder;root.querySelector('#fmActive').value=String(x.isActive);};
  root.querySelectorAll('[data-edit-format]').forEach(b=>b.addEventListener('click',()=>fill(rows.find(x=>x.code===b.dataset.editFormat))));
  root.querySelector('[data-add-format]').addEventListener('click',()=>void saveFormat(root));
}
async function saveFormat(root){
  const code=root.querySelector('#fmCode').value.trim().toUpperCase().replace(/\s+/g,'_');
  const body={code,nameEn:root.querySelector('#fmEn').value.trim(),nameAr:root.querySelector('#fmAr').value.trim(),isActive:root.querySelector('#fmActive').value==='true',sortOrder:Number(root.querySelector('#fmSort').value||0)};
  const out=root.querySelector('#fmState');
  if(!code||!body.nameEn||!body.nameAr){out.innerHTML=`<div class="state error">${esc(t('Code and both names are required.','الكود والاسمان مطلوبون.'))}</div>`;return;}
  try{await json(`${API}/formats/${encodeURIComponent(code)}`,{method:'PUT',body:JSON.stringify(body)});await loadFormats();renderFilterOptions();openFormatManager();}catch(error){out.innerHTML=`<div class="state error">${esc(error.message)}</div>`;}
}

function openDepartmentManager(){
  const rows=departments.slice().sort((a,b)=>a.sortOrder-b.sortOrder);
  const root=modal(t('Department management','إدارة الإدارات'),`
    <p>${esc(t('Departments are the authoritative dropdown source used by Tape Management.','الإدارات هي المصدر المركزي للقائمة المنسدلة المستخدمة في إدارة الأشرطة.'))}</p>
    <div class="t22-form"><label><span>Code</span><input id="dmCode"/></label><label><span>English</span><input id="dmEn"/></label><label><span>العربية</span><input id="dmAr" dir="rtl"/></label><label><span>${esc(t('Order','الترتيب'))}</span><input id="dmSort" type="number" value="100"/></label><label><span>${esc(t('Active','نشط'))}</span><select id="dmActive"><option value="true">${esc(t('Yes','نعم'))}</option><option value="false">${esc(t('No','لا'))}</option></select></label></div>
    <button type="button" class="primary" data-add-dept>${esc(t('Add / update department','إضافة / تحديث الإدارة'))}</button><div id="dmState"></div>
    <table class="t22-admin-table"><thead><tr><th>Code</th><th>English</th><th>العربية</th><th>${esc(t('Active','نشط'))}</th><th></th></tr></thead><tbody>${rows.map(x=>`<tr><td>${esc(x.code)}</td><td>${esc(x.nameEn)}</td><td>${esc(x.nameAr)}</td><td>${x.isActive?'✓':'—'}</td><td><button type="button" data-edit-dept="${esc(x.code)}">${esc(t('Edit','تعديل'))}</button></td></tr>`).join('')}</tbody></table>`);
  const fill=x=>{root.querySelector('#dmCode').value=x.code;root.querySelector('#dmCode').disabled=true;root.querySelector('#dmEn').value=x.nameEn;root.querySelector('#dmAr').value=x.nameAr;root.querySelector('#dmSort').value=x.sortOrder;root.querySelector('#dmActive').value=String(x.isActive);};
  root.querySelectorAll('[data-edit-dept]').forEach(b=>b.addEventListener('click',()=>fill(rows.find(x=>x.code===b.dataset.editDept))));
  root.querySelector('[data-add-dept]').addEventListener('click',()=>void saveDepartment(root));
}
async function saveDepartment(root){
  const code=root.querySelector('#dmCode').value.trim();
  const body={code,nameEn:root.querySelector('#dmEn').value.trim(),nameAr:root.querySelector('#dmAr').value.trim(),isActive:root.querySelector('#dmActive').value==='true',sortOrder:Number(root.querySelector('#dmSort').value||0)};
  const out=root.querySelector('#dmState');
  if(!code||!body.nameEn||!body.nameAr){out.innerHTML=`<div class="state error">${esc(t('Code and both names are required.','الكود والاسمان مطلوبون.'))}</div>`;return;}
  try{await json(`${API}/departments/${encodeURIComponent(code)}`,{method:'PUT',body:JSON.stringify(body)});await loadDepartments();renderFilterOptions();openDepartmentManager();}catch(error){out.innerHTML=`<div class="state error">${esc(error.message)}</div>`;}
}

bootstrap();
})();