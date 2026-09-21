(() => {
'use strict';

const PAGE_SIZE = 24;
const OWNER = 'p140-authoritative-pagination';
let renderSerial = 0;
let repairTimer = 0;
let currentPageSelection = new Map();

const isArabic = () => {
  try { return !!arabic; } catch { return document.documentElement.dir === 'rtl'; }
};
const tr = (en, ar) => isArabic() ? ar : en;
const escapeHtml = value => {
  try { if (typeof esc === 'function') return esc(value); } catch { }
  return String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
};
const numberValue = value => Number.isFinite(Number(value)) ? Number(value) : 0;

async function api(url) {
  const response = await fetch(url, { headers:{Accept:'application/json'}, cache:'no-store' });
  if (response.ok) return response.json();

  let payload = null;
  try { payload = await response.json(); } catch {}
  const correlationId = response.headers.get('X-Correlation-ID') || payload?.correlationId || '';
  const detail = payload?.detail || payload?.error || `HTTP ${response.status}`;
  const error = new Error(correlationId ? `${detail} · ID ${correlationId}` : detail);
  error.status = response.status;
  error.correlationId = correlationId;
  throw error;
}

function currentPage() {
  try { return Math.max(1, Number(p05Page || 1)); } catch { return 1; }
}

function buildParams(includeFacets, pageSize = PAGE_SIZE) {
  const params = new URLSearchParams({
    page: String(currentPage()),
    pageSize: String(pageSize),
    includeFacets: String(includeFacets)
  });
  try { if (p05Query) params.set('query', p05Query); } catch { }
  try { if (p05Lifecycle) params.set('lifecycle', p05Lifecycle); } catch { }
  try { if (p05Category) params.set('category', p05Category); } catch { }
  try { if (p05Tag) params.set('tag', p05Tag); } catch { }
  try { if (p05CollectionId) params.set('collectionId', p05CollectionId); } catch { }
  return params;
}

function mediaKindOptions() {
  try { if (typeof kindOptions === 'function') return kindOptions(); } catch { }
  const selected = (() => { try { return p128MediaKind || ''; } catch { return ''; } })();
  return `<option value="">${escapeHtml(tr('All media types','كل أنواع الميديا'))}</option>` +
    ['Video','Audio','Image','Document','Other'].map(kind =>
      `<option value="${kind}" ${selected===kind?'selected':''}>${escapeHtml(kind)}</option>`
    ).join('');
}

function assetTitle(asset) {
  return String((isArabic() && asset.titleAr ? asset.titleAr : asset.title) || '').trim() || String(asset.id || '');
}

function renderCard(asset) {
  const title = assetTitle(asset);
  const kind = String(asset.mediaKind || 'Other');
  return `<article class="p128-asset-card p140-selectable-card" data-p140-asset-card="${escapeHtml(asset.id)}">
    <label class="p140-card-select" title="${escapeHtml(tr('Select asset','اختيار الميديا'))}">
      <input type="checkbox" data-p140-select-asset="${escapeHtml(asset.id)}" data-p140-select-title="${escapeHtml(title)}" />
      <span>${escapeHtml(tr('Select','اختيار'))}</span>
    </label>
    <div class="p128-thumb ${escapeHtml(kind.toLowerCase())}"><span class="p128-type-pill">${escapeHtml(kind)}</span></div>
    <div class="p128-card-title">${escapeHtml(title || '—')}</div>
    <div class="p128-id">${escapeHtml(asset.id)}</div>
    <div class="p128-card-meta"><span class="p128-dot"></span><span>v${escapeHtml(asset.version)} · ${escapeHtml(asset.lifecycle || 'Draft')}</span></div>
    <div class="p128-card-actions">
      <button type="button" data-mam-open-asset="${escapeHtml(asset.id)}"><i class="bi bi-eye"></i> ${escapeHtml(tr('Details','التفاصيل'))}</button>
      <button type="button" data-p05-edit="${escapeHtml(asset.id)}"><i class="bi bi-pencil-square"></i> ${escapeHtml(tr('Edit','تعديل'))}</button>
      <button type="button" class="danger" data-mam-delete-asset="${escapeHtml(asset.id)}" data-mam-delete-title="${escapeHtml(title)}"><i class="bi bi-trash3"></i> ${escapeHtml(tr('Delete','حذف'))}</button>
    </div>
  </article>`;
}

function renderRow(asset) {
  const title = assetTitle(asset);
  const kind = String(asset.mediaKind || 'Other');
  return `<div class="row p140-selectable-row" data-p140-asset-row="${escapeHtml(asset.id)}">
    <span class="p140-row-select"><input type="checkbox" data-p140-select-asset="${escapeHtml(asset.id)}" data-p140-select-title="${escapeHtml(title)}" aria-label="${escapeHtml(tr('Select asset','اختيار الميديا'))}"/><b>${escapeHtml(kind)}</b></span>
    <span><strong>${escapeHtml(title)}</strong><br><small>${escapeHtml(asset.id)}</small></span>
    <span>v${escapeHtml(asset.version)} · ${escapeHtml(asset.lifecycle || 'Draft')}</span>
    <span class="p140-row-actions"><button type="button" class="action" data-mam-open-asset="${escapeHtml(asset.id)}">${escapeHtml(tr('Details','التفاصيل'))}</button><button type="button" class="action" data-p05-edit="${escapeHtml(asset.id)}">${escapeHtml(tr('Edit','تعديل'))}</button><button type="button" class="action danger" data-mam-delete-asset="${escapeHtml(asset.id)}" data-mam-delete-title="${escapeHtml(title)}">${escapeHtml(tr('Delete','حذف'))}</button></span>
  </div>`;
}

function toast(kind, heading, detail) {
  if (typeof window.mamToast === 'function') {
    window.mamToast(kind, heading, detail);
    return;
  }
  alert(`${heading}: ${detail}`);
}

function openAssetDetails(assetId) {
  try { p12SelectedAssetId = assetId; } catch { }
  try { route = 'asset'; render(); window.scrollTo({top:0,behavior:'auto'}); } catch { }
}

function updateBulkToolbar(items) {
  const count = currentPageSelection.size;
  const countNode = document.getElementById('p140SelectedCount');
  const deleteButton = document.getElementById('p140DeleteSelected');
  const selectAll = document.getElementById('p140SelectAllCurrent');
  if (countNode) countNode.textContent = String(count);
  if (deleteButton) deleteButton.disabled = count === 0;
  if (selectAll) {
    const visibleIds = items.map(item => String(item.id));
    const selectedVisible = visibleIds.filter(id => currentPageSelection.has(id)).length;
    selectAll.checked = visibleIds.length > 0 && selectedVisible === visibleIds.length;
    selectAll.indeterminate = selectedVisible > 0 && selectedVisible < visibleIds.length;
  }
}

function bindBulkSelection(items) {
  currentPageSelection = new Map();
  const inputs = [...document.querySelectorAll('[data-p140-select-asset]')];
  inputs.forEach(input => input.addEventListener('change', () => {
    const id = String(input.dataset.p140SelectAsset || '');
    const title = String(input.dataset.p140SelectTitle || id);
    if (input.checked) currentPageSelection.set(id, title);
    else currentPageSelection.delete(id);
    updateBulkToolbar(items);
  }));

  document.getElementById('p140SelectAllCurrent')?.addEventListener('change', event => {
    const checked = !!event.currentTarget.checked;
    inputs.forEach(input => {
      input.checked = checked;
      const id = String(input.dataset.p140SelectAsset || '');
      const title = String(input.dataset.p140SelectTitle || id);
      if (checked) currentPageSelection.set(id, title);
      else currentPageSelection.delete(id);
    });
    updateBulkToolbar(items);
  });

  document.getElementById('p140ClearSelection')?.addEventListener('click', () => {
    inputs.forEach(input => { input.checked = false; });
    currentPageSelection.clear();
    updateBulkToolbar(items);
  });

  document.getElementById('p140DeleteSelected')?.addEventListener('click', () => {
    const selected = [...currentPageSelection.entries()].map(([id,title]) => ({id,title}));
    if (selected.length) void confirmDeleteAssets(selected);
  });

  document.querySelectorAll('[data-mam-delete-asset]').forEach(button => button.addEventListener('click', () => {
    const id = String(button.dataset.mamDeleteAsset || '');
    const title = String(button.dataset.mamDeleteTitle || id);
    void confirmDeleteAssets([{id,title}]);
  }));

  document.querySelectorAll('[data-mam-open-asset]').forEach(button => button.addEventListener('click', () => openAssetDetails(button.dataset.mamOpenAsset || '')));
  document.querySelectorAll('[data-p05-edit]').forEach(button => button.addEventListener('click', () => {
    const id = String(button.dataset.p05Edit || '');
    if (typeof p05OpenEditor === 'function') void p05OpenEditor(id);
  }));

  updateBulkToolbar(items);
}

async function confirmDeleteAssets(selected) {
  const unique = [...new Map((selected || []).filter(x => x?.id).map(x => [String(x.id), {id:String(x.id),title:String(x.title||x.id)}])).values()];
  if (!unique.length) return;

  document.getElementById('p140BulkDeleteModal')?.remove();
  const backdrop = document.createElement('div');
  backdrop.id = 'p140BulkDeleteModal';
  backdrop.className = 'mam-modal-backdrop';
  const preview = unique.slice(0,6).map(item => `<li><strong>${escapeHtml(item.title)}</strong><small>${escapeHtml(item.id)}</small></li>`).join('');
  const more = unique.length > 6 ? `<li><strong>+${unique.length-6} ${escapeHtml(tr('more','أخرى'))}</strong></li>` : '';
  backdrop.innerHTML = `<div class="mam-modal p140-bulk-delete-modal" role="dialog" aria-modal="true" aria-labelledby="p140BulkDeleteTitle">
    <div class="mam-modal-header"><h3 id="p140BulkDeleteTitle">${escapeHtml(unique.length===1?tr('Permanently delete media','حذف الميديا نهائيًا'):tr(`Permanently delete ${unique.length} media items`,`حذف ${unique.length} ميديا نهائيًا`))}</h3></div>
    <div class="mam-modal-body">
      <p>${escapeHtml(tr('The selected media and all related Primary/Backup files, derivatives, OCR, transcripts, indexes, tags, categories and operational records will be permanently deleted. Immutable audit evidence is retained.','سيتم حذف الميديا المحددة نهائيًا مع ملفات Primary وBackup والمشتقات وOCR والتفريغ والفهرسة والوسوم والتصنيفات وسجلات التشغيل المرتبطة. يبقى سجل التدقيق غير القابل للتعديل.'))}</p>
      <ul class="p140-delete-list">${preview}${more}</ul>
      <label>${escapeHtml(tr('Type DELETE to confirm','اكتب DELETE للتأكيد'))}<input id="p140DeleteConfirmText" autocomplete="off" placeholder="DELETE"/></label>
      <div id="p140DeleteProgress" class="p140-delete-progress" aria-live="polite"></div>
    </div>
    <div class="mam-modal-footer"><button type="button" id="p140DeleteCancel" class="action mam-btn-secondary">${escapeHtml(tr('Cancel','إلغاء'))}</button><button type="button" id="p140DeleteConfirm" class="action mam-btn-danger" disabled>${escapeHtml(tr('Delete selected media','حذف الميديا المحددة'))}</button></div>
  </div>`;
  document.body.appendChild(backdrop);

  const input = backdrop.querySelector('#p140DeleteConfirmText');
  const confirm = backdrop.querySelector('#p140DeleteConfirm');
  const cancel = backdrop.querySelector('#p140DeleteCancel');
  const progress = backdrop.querySelector('#p140DeleteProgress');
  input?.addEventListener('input', () => { if (confirm) confirm.disabled = input.value.trim() !== 'DELETE'; });
  cancel?.addEventListener('click', () => backdrop.remove());

  confirm?.addEventListener('click', async () => {
    confirm.disabled = true;
    if (cancel) cancel.disabled = true;
    if (input) input.disabled = true;
    const failures = [];
    let deleted = 0;

    for (let index=0; index<unique.length; index++) {
      const item = unique[index];
      if (progress) progress.textContent = tr(`Deleting ${index+1} of ${unique.length}: ${item.title}`,`جاري حذف ${index+1} من ${unique.length}: ${item.title}`);
      try {
        const response = await fetch(`/client-api/admin/assets/${encodeURIComponent(item.id)}`, {method:'DELETE',headers:{Accept:'application/json'}});
        const payload = await response.json().catch(() => null);
        if (!response.ok) {
          failures.push({item,detail:payload?.detail || `HTTP ${response.status}`});
          continue;
        }
        deleted++;
        currentPageSelection.delete(item.id);
      } catch (error) {
        failures.push({item,detail:String(error?.message || error || 'Delete failed')});
      }
    }

    backdrop.remove();
    if (deleted) toast('success', tr('Media deleted','تم حذف الميديا'), tr(`${deleted} media item(s) were permanently deleted.`,`تم حذف ${deleted} ميديا نهائيًا.`));
    if (failures.length) {
      const detail = failures.slice(0,3).map(x => `${x.item.title}: ${x.detail}`).join(' · ');
      toast('error', tr(`${failures.length} deletion(s) failed`,`فشل حذف ${failures.length} ميديا`), detail);
    }
    await renderAuthoritativeLibrary();
  });

  setTimeout(() => input?.focus(), 0);
}

async function hydrateFacets(serial) {
  try {
    const params = buildParams(true, 1);
    params.set('page', '1');
    const result = await api(`/client-api/curation/search?${params}`);
    if (serial !== renderSerial || String(route) !== 'library') return;

    const life = document.getElementById('p128Life');
    if (life) {
      let selected = ''; try { selected = p05Lifecycle || ''; } catch { }
      life.innerHTML = `<option value="">${escapeHtml(tr('All states','كل الحالات'))}</option>` +
        (result.facets?.lifecycles || []).map(x =>
          `<option value="${escapeHtml(x.value)}" ${x.value===selected?'selected':''}>${escapeHtml(x.value)}</option>`
        ).join('');
    }

    const category = document.getElementById('p128Cat');
    if (category) {
      let selected = ''; try { selected = p05Category || ''; } catch { }
      category.innerHTML = `<option value="">${escapeHtml(tr('All categories','كل التصنيفات'))}</option>` +
        (result.facets?.categories || []).map(x =>
          `<option value="${escapeHtml(x.value)}" ${x.value===selected?'selected':''}>${escapeHtml(x.value)}</option>`
        ).join('');
    }
  } catch {
    // Facets are enhancement data and must never delay or replace the media cards.
  }
}

function ensureCanonicalHost() {
  let host = document.getElementById('p128LibraryHost');
  const root = document.getElementById('content');
  if (!host && root) {
    host = document.createElement('div');
    host.id = 'p128LibraryHost';
    host.className = 'p128-library';
    root.replaceChildren(host);
  }
  if (host) host.dataset.mamLibraryOwner = OWNER;
  return host;
}

function scheduleOwnerRepair(delay = 0) {
  clearTimeout(repairTimer);
  repairTimer = setTimeout(() => {
    if (String(route) !== 'library') return;
    const host = document.getElementById('p128LibraryHost');
    const finalSurface = host?.dataset?.mamLibraryOwner === OWNER &&
      (host.querySelector('[data-p140-final="1"]') || host.querySelector('.state.loading'));
    if (!finalSurface || document.querySelector('#content .p133-library')) {
      void renderAuthoritativeLibrary();
    }
  }, delay);
}

async function renderAuthoritativeLibrary() {
  if (String(route) !== 'library') return;

  const serial = ++renderSerial;
  const host = ensureCanonicalHost();
  if (!host) return;

  host.innerHTML = `<div class="state loading"><strong>${escapeHtml(tr('Loading media library…','جاري تحميل مكتبة الوسائط…'))}</strong></div>`;

  try {
    const [result, collections] = await Promise.all([
      api(`/client-api/curation/search?${buildParams(false)}`),
      api('/client-api/curation/collections').catch(() => [])
    ]);

    if (serial !== renderSerial || String(route) !== 'library' || !host.isConnected) return;

    const total = numberValue(result.totalCount);
    const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (currentPage() > totalPages) {
      try { p05Page = totalPages; } catch { }
      return renderAuthoritativeLibrary();
    }

    let items = Array.isArray(result.items) ? result.items : [];
    items = items.map(item => ({ ...item, mediaKind:item.mediaKind || 'Other' }));

    try {
      if (p128MediaKind) items = items.filter(item => item.mediaKind === p128MediaKind);
    } catch { }

    try {
      if (p128Sort === 'title') {
        items.sort((a,b) => String(isArabic() && a.titleAr ? a.titleAr : a.title)
          .localeCompare(String(isArabic() && b.titleAr ? b.titleAr : b.title), isArabic() ? 'ar' : 'en'));
      }
    } catch { }

    const page = currentPage();
    const start = total === 0 ? 0 : ((page - 1) * PAGE_SIZE) + 1;
    const end = total === 0 ? 0 : Math.min(total, start + items.length - 1);
    let query = ''; try { query = p05Query || ''; } catch { }
    let lifecycle = ''; try { lifecycle = p05Lifecycle || ''; } catch { }
    let categoryValue = ''; try { categoryValue = p05Category || ''; } catch { }
    let collectionValue = ''; try { collectionValue = p05CollectionId || ''; } catch { }
    let sort = 'newest'; try { sort = p128Sort || 'newest'; } catch { }
    let grid = true; try { grid = !!p05Grid; } catch { }

    host.innerHTML = `
      <section class="p128-library-hero" data-p140-final="1">
        <div class="p128-library-copy">
          <span class="p128-kicker">SEARCH & CURATION <i class="bi bi-headphones"></i></span>
          <h2>${escapeHtml(tr('Media Library','مكتبة الوسائط'))}</h2>
          <p>${escapeHtml(total)} ${escapeHtml(tr('matching assets · authoritative results','أصل مطابق · نتائج مركزية'))}</p>
          <p>${escapeHtml(tr('Search, browse and manage all media assets in one place','ابحث واستعرض وأدر جميع الأصول الإعلامية في مكان واحد'))}</p>
          <div class="p128-library-actions">
            <button class="p128-btn primary" id="p128SearchTop"><i class="bi bi-search"></i>${escapeHtml(tr('Search','البحث'))}</button>
            <button class="p128-btn" id="p128CreateCategory"><i class="bi bi-tag"></i>${escapeHtml(tr('Create category','إنشاء تصنيف'))}</button>
          </div>
        </div>
      </section>

      <section class="p128-filter-panel">
        <div class="p128-filter-title"><i class="bi bi-funnel"></i> ${escapeHtml(tr('Advanced search filters','تصفية البحث المتقدم'))}</div>
        <div class="p128-filters">
          <input id="p128Query" class="p128-control" value="${escapeHtml(query)}" placeholder="${escapeHtml(tr('Arabic or English search','بحث عربي أو إنجليزي'))}"/>
          <select id="p128Kind" class="p128-control">${mediaKindOptions()}</select>
          <select id="p128Life" class="p128-control"><option value="">${escapeHtml(tr('All states','كل الحالات'))}</option></select>
          <select id="p128Cat" class="p128-control"><option value="">${escapeHtml(tr('All categories','كل التصنيفات'))}</option></select>
          <select id="p128Collection" class="p128-control">
            <option value="">${escapeHtml(tr('All collections','كل المجموعات'))}</option>
            ${(collections||[]).map(c => `<option value="${escapeHtml(c.collectionId)}" ${c.collectionId===collectionValue?'selected':''}>${escapeHtml(isArabic()&&c.nameAr?c.nameAr:c.nameEn)}</option>`).join('')}
          </select>
          <button class="p128-filter-btn primary" id="p128Apply"><i class="bi bi-search"></i> ${escapeHtml(tr('Search','بحث'))}</button>
          <button class="p128-filter-btn" id="p128Reset"><i class="bi bi-arrow-clockwise"></i> ${escapeHtml(tr('Reset','مسح'))}</button>
        </div>
      </section>

      <section class="p128-result-head">
        <strong><i class="bi bi-list-ul"></i> ${escapeHtml(tr(`Showing ${start}–${end} of ${total}`,`عرض ${start}–${end} من إجمالي ${total}`))}</strong>
        <div style="display:flex;gap:9px;align-items:center">
          <select id="p128Sort" class="p128-control" style="height:37px;width:145px">
            <option value="newest" ${sort==='newest'?'selected':''}>${escapeHtml(tr('Newest first','الأحدث أولاً'))}</option>
            <option value="title" ${sort==='title'?'selected':''}>${escapeHtml(tr('By title','حسب الاسم'))}</option>
          </select>
          <div class="p128-view-switch">
            <button id="p128Grid" class="${grid?'active':''}"><i class="bi bi-grid"></i></button>
            <button id="p128List" class="${!grid?'active':''}"><i class="bi bi-list"></i></button>
          </div>
        </div>
      </section>

      ${items.length ? `<section class="p140-bulk-toolbar">
        <label class="p140-select-all"><input type="checkbox" id="p140SelectAllCurrent"/> <span>${escapeHtml(tr('Select all on this page','اختيار كل الميديا في هذه الصفحة'))}</span></label>
        <div class="p140-bulk-actions"><span><strong id="p140SelectedCount">0</strong> ${escapeHtml(tr('selected','محدد'))}</span><button type="button" id="p140ClearSelection" class="p128-btn">${escapeHtml(tr('Clear selection','إلغاء التحديد'))}</button><button type="button" id="p140DeleteSelected" class="p128-btn p140-danger-button" disabled><i class="bi bi-trash3"></i> ${escapeHtml(tr('Delete selected','حذف المحدد'))}</button></div>
      </section>` : ''}

      <section id="p128Assets">
        ${items.length
          ? (grid
            ? `<div class="p128-asset-grid">${items.map(renderCard).join('')}</div>`
            : `<div class="list">${items.map(renderRow).join('')}</div>`)
          : `<div class="state empty"><strong>${escapeHtml(tr('No results','لا توجد نتائج'))}</strong><br>${escapeHtml(tr('No assets match the current filters.','لا توجد أصول تطابق عوامل التصفية الحالية.'))}</div>`}
      </section>

      <div class="p127-pager">
        <button id="p128Prev" ${page<=1?'disabled':''}><i class="bi bi-chevron-right"></i></button>
        <span class="p127-page-info">${escapeHtml(tr(`Page ${page} / ${totalPages}`,`صفحة ${page} / ${totalPages}`))}</span>
        <button id="p128Next" ${page>=totalPages?'disabled':''}><i class="bi bi-chevron-left"></i></button>
      </div>`;

    try {
      if (typeof bindLibrary === 'function') bindLibrary(items, collections || [], totalPages);
    } catch { }

    const navigateFromLibrary = routeKey => {
      try {
        route = routeKey;
        if (typeof persistRoute === 'function') persistRoute();
        else {
          const url = new URL(location.href);
          const state = new URLSearchParams(url.hash.replace(/^#/, ''));
          state.set('route', routeKey);
          url.hash = state.toString();
          history.replaceState(history.state, '', url.href);
        }
        if (typeof render === 'function') render();
        else location.hash = `#route=${encodeURIComponent(routeKey)}`;
      } catch {
        location.hash = `#route=${encodeURIComponent(routeKey)}`;
      }
    };

    document.getElementById('p128SearchTop')?.addEventListener('click', event => {
      event.preventDefault();
      event.stopImmediatePropagation();
      navigateFromLibrary('search');
    }, true);

    document.getElementById('p128CreateCategory')?.addEventListener('click', event => {
      event.preventDefault();
      event.stopImmediatePropagation();
      navigateFromLibrary('categories');
    }, true);

    bindBulkSelection(items);

    void hydrateFacets(serial);
    queueMicrotask(() => {
      try { window.mamMediaPreviewRuntime?.scan?.(); } catch { }
      try { window.mamUnifiedExperience?.composeLibrary?.(); } catch { }
    });
  } catch (error) {
    if (serial !== renderSerial || String(route) !== 'library') return;
    host.innerHTML = `<div class="state error"><strong>${escapeHtml(tr('Media library failed to load','تعذر تحميل مكتبة الوسائط'))}</strong><br>${escapeHtml(error?.message || error)}</div>`;
  }
}

try { renderLibrary = renderAuthoritativeLibrary; } catch { }
try { p05LoadLibrary = renderAuthoritativeLibrary; } catch { }
try { loadLiveLibrary = renderAuthoritativeLibrary; } catch { }

window.mamAuthoritativeMediaLibrary = Object.freeze({
  version:'p140-library-owner-2',
  pageSize:PAGE_SIZE,
  render:renderAuthoritativeLibrary,
  diagnose:() => ({
    route:String(route||''),
    page:currentPage(),
    owner:document.getElementById('p128LibraryHost')?.dataset?.mamLibraryOwner || null,
    visibleCards:document.querySelectorAll('.p128-asset-card').length
  })
});

const contentRoot = document.getElementById('content');
if (contentRoot) {
  const ownerObserver = new MutationObserver(() => {
    if (String(route) === 'library') scheduleOwnerRepair(0);
  });
  ownerObserver.observe(contentRoot, { childList:true, subtree:true });
}

window.addEventListener('hashchange', () => {
  if (String(route) === 'library') scheduleOwnerRepair(0);
});

if (String(route) === 'library') queueMicrotask(() => void renderAuthoritativeLibrary());
})();