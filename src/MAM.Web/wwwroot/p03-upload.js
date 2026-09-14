const p03AllowedExtensions=['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v','.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma','.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp','.pdf','.doc','.docx','.rtf','.txt','.odt'];
const p03VideoExtensions=new Set(['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v']);
const p03AudioExtensions=new Set(['.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma']);
const p03ImageExtensions=new Set(['.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp']);
const p03DocumentExtensions=new Set(['.pdf','.doc','.docx','.rtf','.txt','.odt']);
const p03OriginalShellPage = shellPage;
shellPage = function(){
  if(route !== 'upload') return p03OriginalShellPage();
  return `${lead(arabic?'رفع الملفات إلى التخزين الأساسي':'Durable Primary Upload',arabic?'اختيار الملف محلي مؤقت؛ كل الجلسات والنسخة الأصلية الموثقة تحت سلطة الخادم.':'Local selection is temporary; sessions and the verified original remain server-authoritative.','P12 · CENTRAL API')}
    <div class="card"><h3>${arabic?'رفع قابل للاستكمال':'Resumable upload'}</h3>
      <p>${arabic?'يتم التحقق من الحجم و SHA-256 على الخادم قبل اعتماد النسخة الأصلية.':'Server verifies final size and SHA-256 before Primary promotion.'}</p>
      <div class="state loading"><strong>${arabic?'الأنواع المسموح بها':'Allowed media'}</strong><br>${arabic?'فيديو: MXF, MOV, MP4, MKV, AVI, WEBM, M4V · صوت: WAV, MP3, M4A, AAC, FLAC, OGG, WMA · صور: JPG, PNG, TIFF, BMP, WEBP · مستندات: PDF, DOC, DOCX, RTF, TXT, ODT':'Video: MXF, MOV, MP4, MKV, AVI, WEBM, M4V · Audio: WAV, MP3, M4A, AAC, FLAC, OGG, WMA · Images: JPG, PNG, TIFF, BMP, WEBP · Documents: PDF, DOC, DOCX, RTF, TXT, ODT'}</div>
      <div class="toolbar"><input id="p03Title" maxlength="300" placeholder="${arabic?'عنوان الأصل':'Asset title'}" aria-label="${arabic?'عنوان الأصل':'Asset title'}" /></div>
      <div class="toolbar"><input id="p03File" type="file" accept="${p03AllowedExtensions.join(',')}" aria-label="${arabic?'اختيار ملف':'Choose file'}" /><button id="p03Upload" class="action">${arabic?'بدء / استكمال':'Start / resume'}</button></div>
      <div id="p03UploadState" aria-live="polite">${state('empty',arabic?'جاهز للرفع':'Ready',arabic?'اختر ملفًا ثم ابدأ الرفع.':'Choose a file, then start the durable upload.')}</div>
    </div>
    <div class="card"><h3>${arabic?'المعالجة التلقائية بعد الرفع':'Automatic post-upload processing'}</h3><p>${arabic?'الفيديو والصوت يتم إدراجهما تلقائيًا للفحص والتفريغ الزمني؛ PDF والصور يتم إدراجهما لـ OCR؛ ومستندات Word/النص يتم إدراجها لاستخراج النص والفهرسة.':'Video/audio are automatically queued for inspection and timestamped transcription; PDF/images for OCR; Word/text documents for searchable text extraction.'}</p></div>
    <div class="card"><h3>${arabic?'حدود الأمان':'Storage boundary'}</h3><p>${arabic?'المتصفح لا يستقبل مسار التخزين الأساسي أو بيانات اعتماده.':'The browser never receives the Primary Storage path or credentials.'}</p></div>`;
};

const p03OriginalRender = render;
render = function(){
  p03OriginalRender();
  if(route === 'upload') bindP03UploadWorkspace();
};

let p03SessionId = null;
let p03SelectedFingerprint = null;

