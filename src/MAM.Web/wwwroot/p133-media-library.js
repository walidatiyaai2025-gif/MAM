(() => {
  const UNCATEGORIZED='00000000-0000-0000-0000-000000000001';
  const model={snapshot:null,tab:'upload',selectedAssetId:null,busyAssetId:null,message:null,contextAssetId:null};
  const text=(en,ar)=>arabic?ar:en;
  const html=value=>esc(value??'');
  const id=value=>String(value??'').toLowerCase();
  const categoryName=category=>arabic?(category?.nameAr||category?.nameEn||text('Uncategorized','غير مصنف')):(category?.nameEn||category?.nameAr||'Uncategorized');
  const assetCategoryName=asset=>arabic?(asset?.categoryNameAr||asset?.categoryNameEn||'غير مصنف'):(asset?.categoryNameEn||asset?.categoryNameAr||'Uncategorized');
  const localDateParts=value=>{
    const d=new Date(value);
    if(Number.isNaN(d.getTime()))return null;
    return {year:d.getFullYear(),month:d.getMonth()+1,day:d.getDate(),date:d};
  };
  const productionParts=value=>{
    if(!value)return null;
    const parts=String(value).slice(0,10).split('-').map(Number);
    if(parts.length!==3||parts.some(Number.isNaN))return null;
    return {year:parts[0],month:parts[1],day:parts[2],date:new Date(parts[0],parts[1]-1,parts[2])};
  };
  const monthLabel=(year,month)=>new Intl.DateTimeFormat(arabic?'ar-KW':'en-US',{month:'long',year:'numeric'}).format(new Date(year,month-1,1));
  const dayLabel=(year,month,day)=>new Intl.DateTimeFormat(arabic?'ar-KW':'en-US',{day:'2-digit',month:'long',year:'numeric'}).format(new Date(year,month-1,day));
  const dateTimeLabel=value=>{
    const d=new Date(value); if(Number.isNaN(d.getTime()))return text('Unavailable','غير متاح');
    return new Intl.DateTimeFormat(arabic?'ar-KW':'en-GB',{dateStyle:'medium',timeStyle:'short'}).format(d);
  };
  const productionLabel=value=>{
    const p=productionParts(value); if(!p)return text('No production date','بدون تاريخ إنتاج');
    return new Intl.DateTimeFormat(arabic?'ar-KW':'en-GB',{dateStyle:'medium'}).format(p.date);
  };

  function shell(titleText,subtitle,badge='MEDIA LIBRARY'){
    return lead(titleText,subtitle,badge);
  }

  function apiState(kind,heading,detail){
    return `${shell(text('Media Library','مكتبة الوسائط'),text('Authoritative organization by upload date, actual production date and category.','تنظيم موثوق حسب تاريخ الرفع وتاريخ الإنتاج الفعلي والتصنيف.'),'CENTRAL API')} ${state(kind,heading,detail)}`;
  }

  async function jsonOrNull(response){
    try{return await response.json();}catch{return null;}
  }

  async function loadSnapshot(){
    const languageAtRequest=arabic;
    content.innerHTML=apiState('loading','Loading',text('Loading authoritative Media Library organization…','جاري تحميل تنظيم مكتبة الوسائط الموثوق…'));
    try{
      const response=await fetch('/client-api/media-library/snapshot',{headers:{Accept:'application/json'},cache:'no-store'});
      if(route!=='library'||languageAtRequest!==arabic)return;
      if(response.status===401||response.status===403){content.innerHTML=apiState('denied','Permission denied',text('The current identity cannot read the Media Library.','لا توجد صلاحية للهوية الحالية لقراءة مكتبة الوسائط.'));return;}
      if(response.status===503){content.innerHTML=apiState('degraded','Degraded',text('The authoritative Media Library store is unavailable.','مخزن مكتبة الوسائط الموثوق غير متاح حاليًا.'));return;}
      if(!response.ok)throw new Error(`HTTP ${response.status}`);
      const snapshot=await response.json();
      model.snapshot={categories:Array.isArray(snapshot.categories)?snapshot.categories:[],assets:Array.isArray(snapshot.assets)?snapshot.assets:[]};
      model.message=null;
      renderLibrary();
    }catch(error){
      if(route!=='library'||languageAtRequest!==arabic)return;
      content.innerHTML=apiState('error','API error',text('Media Library could not be loaded. Retry from this page.','تعذر تحميل مكتبة الوسائط. أعد المحاولة من هذه الصفحة.'));
    }
  }

  function renderLibrary(){
    if(route!=='library'||!model.snapshot)return;
    const tabs=[
      ['upload',text('By Upload Date','حسب تاريخ الرفع')],
      ['production',text('By Production Date','حسب تاريخ الإنتاج الفعلي')],
      ['category',text('By Category','حسب التصنيف')]
    ];
    const tabButtons=tabs.map(([key,label])=>`<button class="p133-tab" type="button" role="tab" data-p133-tab="${key}" aria-selected="${model.tab===key}">${html(label)}</button>`).join('');
    const assets=model.snapshot.assets;
    const message=model.message?`<div class="state ${html(model.message.kind)}" role="status"><strong>${html(model.message.heading)}</strong><br>${html(model.message.detail)}</div>`:'';
    content.innerHTML=`${shell(text('Media Library','مكتبة الوسائط'),text('System upload dates are read-only. Production dates remain nullable. Category changes are committed only after an authoritative server reread.','تواريخ الرفع يحددها النظام ولا يمكن تعديلها. تاريخ الإنتاج اختياري، وتغيير التصنيف لا يظهر نجاحه إلا بعد الحفظ والقراءة الموثوقة.'),'CENTRAL API LIVE')}
      <section class="p133-library" aria-label="${html(text('Media Library organization','تنظيم مكتبة الوسائط'))}">
        <div class="card p133-toolbar"><div class="p133-tabs" role="tablist" aria-label="${html(text('Media Library views','طرق عرض مكتبة الوسائط'))}">${tabButtons}</div><button class="action" type="button" data-p133-visual-search>${html(text('Search by Image','البحث بالصورة'))}</button><span class="p133-badge">${assets.length} ${html(text('visible media','وسائط ظاهرة'))}</span></div>
        ${message}
        <div class="card"><div class="p133-tree" role="tabpanel">${treeForCurrentTab()}</div></div>
        <div id="p133MutationState" class="p133-inline-state" aria-live="polite"></div>
      </section>
      <div id="p133Context" class="p133-context" role="dialog" aria-modal="false" hidden></div>`;
    bindLibrary();
  }

  function treeForCurrentTab(){
    if(!model.snapshot||model.snapshot.assets.length===0)return `<div class="p133-empty">${html(text('No media is available for this view.','لا توجد وسائط متاحة في هذا العرض.'))}</div>`;
    if(model.tab==='production')return dateTree(true);
    if(model.tab==='category')return categoryTree();
    return dateTree(false);
  }

  function dateTree(production){
    const groups=new Map(); const noDate=[];
    for(const asset of model.snapshot.assets){
      const p=production?productionParts(asset.productionDate):localDateParts(asset.uploadedAtUtc);
      if(!p){if(production)noDate.push(asset);continue;}
      const y=String(p.year),m=String(p.month).padStart(2,'0'),d=String(p.day).padStart(2,'0');
      if(!groups.has(y))groups.set(y,new Map());
      if(!groups.get(y).has(m))groups.get(y).set(m,new Map());
      if(!groups.get(y).get(m).has(d))groups.get(y).get(m).set(d,[]);
      groups.get(y).get(m).get(d).push(asset);
    }
    const years=[...groups.keys()].sort((a,b)=>Number(b)-Number(a));
    let out=years.map(year=>{
      const months=groups.get(year); const monthKeys=[...months.keys()].sort((a,b)=>Number(b)-Number(a));
      const count=[...months.values()].reduce((sum,days)=>sum+[...days.values()].reduce((s,items)=>s+items.length,0),0);
      const body=monthKeys.map(month=>{
        const days=months.get(month); const dayKeys=[...days.keys()].sort((a,b)=>Number(b)-Number(a));
        const monthCount=[...days.values()].reduce((s,items)=>s+items.length,0);
        const dayBody=dayKeys.map(day=>{
          const items=sortAssets(days.get(day));
          return `<details class="p133-level"><summary>${html(dayLabel(Number(year),Number(month),Number(day)))} <span class="p133-count">${items.length}</span></summary><div>${items.map(mediaRow).join('')}</div></details>`;
        }).join('');
        return `<details class="p133-level"><summary>${html(monthLabel(Number(year),Number(month)))} <span class="p133-count">${monthCount}</span></summary><div>${dayBody}</div></details>`;
      }).join('');
      return `<details open><summary>${html(year)} <span class="p133-count">${count}</span></summary><div>${body}</div></details>`;
    }).join('');
    if(production&&noDate.length){
      out+=`<details open><summary>${html(text('No production date','بدون تاريخ إنتاج'))} <span class="p133-count">${noDate.length}</span></summary><div>${sortAssets(noDate).map(mediaRow).join('')}</div></details>`;
    }
    return out||`<div class="p133-empty">${html(text('No dated media is available.','لا توجد وسائط مؤرخة متاحة.'))}</div>`;
  }

  function sortAssets(items){
    return [...items].sort((a,b)=>String(a.title||'').localeCompare(String(b.title||''),arabic?'ar':'en',{sensitivity:'base'})||id(a.assetId).localeCompare(id(b.assetId)));
  }

  function mediaRow(asset){
    const busy=id(model.busyAssetId)===id(asset.assetId);
    return `<div class="p133-media-row" data-p133-asset-row="${html(asset.assetId)}" draggable="${model.tab==='category'&&!busy?'true':'false'}">
      <button type="button" class="p133-media-open" data-p133-open="${html(asset.assetId)}">${html(asset.title)}<div class="p133-meta">${html(asset.mediaKind)} · ${html(assetCategoryName(asset))} · v${html(asset.version)}</div></button>
      <span class="p133-meta">${html(productionLabel(asset.productionDate))}</span>
      <button type="button" class="p133-change" data-p133-change="${html(asset.assetId)}" ${busy?'disabled':''}>${html(busy?text('Saving…','جاري الحفظ…'):text('Change category','تغيير التصنيف'))}</button>
    </div>`;
  }

  function categoryTree(){
    const categories=model.snapshot.categories||[];
    const byParent=new Map();
    for(const category of categories){const key=id(category.parentCategoryId)||'root';if(!byParent.has(key))byParent.set(key,[]);byParent.get(key).push(category);}
    for(const values of byParent.values())values.sort((a,b)=>(a.sortOrder??0)-(b.sortOrder??0)||categoryName(a).localeCompare(categoryName(b),arabic?'ar':'en'));
    const assetsByCategory=new Map();
    for(const asset of model.snapshot.assets){const key=id(asset.categoryId)||UNCATEGORIZED;if(!assetsByCategory.has(key))assetsByCategory.set(key,[]);assetsByCategory.get(key).push(asset);}
    const seen=new Set();
    const renderCategory=(category,depth=0)=>{
      const key=id(category.categoryId); if(seen.has(key))return ''; seen.add(key);
      const direct=sortAssets(assetsByCategory.get(key)||[]); const children=byParent.get(key)||[];
      const childHtml=children.map(child=>renderCategory(child,depth+1)).join('');
      const system=category.isSystem?` <span class="p133-badge">${html(text('System','نظامي'))}</span>`:'';
      return `<details class="p133-level p133-category-target" open data-p133-drop-category="${html(category.categoryId)}"><summary>${html(categoryName(category))}${system} <span class="p133-count">${direct.length}</span></summary><div>${direct.map(mediaRow).join('')}${childHtml}</div></details>`;
    };
    let out=(byParent.get('root')||[]).map(category=>renderCategory(category)).join('');
    for(const category of categories){if(!seen.has(id(category.categoryId)))out+=renderCategory(category);}
    return out||`<div class="p133-empty">${html(text('No categories are available.','لا توجد تصنيفات متاحة.'))}</div>`;
  }

  function bindLibrary(){
    content.querySelectorAll('[data-p133-tab]').forEach(button=>button.addEventListener('click',()=>{model.tab=button.dataset.p133Tab;model.message=null;renderLibrary();}));
    content.querySelector('[data-p133-visual-search]')?.addEventListener('click',openVisualSearch);
    content.querySelectorAll('[data-p133-open]').forEach(button=>button.addEventListener('click',()=>openDetails(button.dataset.p133Open)));
    content.querySelectorAll('[data-p133-change]').forEach(button=>button.addEventListener('click',event=>openContext(button.dataset.p133Change,event.currentTarget.getBoundingClientRect())));
    content.querySelectorAll('[data-p133-asset-row]').forEach(row=>{
      row.addEventListener('dragstart',event=>{if(model.tab!=='category')return;event.dataTransfer.effectAllowed='move';event.dataTransfer.setData('text/plain',row.dataset.p133AssetRow);});
      row.addEventListener('contextmenu',event=>{event.preventDefault();openContext(row.dataset.p133AssetRow,{left:event.clientX,top:event.clientY,bottom:event.clientY});});
    });
    content.querySelectorAll('[data-p133-drop-category]').forEach(target=>{
      target.addEventListener('dragover',event=>{event.preventDefault();event.dataTransfer.dropEffect='move';target.classList.add('p133-drag-over');});
      target.addEventListener('dragleave',()=>target.classList.remove('p133-drag-over'));
      target.addEventListener('drop',async event=>{event.preventDefault();target.classList.remove('p133-drag-over');const assetId=event.dataTransfer.getData('text/plain');if(assetId)await changeCategory(assetId,target.dataset.p133DropCategory);});
    });
  }

  function openVisualSearch(){
    route='search';
    render();
    let attempts=0;
    const selectImageMode=()=>{
      const button=document.querySelector('[data-visual-mode="image"]');
      if(button){button.click();return;}
      attempts+=1;
      if(route==='search'&&attempts<20)setTimeout(selectImageMode,50);
    };
    selectImageMode();
  }

  function categoryOptions(selectedId){
    return (model.snapshot?.categories||[]).map(category=>`<option value="${html(category.categoryId)}" ${id(category.categoryId)===id(selectedId)?'selected':''}>${html(categoryName(category))}</option>`).join('');
  }

  function openContext(assetId,position){
    const asset=model.snapshot?.assets.find(x=>id(x.assetId)===id(assetId)); if(!asset)return;
    model.contextAssetId=asset.assetId;
    const menu=document.getElementById('p133Context'); if(!menu)return;
    menu.innerHTML=`<strong>${html(text('Change category','تغيير التصنيف'))}</strong><div class="p133-help">${html(asset.title)}</div><label>${html(text('Target category','التصنيف الجديد'))}<select id="p133ContextCategory">${categoryOptions(asset.categoryId)}</select></label><div class="p133-context-actions"><button type="button" class="p133-change" id="p133ContextCancel">${html(text('Cancel','إلغاء'))}</button><button type="button" class="action" id="p133ContextSave">${html(text('Save','حفظ'))}</button></div>`;
    menu.hidden=false;
    const left=Math.min(Math.max(8,Number(position.left)||8),Math.max(8,window.innerWidth-390));
    const top=Math.min(Math.max(8,Number(position.bottom??position.top)||8),Math.max(8,window.innerHeight-230));
    menu.style.left=`${left}px`;menu.style.top=`${top}px`;
    document.getElementById('p133ContextCancel')?.addEventListener('click',closeContext);
    document.getElementById('p133ContextSave')?.addEventListener('click',async()=>{const select=document.getElementById('p133ContextCategory');if(select)await changeCategory(asset.assetId,select.value);closeContext();});
    document.getElementById('p133ContextCategory')?.focus();
  }

  function closeContext(){const menu=document.getElementById('p133Context');if(menu)menu.hidden=true;model.contextAssetId=null;}
  document.addEventListener('keydown',event=>{if(event.key==='Escape')closeContext();});
  document.addEventListener('pointerdown',event=>{const menu=document.getElementById('p133Context');if(menu&&!menu.hidden&&!menu.contains(event.target)&&!event.target.closest?.('[data-p133-change]'))closeContext();});

  async function changeCategory(assetId,categoryId){
    const asset=model.snapshot?.assets.find(x=>id(x.assetId)===id(assetId)); if(!asset||model.busyAssetId)return;
    if(id(asset.categoryId)===id(categoryId)){model.message={kind:'empty',heading:text('No change','لا تغيير'),detail:text('The media is already assigned to this category.','الوسائط مصنفة بالفعل بهذا التصنيف.')};renderLibrary();return;}
    model.busyAssetId=asset.assetId; model.message={kind:'loading',heading:text('Saving','جاري الحفظ'),detail:text('Applying the category change on the authoritative server…','جاري تطبيق تغيير التصنيف على الخادم الموثوق…')}; renderLibrary();
    try{
      const response=await fetch(`/client-api/media-library/assets/${encodeURIComponent(asset.assetId)}/organization`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({expectedVersion:asset.version,productionDate:asset.productionDate||null,categoryId})});
      const payload=await jsonOrNull(response);
      if(response.status===409){if(payload?.current)replaceAsset(payload.current);model.message={kind:'error',heading:text('Conflict','تعارض'),detail:text('The media changed on the server. The current authoritative version was reloaded; retry your change.','تم تعديل الوسائط على الخادم. تم تحميل النسخة الموثوقة الحالية؛ أعد المحاولة.')};return;}
      if(response.status===401||response.status===403){model.message={kind:'denied',heading:'Permission denied',detail:text('You do not have permission to change this media category.','لا توجد صلاحية لتغيير تصنيف هذه الوسائط.')};return;}
      if(!response.ok||!payload)throw new Error(`HTTP ${response.status}`);
      replaceAsset(payload);
      await refreshSnapshotAfterMutation();
      model.message={kind:'empty',heading:text('Saved','تم الحفظ'),detail:text('Category change was persisted and confirmed by an authoritative reread.','تم حفظ تغيير التصنيف وتأكيده بقراءة موثوقة من الخادم.')};
    }catch{model.message={kind:'error',heading:text('Save failed','فشل الحفظ'),detail:text('The category was not shown as changed because the authoritative update could not be confirmed.','لم يتم إظهار التصنيف كتغيير ناجح لأن التحديث الموثوق لم يمكن تأكيده.')};}
    finally{model.busyAssetId=null;if(route==='library')renderLibrary();}
  }

  function replaceAsset(updated){
    if(!model.snapshot)return;const index=model.snapshot.assets.findIndex(x=>id(x.assetId)===id(updated.assetId));if(index>=0)model.snapshot.assets[index]=updated;
  }

  async function refreshSnapshotAfterMutation(){
    const response=await fetch('/client-api/media-library/snapshot',{headers:{Accept:'application/json'},cache:'no-store'});if(!response.ok)throw new Error('authoritative reread failed');const snapshot=await response.json();model.snapshot={categories:Array.isArray(snapshot.categories)?snapshot.categories:[],assets:Array.isArray(snapshot.assets)?snapshot.assets:[]};
  }

  function openDetails(assetId){
    model.selectedAssetId=assetId;
    try{p12SelectedAssetId=assetId;}catch{}
    route='asset';
    render();
  }

  async function attachOrganizationDetails(assetId){
    if(route!=='asset')return;
    const host=document.getElementById('p12AssetDiscovery');
    if(!host||host.querySelector('[data-p133-organization]'))return;
    const section=document.createElement('section');
    section.className='card p133-details';
    section.dataset.p133Organization='1';
    section.innerHTML=`<div class="state loading"><strong>${html(text('Loading media organization','جاري تحميل تنظيم الوسائط'))}</strong><br>${html(text('Loading upload date, production date and category…','جاري تحميل تاريخ الرفع وتاريخ الإنتاج والتصنيف…'))}</div>`;
    host.prepend(section);
    try{
      const [assetResponse,snapshotResponse]=await Promise.all([
        fetch(`/client-api/media-library/assets/${encodeURIComponent(assetId)}`,{headers:{Accept:'application/json'},cache:'no-store'}),
        model.snapshot?Promise.resolve(null):fetch('/client-api/media-library/snapshot',{headers:{Accept:'application/json'},cache:'no-store'})
      ]);
      if(route!=='asset'||!section.isConnected)return;
      if(assetResponse.status===401||assetResponse.status===403){section.innerHTML=state('denied','Permission denied',text('You cannot view this media organization.','لا توجد صلاحية لعرض تنظيم هذه الوسائط.'));return;}
      if(assetResponse.status===404){section.innerHTML=state('empty','Not found',text('The media asset no longer exists.','أصل الوسائط لم يعد موجودًا.'));return;}
      if(!assetResponse.ok)throw new Error(`HTTP ${assetResponse.status}`);
      const asset=await assetResponse.json();
      if(snapshotResponse){if(!snapshotResponse.ok)throw new Error('snapshot failed');const snapshot=await snapshotResponse.json();model.snapshot={categories:Array.isArray(snapshot.categories)?snapshot.categories:[],assets:Array.isArray(snapshot.assets)?snapshot.assets:[]};}
      model.selectedAssetId=asset.assetId;
      replaceAsset(asset);
      renderOrganizationCard(section,asset);
    }catch{
      if(section.isConnected)section.innerHTML=state('error','API error',text('Media organization could not be loaded.','تعذر تحميل تنظيم الوسائط.'));
    }
  }

  function renderOrganizationCard(section,asset,message=null){
    if(!section?.isConnected||route!=='asset')return;
    const messageHtml=message?`<div class="state ${html(message.kind)}" role="status"><strong>${html(message.heading)}</strong><br>${html(message.detail)}</div>`:'';
    section.innerHTML=`<div class="mam-visual-segments-header"><div><h3>${html(text('Media organization','تنظيم الوسائط'))}</h3><p>${html(text('Upload date is system-owned. Production date and category are editable here without replacing the rest of Asset Details.','تاريخ الرفع يملكه النظام. يمكن تعديل تاريخ الإنتاج والتصنيف هنا دون استبدال بقية شاشة تفاصيل الأصل.'))}</p></div><span class="p133-badge">v${html(asset.version)}</span></div>
      ${messageHtml}
      <div class="p133-details-grid">
        <div class="p133-field"><label>${html(text('Upload Date','تاريخ الرفع'))}</label><div class="p133-readonly" aria-readonly="true">${html(dateTimeLabel(asset.uploadedAtUtc))}</div><div class="p133-help">${html(text('Assigned automatically by the system and read-only.','يحدده النظام تلقائيًا ولا يمكن تعديله.'))}</div></div>
        <div class="p133-field"><label>${html(text('Actual Production Date','تاريخ الإنتاج الفعلي'))}</label><input data-p133-production type="date" value="${html(asset.productionDate||'')}"/><div class="p133-help">${html(text('Optional. Blank is stored as NULL.','اختياري. تركه فارغًا يحفظ NULL.'))}</div></div>
        <div class="p133-field"><label>${html(text('Category','التصنيف'))}</label><select data-p133-category>${categoryOptions(asset.categoryId)}</select></div>
        <div class="p133-field"><label>${html(text('Lifecycle','دورة الحياة'))}</label><div class="p133-readonly">${html(asset.lifecycle)}</div></div>
      </div>
      <div data-p133-detail-state class="p133-inline-state" aria-live="polite"></div>
      <div class="p133-detail-actions"><button type="button" class="p133-change" data-p133-back-library>${html(text('Back to Media Library','العودة لمكتبة الوسائط'))}</button><button type="button" class="p133-change" data-p133-clear-production>${html(text('Clear production date','مسح تاريخ الإنتاج'))}</button><button type="button" class="action" data-p133-save-details>${html(text('Save organization','حفظ التنظيم'))}</button></div>`;
    section.querySelector('[data-p133-back-library]')?.addEventListener('click',()=>{route='library';render();});
    section.querySelector('[data-p133-clear-production]')?.addEventListener('click',async()=>{const input=section.querySelector('[data-p133-production]');if(input)input.value='';await saveOrganization(section,asset);});
    section.querySelector('[data-p133-save-details]')?.addEventListener('click',()=>saveOrganization(section,asset));
  }

  async function saveOrganization(section,asset){
    const production=section.querySelector('[data-p133-production]')?.value||null;
    const categoryId=section.querySelector('[data-p133-category]')?.value||UNCATEGORIZED;
    const stateBox=section.querySelector('[data-p133-detail-state]');
    const save=section.querySelector('[data-p133-save-details]');
    const clear=section.querySelector('[data-p133-clear-production]');
    if(save)save.disabled=true;if(clear)clear.disabled=true;if(stateBox)stateBox.innerHTML=`<div class="state loading"><strong>${html(text('Saving','جاري الحفظ'))}</strong><br>${html(text('Waiting for authoritative persistence and reread…','بانتظار الحفظ والقراءة الموثوقة…'))}</div>`;
    try{
      const response=await fetch(`/client-api/media-library/assets/${encodeURIComponent(asset.assetId)}/organization`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({expectedVersion:asset.version,productionDate:production,categoryId})});
      const payload=await jsonOrNull(response);
      if(response.status===409){const current=payload?.current||asset;replaceAsset(current);renderOrganizationCard(section,current,{kind:'error',heading:text('Conflict','تعارض'),detail:text('This media changed on the server. Current values were reloaded; review and retry.','تم تعديل هذه الوسائط على الخادم. تم تحميل القيم الحالية؛ راجعها ثم أعد المحاولة.')});return;}
      if(response.status===401||response.status===403){renderOrganizationCard(section,asset,{kind:'denied',heading:'Permission denied',detail:text('You do not have permission to edit this media.','لا توجد صلاحية لتعديل هذه الوسائط.')});return;}
      if(!response.ok||!payload)throw new Error(`HTTP ${response.status}`);
      const reread=await fetch(`/client-api/media-library/assets/${encodeURIComponent(asset.assetId)}`,{headers:{Accept:'application/json'},cache:'no-store'});if(!reread.ok)throw new Error('reread failed');const confirmed=await reread.json();replaceAsset(confirmed);
      renderOrganizationCard(section,confirmed,{kind:'empty',heading:text('Saved','تم الحفظ'),detail:text('Changes were persisted and confirmed by the authoritative server.','تم حفظ التغييرات وتأكيدها من الخادم الموثوق.')});
    }catch{renderOrganizationCard(section,asset,{kind:'error',heading:text('Save failed','فشل الحفظ'),detail:text('No success is claimed because persistence and authoritative reread could not both be confirmed.','لا يتم إظهار نجاح لأن الحفظ والقراءة الموثوقة لم يتم تأكيدهما معًا.')});}
  }

  const previousAssetDiscovery=typeof p12AttachAssetDiscovery==='function'?p12AttachAssetDiscovery:null;
  if(previousAssetDiscovery){
    const integratedAssetDiscovery=async function(assetId,technical,...rest){
      await previousAssetDiscovery.call(this,assetId,technical,...rest);
      await attachOrganizationDetails(assetId);
    };
    try{p12AttachAssetDiscovery=integratedAssetDiscovery;}catch{}
    window.p12AttachAssetDiscovery=integratedAssetDiscovery;
  }

  loadLiveLibrary=loadSnapshot;

  window.MamMediaLibraryTrees={
    reload:()=>loadSnapshot(),
    selectTab:key=>{if(['upload','production','category'].includes(key)){model.tab=key;if(route==='library')renderLibrary();}},
    selectedAsset:()=>model.selectedAssetId,
    attachOrganization:assetId=>attachOrganizationDetails(assetId)
  };
})();
