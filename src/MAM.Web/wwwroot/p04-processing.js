const p04PreviousShellPage = shellPage;
shellPage = function(){
  if(route === 'asset'){
    return `${lead(arabic?'تفاصيل الأصل':'Asset Details',arabic?'بيانات فنية ومعاينات من خدمة المعالجة المركزية.':'Authoritative technical metadata and previews from the Central API.','P04 · PROCESSING')}
      <div id="p04AssetState">${state('loading','Loading',arabic?'جاري تحميل بيانات الأصل…':'Loading technical metadata and verified derivatives…')}</div>`;
  }
  if(route === 'queue'){
    return `${lead(arabic?'قائمة المعالجة':'Processing Queue',arabic?'حالة مباشرة من مخزن الوظائف المركزي.':'Live durable job state through the Central API.','P04 · PROCESSING')}
      <div id="p04QueueState">${state('loading','Loading',arabic?'جاري تحميل الوظائف…':'Loading durable processing jobs…')}</div>`;
  }
  return p04PreviousShellPage();
};

const p04PreviousRender = render;
render = function(){
  p04PreviousRender();
  if(route === 'asset') void p04LoadAsset();
  if(route === 'queue') void p04LoadQueue();
};

async function p04LoadAsset(){
  const languageAtRequest=arabic;
  const host=document.getElementById('p04AssetState');
  if(!host)return;
  try{
    const catalogResponse=await fetch('/client-api/catalog/assets',{headers:{'Accept':'application/json'}});
    if(!catalogResponse.ok) return p04ShowFailure(host,catalogResponse.status,'asset');
    const assets=await catalogResponse.json();
    if(route!=='asset'||languageAtRequest!==arabic)return;
    if(!Array.isArray(assets)||assets.length===0){host.innerHTML=state('empty','Empty',arabic?'لا توجد أصول للمعالجة.':'No catalog assets are available for processing.');return;}
    const asset=assets[0];
    const [technicalResponse,derivativesResponse]=await Promise.all([
      fetch(`/client-api/processing/assets/${asset.id}/technical`,{headers:{'Accept':'application/json'}}),
      fetch(`/client-api/processing/assets/${asset.id}/derivatives`,{headers:{'Accept':'application/json'}})
    ]);
    if(route!=='asset'||languageAtRequest!==arabic)return;
    if(technicalResponse.status===401||technicalResponse.status===403||technicalResponse.status===503)return p04ShowFailure(host,technicalResponse.status,'asset');
    if(!derivativesResponse.ok)return p04ShowFailure(host,derivativesResponse.status,'asset');
    const technical=technicalResponse.status===404?null:await technicalResponse.json();
    const derivatives=await derivativesResponse.json();
    const actions=[`<button class="action" data-p04-profile="inspect-v1">${arabic?'فحص فني':'Queue inspection'}</button>`];
    if(technical?.mediaType==='Video')actions.push(`<button class="action" data-p04-profile="video-proxy-v1">${arabic?'إنشاء Proxy':'Queue video proxy'}</button>`);
    if(technical?.mediaType==='Image')actions.push(`<button class="action" data-p04-profile="image-preview-v1">${arabic?'إنشاء معاينة':'Queue image preview'}</button>`);
    if(technical?.mediaType==='Audio')actions.push(`<button class="action" data-p04-profile="audio-preview-v1">${arabic?'معاينة صوت':'Queue audio preview'}</button>`);
    if(technical?.mediaType==='Document')actions.push(`<button class="action" data-p04-profile="pdf-inline-v1">${arabic?'تحديث فحص PDF':'Queue PDF inspection'}</button>`);

    const technicalHtml=technical
      ? `<p><strong>${esc(technical.mediaType)}</strong> · ${esc(technical.videoCodec||'—')} · ${esc(technical.audioCodec||'—')}<br>${esc(technical.width||'—')}×${esc(technical.height||'—')} · ${esc(technical.durationSeconds??'—')}s<br>${arabic?'تم الفحص':'Inspected'}: ${esc(technical.inspectedAtUtc)}</p>`
      : `<p>${arabic?'لم يتم الفحص بعد.':'Not inspected yet. Queue technical inspection first.'}</p>`;
    const derivativeHtml=Array.isArray(derivatives)&&derivatives.length
      ? derivatives.map(d=>p04Derivative(asset.id,d)).join('')
      : `<p>${arabic?'لا توجد مشتقات موثقة بعد.':'No verified derivatives yet.'}</p>`;
    const pdfHtml=technical?.mediaType==='Document'
      ? `<div class="card"><h3>${arabic?'معاينة PDF':'PDF inline preview'}</h3><iframe title="PDF preview" src="/client-api/processing/assets/${asset.id}/preview/original" style="width:100%;height:420px;border:0;border-radius:8px"></iframe></div>`:'';
    host.innerHTML=`<div class="card"><h3>${esc(asset.title)}</h3><p>${esc(asset.id)} · v${esc(asset.version)}</p><div class="toolbar">${actions.join('')}</div><div id="p04ActionState" aria-live="polite"></div></div>
      <div class="grid two"><div class="card"><h3>${arabic?'البيانات الفنية':'Technical metadata'}</h3>${technicalHtml}</div><div class="card"><h3>${arabic?'المعاينات الموثقة':'Verified previews'}</h3>${derivativeHtml}</div></div>${pdfHtml}
      <div class="card"><div class="state loading"><strong>Central API</strong><br>${arabic?'لا يتم كشف مسارات أو بيانات اعتماد التخزين للمتصفح.':'Preview bytes are server-mediated; storage paths and credentials are never exposed to the browser.'}</div></div>`;
    host.querySelectorAll('[data-p04-profile]').forEach(button=>button.addEventListener('click',()=>p04Enqueue(asset.id,button.dataset.p04Profile)));
  }catch{if(route==='asset'&&languageAtRequest===arabic)host.innerHTML=state('error','API error',arabic?'تعذر تحميل تفاصيل المعالجة.':'Processing details could not be loaded. Retry is available.');}
}

