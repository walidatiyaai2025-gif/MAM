(() => {
'use strict';

const SESSION={Preparing:0,Ready:1,Running:2,Completed:3,CompletedWithErrors:4,Cancelled:5};
const ITEM={Pending:0,Uploading:1,Uploaded:2,AlreadyExists:3,Linked:4,Failed:5,Unsupported:6,Cancelled:7};
const fallbackAllowed=['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v','.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma','.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp','.pdf','.doc','.docx','.rtf','.txt','.odt'];
const stateModel={
  rootName:'',
  files:new Map(),
  session:null,
  running:false,
  abort:null,
  scan:null,
  selectedAt:0
};

const isArabic=()=>typeof arabic!=='undefined'?!!arabic:document.documentElement.lang==='ar';
const t=(en,ar)=>isArabic()?ar:en;
const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const allowed=()=>new Set((typeof p03AllowedExtensions!=='undefined'?p03AllowedExtensions:fallbackAllowed).map(x=>x.toLowerCase()));
const ext=name=>{const i=String(name||'').lastIndexOf('.');return i<0?'':String(name).slice(i).toLowerCase();};
const terminal=s=>[ITEM.Uploaded,ITEM.AlreadyExists,ITEM.Linked,ITEM.Failed,ITEM.Unsupported,ITEM.Cancelled].includes(Number(s));
const successful=s=>[ITEM.Uploaded,ITEM.AlreadyExists,ITEM.Linked].includes(Number(s));
const resumableSession=s=>[SESSION.Ready,SESSION.Running,SESSION.CompletedWithErrors].includes(Number(s));
const itemLabel=s=>({
  [ITEM.Pending]:t('Pending','في الانتظار'),
  [ITEM.Uploading]:t('Uploading','قيد الرفع'),
  [ITEM.Uploaded]:t('Uploaded','تم الرفع'),
  [ITEM.AlreadyExists]:t('Already exists','موجود مسبقًا'),
  [ITEM.Linked]:t('Reclassified','أعيد تصنيفه'),
  [ITEM.Failed]:t('Failed','فشل'),
  [ITEM.Unsupported]:t('Unsupported','غير مدعوم'),
  [ITEM.Cancelled]:t('Cancelled','ملغي')
})[Number(s)]||String(s);

function formatBytes(value){
  let n=Math.max(0,Number(value||0)),i=0;const units=['B','KB','MB','GB','TB'];
  while(n>=1024&&i<units.length-1){n/=1024;i++;}
  return `${n.toFixed(i?1:0)} ${units[i]}`;
}

function injectStyle(){
  if(document.getElementById('p137BulkImportStyle'))return;
  const style=document.createElement('style');
  style.id='p137BulkImportStyle';
  style.textContent=`
    .p140-upload-tabs{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin:0 0 18px;padding:7px;border:1px solid #dbe6ef;background:#fff;border-radius:14px;box-shadow:0 5px 18px rgba(9,44,75,.05)}
    .p140-upload-tab{min-height:48px;border:1px solid transparent;background:transparent;color:#173b5d;border-radius:10px;font-weight:800;cursor:pointer;display:flex;align-items:center;justify-content:center;gap:8px}
    .p140-upload-tab[aria-selected="true"]{background:#082f54;color:#fff;border-color:#082f54;box-shadow:0 5px 14px rgba(8,47,84,.18)}
    .p140-upload-pane[hidden]{display:none!important}
    .p137-bulk-card{margin:0 0 18px;border:1px solid #d8dee8;border-radius:18px;background:linear-gradient(135deg,#fff 0%,#f8fafc 100%);box-shadow:0 8px 26px rgba(7,24,46,.06);overflow:hidden}
    .p137-bulk-head{display:flex;align-items:flex-start;justify-content:space-between;gap:18px;padding:20px 22px;border-bottom:1px solid #eaecf0}
    .p137-bulk-title{display:flex;gap:13px;align-items:flex-start}.p137-bulk-title i{font-size:24px;color:#99731f}.p137-bulk-title h3{margin:0;color:#0a2342;font-size:19px}.p137-bulk-title p{margin:5px 0 0;color:#667085;font-size:13px;line-height:1.6}
    .p137-badge{display:inline-flex;align-items:center;gap:6px;padding:6px 9px;border-radius:999px;background:#fff7e6;color:#7a5512;font-weight:700;font-size:11px;white-space:nowrap}
    .p137-body{padding:20px 22px}.p137-grid{display:grid;grid-template-columns:minmax(0,1.1fr) minmax(320px,.9fr);gap:18px}.p137-panel{border:1px solid #eaecf0;border-radius:14px;background:#fff;padding:16px}
    .p137-drop{position:relative;display:flex;min-height:148px;border:2px dashed #b8c1cc;border-radius:14px;align-items:center;justify-content:center;text-align:center;padding:18px;cursor:pointer;transition:.18s}.p137-drop:hover,.p137-drop.dragging{border-color:#b58a2a;background:#fffaf0}.p137-drop input{position:absolute;inset:0;opacity:0;cursor:pointer}.p137-drop i{display:block;font-size:30px;color:#b58a2a;margin-bottom:8px}.p137-drop strong{display:block;color:#0a2342}.p137-drop span{display:block;color:#667085;font-size:12px;margin-top:5px}
    .p137-actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:14px}.p137-btn{border:1px solid #d0d5dd;border-radius:9px;background:#fff;color:#0a2342;padding:9px 13px;font-weight:650;cursor:pointer}.p137-btn.primary{background:#b58a2a;color:#fff;border-color:#b58a2a}.p137-btn.danger{color:#b42318;border-color:#fecdca}.p137-btn:disabled{opacity:.45;cursor:not-allowed}
    .p137-rule{margin-top:12px;padding:10px 12px;border-radius:10px;background:#eff8ff;color:#175cd3;font-size:12px;line-height:1.6}.p137-status{margin-top:12px;min-height:22px;color:#475467;font-size:13px;white-space:pre-wrap}.p137-status.error{color:#b42318}.p137-status.success{color:#027a48}.p137-status.warn{color:#b54708}
    .p137-progress-row{margin-bottom:13px}.p137-progress-label{display:flex;justify-content:space-between;gap:10px;font-size:12px;color:#475467;margin-bottom:6px}.p137-progress{height:11px;background:#eaecf0;border-radius:999px;overflow:hidden}.p137-progress>span{display:block;height:100%;width:0;background:linear-gradient(90deg,#99731f,#d5ad4d);transition:width .15s}
    .p137-metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:12px}.p137-metric{padding:9px;border-radius:9px;background:#f8fafc}.p137-metric strong{display:block;color:#0a2342;font-size:16px}.p137-metric span{font-size:10px;color:#667085}
    .p137-list{margin-top:12px;max-height:250px;overflow:auto;border-top:1px solid #eaecf0}.p137-row{display:grid;grid-template-columns:105px minmax(0,1fr);gap:9px;padding:8px 0;border-bottom:1px solid #f2f4f7;font-size:11px}.p137-row b{color:#344054}.p137-row span{overflow-wrap:anywhere;color:#667085}
    .p137-session-note{margin-top:10px;font-size:11px;color:#667085}.p137-hidden{display:none!important}
    @media(max-width:900px){.p137-grid{grid-template-columns:1fr}.p137-bulk-head{flex-direction:column}.p137-metrics{grid-template-columns:repeat(2,minmax(0,1fr))}}
  `;
  document.head.appendChild(style);
}

function markup(){
  return `
  <section id="p137BulkImportCard" class="p137-bulk-card" aria-label="${escapeHtml(t('Bulk Folder Import','استيراد مجلدات مجمّع'))}">
    <div class="p137-bulk-head">
      <div class="p137-bulk-title">
        <i class="bi bi-folder2-open"></i>
        <div>
          <h3>${escapeHtml(t('Bulk Folder Import','استيراد مجلدات مجمّع'))}</h3>
          <p>${escapeHtml(t('Select one folder, preserve resumable upload progress, create missing categories, skip duplicate media, and export a complete result report.','اختر مجلدًا واحدًا لرفع محتواه بصورة قابلة للاستكمال، وإنشاء التصنيفات الناقصة، وتخطي الميديا المكررة، وإخراج تقرير كامل بالنتيجة.'))}</p>
        </div>
      </div>
      <span class="p137-badge"><i class="bi bi-shield-check"></i> ${escapeHtml(t('CENTRAL API','الخدمة المركزية'))}</span>
    </div>
    <div class="p137-body">
      <div class="p137-grid">
        <div class="p137-panel">
          <label id="p137FolderDrop" class="p137-drop">
            <input id="p137FolderInput" type="file" webkitdirectory directory multiple aria-label="${escapeHtml(t('Select media folder','اختيار مجلد الميديا'))}">
            <div><i class="bi bi-folder-plus"></i><strong id="p137FolderName">${escapeHtml(stateModel.rootName||t('Choose a folder','اختر مجلدًا'))}</strong><span>${escapeHtml(t('All nested files are scanned locally before the manifest is sent.','يتم فحص كل الملفات الفرعية محليًا قبل إرسال قائمة الاستيراد.'))}</span></div>
          </label>
          <div class="p137-rule"><strong>${escapeHtml(t('Category rule:','قاعدة التصنيف:'))}</strong> ${escapeHtml(t('The first child folder is the category. Root-level files use the selected root folder name.','أول مجلد فرعي هو التصنيف، والملفات الموجودة مباشرة في الجذر تستخدم اسم المجلد الجذر.'))}</div>
          <div class="p137-actions">
            <button type="button" id="p137Start" class="p137-btn primary">${escapeHtml(t('Scan & start','فحص وبدء'))}</button>
            <button type="button" id="p137Resume" class="p137-btn">${escapeHtml(t('Resume / retry','استكمال / إعادة محاولة'))}</button>
            <button type="button" id="p137Pause" class="p137-btn">${escapeHtml(t('Pause','إيقاف مؤقت'))}</button>
            <button type="button" id="p137Cancel" class="p137-btn danger">${escapeHtml(t('Cancel session','إلغاء الجلسة'))}</button>
            <button type="button" id="p137Txt" class="p137-btn">${escapeHtml(t('TXT report','تقرير TXT'))}</button>
            <button type="button" id="p137Csv" class="p137-btn">${escapeHtml(t('CSV report','تقرير CSV'))}</button>
          </div>
          <div id="p137Status" class="p137-status" aria-live="polite">${escapeHtml(t('Ready. Choose a folder to begin.','جاهز. اختر مجلدًا للبدء.'))}</div>
          <div id="p137SessionNote" class="p137-session-note"></div>
        </div>
        <div class="p137-panel">
          <div class="p137-progress-row"><div class="p137-progress-label"><span>${escapeHtml(t('Overall progress','التقدم الكلي'))}</span><strong id="p137OverallText">0%</strong></div><div class="p137-progress"><span id="p137OverallBar"></span></div></div>
          <div class="p137-progress-row"><div class="p137-progress-label"><span id="p137CurrentName">${escapeHtml(t('Current file','الملف الحالي'))}</span><strong id="p137CurrentText">0%</strong></div><div class="p137-progress"><span id="p137CurrentBar"></span></div></div>
          <div id="p137Metrics" class="p137-metrics"></div>
          <div id="p137Rows" class="p137-list"></div>
        </div>
      </div>
    </div>
  </section>`;
}

function enhance(){
  injectStyle();
  if(typeof route==='undefined'||route!=='upload')return;
  if(document.getElementById('p137BulkImportCard'))return;
  const host=document.getElementById('content');
  if(!host)return;
  const stepper=host.querySelector('.p132-stepper');
  const layout=host.querySelector('.p132-upload-layout');
  const wrapper=document.createElement('div');
  wrapper.innerHTML=markup().trim();
  const card=wrapper.firstElementChild;

  const tabs=document.createElement('div');
  tabs.className='p140-upload-tabs';
  tabs.innerHTML=`<button type="button" class="p140-upload-tab" id="p140SingleTab" aria-selected="true"><i class="bi bi-file-earmark-arrow-up"></i>${escapeHtml(t('Add one media','إضافة ميديا واحدة'))}</button><button type="button" class="p140-upload-tab" id="p140FolderTab" aria-selected="false"><i class="bi bi-folder2-open"></i>${escapeHtml(t('Add complete folder','إضافة فولدر كامل'))}</button>`;
  const single=document.createElement('div');single.id='p140SinglePane';single.className='p140-upload-pane';
  const folder=document.createElement('div');folder.id='p140FolderPane';folder.className='p140-upload-pane';folder.hidden=true;
  const anchor=stepper||layout;
  if(anchor)anchor.before(tabs);else host.appendChild(tabs);
  if(stepper)single.appendChild(stepper);
  if(layout)single.appendChild(layout);
  tabs.after(single);
  folder.appendChild(card);
  single.after(folder);
  const activate=mode=>{
    const one=mode==='single';
    single.hidden=!one;folder.hidden=one;
    document.getElementById('p140SingleTab')?.setAttribute('aria-selected',String(one));
    document.getElementById('p140FolderTab')?.setAttribute('aria-selected',String(!one));
  };
  document.getElementById('p140SingleTab')?.addEventListener('click',()=>activate('single'));
  document.getElementById('p140FolderTab')?.addEventListener('click',()=>activate('folder'));
  bind();
  if(stateModel.session)renderSnapshot(stateModel.session);
  else renderEmptyMetrics();
}

function bind(){
  const input=document.getElementById('p137FolderInput');
  const drop=document.getElementById('p137FolderDrop');
  const start=document.getElementById('p137Start');
  const resume=document.getElementById('p137Resume');
  const pause=document.getElementById('p137Pause');
  const cancel=document.getElementById('p137Cancel');
  const txt=document.getElementById('p137Txt');
  const csv=document.getElementById('p137Csv');
  if(!input||!start)return;

  input.addEventListener('change',()=>selectFiles(input.files));
  if(drop){
    ['dragenter','dragover'].forEach(name=>drop.addEventListener(name,e=>{e.preventDefault();drop.classList.add('dragging');}));
    ['dragleave','drop'].forEach(name=>drop.addEventListener(name,e=>{e.preventDefault();drop.classList.remove('dragging');}));
  }

  start.addEventListener('click',()=>void startNew());
  resume?.addEventListener('click',()=>void resumeLatest());
  pause?.addEventListener('click',()=>{
    stateModel.abort?.abort();
    stateModel.running=false;
    setStatus(t('Paused. The server-acknowledged offset remains resumable.','تم الإيقاف مؤقتًا. الإزاحة المؤكدة على الخادم محفوظة ويمكن الاستكمال.'),'warn');
  });
  cancel?.addEventListener('click',()=>void cancelSession());
  txt?.addEventListener('click',()=>void downloadReport('txt',false));
  csv?.addEventListener('click',()=>void downloadReport('csv',false));
  updateButtons();
}

function selectFiles(fileList){
  const files=[...(fileList||[])];
  stateModel.files.clear();
  stateModel.session=null;
  stateModel.scan=null;
  if(!files.length){
    stateModel.rootName='';
    setStatus(t('No folder selected.','لم يتم اختيار مجلد.'),'warn');
    updateButtons();
    return;
  }

  const firstPath=normalizeBrowserPath(files[0].webkitRelativePath||files[0].name);
  const firstParts=firstPath.split('/').filter(Boolean);
  const root=firstParts.length>1?firstParts[0]:t('Selected Folder','المجلد المختار');
  stateModel.rootName=root;
  stateModel.selectedAt=Date.now();

  for(const file of files){
    const browserPath=normalizeBrowserPath(file.webkitRelativePath||file.name);
    const parts=browserPath.split('/').filter(Boolean);
    let relative=parts.length>1?parts.slice(1).join('/'):file.name;
    if(parts.length>1&&parts[0]!==root){
      relative=parts.join('/');
    }
    stateModel.files.set(relative,{file,relative,sha256:null,supported:allowed().has(ext(file.name))});
  }

  const name=document.getElementById('p137FolderName');
  if(name)name.textContent=`${root} · ${files.length} ${t('files','ملف')}`;
  setStatus(t('Folder selected. Scan calculates SHA-256 without uploading anything yet.','تم اختيار المجلد. الفحص يحسب SHA-256 محليًا قبل رفع أي ملف.'),'');
  renderEmptyMetrics();
  updateButtons();
}

function normalizeBrowserPath(value){
  return String(value||'').replace(/\\/g,'/').replace(/^\/+|\/+$/g,'');
}

function categoryFor(relative){
  const parts=String(relative||'').split('/').filter(Boolean);
  return parts.length>1?parts[0]:stateModel.rootName;
}

async function scanFiles(){
  if(!stateModel.files.size)throw new Error(t('Choose a folder first.','اختر مجلدًا أولًا.'));
  const entries=[...stateModel.files.values()];
  const supportedEntries=entries.filter(x=>x.supported&&x.file.size>0);
  const totalBytes=supportedEntries.reduce((sum,x)=>sum+x.file.size,0);
  let doneBytes=0,doneFiles=0;
  const categories=new Set(entries.filter(x=>x.supported).map(x=>categoryFor(x.relative)));
  const serverCategories=await safeJson('/client-api/discovery/categories',[]);
  const existingNames=new Set((Array.isArray(serverCategories)?serverCategories:[]).filter(x=>x.parentCategoryId==null).map(x=>String(x.nameEn||'').trim().toLowerCase()));
  const newCategories=[...categories].filter(x=>!existingNames.has(String(x).trim().toLowerCase()));

  for(const entry of entries){
    if(!entry.supported||entry.file.size<=0){
      entry.sha256=null;
      doneFiles++;
      setScanProgress(doneFiles,entries.length,entry.relative,totalBytes?doneBytes*100/totalBytes:doneFiles*100/entries.length);
      continue;
    }
    entry.sha256=await hashFileStreaming(entry.file,(read,total)=>{
      const pct=totalBytes?((doneBytes+read)*100/totalBytes):0;
      setScanProgress(doneFiles,entries.length,entry.relative,pct);
    });
    doneBytes+=entry.file.size;
    doneFiles++;
    setScanProgress(doneFiles,entries.length,entry.relative,totalBytes?doneBytes*100/totalBytes:doneFiles*100/entries.length);
  }

  stateModel.scan={
    total:entries.length,
    supported:supportedEntries.length,
    unsupported:entries.length-supportedEntries.length,
    totalBytes:entries.reduce((s,x)=>s+x.file.size,0),
    categories:categories.size,
    newCategories:newCategories.length,
    existingCategories:categories.size-newCategories.length
  };
  const metrics=document.getElementById('p137Metrics');
  if(metrics)metrics.innerHTML=[
    metric(entries.length,t('Files','الملفات')),
    metric(formatBytes(stateModel.scan.totalBytes),t('Total size','الحجم')),
    metric(categories.size,t('Categories','التصنيفات')),
    metric(newCategories.length,t('New categories','تصنيفات جديدة')),
    metric(stateModel.scan.unsupported,t('Unsupported','غير مدعوم')),
    metric(supportedEntries.length,t('Ready','جاهز'))
  ].join('');
  return entries;
}

function setScanProgress(done,total,path,percent){
  setStatus(`${t('Scanning & hashing','فحص وحساب البصمة')} · ${done}/${total} · ${path}`,'');
  setProgress('p137OverallBar','p137OverallText',percent);
  const current=stateModel.files.get(path);
  if(current&&current.file.size){
    const label=document.getElementById('p137CurrentName');if(label)label.textContent=path;
  }
}

async function startNew(){
  if(stateModel.running)return;
  setBusy(true);
  try{
    const entries=await scanFiles();
    setStatus(t('Creating the durable import manifest and resolving categories/duplicates…','جاري إنشاء قائمة الاستيراد المتينة وفحص التصنيفات والملفات المكررة…'),'');
    const body={
      rootFolderName:stateModel.rootName,
      files:entries.map(x=>({relativePath:x.relative,length:x.file.size,sha256:x.sha256}))
    };
    const response=await api('/client-api/bulk-import/sessions',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},body:JSON.stringify(body)});
    stateModel.session=response;
    renderSnapshot(response);
    await runSession(response);
  }catch(error){
    if(error?.name!=='AbortError')setStatus(message(error,t('Bulk import could not start.','تعذر بدء الاستيراد.')),'error');
  }finally{
    setBusy(false);
  }
}

