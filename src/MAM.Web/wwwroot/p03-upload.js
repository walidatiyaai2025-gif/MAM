const p03AllowedExtensions=['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v','.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma','.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp','.pdf','.doc','.docx','.rtf','.txt','.odt'];
const p03VideoExtensions=new Set(['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v']);
const p03AudioExtensions=new Set(['.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma']);
const p03ImageExtensions=new Set(['.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp']);
const p03DocumentExtensions=new Set(['.pdf','.doc','.docx','.rtf','.txt','.odt']);
const p03OriginalShellPage = shellPage;

function p03MediaKind(extension){
  if(p03VideoExtensions.has(extension)) return 'Video';
  if(p03AudioExtensions.has(extension)) return 'Audio';
  if(p03ImageExtensions.has(extension)) return 'Image';
  if(p03DocumentExtensions.has(extension)) return 'Document';
  return 'Other';
}

function p03MediaKindLabel(kind){
  if(!arabic)return kind;
  return ({Video:'فيديو',Audio:'صوت',Image:'صورة',Document:'مستند',Other:'أخرى'})[kind]||kind;
}

function p03UploadShell(){
  const chips=p03AllowedExtensions.slice(0,14).map(extension=>`<span class="p132-chip">${esc(extension.slice(1).toUpperCase())}</span>`).join('');
  const remaining=Math.max(0,p03AllowedExtensions.length-14);
  return `
    <div class="p132-page-head">
      <div class="p132-page-head-main">
        <span class="p132-page-icon"><i class="bi bi-file-earmark-arrow-up"></i></span>
        <div><h2>${arabic?'إضافة ميديا جديدة':'Add New Media'}</h2><p>${arabic?'رفع الملف والتحقق منه وإدخاله إلى المعالجة المركزية الموثوقة.':'Upload, verify and hand the file to authoritative central processing.'}</p></div>
      </div>
      <button type="button" id="p132UploadBack" class="p132-back"><i class="bi ${arabic?'bi-arrow-right':'bi-arrow-left'}"></i>${arabic?'العودة':'Back'}</button>
    </div>
    <div class="p132-stepper" aria-label="${arabic?'مراحل إضافة الميديا':'Add media steps'}">
      <div class="p132-step active" data-p03-step="1"><span class="p132-step-number">1</span><span class="p132-step-label">${arabic?'رفع الملف':'Upload file'}</span></div>
      <div class="p132-step" data-p03-step="2"><span class="p132-step-number">2</span><span class="p132-step-label">${arabic?'البيانات الوصفية':'Metadata'}</span></div>
      <div class="p132-step" data-p03-step="3"><span class="p132-step-number">3</span><span class="p132-step-label">${arabic?'المراجعة':'Review'}</span></div>
      <div class="p132-step" data-p03-step="4"><span class="p132-step-number">4</span><span class="p132-step-label">${arabic?'إنهاء':'Complete'}</span></div>
    </div>
    <div class="p132-upload-layout">
      <section class="p132-panel p132-upload-info">
        <h3 class="p132-panel-title"><i class="bi bi-info-circle"></i>${arabic?'معلومات سريعة':'Quick information'}</h3>
        <div class="p132-field"><label for="p03Title">${arabic?'عنوان الأصل':'Asset title'} <span class="p132-required">*</span></label><input id="p03Title" maxlength="300" placeholder="${arabic?'أدخل عنوانًا مناسبًا للملف':'Enter a clear asset title'}" aria-label="${arabic?'عنوان الأصل':'Asset title'}"/></div>
        <div class="p132-field"><label>${arabic?'نوع الوسائط':'Media type'}</label><div id="p132DetectedKind" class="p132-detected-kind"><span>${arabic?'يُكتشف تلقائيًا من الملف':'Detected from the selected file'}</span><strong>—</strong></div></div>
        <div class="p132-field"><label for="p132Category">${arabic?'التصنيف الرئيسي':'Primary category'}</label><select id="p132Category" aria-label="${arabic?'التصنيف الرئيسي':'Primary category'}"><option value="">${arabic?'بدون تصنيف — اختياري':'No category — optional'}</option></select></div>
        <div class="p132-field"><label for="p132Notes">${arabic?'ملاحظات وصفية':'Descriptive notes'}</label><textarea id="p132Notes" maxlength="2000" placeholder="${arabic?'ملاحظات مختصرة عن الأصل…':'Short notes about the asset…'}"></textarea></div>
        <details class="p132-advanced"><summary>${arabic?'خيارات متقدمة':'Advanced options'}</summary><p>${arabic?'يظل التخزين الأساسي وبيانات اعتماده تحت سلطة الخادم. يتم التحقق من الحجم وSHA-256 قبل اعتماد النسخة الأصلية.':'Primary storage and credentials remain server-authoritative. Size and SHA-256 are verified before the original is promoted.'}</p></details>
        <button id="p03Upload" class="p132-upload-primary"><span>${arabic?'بدء / استئناف الرفع والمعالجة':'Start / resume upload & processing'}</span><i class="bi ${arabic?'bi-arrow-left':'bi-arrow-right'}"></i></button>
      </section>

      <section class="p132-panel p132-upload-drop-panel">
        <label id="p132Dropzone" class="p132-dropzone" for="p03File">
          <input id="p03File" type="file" accept="${p03AllowedExtensions.join(',')}" aria-label="${arabic?'اختيار ملف':'Choose file'}"/>
          <span class="p132-dropzone-content">
            <i class="bi bi-cloud-arrow-up p132-dropzone-icon"></i>
            <strong>${arabic?'اسحب وأفلت الملف هنا':'Drag and drop a file here'}</strong>
            <span>${arabic?'أو اختر ملفًا من جهازك':'or choose a file from your device'}</span>
            <span class="p132-file-button"><i class="bi bi-folder2-open"></i> ${arabic?'اختيار ملف':'Choose file'}</span>
            <span id="p132SelectedFile" class="p132-selected-file" hidden></span>
          </span>
        </label>
        <div class="p132-supported"><h4>${arabic?'الأنواع المدعومة':'Supported formats'}</h4><div class="p132-chip-list">${chips}${remaining?`<span class="p132-chip p132-more-chip">+${remaining}</span>`:''}</div></div>
        <div id="p03UploadState" class="p132-upload-status" aria-live="polite">${state('empty',arabic?'جاهز للرفع':'Ready',arabic?'اختر ملفًا لبدء الرفع الموثق.':'Choose a file to begin the verified upload.')}</div>
      </section>

      <aside class="p132-panel p132-upload-guide">
        <h3 class="p132-panel-title"><i class="bi bi-info-circle"></i>${arabic?'إرشادات وقيود الرفع':'Upload guidance & limits'}</h3>
        <div class="p132-guide-list">
          <div class="p132-guide-item"><i class="bi bi-file-earmark"></i><div><strong>${arabic?'الحد الأقصى لحجم الملف':'Maximum file size'}</strong><span>${arabic?'يتم تطبيق حد الخادم الفعلي على جلسة الرفع.':'The authoritative server limit is enforced when the upload session is created.'}</span></div></div>
          <div class="p132-guide-item"><i class="bi bi-files"></i><div><strong>${arabic?'الأنواع المسموح بها':'Allowed formats'}</strong><span>${arabic?'فيديو وصوت وصور ومستندات بحسب صلاحيات المستخدم.':'Video, audio, images and documents according to the current user permissions.'}</span></div></div>
          <div class="p132-guide-item"><i class="bi bi-shield-check"></i><div><strong>${arabic?'التحقق الآمن':'Secure verification'}</strong><span>${arabic?'يتم فحص SHA-256 والحجم قبل اعتماد الأصل.':'SHA-256 and size are verified before Primary promotion.'}</span></div></div>
          <div class="p132-guide-item"><i class="bi bi-cpu"></i><div><strong>${arabic?'المعالجة التلقائية':'Automatic processing'}</strong><span>${arabic?'تفريغ زمني للصوت والفيديو وOCR للصور والمستندات القابلة للمعالجة.':'Timestamped transcription for audio/video and OCR/text extraction for supported documents and images.'}</span></div></div>
          <div class="p132-guide-item"><i class="bi bi-universal-access"></i><div><strong>${arabic?'أفضل الممارسات':'Best practices'}</strong><span>${arabic?'استخدم اسمًا واضحًا، وتصنيفًا مناسبًا، وتحقق من الملف قبل البدء.':'Use a clear title and category and verify the source file before starting.'}</span></div></div>
        </div>
      </aside>
    </div>`;
}

