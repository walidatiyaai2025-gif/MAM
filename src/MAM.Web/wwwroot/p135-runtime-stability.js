(() => {
  'use strict';
  const content = document.getElementById('content');
  if (!content) return;
  const seenMessages = new WeakMap();

  function routeName(){
    try { if (typeof route !== 'undefined') return route; } catch {}
    return new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  }
  function isArabic(){ try { return typeof arabic !== 'undefined' && !!arabic; } catch { return false; } }

  function stabilizeLibrary(){
    if(routeName()!=='library' || window.mamP135?.libraryMode!=='all') return;
    const section=content.querySelector('.p133-library');
    if(!section || !section.querySelector('[data-p135-all-media]')) return;
    // P133 remains the authoritative backing surface, but once the P135 All Media host
    // exists we remove the class P135 itself watches for. This prevents mutation-driven
    // re-entry while the async premium grid is loading without affecting explicit tab actions.
    section.classList.remove('p133-library');
    section.classList.add('p135-library-root');
  }

  function activateAssetPanel(shell,key){
    shell.querySelectorAll('[data-p135-asset-tab]').forEach(button=>button.setAttribute('aria-selected',String(button.dataset.p135AssetTab===key)));
    shell.querySelectorAll('[data-p135-asset-panel]').forEach(panel=>{panel.hidden=panel.dataset.p135AssetPanel!==key;});
  }

  function stabilizeAssetTabs(){
    if(routeName()!=='asset') return;
    const host=document.getElementById('p04AssetState');
    const shell=host?.querySelector('.p135-asset-tabs');
    if(!host||!shell) return;
    const overview=shell.querySelector('[data-p135-asset-panel="overview"]');
    if(overview){
      [...host.children].filter(node=>node!==shell).forEach(node=>overview.appendChild(node));
    }
    const discovery=shell.querySelector('[data-p135-asset-panel="discovery"]');
    const organization=discovery?.querySelector('[data-p133-organization]');
    if(organization && !shell.querySelector('[data-p135-asset-tab="organization"]')){
      const tabbar=shell.querySelector('.p135-asset-tabbar');
      const panels=shell.querySelector('.p135-asset-panels');
      if(tabbar&&panels){
        const button=document.createElement('button');
        button.type='button';button.dataset.p135AssetTab='organization';button.setAttribute('role','tab');button.setAttribute('aria-selected','false');
        button.textContent=isArabic()?'التنظيم':'Organization';
        const panel=document.createElement('div');panel.className='p135-asset-panel';panel.dataset.p135AssetPanel='organization';panel.setAttribute('role','tabpanel');panel.hidden=true;
        panel.appendChild(organization);tabbar.appendChild(button);panels.appendChild(panel);
        button.addEventListener('click',()=>activateAssetPanel(shell,'organization'));
      }
    }
  }

  function popupKind(state,text){
    if(state.classList.contains('error')||state.classList.contains('denied')||state.classList.contains('degraded')||/fail|error|denied|تعذر|فشل|خطأ|تعارض|غير مسموح/i.test(text))return 'error';
    if(state.classList.contains('warning'))return 'warning';
    return 'success';
  }

  function promoteMessages(){
    if(!window.mamPopup)return;
    const selectors=[
      '#p04ActionState','#p04QueueActionState','#p133MutationState','#mamVisualState',
      '[data-p133-detail-state]','[data-visual-segment-state]',
      '.state.error','.state.denied','.state.degraded','.state.warning'
    ].join(',');
    content.querySelectorAll(selectors).forEach(host=>{
      const state=host.classList.contains('state')?host:host.querySelector(':scope > .state');
      if(!state)return;
      const text=(state.textContent||'').replace(/\s+/g,' ').trim();
      if(!text||/loading|جاري|waiting|انتظار/i.test(text))return;
      const signature=`${state.className}|${text}`;
      if(seenMessages.get(host)===signature)return;
      seenMessages.set(host,signature);
      const heading=state.querySelector('strong')?.textContent?.trim()||(isArabic()?'تم':'Completed');
      const detail=text.startsWith(heading)?text.slice(heading.length).trim():text;
      const kind=popupKind(state,text);
      if(kind==='error')window.mamPopup.error(heading,detail);
      else if(kind==='warning')window.mamPopup.warning(heading,detail);
      else window.mamPopup.success(heading,detail);
      if(!host.classList.contains('state'))host.classList.add('p135-inline-promoted');
    });
  }

  function reconcile(){stabilizeLibrary();stabilizeAssetTabs();promoteMessages();}
  const observer=new MutationObserver(reconcile);
  observer.observe(content,{childList:true,subtree:true});
  reconcile();
  window.mamP135Stability=Object.freeze({version:'p135-stability-1',reconcile});
})();