async function resumeLatest(){
  if(stateModel.running)return;
  if(!stateModel.files.size||!stateModel.rootName){
    setStatus(t('Select the same local folder first, then choose Resume / retry.','اختر نفس المجلد المحلي أولًا ثم اضغط استكمال / إعادة محاولة.'),'warn');
    return;
  }
  setBusy(true);
  try{
    if(!stateModel.scan)await scanFiles();
    const recent=await api('/client-api/bulk-import/sessions?limit=30',{headers:{Accept:'application/json'}});
    const candidate=(Array.isArray(recent)?recent:[]).find(x=>
      String(x.rootFolderName||'').toLowerCase()===stateModel.rootName.toLowerCase()&&resumableSession(x.state));
    if(!candidate)throw new Error(t('No resumable session exists for this folder.','لا توجد جلسة قابلة للاستكمال لهذا المجلد.'));
    const snapshot=await api(`/client-api/bulk-import/sessions/${candidate.sessionId}`,{headers:{Accept:'application/json'}});
    validateLocalManifest(snapshot);
    stateModel.session=snapshot;
    renderSnapshot(snapshot);
    await runSession(snapshot);
  }catch(error){
    if(error?.name!=='AbortError')setStatus(message(error,t('Resume failed.','تعذر الاستكمال.')),'error');
  }finally{
    setBusy(false);
  }
}

