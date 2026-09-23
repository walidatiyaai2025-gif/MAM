(() => {
'use strict';

const content = document.getElementById('content');
if (!content) return;

const tr = (en, ar) => (window.arabic ? ar : en);
const safe = value => typeof window.esc === 'function'
  ? window.esc(value ?? '')
  : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const currentRoute = () => { try { return typeof route !== 'undefined' ? route : ''; } catch { return ''; } };

function restoredLibraryView() {
  try {
    const params = new URLSearchParams(location.hash.replace(/^#/, ''));
    const fromHash = params.get('libraryTab');
    if (['browse','upload','production','category','group','reference'].includes(fromHash)) return fromHash;
    const saved = localStorage.getItem('mam.library.tab');
    if (['browse','upload','production','category','group','reference'].includes(saved)) return saved;
  } catch { }
  return 'browse';
}

function persistLibraryView() {
  try {
    localStorage.setItem('mam.library.tab', libraryView);
    const url = new URL(location.href);
    const params = new URLSearchParams(url.hash.replace(/^#/, ''));
    params.set('route','library');
    if (libraryView === 'browse') params.delete('libraryTab');
    else params.set('libraryTab', libraryView);
    url.hash = params.toString();
    history.replaceState(history.state, '', url.href);
  } catch { }
}

let scheduled = false;
let libraryView = restoredLibraryView();
let librarySnapshot = null;
let librarySnapshotPromise = null;
let librarySerial = 0;
let searchMode = 'mixed';
let searchFile = null;
let searchObjectUrl = '';
let searchSerial = 0;
let tapeSearchAvailable = false;
const popupSeen = new Map();

async function json(url, options = {}) {
  const response = await fetch(url, { cache:'no-store', headers:{Accept:'application/json', ...(options.headers || {})}, ...options });
  if (!response.ok) {
    let payload = null;
    try { payload = await response.json(); } catch {}
    const correlationId = response.headers.get('X-Correlation-ID') || payload?.correlationId || '';
    const detail = payload?.detail || payload?.error || `HTTP ${response.status}`;
    const error = new Error(correlationId ? `${detail} · ID ${correlationId}` : detail);
    error.status = response.status;
    error.payload = payload;
    error.correlationId = correlationId;
    throw error;
  }
  return response.status === 204 ? null : response.json();
}

function notify(message, kind = 'success', title = '') {
  const normalized = String(message || '').replace(/\s+/g,' ').trim();
  if (!normalized) return;
  const now = Date.now();
  const last = popupSeen.get(normalized) || 0;
  if (now - last < 1800) return;
  popupSeen.set(normalized, now);

  let stack = document.getElementById('mamPopupStack');
  if (!stack) {
    stack = document.createElement('div');
    stack.id = 'mamPopupStack';
    stack.className = 'mam-popup-stack';
    stack.setAttribute('aria-live','assertive');
    document.body.appendChild(stack);
  }
  const popup = document.createElement('section');
  popup.className = `mam-popup ${kind}`;
  popup.setAttribute('role','alert');
  popup.innerHTML = `<div class="mam-popup-icon"><i class="bi ${kind === 'error' ? 'bi-exclamation-triangle' : kind === 'warning' ? 'bi-exclamation-circle' : 'bi-check-circle'}"></i></div><div class="mam-popup-copy"><strong>${safe(title || (kind === 'error' ? tr('Action failed','تعذر تنفيذ الإجراء') : tr('Completed','تم التنفيذ')))}</strong><p>${safe(normalized)}</p></div><button type="button" class="mam-popup-close" aria-label="${safe(tr('Close','إغلاق'))}"><i class="bi bi-x-lg"></i></button>`;
  stack.appendChild(popup);
  const close = () => { popup.classList.add('closing'); setTimeout(() => popup.remove(), 180); };
  popup.querySelector('.mam-popup-close')?.addEventListener('click', close);
  setTimeout(close, kind === 'error' ? 9000 : 5500);
}

window.MamPopup = Object.freeze({ notify });

function promoteInlineMessages(root = content) {
  const candidates = new Set();
  if (root instanceof Element) candidates.add(root);
  root.querySelectorAll?.('[aria-live="polite"],[role="status"],#p04ActionState,#p04QueueActionState,[id$="ActionState"],[data-p133-detail-state],#p133MutationState,#mamVisualState,[data-visual-segment-state]').forEach(x => candidates.add(x));
  for (const box of candidates) {
    if (!(box instanceof Element)) continue;
    if (['p04AssetState','p04QueueState'].includes(box.id)) continue;
    const stateNode = box.matches('.state') ? box : box.querySelector('.state');
    if (!stateNode || stateNode.classList.contains('loading') || stateNode.dataset.mamPopupPromoted === '1') continue;
    const message = stateNode.textContent?.replace(/\s+/g,' ').trim();
    if (!message || message.length > 900) continue;
    stateNode.dataset.mamPopupPromoted = '1';
    const kind = stateNode.classList.contains('error') || stateNode.classList.contains('denied') || stateNode.classList.contains('degraded') ? 'error' : 'success';
    notify(message, kind);
    if (box !== content && (box.hasAttribute('aria-live') || /ActionState$/.test(box.id) || box.matches('[data-p133-detail-state],#p133MutationState,#mamVisualState,[data-visual-segment-state]'))) {
      stateNode.style.display = 'none';
    }
  }
}

function enhanceDashboard() {
  if (currentRoute() !== 'dashboard') return;
  const actions = document.querySelector('.p128-hero-actions');
  if (!actions || actions.querySelector('[data-mam-desktop-download]')) return;
  const link = document.createElement('a');
  link.className = 'p128-btn mam-desktop-download';
  link.dataset.mamDesktopDownload = '1';
  link.href = '#';
  link.innerHTML = `<i class="bi bi-pc-display-horizontal"></i>${safe(tr('Download Desktop App','تحميل تطبيق سطح المكتب'))}`;
  link.addEventListener('click', event => {
    if (link.dataset.ready !== '1') { event.preventDefault(); notify(tr('Desktop Setup is not available from this server yet.','ملف تثبيت تطبيق سطح المكتب غير متاح من هذا الخادم حتى الآن.'),'error'); return; }
    notify(tr('The Desktop Setup download has started. It is already configured for this MAM environment.','بدأ تحميل تطبيق سطح المكتب وهو مُعد مسبقًا للعمل على بيئة النظام الحالية.'),'success',tr('Desktop application','تطبيق سطح المكتب'));
  });
  actions.appendChild(link);
  fetch('/desktop-download.json', { cache:'no-store', headers:{Accept:'application/json'} })
    .then(r => r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`)))
    .then(meta => {
      if (!meta?.url) throw new Error('download url missing');
      link.href = meta.url;
      link.download = String(meta.url).split('/').pop() || 'DiwanMAM-Desktop-Setup.exe';
      link.dataset.ready = '1';
      link.title = `${tr('Environment','البيئة')}: ${meta.environment || 'MAM'} · ${meta.apiBaseUrl || ''}`;
    })
    .catch(() => { link.dataset.ready = '0'; link.classList.add('disabled'); });
}

function libraryTabBar() {
  const bar = document.createElement('div');
  bar.className = 'mam-library-tabs';
  bar.setAttribute('role','tablist');
  const tabs = [
    ['browse', tr('All Media','كل الوسائط'), 'bi-grid'],
    ['upload', tr('By Upload Date','حسب تاريخ الرفع'), 'bi-calendar3'],
    ['production', tr('By Production Date','حسب تاريخ الإنتاج'), 'bi-calendar-check'],
    ['category', tr('By Category','حسب التصنيف'), 'bi-diagram-3'],
    ['group', tr('By Groups','حسب المجموعات'), 'bi-collection'],
    ['reference', tr('By References','حسب المراجع'), 'bi-person-bounding-box']
  ];
  bar.innerHTML = tabs.map(([key,label,icon]) => `<button type="button" role="tab" data-mam-library-tab="${key}" aria-selected="${libraryView === key}"><i class="bi ${icon}"></i><span>${safe(label)}</span></button>`).join('');
  return bar;
}

function composeLibrary() {
  if (currentRoute() !== 'library') return;
  const host = document.getElementById('p128LibraryHost');
  if (!host) return;
  const p140Ready = host.dataset.mamLibraryOwner === 'p140-authoritative-pagination' &&
    !!host.querySelector('[data-p140-final="1"]');
  if (!p140Ready && !host.querySelector('.p128-library-hero')) return;
  if (host.dataset.mamUnifiedLibrary === '1' && host.querySelector('.mam-library-tabs')) { applyLibraryView(host); return; }

  const children = [...host.children];
  const hero = children.find(node => node.classList?.contains('p128-library-hero')) || null;
  const browse = document.createElement('div');
  browse.className = 'mam-library-browse';
  browse.dataset.mamLibraryView = 'browse';
  children.filter(node => node !== hero).forEach(node => browse.appendChild(node));
  const organization = document.createElement('div');
  organization.className = 'mam-library-organization';
  organization.dataset.mamLibraryView = 'organization';
  organization.hidden = true;
  const tabs = libraryTabBar();
  if (hero) host.replaceChildren(hero, tabs, browse, organization);
  else host.replaceChildren(tabs, browse, organization);
  host.dataset.mamUnifiedLibrary = '1';
  host.querySelectorAll('[data-mam-library-tab]').forEach(button => button.addEventListener('click', () => {
    libraryView = button.dataset.mamLibraryTab || 'browse';
    persistLibraryView();
    applyLibraryView(host);
  }));
  applyLibraryView(host);
}

function applyLibraryView(host) {
  if (!host) return;
  host.querySelectorAll('[data-mam-library-tab]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.mamLibraryTab === libraryView)));
  const browse = host.querySelector('[data-mam-library-view="browse"]');
  const organization = host.querySelector('[data-mam-library-view="organization"]');
  if (browse) browse.hidden = libraryView !== 'browse';
  if (organization) organization.hidden = libraryView === 'browse';
  if (libraryView !== 'browse' && organization) void renderOrganization(organization, libraryView);
}

function dateParts(value, dateOnly = false) {
  if (!value) return null;
  const d = dateOnly ? new Date(`${String(value).slice(0,10)}T12:00:00`) : new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return { year:d.getFullYear(), month:d.getMonth()+1, day:d.getDate(), date:d };
}
function fmtDate(value, dateOnly=false) { const p=dateParts(value,dateOnly); return p ? new Intl.DateTimeFormat(window.arabic?'ar-KW':'en-GB',{dateStyle:'medium',...(dateOnly?{}:{timeStyle:'short'})}).format(p.date) : tr('No production date','بدون تاريخ إنتاج'); }
function categoryName(c) { return window.arabic ? (c?.nameAr || c?.nameEn || 'غير مصنف') : (c?.nameEn || c?.nameAr || 'Uncategorized'); }
function assetCategory(a) { return window.arabic ? (a?.categoryNameAr || a?.categoryNameEn || 'غير مصنف') : (a?.categoryNameEn || a?.categoryNameAr || 'Uncategorized'); }

function normalizeLibrarySnapshot(data) {
  return {
    categories:Array.isArray(data?.categories) ? data.categories : [],
    assets:Array.isArray(data?.assets) ? data.assets : []
  };
}

async function getLibrarySnapshot(force=false) {
  if (librarySnapshot && !force) return librarySnapshot;
  if (librarySnapshotPromise && !force) return librarySnapshotPromise;

  const serial = ++librarySerial;
  const request = (async () => {
    const normalized = normalizeLibrarySnapshot(await json('/client-api/media-library/snapshot'));
    if (serial === librarySerial || !librarySnapshot) librarySnapshot = normalized;
    return librarySnapshot || normalized;
  })();

  librarySnapshotPromise = request;
  try {
    return await request;
  } finally {
    if (librarySnapshotPromise === request) librarySnapshotPromise = null;
  }
}

function mediaOrgRow(asset, draggable=false) {
  return `<div class="mam-org-media" draggable="${draggable}" data-mam-org-asset="${safe(asset.assetId)}"><button type="button" data-mam-open-asset="${safe(asset.assetId)}"><strong>${safe(asset.title || '—')}</strong><small>${safe(asset.mediaKind || '')} · ${safe(assetCategory(asset))} · v${safe(asset.version)}</small></button><span>${safe(fmtDate(asset.productionDate,true))}</span><button type="button" class="action" data-mam-change-category="${safe(asset.assetId)}">${safe(tr('Change category','تغيير التصنيف'))}</button></div>`;
}

function dateOrganization(snapshot, production) {
  snapshot = normalizeLibrarySnapshot(snapshot);
  const groups = new Map(), noDate=[];
  for (const asset of snapshot.assets) {
    const p = dateParts(production ? asset.productionDate : asset.uploadedAtUtc, production);
    if (!p) { if (production) noDate.push(asset); continue; }
    const key = `${p.year}-${String(p.month).padStart(2,'0')}-${String(p.day).padStart(2,'0')}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(asset);
  }
  const entries = [...groups.entries()].sort((a,b)=>b[0].localeCompare(a[0]));
  let html = entries.map(([key,items]) => {
    const [y,m,d]=key.split('-').map(Number);
    const label=new Intl.DateTimeFormat(window.arabic?'ar-KW':'en-GB',{dateStyle:'long'}).format(new Date(y,m-1,d));
    return `<details open class="mam-org-group"><summary>${safe(label)} <span>${items.length}</span></summary><div>${items.map(a=>mediaOrgRow(a,false)).join('')}</div></details>`;
  }).join('');
  if (production && noDate.length) html += `<details open class="mam-org-group"><summary>${safe(tr('No production date','بدون تاريخ إنتاج'))} <span>${noDate.length}</span></summary><div>${noDate.map(a=>mediaOrgRow(a,false)).join('')}</div></details>`;
  return html || `<div class="state empty"><strong>${safe(tr('No media','لا توجد وسائط'))}</strong></div>`;
}

function categoryOrganization(snapshot) {
  snapshot = normalizeLibrarySnapshot(snapshot);
  const categories = snapshot.categories;
  return categories.map(category => {
    const items = snapshot.assets.filter(a => String(a.categoryId||'').toLowerCase() === String(category.categoryId||'').toLowerCase());
    return `<details open class="mam-org-group mam-org-category" data-mam-drop-category="${safe(category.categoryId)}"><summary>${safe(categoryName(category))} <span>${items.length}</span></summary><div>${items.map(a=>mediaOrgRow(a,true)).join('')}</div></details>`;
  }).join('') || `<div class="state empty"><strong>${safe(tr('No categories','لا توجد تصنيفات'))}</strong></div>`;
}

async function renderOrganization(host, view) {
  if (!host || currentRoute() !== 'library') return;
  host.innerHTML = `<div class="state loading"><strong>${safe(tr('Loading organization…','جاري تحميل التنظيم…'))}</strong></div>`;
  try {
    const snapshot = normalizeLibrarySnapshot(await getLibrarySnapshot(false));
    if (!host.isConnected || currentRoute() !== 'library' || libraryView !== view) return;
    const title = view === 'upload' ? tr('Media by upload date','الوسائط حسب تاريخ الرفع')
      : view === 'production' ? tr('Media by production date','الوسائط حسب تاريخ الإنتاج')
      : view === 'category' ? tr('Media by category','الوسائط حسب التصنيف')
      : view === 'group' ? tr('Media by groups','الوسائط حسب المجموعات')
      : tr('Media by references','الوسائط حسب المراجع');

    let treeHtml;
    if (view === 'group') {
      const groups = await json('/client-api/curation/collections');
      const rows = await Promise.all((groups || []).map(async group => {
        try {
          const result = await json(`/client-api/curation/search?collectionId=${encodeURIComponent(group.collectionId)}&page=1&pageSize=100`);
          return { group, items:Array.isArray(result?.items)?result.items:[] };
        } catch { return { group, items:[] }; }
      }));
      treeHtml = rows.map(({group,items}) => `<details class="mam-org-group"><summary>${safe(window.arabic?(group.nameAr||group.nameEn):group.nameEn)} <span>${items.length}</span></summary><div>${items.map(item=>`<div class="mam-org-media"><button type="button" data-mam-open-asset="${safe(item.assetId||item.id)}"><strong>${safe(item.title||'—')}</strong><small>${safe(item.mediaKind||'')}</small></button></div>`).join('') || `<div class="state empty">${safe(tr('No media in this group','لا توجد وسائط في هذه المجموعة'))}</div>`}</div></details>`).join('') || `<div class="state empty"><strong>${safe(tr('No groups','لا توجد مجموعات'))}</strong></div>`;
    } else if (view === 'reference') {
      const refsResponse = await json('/client-api/discovery/references');
      const refs = Array.isArray(refsResponse) ? refsResponse : (Array.isArray(refsResponse?.items) ? refsResponse.items : []);
      treeHtml = refs.map(ref => {
        const name = window.arabic ? (ref.nameAr || ref.nameEn) : (ref.nameEn || ref.nameAr);
        const count = Number(ref.taggedAssetCount ?? ref.assetCount ?? ref.referenceCount ?? 0);
        return `<details class="mam-org-group"><summary>${safe(name||tr('Unnamed reference','مرجع بدون اسم'))} <span>${count}</span></summary><div><div class="mam-org-media"><div><strong>${safe(name||'—')}</strong><small>${safe(ref.tagsText||'')}</small></div><span>${count} ${safe(tr('linked media','وسائط مرتبطة'))}</span></div></div></details>`;
      }).join('') || `<div class="state empty"><strong>${safe(tr('No references','لا توجد مراجع'))}</strong></div>`;
    } else {
      treeHtml = view === 'category' ? categoryOrganization(snapshot) : dateOrganization(snapshot, view === 'production');
    }

    host.innerHTML = `<section class="mam-org-card"><header><div><h3>${safe(title)}</h3><p>${safe(tr('Authoritative organization from the central MAM store.','تنظيم موثوق من مخزن النظام المركزي.'))}</p></div><span class="p133-badge">${snapshot.assets.length} ${safe(tr('media','وسائط'))}</span></header><div class="mam-org-tree">${treeHtml}</div></section>`;
    bindOrganization(host, snapshot);
  } catch (error) {
    host.innerHTML = `<div class="state error"><strong>${safe(tr('Media organization failed to load','تعذر تحميل تنظيم الوسائط'))}</strong><br>${safe(error.message)}</div>`;
    promoteInlineMessages(host);
  }
}

function bindOrganization(host, snapshot) {
  snapshot = normalizeLibrarySnapshot(snapshot);
  host.querySelectorAll('[data-mam-open-asset]').forEach(button => button.addEventListener('click', () => openAsset(button.dataset.mamOpenAsset || '', 0)));
  host.querySelectorAll('[data-mam-change-category]').forEach(button => button.addEventListener('click', () => openCategoryDialog(button.dataset.mamChangeCategory || '', snapshot)));
  host.querySelectorAll('[data-mam-org-asset]').forEach(row => row.addEventListener('dragstart', event => { event.dataTransfer?.setData('text/plain', row.dataset.mamOrgAsset || ''); }));
  host.querySelectorAll('[data-mam-drop-category]').forEach(group => {
    group.addEventListener('dragover', event => { event.preventDefault(); group.classList.add('drag-over'); });
    group.addEventListener('dragleave', () => group.classList.remove('drag-over'));
    group.addEventListener('drop', event => { event.preventDefault(); group.classList.remove('drag-over'); const assetId=event.dataTransfer?.getData('text/plain'); if(assetId) void saveCategory(assetId, group.dataset.mamDropCategory || '', snapshot); });
  });
}

function openCategoryDialog(assetId, snapshot) {
  const asset=snapshot.assets.find(a=>String(a.assetId).toLowerCase()===String(assetId).toLowerCase()); if(!asset)return;
  const overlay=document.createElement('div'); overlay.className='mam-dialog-overlay';
  overlay.innerHTML=`<section class="mam-dialog" role="dialog" aria-modal="true" aria-labelledby="mamCategoryTitle"><h3 id="mamCategoryTitle">${safe(tr('Change category','تغيير التصنيف'))}</h3><p>${safe(asset.title||'')}</p><select>${snapshot.categories.map(c=>`<option value="${safe(c.categoryId)}" ${String(c.categoryId).toLowerCase()===String(asset.categoryId).toLowerCase()?'selected':''}>${safe(categoryName(c))}</option>`).join('')}</select><div><button type="button" data-cancel>${safe(tr('Cancel','إلغاء'))}</button><button type="button" class="action" data-save>${safe(tr('Save','حفظ'))}</button></div></section>`;
  document.body.appendChild(overlay);
  const close=()=>overlay.remove();
  overlay.querySelector('[data-cancel]')?.addEventListener('click',close);
  overlay.addEventListener('click',e=>{if(e.target===overlay)close();});
  overlay.querySelector('[data-save]')?.addEventListener('click',async()=>{const categoryId=overlay.querySelector('select')?.value||'';close();await saveCategory(asset.assetId,categoryId,snapshot);});
  overlay.querySelector('select')?.focus();
}

async function saveCategory(assetId, categoryId, snapshot) {
  const asset=snapshot.assets.find(a=>String(a.assetId).toLowerCase()===String(assetId).toLowerCase()); if(!asset)return;
  if(String(asset.categoryId).toLowerCase()===String(categoryId).toLowerCase()){notify(tr('The media is already assigned to this category.','الوسائط مصنفة بالفعل بهذا التصنيف.'),'warning');return;}
  try{
    const response=await fetch(`/client-api/media-library/assets/${encodeURIComponent(asset.assetId)}/organization`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({expectedVersion:asset.version,productionDate:asset.productionDate||null,categoryId})});
    let payload=null; try{payload=await response.json();}catch{}
    if(response.status===409){librarySnapshot=null;notify(tr('The media changed on the server. Current values will be reloaded; retry the change.','تم تعديل الوسائط على الخادم. سيتم تحميل القيم الحالية؛ أعد المحاولة.'),'error',tr('Conflict','تعارض'));await refreshOrganization();return;}
    if(!response.ok||!payload)throw new Error(`HTTP ${response.status}`);
    librarySnapshot=null;
    await getLibrarySnapshot(true);
    notify(tr('Category change was saved and confirmed by an authoritative reread.','تم حفظ تغيير التصنيف وتأكيده بقراءة موثوقة من الخادم.'),'success');
    await refreshOrganization();
  }catch(error){notify(tr('The category change could not be confirmed.','تعذر تأكيد حفظ تغيير التصنيف.'),'error');}
}
async function refreshOrganization(){const host=document.querySelector('[data-mam-library-view="organization"]');if(host&&libraryView!=='browse')await renderOrganization(host,libraryView);}

async function ensureUnifiedLibrary() {
  if (currentRoute() !== 'library') return;
  const host=document.getElementById('p128LibraryHost');
  const p140Ready=host?.dataset?.mamLibraryOwner==='p140-authoritative-pagination' &&
    !!host.querySelector('[data-p140-final="1"]');
  if (host && (p140Ready || host.querySelector('.p128-library-hero'))) { composeLibrary(); return; }
  try { if (typeof render === 'function') render(); } catch {}
  setTimeout(composeLibrary,80);
}

// P134 asks this API to make P133 replace P128. From this layer forward the same
// hook reconciles into the unified premium Library instead, so Browse remains default.
if (window.MamMediaLibraryTrees) {
  window.MamMediaLibraryTrees.reload = () => ensureUnifiedLibrary();
  window.MamMediaLibraryTrees.selectTab = key => {
    if(['browse','upload','production','category','group','reference'].includes(key)){
      libraryView=key;
      persistLibraryView();
      void ensureUnifiedLibrary();
    }
  };
}
try { loadLiveLibrary = ensureUnifiedLibrary; } catch {}

function searchModeButton(key, icon, label, detail) {
  return `<button type="button" class="mam-search-mode" data-mam-search-mode="${key}" aria-pressed="${searchMode===key}"><i class="bi ${icon}"></i><strong>${safe(label)}</strong><small>${safe(detail)}</small></button>`;
}

async function composeSearch(force=false) {
  if (currentRoute() !== 'search') return;
  const host=document.getElementById('p12SearchHost'); if(!host)return;
  if(!force&&host.querySelector('[data-mam-unified-search]')){applySearchMode(host);return;}
  const serial=++searchSerial;
  let categories=[];
  tapeSearchAvailable=false;
  try{
    const [categoryRows,session,effective]=await Promise.all([
      json('/client-api/discovery/categories'),
      json('/client-api/session'),
      json('/client-api/system-functions/effective').catch(()=>[])
    ]);
    categories=categoryRows||[];
    const permissions=new Set(session?.permissions||[]);
    const flags=new Map((effective||[]).map(x=>[x.functionKey,!!x.isEnabled]));
    tapeSearchAvailable=permissions.has('tape.search')&&flags.get('tape.search.in-content')!==false;
  }catch{}
  if(serial!==searchSerial||currentRoute()!=='search'||!host.isConnected)return;
  host.innerHTML=`<section class="mam-search-page" data-mam-unified-search>
    <div class="mam-search-title"><div><span>${safe(tr('DISCOVERY','البحث والاكتشاف'))}</span><h2>${safe(tr('Content Search','البحث في المحتوى'))}</h2><p>${safe(tr('Search titles, metadata, OCR, transcripts and visually similar media.','ابحث داخل العناوين والبيانات الوصفية وOCR والتفريغ الصوتي والوسائط المتشابهة بصريًا.'))}</p></div><i class="bi bi-search"></i></div>
    <div class="mam-search-card"><header><h3>${safe(tr('Search method','طريقة البحث'))}</h3><p>${safe(tr('Choose the method that matches what you have.','اختر طريقة البحث المناسبة لاحتياجاتك.'))}</p></header>
      <div class="mam-search-modes">${searchModeButton('image','bi-image',tr('Image only','صورة فقط'),tr('Search using an image','البحث باستخدام صورة'))}${searchModeButton('mixed','bi-card-image',tr('Text + Image','نص + صورة'),tr('Combine text and visual similarity','البحث باستخدام نص وصورة معًا'))}${searchModeButton('text','bi-file-earmark-text',tr('Text only','نص فقط'),tr('Search indexed text','البحث باستخدام النص'))}</div>
      <div class="mam-search-inputs">
        <section data-search-text><h4><i class="bi bi-file-earmark-text"></i>${safe(tr('Text','النص'))}</h4><p>${safe(tr('Enter a word or phrase to search all indexed content.','اكتب كلمة أو جملة للبحث داخل كل المحتوى.'))}</p><div class="mam-search-textbox"><input id="mamUnifiedQuery" maxlength="300" placeholder="${safe(tr('Enter search terms…','أدخل كلمات البحث هنا ...'))}"/><i class="bi bi-search"></i></div></section>
        <section data-search-image><h4><i class="bi bi-image"></i>${safe(tr('Image','الصورة'))}</h4><p>${safe(tr('Upload an image to find visually similar content.','ارفع صورة للبحث عن محتوى مشابه.'))}</p><div class="mam-search-drop" id="mamUnifiedDrop" tabindex="0"><input id="mamUnifiedFile" type="file" accept="image/jpeg,image/png,image/bmp,image/gif,image/tiff,image/webp" hidden/><div id="mamUnifiedPreview"><i class="bi bi-cloud-arrow-up"></i><strong>${safe(tr('Drop an image here or click to upload','اسحب الصورة هنا أو اضغط للرفع'))}</strong><small>JPG · PNG · GIF · WebP · ${safe(tr('max 16 MB','الحد الأقصى 16 ميجابايت'))}</small></div></div></section>
      </div>
      <section class="mam-search-filters"><header><h4><i class="bi bi-funnel"></i>${safe(tr('Filter options','خيارات التصفية'))}</h4></header><div>${tapeSearchAvailable?`<label>${safe(tr('Search source','مصدر البحث'))}<select id="mamUnifiedSource"><option value="all">${safe(tr('Media + Tapes','الميديا + الأشرطة'))}</option><option value="media">${safe(tr('Media only','الميديا فقط'))}</option><option value="tapes">${safe(tr('Tapes only','الأشرطة فقط'))}</option></select></label>`:''}<label>${safe(tr('Media type','نوع الوسائط'))}<select id="mamUnifiedKind"><option value="">${safe(tr('All media types','كل أنواع الوسائط'))}</option>${['Video','Audio','Image','Document','Other'].map(k=>`<option value="${k}">${safe(k)}</option>`).join('')}</select></label><label>${safe(tr('Category','التصنيف'))}<select id="mamUnifiedCategory"><option value="">${safe(tr('All categories','كل التصنيفات'))}</option>${categories.map(c=>`<option value="${safe(c.categoryId)}">${safe(categoryName(c))}</option>`).join('')}</select></label></div></section>
      <div class="mam-search-actions"><button type="button" id="mamUnifiedReset">${safe(tr('Reset','إعادة تعيين'))}</button><button type="button" class="action" id="mamUnifiedRun"><i class="bi bi-search"></i>${safe(tr('Search','بحث'))}</button></div>
    </div>
    <div id="mamUnifiedSearchState" aria-live="polite"></div><div id="mamUnifiedResults"></div>
  </section>`;
  bindSearch(host); applySearchMode(host);
}

function applySearchMode(host=document.getElementById('p12SearchHost')) {
  if(!host)return;
  host.querySelectorAll('[data-mam-search-mode]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.mamSearchMode===searchMode)));
  const textPanel=host.querySelector('[data-search-text]'), imagePanel=host.querySelector('[data-search-image]');
  if(textPanel)textPanel.hidden=searchMode==='image';
  if(imagePanel)imagePanel.hidden=searchMode==='text';
  host.querySelector('.mam-search-inputs')?.classList.toggle('single',searchMode!=='mixed');
}

function bindSearch(host) {
  host.querySelectorAll('[data-mam-search-mode]').forEach(button=>button.addEventListener('click',()=>{searchMode=button.dataset.mamSearchMode||'mixed';applySearchMode(host);}));
  const input=host.querySelector('#mamUnifiedFile'),drop=host.querySelector('#mamUnifiedDrop');
  drop?.addEventListener('click',()=>input?.click());
  drop?.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();input?.click();}});
  input?.addEventListener('change',()=>selectSearchFile(input.files?.[0]||null));
  ['dragenter','dragover'].forEach(name=>drop?.addEventListener(name,e=>{e.preventDefault();drop.classList.add('drag-over');}));
  ['dragleave','drop'].forEach(name=>drop?.addEventListener(name,e=>{e.preventDefault();drop.classList.remove('drag-over');}));
  drop?.addEventListener('drop',e=>selectSearchFile(e.dataTransfer?.files?.[0]||null));
  host.querySelector('#mamUnifiedRun')?.addEventListener('click',()=>void runUnifiedSearch());
  host.querySelector('#mamUnifiedQuery')?.addEventListener('keydown',e=>{if(e.key==='Enter')void runUnifiedSearch();});
  host.querySelector('#mamUnifiedReset')?.addEventListener('click',()=>{const q=host.querySelector('#mamUnifiedQuery');if(q)q.value='';const k=host.querySelector('#mamUnifiedKind');if(k)k.value='';const c=host.querySelector('#mamUnifiedCategory');if(c)c.value='';const s=host.querySelector('#mamUnifiedSource');if(s)s.value='all';clearSearchFile();host.querySelector('#mamUnifiedResults').innerHTML='';host.querySelector('#mamUnifiedSearchState').innerHTML='';});
}

function selectSearchFile(file) {
  if(!file)return;
  const allowed=new Set(['image/jpeg','image/png','image/bmp','image/gif','image/tiff','image/webp']);
  if(!allowed.has(String(file.type).toLowerCase())||file.size<=0||file.size>16*1024*1024){notify(tr('Choose a supported image between 1 byte and 16 MB.','اختر صورة مدعومة بحجم بين 1 بايت و16 ميجابايت.'),'error');return;}
  clearSearchFile(false); searchFile=file; searchObjectUrl=URL.createObjectURL(file);
  const preview=document.getElementById('mamUnifiedPreview');if(preview)preview.innerHTML=`<img src="${safe(searchObjectUrl)}" alt="${safe(tr('Search image preview','معاينة صورة البحث'))}"/><strong>${safe(file.name)}</strong><small>${(file.size/1024/1024).toFixed(2)} MB</small>`;
}
function clearSearchFile(resetPreview=true){if(searchObjectUrl)URL.revokeObjectURL(searchObjectUrl);searchObjectUrl='';searchFile=null;const f=document.getElementById('mamUnifiedFile');if(f)f.value='';if(resetPreview){const p=document.getElementById('mamUnifiedPreview');if(p)p.innerHTML=`<i class="bi bi-cloud-arrow-up"></i><strong>${safe(tr('Drop an image here or click to upload','اسحب الصورة هنا أو اضغط للرفع'))}</strong><small>JPG · PNG · GIF · WebP · ${safe(tr('max 16 MB','الحد الأقصى 16 ميجابايت'))}</small>`;}}

async function runUnifiedSearch() {
  const stateBox=document.getElementById('mamUnifiedSearchState'),results=document.getElementById('mamUnifiedResults');if(!stateBox||!results)return;
  const query=document.getElementById('mamUnifiedQuery')?.value.trim()||'';
  const kind=document.getElementById('mamUnifiedKind')?.value||'';
  const category=document.getElementById('mamUnifiedCategory')?.value||'';
  const source=document.getElementById('mamUnifiedSource')?.value||(tapeSearchAvailable?'all':'media');
  if(searchMode!=='image'&&query.length<2){notify(tr('Enter at least two searchable characters.','أدخل حرفين على الأقل للبحث.'),'error',tr('Validation','تحقق'));return;}
  if(searchMode!=='text'&&!searchFile){notify(tr('Choose an image before starting this search.','اختر صورة قبل بدء البحث.'),'error');return;}
  if(source==='tapes'&&searchMode!=='text'){notify(tr('Tape search is text/barcode based. Choose Text only.','بحث الأشرطة يعتمد على النص أو الباركود. اختر البحث النصي فقط.'),'error');return;}
  stateBox.innerHTML=`<div class="state loading"><strong>${safe(tr('Searching…','جاري البحث…'))}</strong></div>`;results.innerHTML='';
  try{
    const params=new URLSearchParams({query,page:'1',pageSize:'100'});if(kind)params.set('mediaKind',kind);if(category)params.set('categoryId',category);
    const includeMedia=source!=='tapes';
    const includeTapes=tapeSearchAvailable&&source!=='media'&&searchMode==='text';
    const textPromise=(searchMode==='image'||!includeMedia)?Promise.resolve(null):json(`/client-api/discovery/search?${params}`);
    const tapePromise=includeTapes?json(`/client-api/tapes/content-search?query=${encodeURIComponent(query)}&limit=100`):Promise.resolve(null);
    const imagePromise=(searchMode==='text'||!includeMedia)?Promise.resolve(null):json('/client-api/discovery/image-search?limit=100',{method:'POST',headers:{'Content-Type':searchFile.type},body:searchFile});
    const [textResult,tapeResult,imageResult]=await Promise.all([textPromise,tapePromise,imagePromise]);
    stateBox.innerHTML='';
    if(searchMode==='text')renderUnifiedTextResults(textResult,tapeResult,results);
    else if(searchMode==='image')renderVisualResults(imageResult,results,kind);
    else {
      const ids=new Set((textResult?.items||[]).map(x=>String(x.assetId).toLowerCase()));
      const mixed={...(imageResult||{}),items:(imageResult?.items||[]).filter(x=>ids.has(String(x.assetId).toLowerCase()))};
      renderVisualResults(mixed,results,kind,true);
    }
  }catch(error){stateBox.innerHTML='';notify(error?.payload?.detail||tr('Search failed.','فشل البحث.'),'error');}
}

function renderUnifiedTextResults(mediaResult,tapeResult,host){
  const media=Array.isArray(mediaResult?.items)?mediaResult.items:[];
  const tapes=Array.isArray(tapeResult?.items)?tapeResult.items:[];
  if(!media.length&&!tapes.length){host.innerHTML=`<div class="state empty"><strong>${safe(tr('No matches','لا توجد نتائج'))}</strong></div>`;return;}
  const mediaHtml=media.map(item=>`<article><div><strong>${safe(item.title)}</strong><p>${safe(item.snippet||'')}</p><small>${safe(item.mediaKind||'')} · ${safe(item.matchedSource||'')}</small></div><button type="button" class="action" data-mam-result-open="${safe(item.assetId)}" data-mam-seek="${safe(item.startMs??0)}">${safe(tr('Open asset','فتح الأصل'))}</button></article>`).join('');
  const tapeHtml=tapes.map(item=>`<article data-mam-tape-result><div><strong>${safe(item.tapeCode)} · ${safe(item.title||tr('Untitled','بدون عنوان'))}</strong><p>${safe(item.description||'')}</p><small>${safe(tr('Tape','شريط'))} · ${safe(item.ownerDepartment||'')} · ${safe(item.digitizationStatus||'')}</small></div><button type="button" class="action" data-mam-tape-open="${safe(item.tapeCode)}">${safe(tr('Open tape','فتح الشريط'))}</button></article>`).join('');
  host.innerHTML=`<section class="mam-search-results"><h3>${media.length+tapes.length} ${safe(tr('results','نتائج'))}</h3>${mediaHtml}${tapeHtml}</section>`;
  bindResultOpen(host);
  host.querySelectorAll('[data-mam-tape-open]').forEach(button=>button.addEventListener('click',()=>window.open(`/tape-inventory.html?scan=${encodeURIComponent(button.dataset.mamTapeOpen||'')}`,'_blank','noopener')));
}

function renderTextResults(result,host){const items=Array.isArray(result?.items)?result.items:[];if(!items.length){host.innerHTML=`<div class="state empty"><strong>${safe(tr('No matches','لا توجد نتائج'))}</strong></div>`;return;}host.innerHTML=`<section class="mam-search-results"><h3>${items.length} ${safe(tr('results','نتائج'))}</h3>${items.map(item=>`<article><div><strong>${safe(item.title)}</strong><p>${safe(item.snippet||'')}</p><small>${safe(item.mediaKind||'')} · ${safe(item.matchedSource||'')}</small></div><button type="button" class="action" data-mam-result-open="${safe(item.assetId)}" data-mam-seek="${safe(item.startMs??0)}">${safe(tr('Open asset','فتح الأصل'))}</button></article>`).join('')}</section>`;bindResultOpen(host);}
function renderVisualResults(result,host,kind,mixed=false){let items=Array.isArray(result?.items)?result.items:[];if(kind)items=items.filter(x=>String(x.mediaKind)===kind);if(!items.length){host.innerHTML=`<div class="state empty"><strong>${safe(mixed?tr('No combined matches','لا توجد نتائج تحقق النص والصورة معًا'):tr('No visual matches','لا توجد نتائج مشابهة بصريًا'))}</strong></div>`;return;}host.innerHTML=`<section class="mam-search-results"><h3>${items.length} ${safe(mixed?tr('combined matches','نتائج مشتركة'):tr('visual matches','نتائج بصرية'))}</h3>${items.map(item=>{const image=item.hasThumbnail&&item.segmentId?`<img src="/client-api/discovery/assets/${encodeURIComponent(item.assetId)}/visual-segments/${encodeURIComponent(item.segmentId)}/thumbnail" alt=""/>`:`<i class="bi bi-image"></i>`;return `<article class="visual"><div class="mam-result-thumb">${image}</div><div><strong>${safe(item.title)}</strong><p>${safe(item.mediaKind||'')} · ${Math.round(Number(item.score||0)*100)}%</p><small>${item.startMs!=null?`${Math.floor(Number(item.startMs)/1000)}s`:safe(item.sourceKind||'visual')}</small></div><button type="button" class="action" data-mam-result-open="${safe(item.assetId)}" data-mam-seek="${safe(item.startMs??0)}">${safe(item.startMs!=null?tr('Open at match','فتح عند موضع التطابق'):tr('Open asset','فتح الأصل'))}</button></article>`;}).join('')}</section>`;bindResultOpen(host);}
function bindResultOpen(host){host.querySelectorAll('[data-mam-result-open]').forEach(button=>button.addEventListener('click',()=>openAsset(button.dataset.mamResultOpen||'',Number(button.dataset.mamSeek||0))));}

function openAsset(assetId, seekMs=0) {
  if(!assetId)return; window.mamVisualPendingSeekMs=Number.isFinite(seekMs)?seekMs:0;
  try{p12SelectedAssetId=assetId;}catch{} window.p12SelectedAssetId=assetId;
  try{route='asset';render();}catch{location.hash=`#route=asset&asset=${encodeURIComponent(assetId)}`;}
  if(seekMs>0)setTimeout(()=>seekAndFocus(seekMs),400);
}

function activatePreviewPanel() {
  const shell=document.querySelector('.mam-asset-tabs-shell');
  if(!shell)return null;
  const tabs=shell.querySelector('.mam-asset-tabs');
  if(tabs)selectAssetTab('preview',tabs,shell,true);
  const panel=shell.querySelector('[data-mam-asset-panel="preview"]');
  panel?.scrollIntoView({behavior:'smooth',block:'start'});
  return panel;
}

function seekAndFocus(ms,attempt=0) {
  const panel=activatePreviewPanel();
  const media=panel?.querySelector('video,audio')||document.querySelector('#p04AssetState video, #p04AssetState audio');
  if(!media){
    if(attempt<20)setTimeout(()=>seekAndFocus(ms,attempt+1),100);
    return !!panel;
  }
  media.scrollIntoView({behavior:'smooth',block:'center'});
  if(!media.hasAttribute('tabindex'))media.setAttribute('tabindex','-1');
  try{media.focus({preventScroll:true});}catch{try{media.focus();}catch{}}
  const apply=()=>{try{media.currentTime=Math.max(0,Number(ms||0)/1000);const promise=media.play?.();if(promise?.catch)promise.catch(()=>{});}catch{}};
  if(media.readyState>=1)apply();else media.addEventListener('loadedmetadata',apply,{once:true});
  return true;
}

function localizeSeekButtons(root=content){root.querySelectorAll?.('[data-segment-seek]').forEach(button=>{button.textContent=tr('Play from here','تشغيل من هنا');button.setAttribute('aria-label',tr('Play from this point in the preview','تشغيل من هذا الموضع في المعاينة'));});}
document.addEventListener('click',event=>{
  const button=event.target.closest?.('[data-segment-seek]');
  if(!button)return;
  event.preventDefault();
  event.stopPropagation();
  const ms=Number(button.dataset.segmentSeek||0);
  requestAnimationFrame(()=>seekAndFocus(ms));
},true);

function composeAssetTabs() {
  if(currentRoute()!=='asset')return;
  const discoveryHost=document.getElementById('p12AssetDiscovery');
  const assetState=document.getElementById('p04AssetState');
  if(!discoveryHost||!assetState)return;

  let shell=assetState.querySelector(':scope > .mam-asset-tabs-shell');
  if(!shell){
    shell=document.createElement('section');
    shell.className='mam-asset-tabs-shell';
    shell.dataset.mamAssetTabsShell='1';

    const defs=[
      ['preview',tr('Preview','المعاينة'),'bi-play-btn'],
      ['technical',tr('Technical','البيانات الفنية'),'bi-cpu'],
      ['organization',tr('Organization','التنظيم'),'bi-diagram-3'],
      ['discovery',tr('Transcript & Visual','التفريغ والمقاطع'),'bi-search']
    ];

    shell.innerHTML=`
      <div class="mam-asset-tabs" role="tablist">
        ${defs.map(([key,label,icon],index)=>`<button type="button" role="tab" data-mam-asset-tab="${key}" aria-selected="${index===0}" tabindex="${index===0?'0':'-1'}"><i class="bi ${icon}"></i><span>${safe(label)}</span></button>`).join('')}
      </div>
      ${defs.map(([key])=>`<section class="mam-asset-panel" role="tabpanel" data-mam-asset-panel="${key}" ${key==='preview'?'':'hidden'}></section>`).join('')}
    `;

    discoveryHost.before(shell);
    const tabs=shell.querySelector('.mam-asset-tabs');
    tabs.querySelectorAll('[data-mam-asset-tab]').forEach(button=>button.addEventListener('click',()=>{
      selectAssetTab(button.dataset.mamAssetTab||'preview',tabs,shell);
    }));
  }

  const tabs=shell.querySelector('.mam-asset-tabs');
  const preview=shell.querySelector('[data-mam-asset-panel="preview"]');
  const technical=shell.querySelector('[data-mam-asset-panel="technical"]');
  const organization=shell.querySelector('[data-mam-asset-panel="organization"]');
  const discovery=shell.querySelector('[data-mam-asset-panel="discovery"]');
  if(!tabs||!preview||!technical||!organization||!discovery)return;

  // The P04 cards are stable owners of the actual preview/player and technical
  // metadata. Move them into the stable shell, never into p12AssetDiscovery.
  const grid=assetState.querySelector(':scope > .grid.two');
  if(grid){
    const technicalCard=grid.querySelector('[data-mam-technical-card]')||grid.children[0];
    const previewCard=grid.querySelector('[data-mam-preview-card]')||grid.children[1];
    if(technicalCard)technical.appendChild(technicalCard);
    if(previewCard)preview.appendChild(previewCard);
    if(!grid.children.length)grid.remove();
  }

  assetState.querySelectorAll(':scope > [data-mam-preview-card]').forEach(card=>preview.appendChild(card));
  [...assetState.children].filter(node=>node.classList?.contains('card')&&node!==shell).forEach(card=>{
    const heading=card.querySelector('h3')?.textContent||'';
    if(/Document preview|معاينة المستند/i.test(heading))preview.appendChild(card);
    else if(/Central API/i.test(card.textContent||''))technical.appendChild(card);
  });

  // Keep the async Discovery renderer intact as one child. Its later innerHTML
  // updates can no longer erase the tab shell or the video/audio preview.
  if(discoveryHost.parentElement!==discovery)discovery.appendChild(discoveryHost);

  const transcriptTabs=document.getElementById('p126TranscriptTabs');
  const transcriptReview=document.getElementById('mamTranscriptReview');
  if(transcriptTabs&&transcriptTabs.parentElement!==discovery)discovery.appendChild(transcriptTabs);
  if(transcriptReview&&transcriptReview.parentElement!==discovery)discovery.appendChild(transcriptReview);

  const organizationNodes=[
    ...discoveryHost.querySelectorAll('[data-p133-organization]'),
    ...assetState.querySelectorAll(':scope > [data-p133-organization]')
  ];
  if(organizationNodes.length){
    organization.querySelector('[data-mam-organization-placeholder]')?.remove();
    organizationNodes.forEach(node=>organization.appendChild(node));
  }

  if(!organization.children.length){
    organization.innerHTML=`<div class="state empty" data-mam-organization-placeholder><strong>${safe(tr('Organization','التنظيم'))}</strong><br>${safe(tr('Category and production-date organization will appear here when available.','سيظهر هنا تنظيم التصنيف وتاريخ الإنتاج عند توفره.'))}</div>`;
  }

  if(!preview.querySelector('video,audio,img,iframe')&&!preview.querySelector('.state')){
    preview.insertAdjacentHTML('beforeend',`<div class="state empty"><strong>${safe(tr('Preview not generated yet','لم يتم إنشاء المعاينة بعد'))}</strong><br>${safe(tr('For video, use Create Proxy above; the verified player will appear here automatically.','للفيديو استخدم إنشاء Proxy بالأعلى؛ وسيظهر المشغل الموثق هنا تلقائيًا.'))}</div>`);
  }

  let requested=shell.dataset.mamAssetSelected||'';
  if(!requested){
    requested='preview';
    try{
      const value=new URLSearchParams(location.hash.replace(/^#/,'')).get('assetTab');
      if(['preview','technical','organization','discovery'].includes(value))requested=value;
    }catch{}
  }
  selectAssetTab(requested,tabs,shell,false);
  localizeSeekButtons(shell);
}

function selectAssetTab(key,tabs,shell,persist=true){
  const selected=['preview','technical','organization','discovery'].includes(key)?key:'preview';
  shell.dataset.mamAssetSelected=selected;
  tabs.querySelectorAll('[data-mam-asset-tab]').forEach(button=>{
    const active=button.dataset.mamAssetTab===selected;
    button.setAttribute('aria-selected',String(active));
    button.classList.toggle('active',active);
    button.tabIndex=active?0:-1;
  });
  shell.querySelectorAll('[data-mam-asset-panel]').forEach(panel=>{
    panel.hidden=panel.dataset.mamAssetPanel!==selected;
  });

  if(persist){
    try{
      const url=new URL(location.href);
      const state=new URLSearchParams(url.hash.replace(/^#/,''));
      state.set('route','asset');
      if(selected==='preview')state.delete('assetTab');else state.set('assetTab',selected);
      url.hash=state.toString();
      history.replaceState(history.state,'',url.href);
    }catch{}
  }

  if(selected==='preview'){
    const media=shell.querySelector('[data-mam-asset-panel="preview"] video,[data-mam-asset-panel="preview"] audio');
    if(media)media.preload='metadata';
  }
}

function finalReconcile() {
  scheduled=false;
  enhanceDashboard();
  composeLibrary();
  if(currentRoute()==='search')void composeSearch(false);
  composeAssetTabs();
  localizeSeekButtons();
  promoteInlineMessages();
}
function schedule(){if(scheduled)return;scheduled=true;setTimeout(finalReconcile,0);}

const observer=new MutationObserver(schedule);observer.observe(content,{childList:true,subtree:true,characterData:true});
const previousRender=typeof window.render==='function'?window.render:null;
if(previousRender&&!previousRender.__mamUnifiedExperience){const wrapped=function(...args){const result=previousRender.apply(this,args);schedule();return result;};wrapped.__mamUnifiedExperience=true;window.render=wrapped;try{render=wrapped;}catch{}}

schedule();
window.mamUnifiedExperience=Object.freeze({version:'p135-unified-1',notify,composeLibrary:ensureUnifiedLibrary,composeSearch:()=>composeSearch(true),seekAndFocus});
})();