shellPage = function(){
  if(route !== 'upload') return p03OriginalShellPage();
  return p03UploadShell();
};

const p03OriginalRender = render;
render = function(){
  p03OriginalRender();
  if(route === 'upload') bindP03UploadWorkspace();
};

let p03SessionId = null;
let p03SelectedFingerprint = null;

function p03SetStep(step){
  document.querySelectorAll('[data-p03-step]').forEach(node=>{
    const value=Number(node.dataset.p03Step);
    node.classList.toggle('active',value===step);
    node.classList.toggle('done',value<step);
  });
}

function p03RenderSelectedFile(file){
  const selected=document.getElementById('p132SelectedFile');
  const detected=document.getElementById('p132DetectedKind');
  if(selected){
    selected.hidden=!file;
    selected.textContent=file?`${file.name} · ${p03FormatBytes(file.size)}`:'';
  }
  if(detected){
    const extension=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
    const kind=file?p03MediaKind(extension):'';
    detected.innerHTML=`<span>${file?(arabic?'النوع المكتشف':'Detected type'):(arabic?'يُكتشف تلقائيًا من الملف':'Detected from the selected file')}</span><strong>${file?esc(p03MediaKindLabel(kind)):'—'}</strong>`;
  }
}

function p03ShowErrorPopup(title,detail){
  document.getElementById('p143UploadErrorModal')?.remove();
  const backdrop=document.createElement('div');
  backdrop.id='p143UploadErrorModal';
  backdrop.className='mam-modal-backdrop';
  backdrop.innerHTML=`<div class="mam-modal p143-error-dialog" role="alertdialog" aria-modal="true" aria-labelledby="p143UploadErrorTitle">
    <div class="p143-error-icon" aria-hidden="true"><i class="bi bi-x-lg"></i></div>
    <div class="p143-error-copy">
      <h3 id="p143UploadErrorTitle">${esc(title)}</h3>
      <p>${esc(detail)}</p>
    </div>
    <div class="mam-modal-footer"><button type="button" class="action mam-btn-secondary" data-p143-error-close>${arabic?'إغلاق':'Close'}</button></div>
  </div>`;
  document.body.appendChild(backdrop);
  const close=()=>backdrop.remove();
  backdrop.querySelector('[data-p143-error-close]')?.addEventListener('click',close);
  backdrop.addEventListener('click',event=>{if(event.target===backdrop)close();});
  setTimeout(()=>backdrop.querySelector('[data-p143-error-close]')?.focus(),0);
}