function validateLocalManifest(snapshot){
  for(const item of snapshot.items||[]){
    const local=stateModel.files.get(item.relativePath);
    if(!local)continue;
    if(Number(item.expectedLength)!==Number(local.file.size))throw new Error(`${t('Local file changed','تغير الملف المحلي')}: ${item.relativePath}`);
    if(item.expectedSha256&&local.sha256&&String(item.expectedSha256).toLowerCase()!==String(local.sha256).toLowerCase())
      throw new Error(`${t('Local SHA-256 changed','تغير SHA-256 للملف المحلي')}: ${item.relativePath}`);
  }
}

async function runSession(initial){
  stateModel.abort?.abort();
  const controller=new AbortController();
  stateModel.abort=controller;
  stateModel.running=true;
  updateButtons();
  let snapshot=initial;

  try{
    const work=(snapshot.items||[]).filter(x=>[ITEM.Pending,ITEM.Uploading,ITEM.Failed].includes(Number(x.state)));
    for(const original of work){
      if(controller.signal.aborted)throw new DOMException('Aborted','AbortError');
      const local=stateModel.files.get(original.relativePath);
      if(!local){
        await markFailed(snapshot.sessionId,original.itemId,'local_file_missing',t('The source file is missing from the selected folder.','الملف المصدر غير موجود في المجلد المختار.'),controller.signal);
        snapshot=await refresh(snapshot.sessionId,controller.signal);continue;
      }

      if(Number(original.expectedLength)!==Number(local.file.size)||(
        original.expectedSha256&&local.sha256&&String(original.expectedSha256).toLowerCase()!==String(local.sha256).toLowerCase())){
        await markFailed(snapshot.sessionId,original.itemId,'local_file_changed',t('Local file length or SHA-256 changed since the manifest was created.','تغير حجم الملف المحلي أو SHA-256 بعد إنشاء قائمة الاستيراد.'),controller.signal);
        snapshot=await refresh(snapshot.sessionId,controller.signal);continue;
      }

      setCurrent(original.relativePath,0);
      try{
        const action=Number(original.state)===ITEM.Failed?'retry':'begin';
        let item=await api(`/client-api/bulk-import/sessions/${snapshot.sessionId}/items/${original.itemId}/${action}`,{method:'POST',headers:{Accept:'application/json'},signal:controller.signal});
        if(successful(item.state)){snapshot=await refresh(snapshot.sessionId,controller.signal);continue;}
        if(!item.uploadSessionId)throw new Error(t('No resumable upload session was returned.','لم يتم إرجاع جلسة رفع قابلة للاستكمال.'));

        const upload=await api(`/client-api/uploads/sessions/${item.uploadSessionId}`,{headers:{Accept:'application/json'},signal:controller.signal});
        let offset=Number(upload.receivedLength||0);
        const chunkSize=Number(upload.session?.chunkSizeBytes||4*1024*1024);
        while(offset<local.file.size){
          if(controller.signal.aborted)throw new DOMException('Aborted','AbortError');
          const end=Math.min(offset+chunkSize,local.file.size);
          const chunk=local.file.slice(offset,end);
          const chunkBuffer=await chunk.arrayBuffer();
          const chunkSha=await hashArrayBuffer(chunkBuffer);
          const receipt=await api(`/client-api/uploads/sessions/${item.uploadSessionId}/chunks?offset=${offset}`,{
            method:'PUT',
            headers:{'X-Chunk-SHA256':chunkSha,'Content-Type':'application/octet-stream','Accept':'application/json'},
            body:chunk,
            signal:controller.signal
          });
          offset=Number(receipt.receivedLength||end);
          const pct=local.file.size?offset*100/local.file.size:100;
          setCurrent(original.relativePath,pct);
          setStatus(`${t('Uploading','رفع')} · ${original.relativePath} · ${pct.toFixed(0)}%`,'');
          renderOverallWithCurrent(snapshot,pct);
        }

        await api(`/client-api/bulk-import/sessions/${snapshot.sessionId}/items/${original.itemId}/finalize`,{method:'POST',headers:{Accept:'application/json'},signal:controller.signal});
        setCurrent(original.relativePath,100);
        snapshot=await refresh(snapshot.sessionId,controller.signal);
      }catch(error){
        if(error?.name==='AbortError')throw error;
        try{await markFailed(snapshot.sessionId,original.itemId,'client_upload_failed',message(error,'Upload failed.'),controller.signal);}catch{}
        setStatus(`${t('File failed','فشل الملف')} · ${original.relativePath} · ${message(error,'')}`,'error');
        try{snapshot=await refresh(snapshot.sessionId,controller.signal);}catch{}
        if(error instanceof TypeError)break;
      }
    }

    snapshot=await refresh(snapshot.sessionId,controller.signal);
    if([SESSION.Completed,SESSION.CompletedWithErrors].includes(Number(snapshot.state))){
      setStatus(Number(snapshot.state)===SESSION.Completed
        ?t('Import completed. TXT report is being downloaded automatically.','اكتمل الاستيراد. سيتم تنزيل تقرير TXT تلقائيًا.')
        :t('Import completed with errors. Review the report or retry failed items.','اكتمل الاستيراد مع أخطاء. راجع التقرير أو أعد محاولة الملفات الفاشلة.'),
        Number(snapshot.state)===SESSION.Completed?'success':'warn');
      await downloadReport('txt',true);
    }
  }finally{
    stateModel.running=false;
    if(stateModel.abort===controller)stateModel.abort=null;
    updateButtons();
  }
}

