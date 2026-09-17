(() => {
  'use strict';

  window.mamLibraryView = window.mamLibraryView || 'all';
  const text = (en, ar) => window.arabic ? ar : en;
  const escHtml = value => typeof window.esc === 'function' ? window.esc(value ?? '') : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

  function ensurePopupLayer(){
    let layer=document.getElementById('p135PopupLayer');
    if(!layer){layer=document.createElement('div');layer.id='p135PopupLayer';layer.className='p135-popup-layer';layer.setAttribute('aria-live','assertive');document.body.appendChild(layer);}
    return layer;
  }

  window.mamNotify = function(message, kind='info', heading=''){
    const detail=String(message||'').trim(); if(!detail)return;
    const layer=ensurePopupLayer();
    const popup=document.createElement('div');
    const normalized=/error|denied|fail/i.test(kind)?'error':/success|saved|queued|empty/i.test(kind)?'success':/warn|degraded/i.test(kind)?'warning':'info';
    popup.className=`p135-popup ${normalized}`;
    popup.setAttribute('role','alertdialog');popup.setAttribute('aria-modal','false');
    popup.innerHTML=`<div class="p135-popup-head"><strong>${escHtml(heading||text('Notification','إشعار'))}</strong><button type="button" aria-label="${escHtml(text('Close','إغلاق'))}">×</button></div><p>${escHtml(detail)}</p>`;
    layer.replaceChildren(popup);
    const close=()=>{if(popup.isConnected)popup.remove();};
    popup.querySelector('button')?.addEventListener('click',close);
    const timeout=normalized==='error'?9000:6000;setTimeout(close,timeout);
  };

  function popupInlineActionMessages(root=document){
    const selectors=['#p04ActionState','#p04QueueActionState','#p133MutationState','[data-p133-detail-state]','[data-visual-segment-state]','#mamVisualState'];
    root.querySelectorAll?.(selectors.join(',')).forEach(host=>{
      const state=host.querySelector('.state'); if(!state||state.classList.contains('loading'))return;
      const signature=state.textContent.trim(); if(!signature||host.dataset.p135Notified===signature)return;
      host.dataset.p135Notified=signature;
      const strong=state.querySelector('strong')?.textContent?.trim()||text('Completed','تم التنفيذ');
      const detail=signature.replace(strong,'').trim();
      const kind=[...state.classList].find(x=>['error','denied','degraded','empty','success'].includes(x))||'info';
      window.mamNotify(detail||signature,kind,strong);
      host.classList.add('p135-inline-action-message');
    });
  }

  function enhanceDashboard(){
    if(typeof route!=='undefined'&&route!=='dashboard')return;
    const actions=document.querySelector('#p128DashboardHost .p128-hero-actions'); if(!actions||actions.querySelector('[data-p135-desktop-download]'))return;
    const link=document.createElement('a');link.className='p128-btn p135-desktop-download';link.dataset.p135DesktopDownload='1';link.href='/downloads/desktop';
    link.innerHTML=`<i class="bi bi-windows"></i>${escHtml(text('Download Desktop App','تحميل تطبيق الديسكتوب'))}`;
    link.addEventListener('click',()=>window.mamNotify(text('The installer is prepared for this environment. Run it and continue with Next to start using the Desktop app.','سيتم تنزيل نسخة الإعداد المضبوطة تلقائيًا على هذه البيئة. شغّلها وأكمل Next لبدء استخدام تطبيق الديسكتوب.'),'info',text('Desktop installer','تثبيت تطبيق الديسكتوب')));
    actions.appendChild(link);
  }

  function libraryTabsMarkup(){
    const current=window.mamLibraryView||'all';
    const tabs=[['all',text('All Media','كل الوسائط')],['upload',text('By Upload Date','حسب تاريخ الرفع')],['production',text('By Production Date','حسب تاريخ الإنتاج')],['category',text('By Category','حسب التصنيف')]];
    return `<div class="p135-library-tabs" role="tablist" aria-label="${escHtml(text('Media Library views','طرق عرض مكتبة الوسائط'))}">${tabs.map(([k,l])=>`<button type="button" role="tab" data-p135-library-view="${k}" aria-selected="${current===k}">${escHtml(l)}</button>`).join('')}</div>`;
  }

  function bindLibraryTabs(host){
    host.querySelectorAll('[data-p135-library-view]').forEach(button=>button.addEventListener('click',()=>{
      const view=button.dataset.p135LibraryView||'all';window.mamLibraryView=view;
      if(view==='all'){
        try{render();}catch{}
      }else if(window.MamMediaLibraryTrees){
        window.MamMediaLibraryTrees.selectTab(view);void window.MamMediaLibraryTrees.reload();
      }
    }));
  }

  function enhanceLibrary(){
    if(typeof route==='undefined'||route!=='library')return;
    const p128=document.querySelector('#p128LibraryHost');
    if(p128&&!p128.querySelector('.p135-library-tabs')){
      p128.insertAdjacentHTML('afterbegin',libraryTabsMarkup());bindLibraryTabs(p128);
    }
    const p133=document.querySelector('.p133-library');
    if(p133&&!p133.querySelector('.p135-library-tabs')){
      p133.insertAdjacentHTML('afterbegin',libraryTabsMarkup());bindLibraryTabs(p133);
    }
  }

  let combinedMode=false;
  function enhanceSearch(){
    if(typeof route==='undefined'||route!=='search')return;
    const host=document.getElementById('p12SearchHost');if(!host)return;
    const old=host.querySelector('.mam-visual-modebar');
    if(old&&!host.querySelector('.p135-search-modes')){
      old.hidden=true;
      const modes=document.createElement('div');modes.className='p135-search-modes';
      modes.innerHTML=`
        <button type="button" class="p135-search-mode" data-p135-search-mode="text" aria-pressed="true"><i class="bi bi-file-earmark-text"></i><span>${escHtml(text('Text only','نص فقط'))}</span><small>${escHtml(text('Search using text','البحث باستخدام النص'))}</small></button>
        <button type="button" class="p135-search-mode" data-p135-search-mode="combined" aria-pressed="false"><i class="bi bi-images"></i><span>${escHtml(text('Text + Image','نص + صورة'))}</span><small>${escHtml(text('Use text and a reference image together','البحث باستخدام نص وصورة معًا'))}</small></button>
        <button type="button" class="p135-search-mode" data-p135-search-mode="image" aria-pressed="false"><i class="bi bi-image"></i><span>${escHtml(text('Image only','صورة فقط'))}</span><small>${escHtml(text('Search using an image','البحث باستخدام صورة'))}</small></button>`;
      host.prepend(modes);
      modes.querySelectorAll('[data-p135-search-mode]').forEach(button=>button.addEventListener('click',()=>setSearchMode(button.dataset.p135SearchMode)));
    }
  }

  function setSearchMode(mode){
    const host=document.getElementById('p12SearchHost');if(!host)return;
    combinedMode=mode==='combined';
    host.querySelectorAll('[data-p135-search-mode]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.p135SearchMode===mode)));
    const textPanel=host.querySelector('[data-visual-text-panel]');const imagePanel=host.querySelector('[data-visual-image-panel]');
    if(textPanel)textPanel.hidden=mode==='image';if(imagePanel)imagePanel.hidden=mode==='text';
    if(mode==='image')host.querySelector('[data-visual-mode="image"]')?.click();
    if(mode==='text')host.querySelector('[data-visual-mode="text"]')?.click();
    if(combinedMode){if(textPanel)textPanel.hidden=false;if(imagePanel)imagePanel.hidden=false;}
  }

  function combinedQuery(){
    if(!combinedMode)return '';
    const host=document.getElementById('p12SearchHost');
    const input=host?.querySelector('input[type="search"],input[name="query"],input[id*="Query"],input[placeholder*="بحث"],input[placeholder*="Search"]');
    return String(input?.value||'').trim();
  }

  document.addEventListener('click',event=>{
    const run=event.target.closest?.('#mamVisualRun');if(!run||!combinedMode)return;
    const q=combinedQuery();if(!q)return;
    // The visual-search module owns the body upload. Add the text constraint to its request through a one-shot fetch wrapper.
    const original=window.fetch;let used=false;
    window.fetch=function(input,init){
      const value=String(input);
      if(!used&&value.includes('/client-api/discovery/image-search')){used=true;window.fetch=original;const u=new URL(value,location.origin);u.searchParams.set('query',q);return original(u.pathname+u.search,init);}
      return original(input,init);
    };
    setTimeout(()=>{if(window.fetch!==original)window.fetch=original;},1000);
  },true);

  function enhanceAssetTabs(){
    if(typeof route==='undefined'||route!=='asset')return;
    const host=document.getElementById('p04AssetState');if(!host||host.dataset.p135Tabbed==='1')return;
    const header=host.querySelector(':scope > .card');const grid=host.querySelector(':scope > .grid.two');const discovery=host.querySelector(':scope > #p12AssetDiscovery');
    if(!header||!grid||!discovery)return;
    const cards=[...grid.children];if(cards.length<2)return;
    host.dataset.p135Tabbed='1';
    const tabs=document.createElement('div');tabs.className='p135-asset-tabs';tabs.setAttribute('role','tablist');
    const panels=document.createElement('div');panels.className='p135-asset-panels';
    const specs=[
      ['technical',text('Technical Data','البيانات الفنية'),cards[0]],
      ['preview',text('Preview & Derivatives','المعاينة والمشتقات'),cards[1]],
      ['discovery',text('Discovery & Organization','الاكتشاف والتنظيم'),discovery]
    ];
    const pdf=[...host.children].find(x=>x.classList?.contains('card')&&x.querySelector('iframe'));
    if(pdf)specs[1][2].appendChild(pdf);
    const security=[...host.children].find(x=>x.classList?.contains('card')&&x!==header&&x!==pdf&&x.querySelector('.state.loading')&&!x.closest('#p12AssetDiscovery'));
    if(security)specs.push(['security',text('Security','الأمان'),security]);
    specs.forEach(([key,label,node],index)=>{
      const b=document.createElement('button');b.type='button';b.dataset.p135AssetTab=key;b.setAttribute('role','tab');b.setAttribute('aria-selected',String(index===0));b.textContent=label;tabs.appendChild(b);
      const panel=document.createElement('section');panel.dataset.p135AssetPanel=key;panel.className=index===0?'is-active':'';panel.appendChild(node);panels.appendChild(panel);
    });
    grid.remove();header.insertAdjacentElement('afterend',tabs);tabs.insertAdjacentElement('afterend',panels);
    tabs.querySelectorAll('[data-p135-asset-tab]').forEach(b=>b.addEventListener('click',()=>{
      tabs.querySelectorAll('button').forEach(x=>x.setAttribute('aria-selected',String(x===b)));
      panels.querySelectorAll('[data-p135-asset-panel]').forEach(p=>p.classList.toggle('is-active',p.dataset.p135AssetPanel===b.dataset.p135AssetTab));
    }));
  }

  function reconcile(){enhanceDashboard();enhanceLibrary();enhanceSearch();enhanceAssetTabs();popupInlineActionMessages();}
  const observer=new MutationObserver(()=>setTimeout(reconcile,0));observer.observe(document.body,{childList:true,subtree:true});
  document.addEventListener('DOMContentLoaded',reconcile);setTimeout(reconcile,0);setTimeout(reconcile,300);

  window.MamUnifiedUx=Object.freeze({version:'p135-unified-ux-1',reconcile,notify:window.mamNotify});
})();