function p03FormatBytes(value){
  let n=Number(value||0),i=0;const units=['B','KB','MB','GB','TB'];
  while(n>=1024&&i<units.length-1){n/=1024;i++;}
  return `${n.toFixed(i?1:0)} ${units[i]}`;
}

function p03RenderProgress(host,title,detail,percent){
  const bounded=Math.max(0,Math.min(100,Number(percent||0)));
  host.innerHTML=`<div class="state loading"><strong>${esc(title)}</strong><br>${esc(detail)}<div class="p132-progress" style="--progress:${bounded}%"><span></span></div></div>`;
}

async function p03LoadCategoryOptions(){
  const select=document.getElementById('p132Category');
  if(!select)return;
  const selected=select.value;
  try{
    const response=await fetch('/client-api/discovery/categories',{headers:{Accept:'application/json'}});
    if(!response.ok)throw new Error('HTTP '+response.status);
    const categories=await response.json();
    const items=Array.isArray(categories)?categories:[];
    select.innerHTML=`<option value="">${arabic?'بدون تصنيف — اختياري':'No category — optional'}</option>`;
    items.sort((a,b)=>Number(a.sortOrder||0)-Number(b.sortOrder||0)||String(a.nameEn||a.nameAr||'').localeCompare(String(b.nameEn||b.nameAr||'')));
    for(const item of items){
      const value=String(item.nameEn||item.nameAr||'').trim();
      if(!value)continue;
      const option=document.createElement('option');
      option.value=value;
      option.textContent=arabic?(item.nameAr||item.nameEn||value):(item.nameEn||item.nameAr||value);
      select.appendChild(option);
    }
    select.value=selected;
  }catch{
    select.innerHTML=`<option value="">${arabic?'تعذر تحميل التصنيفات':'Categories unavailable'}</option>`;
  }
}