async function refresh(sessionId,signal){
  const snapshot=await api(`/client-api/bulk-import/sessions/${sessionId}`,{headers:{Accept:'application/json'},signal});
  stateModel.session=snapshot;
  renderSnapshot(snapshot);
  return snapshot;
}

async function markFailed(sessionId,itemId,code,detail,signal){
  return await api(`/client-api/bulk-import/sessions/${sessionId}/items/${itemId}/fail`,{
    method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},
    body:JSON.stringify({code,detail}),signal
  });
}

async function cancelSession(){
  const snapshot=stateModel.session;
  if(!snapshot?.sessionId)return;
  stateModel.abort?.abort();
  stateModel.running=false;
  updateButtons();
  try{
    const cancelled=await api(`/client-api/bulk-import/sessions/${snapshot.sessionId}/cancel`,{method:'POST',headers:{Accept:'application/json'}});
    stateModel.session=cancelled;
    renderSnapshot(cancelled);
    setStatus(t('Bulk import session cancelled.','تم إلغاء جلسة الاستيراد.'),'warn');
  }catch(error){setStatus(message(error,t('Cancel failed.','فشل الإلغاء.')),'error');}
}

function renderSnapshot(snapshot){
  stateModel.session=snapshot;
  const total=Math.max(1,Number(snapshot.totalFiles||0));
  const processed=Number(snapshot.processedFiles||0);
  setProgress('p137OverallBar','p137OverallText',processed*100/total);
  const metrics=document.getElementById('p137Metrics');
  if(metrics)metrics.innerHTML=[
    metric(`${processed}/${snapshot.totalFiles||0}`,t('Processed','تمت معالجته')),
    metric(snapshot.uploaded||0,t('Uploaded','مرفوع')),
    metric(snapshot.alreadyExists||0,t('Existing','موجود')),
    metric(snapshot.linked||0,t('Reclassified','أعيد تصنيفه')),
    metric(snapshot.failed||0,t('Failed','فشل')),
    metric(snapshot.unsupported||0,t('Unsupported','غير مدعوم'))
  ].join('');
  const rows=document.getElementById('p137Rows');
  if(rows)rows.innerHTML=(snapshot.items||[]).filter(x=>Number(x.state)!==ITEM.Pending).slice(-30).reverse().map(x=>
    `<div class="p137-row"><b>${escapeHtml(itemLabel(x.state))}</b><span>${escapeHtml(x.relativePath)}${x.reasonCode?` · ${escapeHtml(x.reasonCode)}`:''}</span></div>`
  ).join('');
  const note=document.getElementById('p137SessionNote');
  if(note)note.textContent=`${t('Session','الجلسة')}: ${snapshot.sessionId} · ${t('Root','الجذر')}: ${snapshot.rootFolderName} · ${formatBytes(snapshot.totalBytes)}`;
  updateButtons();
}