function p04Derivative(assetId,d){
  const url=`/client-api/processing/assets/${assetId}/derivatives/${d.derivativeId}/content`;
  let preview='';
  if(String(d.contentType).startsWith('image/'))preview=`<img src="${url}" alt="Verified preview" style="max-width:100%;max-height:320px;border-radius:8px"/>`;
  else if(String(d.contentType).startsWith('video/'))preview=`<video controls preload="metadata" src="${url}" style="width:100%;max-height:360px"></video>`;
  else if(String(d.contentType).startsWith('audio/'))preview=`<audio controls preload="metadata" src="${url}" style="width:100%"></audio>`;
  return `<div class="state loading"><strong>${esc(d.profileId)} v${esc(d.profileVersion)}</strong><br>${esc(d.contentType)} · ${esc(d.length)} B · SHA ${esc(String(d.sha256).slice(0,16))}…${preview}</div>`;
}

async function p04Enqueue(assetId,profileId){
  const output=document.getElementById('p04ActionState');
  if(output)output.innerHTML=state('loading','Loading',arabic?'جاري إضافة الوظيفة…':'Queueing durable processing job…');
  try{
    const response=await fetch(`/client-api/processing/assets/${assetId}/jobs`,{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json'},body:JSON.stringify({profileId})});
    if(!response.ok){if(output)p04ShowFailure(output,response.status,'asset');return;}
    const job=await response.json();
    if(output)output.innerHTML=state('empty',arabic?'تمت الإضافة':'Queued',arabic?`تم حفظ الوظيفة ${job.jobId} في القائمة المركزية.`:`Job ${job.jobId} is durably queued in the central processing store.`);
  }catch{if(output)output.innerHTML=state('error','API error',arabic?'تعذر إضافة الوظيفة.':'Processing job could not be queued.');}
}

async function p04LoadQueue(){
  const languageAtRequest=arabic;
  const host=document.getElementById('p04QueueState');
  if(!host)return;
  try{
    const response=await fetch('/client-api/processing/jobs?limit=100',{headers:{'Accept':'application/json'}});
    if(!response.ok)return p04ShowFailure(host,response.status,'queue');
    const jobs=await response.json();
    if(route!=='queue'||languageAtRequest!==arabic)return;
    if(!Array.isArray(jobs)||jobs.length===0){host.innerHTML=state('empty','Empty',arabic?'لا توجد وظائف معالجة.':'No processing jobs are queued.');return;}
    host.innerHTML=`<div id="p04QueueActionState" aria-live="polite"></div><div class="list">${jobs.map(j=>`<div class="row"><b>${esc(String(j.jobId).slice(0,13))}</b><span>${esc(j.profileId)} v${esc(j.profileVersion)}</span><span>${esc(j.state)} · attempt ${esc(j.attemptCount)}</span><span>${j.state===3?`<button class="action" data-p04-retry="${esc(j.jobId)}">${arabic?'إعادة المحاولة':'Retry'}</button>`:esc(j.lastError||'')}</span></div>`).join('')}</div>`;
    host.querySelectorAll('[data-p04-retry]').forEach(button=>button.addEventListener('click',async()=>{
      const output=document.getElementById('p04QueueActionState');
      button.disabled=true;
      if(output)output.innerHTML=state('loading','Loading',arabic?'جاري إعادة إضافة الوظيفة…':'Retrying processing job…');
      try{
        const retry=await fetch(`/client-api/processing/jobs/${button.dataset.p04Retry}/retry`,{method:'POST',headers:{Accept:'application/json'}});
        if(!retry.ok){if(output)p04ShowFailure(output,retry.status,'queue');return;}
        const job=await retry.json();
        if(output)output.innerHTML=state('empty',arabic?'تمت إعادة المحاولة':'Retry queued',arabic?`تمت إعادة الوظيفة ${job.jobId} للقائمة.`:`Job ${job.jobId} was re-queued.`);
        setTimeout(()=>{if(route==='queue')void p04LoadQueue();},500);
      }catch{if(output)output.innerHTML=state('error','API error',arabic?'فشلت إعادة المحاولة.':'Processing retry failed.');}
      finally{button.disabled=false;}
    }));
  }catch{if(route==='queue'&&languageAtRequest===arabic)host.innerHTML=state('error','API error',arabic?'تعذر تحميل قائمة المعالجة.':'Processing queue could not be loaded.');}
}

function p04ShowFailure(host,statusCode,surface){
  if(statusCode===401||statusCode===403){host.innerHTML=state('denied','Permission denied',arabic?'لا توجد صلاحية لهذا الإجراء.':'The current identity is not authorized for this processing action.');return;}
  if(statusCode===503){host.innerHTML=state('degraded','Degraded',arabic?'خدمة المعالجة غير جاهزة حاليًا.':'The processing service is currently degraded.');return;}
  host.innerHTML=state('error','API error',`${surface} · HTTP ${statusCode}`);
}
