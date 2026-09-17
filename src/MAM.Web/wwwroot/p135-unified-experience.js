(() => {
  'use strict';

  const text=(en,ar)=>window.arabic?ar:en;
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  let libraryView='all';
  let popupSerial=0;

  const style=document.createElement('style');
  style.textContent=`
    .p135-tabs{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 14px;padding:8px;background:#fff;border:1px solid #dce7f1;border-radius:14px;box-shadow:0 8px 22px rgba(16,49,78,.05)}
    .p135-tabs button{border:1px solid #cbdbea;background:#f8fbfe;color:#123b5e;border-radius:10px;padding:10px 16px;font-weight:800;cursor:pointer}
    .p135-tabs button.active{background:#0b3a62;color:#fff;border-color:#0b3a62;box-shadow:0 5px 14px rgba(11,58,98,.18)}
    .p135-desktop{margin-top:14px;border:1px solid #d6e4ef;border-radius:16px;background:linear-gradient(135deg,#fff 0%,#f5f9fc 100%);padding:18px;display:flex;align-items:center;justify-content:space-between;gap:16px}
    .p135-desktop h3{margin:0 0 5px;color:#0b3153}.p135-desktop p{margin:0;color:#6c7f91}.p135-desktop a{display:inline-flex;align-items:center;gap:8px;text-decoration:none;background:#c99820;color:#fff;padding:11px 18px;border-radius:10px;font-weight:900;white-space:nowrap}
    .p135-search-modes{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin:0 0 16px}.p135-search-modes button{min-height:92px;border:1px solid #d5e2ed;background:#f9fbfd;border-radius:14px;color:#0d3152;font-size:16px;font-weight:900;cursor:pointer}.p135-search-modes button.active{border-color:#c9951f;background:#fffaf0;box-shadow:inset 0 0 0 1px #c9951f}.p135-search-modes small{display:block;margin-top:6px;color:#718397;font-size:12px;font-weight:600}
    .p135-asset-tabs{display:flex;gap:8px;overflow:auto;padding:7px;margin:10px 0 14px;background:#f5f9fc;border:1px solid #d8e5ef;border-radius:13px}.p135-asset-tabs button{white-space:nowrap;border:1px solid #d1dfeb;background:#fff;border-radius:9px;padding:9px 14px;color:#133b5c;font-weight:800;cursor:pointer}.p135-asset-tabs button.active{background:#0b3a62;color:#fff;border-color:#0b3a62}
    .p135-popup-stack{position:fixed;z-index:99999;top:18px;left:18px;display:flex;flex-direction:column;gap:10px;max-width:420px}.p135-popup{background:#fff;border:1px solid #d8e4ee;border-radius:14px;box-shadow:0 18px 50px rgba(13,42,68,.22);padding:14px 16px;display:grid;grid-template-columns:1fr auto;gap:12px;align-items:start}.p135-popup strong{color:#0b3153}.p135-popup button{border:0;background:#eef4f8;border-radius:8px;width:28px;height:28px;cursor:pointer}.p135-popup.error{border-inline-start:5px solid #c83b3b}.p135-popup.denied{border-inline-start:5px solid #b86619}.p135-popup.empty{border-inline-start:5px solid #2c8f52}
    @media(max-width:800px){.p135-search-modes{grid-template-columns:1fr}.p135-desktop{align-items:stretch;flex-direction:column}.p135-desktop a{justify-content:center}.p135-popup-stack{left:10px;right:10px;max-width:none}}
  `;
  document.head.appendChild(style);

  function currentRoute(){try{return typeof route!=='undefined'?route:new URLSearchParams(location.hash.replace(/^#/,'')).get('route');}catch{return '';}}

  function popup(message,kind='empty',heading=''){
    const clean=String(message||'').trim(); if(!clean)return;
    let stack=document.querySelector('.p135-popup-stack');
    if(!stack){stack=document.createElement('div');stack.className='p135-popup-stack';stack.setAttribute('aria-live','polite');document.body.appendChild(stack);}
    const key=`${kind}|${heading}|${clean}`;
    if([...stack.children].some(x=>x.dataset.key===key))return;
    const item=document.createElement('div');item.className=`p135-popup ${kind}`;item.dataset.key=key;item.dataset.serial=String(++popupSerial);
    item.innerHTML=`<div>${heading?`<strong>${esc(heading)}</strong><br>`:''}<span>${esc(clean)}</span></div><button type="button" aria-label="${esc(text('Close','إغلاق'))}">×</button>`;
    item.querySelector('button').addEventListener('click',()=>item.remove());stack.appendChild(item);setTimeout(()=>item.remove(),7000);
  }
  window.mamNotify=popup;

  function convertInlineMessages(root=document){
    root.querySelectorAll?.('.state:not(.loading):not([data-p135-popup])').forEach(node=>{
      node.dataset.p135Popup='1';
      const strong=node.querySelector('strong');
      const heading=strong?.textContent?.trim()||'';
      let detail=node.textContent?.trim()||'';
      if(heading&&detail.startsWith(heading))detail=detail.slice(heading.length).trim();
      const kind=[...node.classList].find(x=>['error','denied','degraded','empty'].includes(x))||'empty';
      popup(detail||heading,kind,detail?heading:'');
      node.style.display='none';
    });
  }

  function desktopDownloadName(){
    const host=location.hostname.replace(/[^a-z0-9.-]/gi,'-');
    const protocol=location.protocol==='https:'?'https':'http';
    const apiPort=window.MAM_DESKTOP_API_PORT||7443;
    return `DiwanMAM-Desktop-Setup--env-${protocol}--host-${host}--api-${apiPort}.exe`;
  }

  function enhanceDashboard(){
    if(currentRoute()!=='dashboard')return;
    const host=document.querySelector('#p128DashboardHost,.p128-dashboard');if(!host||host.querySelector('[data-p135-desktop]'))return;
    const card=document.createElement('section');card.className='p135-desktop';card.dataset.p135Desktop='1';
    card.innerHTML=`<div><h3>${esc(text('Desktop application','تطبيق سطح المكتب'))}</h3><p>${esc(text('Download the Windows Desktop application pre-bound to this MAM environment. Install with Next → Next; no API configuration is required.','حمّل تطبيق سطح المكتب لويندوز مضبوطًا تلقائيًا على نفس بيئة MAM الحالية. ثبّته Next ثم Next بدون إدخال إعدادات API.'))}</p></div><a href="/downloads/DiwanMAM-Desktop-Setup.exe" download="${esc(desktopDownloadName())}"><span>⇩</span>${esc(text('Download Desktop App','تحميل تطبيق سطح المكتب'))}</a>`;
    host.prepend(card);
  }

  function enhanceSearch(){
    if(currentRoute()!=='search')return;
    const host=document.getElementById('p12SearchHost');if(!host)return;
    let bar=host.querySelector('.p135-search-modes');
    if(!bar){
      bar=document.createElement('div');bar.className='p135-search-modes';
      bar.innerHTML=`<button type="button" data-p135-mode="text"><span>${esc(text('Text only','نص فقط'))}</span><small>${esc(text('Search using text','البحث باستخدام النص'))}</small></button><button type="button" data-p135-mode="combined"><span>${esc(text('Text + Image','نص + صورة'))}</span><small>${esc(text('Use text and image together','البحث باستخدام نص وصورة معًا'))}</small></button><button type="button" data-p135-mode="image"><span>${esc(text('Image only','صورة فقط'))}</span><small>${esc(text('Search using an image','البحث باستخدام صورة'))}</small></button>`;
      host.prepend(bar);
      bar.querySelectorAll('[data-p135-mode]').forEach(btn=>btn.addEventListener('click',()=>setSearchMode(btn.dataset.p135Mode)));
    }
    if(!bar.querySelector('.active'))setSearchMode('combined');
  }

  function setSearchMode(mode){
    const host=document.getElementById('p12SearchHost');if(!host)return;
    host.querySelectorAll('[data-p135-mode]').forEach(b=>b.classList.toggle('active',b.dataset.p135Mode===mode));
    const nativeText=host.querySelector('[data-visual-text-panel]')||host.children[host.querySelector('.p135-search-modes')?1:0];
    const nativeImage=host.querySelector('[data-visual-image-panel]');
    if(mode==='image')host.querySelector('[data-visual-mode="image"]')?.click();
    else if(mode==='text')host.querySelector('[data-visual-mode="text"]')?.click();
    if(nativeText)nativeText.hidden=mode==='image';
    if(nativeImage)nativeImage.hidden=mode==='text';
    if(mode==='combined'){if(nativeText)nativeText.hidden=false;if(nativeImage)nativeImage.hidden=false;}
    const legacy=host.querySelector('.mam-visual-modebar');if(legacy)legacy.style.display='none';
  }

  function enhanceAssetTabs(){
    if(currentRoute()!=='asset')return;
    const host=document.getElementById('p12AssetDiscovery')||document.getElementById('content');
    if(!host||host.querySelector(':scope > .p135-asset-tabs'))return;
    const cards=[...host.children].filter(x=>x.classList?.contains('card'));
    if(cards.length<2)return;
    const tabs=document.createElement('div');tabs.className='p135-asset-tabs';
    cards.forEach((card,i)=>{
      card.dataset.p135AssetPanel=String(i);
      const title=card.querySelector('h2,h3,strong')?.textContent?.trim()||`${text('Section','قسم')} ${i+1}`;
      const button=document.createElement('button');button.type='button';button.textContent=title;button.dataset.p135AssetTab=String(i);if(i===0)button.classList.add('active');
      button.addEventListener('click',()=>{
        tabs.querySelectorAll('button').forEach(b=>b.classList.toggle('active',b===button));
        cards.forEach((c,j)=>c.hidden=j!==i);
      });tabs.appendChild(button);card.hidden=i!==0;
    });
    host.prepend(tabs);
  }

  function localizePlayFromHere(root=document){
    root.querySelectorAll?.('button,a').forEach(el=>{
      const value=(el.textContent||'').trim().toLowerCase();
      if(value!=='play from here'&&value!=='التشغيل من هنا')return;
      el.textContent=text('Play from here','التشغيل من هنا');
      if(el.dataset.p135PlayBound==='1')return;el.dataset.p135PlayBound='1';
      el.addEventListener('click',()=>{
        const preview=document.querySelector('video,.p04-preview video,[data-media-preview] video,[data-preview] video');
        if(!preview){popup(text('Video preview is not available on this asset.','معاينة الفيديو غير متاحة لهذا الأصل.'),'error',text('Preview','المعاينة'));return;}
        const seek=Number(el.dataset.seekMs||el.dataset.visualSeek||el.dataset.startMs||0);
        if(Number.isFinite(seek)&&seek>0)preview.currentTime=seek/1000;
        preview.scrollIntoView({behavior:'smooth',block:'center'});
        const p=preview.play?.();if(p?.catch)p.catch(()=>{});
      });
    });
  }

  function enhanceLibraryTabs(){
    if(currentRoute()!=='library')return;
    const content=document.getElementById('content');if(!content||content.querySelector(':scope > .p135-tabs'))return;
    const tabs=document.createElement('div');tabs.className='p135-tabs';
    const views=[['all',text('All Media','كل الوسائط')],['upload',text('By Upload Date','حسب تاريخ الرفع')],['production',text('By Production Date','حسب تاريخ الإنتاج')],['category',text('By Category','حسب التصنيف')]];
    tabs.innerHTML=views.map(([k,l])=>`<button type="button" data-p135-library="${k}" class="${libraryView===k?'active':''}">${esc(l)}</button>`).join('');
    content.prepend(tabs);
    tabs.querySelectorAll('[data-p135-library]').forEach(b=>b.addEventListener('click',async()=>{
      libraryView=b.dataset.p135Library;tabs.querySelectorAll('button').forEach(x=>x.classList.toggle('active',x===b));
      if(libraryView==='all'){
        window.__mamP135PreferReferenceLibrary=true;
        if(typeof render==='function')render();
      }else{
        window.__mamP135PreferReferenceLibrary=false;
        if(window.MamMediaLibraryTrees){window.MamMediaLibraryTrees.selectTab(libraryView);await window.MamMediaLibraryTrees.reload();}
      }
      setTimeout(enhanceLibraryTabs,0);
    }));
  }

  function reconcile(){
    enhanceDashboard();enhanceSearch();enhanceAssetTabs();enhanceLibraryTabs();localizePlayFromHere();convertInlineMessages();
  }

  window.__mamP135PreferReferenceLibrary=true;
  const observer=new MutationObserver(()=>{clearTimeout(window.__mamP135Timer);window.__mamP135Timer=setTimeout(reconcile,25);});
  observer.observe(document.body,{childList:true,subtree:true});
  window.addEventListener('hashchange',()=>setTimeout(reconcile,0));
  setTimeout(reconcile,0);setTimeout(reconcile,250);setTimeout(reconcile,1000);
})();