function renderOverallWithCurrent(snapshot,currentPct){
  if(!snapshot)return;
  const total=Math.max(1,Number(snapshot.totalFiles||0));
  const value=(Number(snapshot.processedFiles||0)+Math.max(0,Math.min(100,currentPct))/100)*100/total;
  setProgress('p137OverallBar','p137OverallText',value);
}

function renderEmptyMetrics(){
  const metrics=document.getElementById('p137Metrics');
  if(metrics)metrics.innerHTML=[
    metric(stateModel.files.size,t('Selected','مختار')),
    metric('0',t('Uploaded','مرفوع')),
    metric('0',t('Existing','موجود')),
    metric('0',t('Reclassified','أعيد تصنيفه')),
    metric('0',t('Failed','فشل')),
    metric('0',t('Unsupported','غير مدعوم'))
  ].join('');
  setProgress('p137OverallBar','p137OverallText',0);
  setProgress('p137CurrentBar','p137CurrentText',0);
}

function metric(value,label){return `<div class="p137-metric"><strong>${escapeHtml(value)}</strong><span>${escapeHtml(label)}</span></div>`;}

function setCurrent(path,percent){
  const label=document.getElementById('p137CurrentName');if(label)label.textContent=path||t('Current file','الملف الحالي');
  setProgress('p137CurrentBar','p137CurrentText',percent);
}

