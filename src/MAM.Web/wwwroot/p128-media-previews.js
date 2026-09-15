(() => {
'use strict';
const processed=new Set();
const h=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function api(url){const r=await fetch(url,{headers:{Accept:'application/json'},cache:'no-store'});if(!r.ok)throw new Error(`HTTP ${r.status}`);return r.json();}
function time(seconds){const n=Math.max(0,Math.round(Number(seconds||0))),m=Math.floor(n/60),s=n%60;return `${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;}

async function hydrate(card){
  if(!card||card.dataset.p128Preview==='1')return;
  card.dataset.p128Preview='1';
  const open=card.querySelector('[data-mam-open-asset]');const id=open?.dataset.mamOpenAsset;if(!id)return;
  const thumb=card.querySelector('.p128-thumb');if(!thumb)return;
  try{
    const [derivatives,technical]=await Promise.all([
      api(`/client-api/processing/assets/${encodeURIComponent(id)}/derivatives`).catch(()=>[]),
      api(`/client-api/processing/assets/${encodeURIComponent(id)}/technical`).catch(()=>null)
    ]);
    const rows=Array.isArray(derivatives)?derivatives:[];
    const image=rows.find(x=>String(x.contentType||'').startsWith('image/'));
    const video=rows.find(x=>String(x.contentType||'').startsWith('video/'));
    const audio=rows.find(x=>String(x.contentType||'').startsWith('audio/'));
    const type=String(technical?.mediaType||'').toLowerCase();
    const keep=[...thumb.children].filter(x=>x.classList.contains('p128-type-pill'));
    thumb.querySelectorAll(':scope>img,:scope>video,:scope>iframe,:scope>.p128-preview-fallback,:scope>.p128-duration').forEach(x=>x.remove());
    thumb.querySelectorAll(':scope>i:not(.p128-type-pill i)').forEach(x=>x.remove());
    let media=null;
    if(image){media=document.createElement('img');media.src=`/client-api/processing/assets/${encodeURIComponent(id)}/derivatives/${encodeURIComponent(image.derivativeId)}/content`;media.alt='';}
    else if(type==='image'){media=document.createElement('img');media.src=`/client-api/processing/assets/${encodeURIComponent(id)}/preview/original`;media.alt='';}
    else if(video){media=document.createElement('video');media.src=`/client-api/processing/assets/${encodeURIComponent(id)}/derivatives/${encodeURIComponent(video.derivativeId)}/content`;media.muted=true;media.preload='metadata';media.playsInline=true;media.addEventListener('loadedmetadata',()=>{try{media.currentTime=Math.min(.25,Math.max(0,(media.duration||1)/20));}catch{}});}
    else if(type==='document'){media=document.createElement('iframe');media.src=`/client-api/processing/assets/${encodeURIComponent(id)}/preview/original#toolbar=0&navpanes=0&scrollbar=0&page=1&view=FitH`;media.title='';media.tabIndex=-1;}
    else if(audio){const wave=document.createElement('div');wave.className='p128-preview-fallback';wave.innerHTML='<i class="bi bi-soundwave"></i>';media=wave;}
    if(media){media.classList.add('p128-preview-media');thumb.prepend(media);}
    if(type==='video'&&video){const play=document.createElement('span');play.className='p128-preview-play';play.innerHTML='<i class="bi bi-play-fill"></i>';thumb.appendChild(play);}
    if((type==='video'||type==='audio')&&Number(technical?.durationSeconds)>0){const d=document.createElement('span');d.className='p128-duration';d.textContent=time(technical.durationSeconds);thumb.appendChild(d);}
    keep.forEach(x=>thumb.appendChild(x));
  }catch{}
}

function scan(){document.querySelectorAll('.p128-asset-card').forEach(card=>{const id=card.querySelector('[data-mam-open-asset]')?.dataset.mamOpenAsset;if(id&&!processed.has(id)){processed.add(id);void hydrate(card);}});}
const observer=new MutationObserver(()=>scan());observer.observe(document.documentElement,{childList:true,subtree:true});scan();

async function runPendingSearch(){
  const q=sessionStorage.getItem('mam.p128.pendingSearch');if(!q)return;
  for(let i=0;i<50;i++){
    const input=document.getElementById('p12SearchQuery');
    if(input){input.value=q;sessionStorage.removeItem('mam.p128.pendingSearch');document.getElementById('p12SearchButton')?.click();return;}
    await new Promise(r=>setTimeout(r,80));
  }
}
if(location.hash.includes('route=search'))void runPendingSearch();
window.addEventListener('hashchange',()=>{if(location.hash.includes('route=search'))void runPendingSearch();});
})();