function bindP03UploadWorkspace(){
  const fileInput=document.getElementById('p03File');
  const titleInput=document.getElementById('p03Title');
  const button=document.getElementById('p03Upload');
  const statusBox=document.getElementById('p03UploadState');
  const dropzone=document.getElementById('p132Dropzone');
  const backButton=document.getElementById('p132UploadBack');
  if(!fileInput||!titleInput||!button||!statusBox)return;
  void p03LoadCategoryOptions();

  backButton?.addEventListener('click',()=>{route='ingest';render();});

  const acceptFile=file=>{
    const fingerprint=file?`${file.name}|${file.size}|${file.lastModified}`:null;
    if(fingerprint!==p03SelectedFingerprint){p03SessionId=null;p03SelectedFingerprint=fingerprint;}
    if(file && !titleInput.value.trim()) titleInput.value=file.name.replace(/\.[^.]+$/,'');
    const extension=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
    p03RenderSelectedFile(file);
    if(file && !p03AllowedExtensions.includes(extension)){
      const detail=arabic?'امتداد الملف غير موجود ضمن الأنواع المسموح برفعها.':'The selected file extension is not allowed by the upload policy.';
      p03ShowErrorPopup(arabic?'نوع ملف غير مسموح':'Unsupported file type',detail);
      statusBox.innerHTML=state('empty',arabic?'لم يبدأ الرفع':'Upload not started',arabic?'اختر ملفًا من الأنواع المدعومة.':'Choose a supported file type.');
      button.disabled=true;
    }else{
      button.disabled=fileInput.disabled;
      if(file) statusBox.innerHTML=state('empty',arabic?'الملف جاهز':'File ready',arabic?'راجع المعلومات ثم ابدأ أو استأنف الرفع.':'Review the information, then start or resume the upload.');
    }
  };

  fileInput.addEventListener('change',()=>acceptFile(fileInput.files?.[0]));

  if(dropzone){
    ['dragenter','dragover'].forEach(name=>dropzone.addEventListener(name,event=>{event.preventDefault();dropzone.classList.add('dragging');}));
    ['dragleave','drop'].forEach(name=>dropzone.addEventListener(name,event=>{event.preventDefault();dropzone.classList.remove('dragging');}));
    dropzone.addEventListener('drop',event=>{
      const file=event.dataTransfer?.files?.[0];
      if(!file)return;
      try{
        const transfer=new DataTransfer();
        transfer.items.add(file);
        fileInput.files=transfer.files;
      }catch{}
      acceptFile(file);
    });
  }

  button.addEventListener('click',async()=>{
    const file=fileInput.files?.[0];
    const assetTitle=titleInput.value.trim();
    const extension=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
    if(!file||!assetTitle){
      const detail=arabic?'يجب اختيار ملف وكتابة عنوان للميديا قبل بدء الرفع.':'Choose a file and enter a media title before uploading.';
      p03ShowErrorPopup(arabic?'بيانات مطلوبة':'Required information',detail);
      return;
    }
    if(!p03AllowedExtensions.includes(extension)){
      const detail=arabic?'نوع الملف المحدد غير مسموح به في سياسة الرفع الحالية.':'The selected media type is not allowed by the current upload policy.';
      p03ShowErrorPopup(arabic?'نوع ملف غير مسموح':'Unsupported file type',detail);
      return;
    }
    button.disabled=true;
    p03SetStep(1);
    try{
      p03RenderProgress(statusBox,arabic?'التحقق من الملف':'Verifying file',arabic?'جاري حساب SHA-256 قبل إنشاء جلسة الرفع…':'Calculating SHA-256 before session creation…',3);
      const fullSha=await p03HashBlob(file);
      let session;
      if(p03SessionId){
        const resumeResponse=await fetch(`/client-api/uploads/sessions/${p03SessionId}`,{headers:{'Accept':'application/json'}});
        if(resumeResponse.ok) session=await resumeResponse.json();
        else p03SessionId=null;
      }
      if(!session){
        const createResponse=await fetch('/client-api/uploads/sessions',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},body:JSON.stringify({title:assetTitle,originalFileName:file.name,expectedLength:file.size,expectedSha256:fullSha})});
        if(!createResponse.ok){await p03ThrowResponse(createResponse);}
        session=await createResponse.json();
        p03SessionId=session.session.sessionId;
      }

      const chunkSize=session.session.chunkSizeBytes;
      let offset=session.receivedLength;
      while(offset<file.size){
        const chunk=file.slice(offset,Math.min(offset+chunkSize,file.size));
        const chunkSha=await p03HashBlob(chunk);
        const percent=Math.max(4,Math.floor(offset*82/file.size)+4);
        p03RenderProgress(statusBox,arabic?'رفع الملف':'Uploading file',`${p03FormatBytes(offset)} / ${p03FormatBytes(file.size)}`,percent);
        const chunkResponse=await fetch(`/client-api/uploads/sessions/${p03SessionId}/chunks?offset=${offset}`,{method:'PUT',headers:{'X-Chunk-SHA256':chunkSha,'Content-Type':'application/octet-stream','Accept':'application/json'},body:chunk});
        if(!chunkResponse.ok){await p03ThrowResponse(chunkResponse);}
        const receipt=await chunkResponse.json();
        offset=receipt.receivedLength;
      }

      p03SetStep(2);
      p03RenderProgress(statusBox,arabic?'اعتماد النسخة الأصلية':'Promoting Primary original',arabic?'جاري التحقق النهائي من الحجم وSHA-256…':'Verifying final size and SHA-256…',88);
      const finalize=await fetch(`/client-api/uploads/sessions/${p03SessionId}/finalize`,{method:'POST',headers:{'Accept':'application/json'}});
      if(!finalize.ok){await p03ThrowResponse(finalize);}
      const result=await finalize.json();

      const metadataResult=await p03ApplyOptionalMetadata(result.assetId);
      p03SetStep(3);
      p03RenderProgress(statusBox,arabic?'إدراج المعالجة':'Queueing processing',arabic?'جاري إضافة مهام المعالجة والفهرسة المناسبة…':'Queueing the appropriate processing and indexing jobs…',94);
      const queued=await p03QueueAutomaticProcessing(result.assetId,extension);
      const queuedLabel=queued.length?queued.join(', '):(arabic?'لا توجد معالجة تلقائية لهذا النوع':'no automatic profile for this type');
      p03SetStep(4);
      const metadataNote=metadataResult.message?`<br>${esc(metadataResult.message)}`:'';
      statusBox.innerHTML=state(metadataResult.ok?'empty':'degraded',arabic?'اكتمل الرفع':'Upload completed',`${arabic?'تم اعتماد النسخة الأصلية':'Primary original verified'} · ${esc(result.assetId)} · SHA-256 ${esc(result.sha256.slice(0,16))}…<br>${arabic?'المعالجة المدرجة':'Queued processing'}: ${esc(queuedLabel)}${metadataNote}`);
      p03SessionId=null;
    }catch(error){
      const message=String(error?.message||error||'upload failed').replace(/\s+/g,' ').trim();
      const status=Number(error?.status||0);
      const denied=status===401||status===403||/permission|denied/i.test(message);
      const degraded=status===503||/degraded|unavailable/i.test(message);
      const title=denied
        ? (arabic?'الوصول مرفوض':'Permission denied')
        : degraded
          ? (arabic?'الخدمة غير متاحة':'Service unavailable')
          : (arabic?'فشل رفع الميديا':'Media upload failed');
      const detail=message && !/^upload failed$/i.test(message)
        ? message
        : (arabic?'لم تكتمل عملية رفع الميديا.':'The media upload did not complete.');
      p03ShowErrorPopup(title,detail);
      statusBox.innerHTML=state('empty',arabic?'لم يكتمل الرفع':'Upload incomplete',arabic?'تم إيقاف العملية بعد الخطأ الموضح في الرسالة.':'The operation stopped because of the error shown in the popup.');
    }finally{button.disabled=fileInput.disabled;}
  });
}