function setProgress(barId,textId,percent){
  const bounded=Math.max(0,Math.min(100,Number(percent||0)));
  const bar=document.getElementById(barId),textNode=document.getElementById(textId);
  if(bar)bar.style.width=`${bounded}%`;
  if(textNode)textNode.textContent=`${bounded.toFixed(0)}%`;
}

function setStatus(value,kind=''){
  const host=document.getElementById('p137Status');if(!host)return;
  host.className=`p137-status ${kind}`.trim();host.textContent=value;
}

function setBusy(value){
  stateModel.running=!!value;
  updateButtons();
}

function updateButtons(){
  const hasFiles=stateModel.files.size>0;
  const hasSession=!!stateModel.session?.sessionId;
  const running=stateModel.running;
  const controls={
    p137Start:!hasFiles||running,
    p137Resume:!hasFiles||running,
    p137Pause:!running,
    p137Cancel:!hasSession||running&&false,
    p137Txt:!hasSession,
    p137Csv:!hasSession,
    p137FolderInput:running
  };
  Object.entries(controls).forEach(([id,disabled])=>{const node=document.getElementById(id);if(node)node.disabled=!!disabled;});
}

async function downloadReport(format,automatic){
  const snapshot=stateModel.session;
  if(!snapshot?.sessionId){if(!automatic)setStatus(t('No import session is available for reporting.','لا توجد جلسة استيراد لإخراج التقرير.'),'warn');return;}
  try{
    const response=await fetch(`/client-api/bulk-import/sessions/${snapshot.sessionId}/report/${format}`,{headers:{Accept:format==='csv'?'text/csv':'text/plain'}});
    if(!response.ok)throw await responseError(response);
    const blob=await response.blob();
    const url=URL.createObjectURL(blob),a=document.createElement('a');
    a.href=url;a.download=`MAM-Bulk-Import-${snapshot.sessionId}.${format}`;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
    if(!automatic)setStatus(t('Report downloaded.','تم تنزيل التقرير.'),'success');
  }catch(error){if(!automatic)setStatus(message(error,t('Report download failed.','فشل تنزيل التقرير.')),'error');}
}

