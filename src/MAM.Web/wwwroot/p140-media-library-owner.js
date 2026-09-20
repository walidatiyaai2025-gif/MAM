(() => {
'use strict';

const PAGE_SIZE = 24;
const OWNER = 'p140-authoritative-pagination';
let renderSerial = 0;
let repairTimer = 0;

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
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
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

function renderCard(asset) {
  try { if (typeof assetCard === 'function') return assetCard(asset); } catch { }
  const title = isArabic() && asset.titleAr ? asset.titleAr : asset.title;
  return `<article class="p128-asset-card">
    <div class="p128-thumb ${escapeHtml(String(asset.mediaKind||'Other').toLowerCase())}"></div>
    <div class="p128-card-title">${escapeHtml(title || '—')}</div>
    <div class="p128-id">${escapeHtml(asset.id)}</div>
  </article>`;
}

function renderRow(asset) {
  try { if (typeof assetRow === 'function') return assetRow(asset); } catch { }
  const title = isArabic() && asset.titleAr ? asset.titleAr : asset.title;
  return `<div class="row"><b>${escapeHtml(asset.mediaKind||'Other')}</b><span>${escapeHtml(title||'—')}</span><span>${escapeHtml(asset.id)}</span></div>`;
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