async function p03ApplyOptionalMetadata(assetId){
  const category=document.getElementById('p132Category')?.value.trim()||'';
  const notes=document.getElementById('p132Notes')?.value.trim()||'';
  if(!category&&!notes)return {ok:true,message:''};
  try{
    const response=await fetch(`/client-api/curation/assets/${assetId}/metadata`,{headers:{Accept:'application/json'}});
    if(!response.ok)throw new Error(`HTTP ${response.status}`);
    const metadata=await response.json();
    const body={
      expectedVersion:metadata.version,
      schemaKey:'core-media-v1',
      titleEn:metadata.titleEn,
      titleAr:metadata.titleAr||'',
      eventDate:metadata.eventDate,
      category:category||metadata.category||'',
      tags:Array.isArray(metadata.tags)?metadata.tags:[],
      preservationNotes:notes||metadata.preservationNotes||''
    };
    const save=await fetch(`/client-api/curation/assets/${assetId}/metadata`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify(body)});
    if(!save.ok)throw new Error(`HTTP ${save.status}`);
    return {ok:true,message:arabic?'تم حفظ التصنيف والملاحظات.':'Category and notes saved.'};
  }catch{
    return {ok:false,message:arabic?'اكتمل رفع الأصل، لكن تعذر حفظ التصنيف أو الملاحظات الآن.':'The original was uploaded, but category/notes could not be saved right now.'};
  }
}