async function api(url,options={}){
  const response=await fetch(url,options);
  if(!response.ok)throw await responseError(response);
  if(response.status===204)return null;
  return await response.json();
}

async function safeJson(url,fallback){
  try{return await api(url,{headers:{Accept:'application/json'}});}catch{return fallback;}
}

async function responseError(response){
  let detail=`HTTP ${response.status}`;
  try{
    const payload=await response.json();
    detail=payload.detail||payload.error||detail;
  }catch{}
  const error=new Error(detail);error.status=response.status;return error;
}

function message(error,fallback){return String(error?.message||error||fallback||'').slice(0,500);}

async function hashArrayBuffer(buffer){
  const digest=await crypto.subtle.digest('SHA-256',buffer);
  return [...new Uint8Array(digest)].map(x=>x.toString(16).padStart(2,'0')).join('');
}

async function hashFileStreaming(file,onProgress){
  const sha=new P137Sha256();
  const chunkSize=4*1024*1024;
  let offset=0;
  while(offset<file.size){
    const end=Math.min(offset+chunkSize,file.size);
    const bytes=new Uint8Array(await file.slice(offset,end).arrayBuffer());
    sha.update(bytes);
    offset=end;
    onProgress?.(offset,file.size);
    await new Promise(resolve=>setTimeout(resolve,0));
  }
  return sha.hex();
}