function bindP03UploadWorkspace(){
  const fileInput=document.getElementById('p03File');
  const titleInput=document.getElementById('p03Title');
  const button=document.getElementById('p03Upload');
  const statusBox=document.getElementById('p03UploadState');
  if(!fileInput||!titleInput||!button||!statusBox)return;

  fileInput.addEventListener('change',()=>{
    const file=fileInput.files?.[0];
    const fingerprint=file?`${file.name}|${file.size}|${file.lastModified}`:null;
    if(fingerprint!==p03SelectedFingerprint){p03SessionId=null;p03SelectedFingerprint=fingerprint;}
    if(file && !titleInput.value.trim()) titleInput.value=file.name.replace(/\.[^.]+$/,'');
    if(file && !p03AllowedExtensions.includes(`.${file.name.split('.').pop()?.toLowerCase()||''}`)){
      statusBox.innerHTML=state('error',arabic?'نوع غير مسموح':'Unsupported type',arabic?'امتداد الملف غير موجود في سياسة الرفع المسموح بها.':'The selected extension is not in the allowed upload policy.');
      button.disabled=true;
    }else button.disabled=false;
  });

  button.addEventListener('click',async()=>{
    const file=fileInput.files?.[0];
    const assetTitle=titleInput.value.trim();
    const extension=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
    if(!file||!assetTitle){statusBox.innerHTML=state('error','API error',arabic?'اختر ملفًا واكتب العنوان.':'Choose a file and enter a title.');return;}
    if(!p03AllowedExtensions.includes(extension)){statusBox.innerHTML=state('error',arabic?'نوع غير مسموح':'Unsupported type',arabic?'هذا النوع غير مسموح به.':'This media type is not allowed.');return;}
    button.disabled=true;
    try{
      statusBox.innerHTML=state('loading','Loading',arabic?'جاري حساب SHA-256…':'Calculating SHA-256 before session creation…');
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
        const percent=Math.floor(offset*100/file.size);
        statusBox.innerHTML=state('loading','Loading',`${arabic?'رفع':'Uploading'} ${percent}% · ${offset}/${file.size}`);
        const chunkResponse=await fetch(`/client-api/uploads/sessions/${p03SessionId}/chunks?offset=${offset}`,{method:'PUT',headers:{'X-Chunk-SHA256':chunkSha,'Content-Type':'application/octet-stream','Accept':'application/json'},body:chunk});
        if(!chunkResponse.ok){await p03ThrowResponse(chunkResponse);}
        const receipt=await chunkResponse.json();
        offset=receipt.receivedLength;
      }

      statusBox.innerHTML=state('loading','Loading',arabic?'جاري التحقق النهائي واعتماد النسخة الأصلية…':'Verifying size/SHA-256 and promoting the Primary original…');
      const finalize=await fetch(`/client-api/uploads/sessions/${p03SessionId}/finalize`,{method:'POST',headers:{'Accept':'application/json'}});
      if(!finalize.ok){await p03ThrowResponse(finalize);}
      const result=await finalize.json();
      statusBox.innerHTML=state('loading',arabic?'تم الاعتماد':'Primary verified',`${arabic?'تم اعتماد النسخة الأصلية؛ جاري إضافة المعالجة والفهرسة تلقائيًا…':'Primary verified; queueing automatic processing and indexing…'} · ${esc(result.assetId)}`);
      const queued=await p03QueueAutomaticProcessing(result.assetId,extension);
      const queuedLabel=queued.length?queued.join(', '):(arabic?'لا توجد معالجة تلقائية لهذا النوع':'no automatic profile for this type');
      statusBox.innerHTML=state('empty',arabic?'اكتمل الرفع':'Upload completed',`${arabic?'اكتمل الرفع الموثق':'Durable upload completed'} · ${esc(result.assetId)} · SHA-256 ${esc(result.sha256.slice(0,16))}…<br>${arabic?'المعالجة المدرجة':'Queued processing'}: ${esc(queuedLabel)}`);
      p03SessionId=null;
    }catch(error){
      const message=String(error?.message||error||'upload failed');
      const kind=/401|403|permission/i.test(message)?'denied':/503|degraded|unavailable/i.test(message)?'degraded':'error';
      statusBox.innerHTML=state(kind,kind==='denied'?'Permission denied':kind==='degraded'?'Degraded':'Retry available',arabic?'توقف الرفع أو المعالجة التلقائية. إذا تم اعتماد الأصل بالفعل سيظل محفوظًا ويمكن إعادة إدراج المعالجة من تفاصيل الأصل.':`Upload or automatic processing paused. If Primary promotion already completed, the asset remains durable and processing can be queued again from Asset Details. ${esc(message.slice(0,180))}`);
    }finally{button.disabled=false;}
  });
}

async function p03QueueAutomaticProcessing(assetId,extension){
  const profiles=[];
  if(p03VideoExtensions.has(extension)||p03AudioExtensions.has(extension)) profiles.push('inspect-v1','transcript-text-v1');
  else if(p03ImageExtensions.has(extension)) profiles.push('inspect-v1','ocr-text-v1');
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
  let detail=`HTTP ${response.status}`;
  try{detail+=` ${JSON.stringify(await response.json())}`;}catch{}
  throw new Error(detail);
}