async function p03QueueAutomaticProcessing(assetId,extension){
  const profiles=[];
  if(p03VideoExtensions.has(extension)) profiles.push('inspect-v1','image-preview-v1','video-proxy-v1','transcript-text-v1');
  else if(p03AudioExtensions.has(extension)) profiles.push('inspect-v1','audio-preview-v1','transcript-text-v1');
  else if(p03ImageExtensions.has(extension)) profiles.push('inspect-v1','image-preview-v1','ocr-text-v1','visual-index-v1');
  else if(p03DocumentExtensions.has(extension)){
    if(extension==='.pdf') profiles.push('ocr-text-v1');
    else profiles.push('inspect-v1','ocr-text-v1');
  }
  const queued=[];
  for(const profileId of profiles){
    const response=await fetch(`/client-api/processing/assets/${assetId}/jobs`,{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},body:JSON.stringify({profileId})});
    if(!response.ok) await p03ThrowResponse(response);
    queued.push(profileId);
  }
  return queued;
}

async function p03HashBlob(blob){
  const buffer=await blob.arrayBuffer();
  const digest=await crypto.subtle.digest('SHA-256',buffer);
  return [...new Uint8Array(digest)].map(value=>value.toString(16).padStart(2,'0')).join('');
}

async function p03ThrowResponse(response){
  let payload=null;
  try{payload=await response.json();}catch{}
  const detail=String(payload?.detail||payload?.message||payload?.error||`HTTP ${response.status}`).trim();
  const error=new Error(detail);
  error.status=response.status;
  error.payload=payload;
  throw error;
}