class P137Sha256{
  constructor(){
    this.h=new Uint32Array([0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]);
    this.buffer=new Uint8Array(64);this.bufferLength=0;this.bytes=0;this.finished=false;
  }
  static K=new Uint32Array([
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
  ]);
  update(data){
    if(this.finished)throw new Error('SHA-256 already finalized');
    this.bytes+=data.length;let pos=0;
    while(pos<data.length){
      const take=Math.min(64-this.bufferLength,data.length-pos);
      this.buffer.set(data.subarray(pos,pos+take),this.bufferLength);
      this.bufferLength+=take;pos+=take;
      if(this.bufferLength===64){this.transform(this.buffer);this.bufferLength=0;}
    }
    return this;
  }
  transform(block){
    const w=new Uint32Array(64);
    for(let i=0;i<16;i++){const j=i*4;w[i]=((block[j]<<24)|(block[j+1]<<16)|(block[j+2]<<8)|block[j+3])>>>0;}
    const rotr=(x,n)=>(x>>>n)|(x<<(32-n));
    for(let i=16;i<64;i++){
      const a=w[i-15],b=w[i-2];
      const s0=(rotr(a,7)^rotr(a,18)^(a>>>3))>>>0;
      const s1=(rotr(b,17)^rotr(b,19)^(b>>>10))>>>0;
      w[i]=(w[i-16]+s0+w[i-7]+s1)>>>0;
    }
    let [a,b,c,d,e,f,g,h]=this.h;
    for(let i=0;i<64;i++){
      const s1=(rotr(e,6)^rotr(e,11)^rotr(e,25))>>>0;
      const ch=((e&f)^((~e)&g))>>>0;
      const t1=(h+s1+ch+P137Sha256.K[i]+w[i])>>>0;
      const s0=(rotr(a,2)^rotr(a,13)^rotr(a,22))>>>0;
      const maj=((a&b)^(a&c)^(b&c))>>>0;
      const t2=(s0+maj)>>>0;
      h=g;g=f;f=e;e=(d+t1)>>>0;d=c;c=b;b=a;a=(t1+t2)>>>0;
    }
    this.h[0]=(this.h[0]+a)>>>0;this.h[1]=(this.h[1]+b)>>>0;this.h[2]=(this.h[2]+c)>>>0;this.h[3]=(this.h[3]+d)>>>0;
    this.h[4]=(this.h[4]+e)>>>0;this.h[5]=(this.h[5]+f)>>>0;this.h[6]=(this.h[6]+g)>>>0;this.h[7]=(this.h[7]+h)>>>0;
  }
  finish(){
    if(this.finished)return;const bitLength=BigInt(this.bytes)*8n;
    this.buffer[this.bufferLength++]=0x80;
    if(this.bufferLength>56){while(this.bufferLength<64)this.buffer[this.bufferLength++]=0;this.transform(this.buffer);this.bufferLength=0;}
    while(this.bufferLength<56)this.buffer[this.bufferLength++]=0;
    for(let i=7;i>=0;i--)this.buffer[this.bufferLength++]=Number((bitLength>>BigInt(i*8))&0xffn);
    this.transform(this.buffer);this.bufferLength=0;this.finished=true;
  }
  hex(){this.finish();return [...this.h].map(x=>x.toString(16).padStart(8,'0')).join('');}
}

const observer=new MutationObserver(()=>requestAnimationFrame(enhance));
const contentHost=document.getElementById('content');
if(contentHost)observer.observe(contentHost,{childList:true,subtree:false});
window.addEventListener('hashchange',()=>requestAnimationFrame(enhance));
document.getElementById('languageButton')?.addEventListener('click',()=>requestAnimationFrame(enhance));
requestAnimationFrame(enhance);

window.mamBulkFolderImport=Object.freeze({
  version:'p137-bulk-folder-import-1',
  enhance,
  getSession:()=>stateModel.session,
  getRoot:()=>stateModel.rootName
});
})();