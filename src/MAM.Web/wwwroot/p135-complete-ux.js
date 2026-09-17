(() => {
  'use strict';

  const contentHost = document.getElementById('content');
  if (!contentHost) return;

  const safe = value => typeof window.esc === 'function'
    ? window.esc(value ?? '')
    : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const t = (en, ar) => {
    try { return typeof arabic !== 'undefined' && arabic ? ar : en; } catch { return en; }
  };
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

  let libraryMode = 'all';
  let previousRoute = '';
  let searchMode = 'combined';
  let libraryRenderToken = 0;
  let popupSequence = 0;
  let popupLastSignature = '';
  let popupLastAt = 0;
  let descriptorCache = null;

  const libraryState = {
    page: 1,
    pageSize: 12,
    query: '',
    kind: '',
    lifecycle: '',
    category: '',
    collectionId: '',
    sort: 'newest',
    grid: true
  };

  function currentRoute() {
    try { if (typeof route !== 'undefined') return route; } catch {}
    return new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  }

  function setRoute(next) {
    try {
      route = next;
      render();
    } catch {
      const params = new URLSearchParams(location.hash.replace(/^#/, ''));
      params.set('route', next);
      location.hash = params.toString();
    }
  }

  function ensurePopup() {
    let overlay = document.getElementById('p135PopupOverlay');
    if (overlay) return overlay;
    overlay = document.createElement('div');
    overlay.id = 'p135PopupOverlay';
    overlay.className = 'p135-popup-overlay';
    overlay.hidden = true;
    overlay.innerHTML = `
      <div id="p135MessageDialog" class="p135-popup" role="alertdialog" aria-modal="true" aria-labelledby="p135PopupTitle" aria-describedby="p135PopupDetail" tabindex="-1">
        <div class="p135-popup-icon" aria-hidden="true"><i class="bi bi-info-circle"></i></div>
        <div class="p135-popup-copy"><strong id="p135PopupTitle"></strong><div id="p135PopupDetail"></div></div>
        <button type="button" class="p135-popup-close" aria-label="${safe(t('Close','إغلاق'))}"><i class="bi bi-x-lg"></i></button>
      </div>`;
    document.body.appendChild(overlay);
    const close = () => {
      overlay.hidden = true;
      overlay.classList.remove('is-open');
    };
    overlay.querySelector('.p135-popup-close')?.addEventListener('click', close);
    overlay.addEventListener('click', event => { if (event.target === overlay) close(); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && !overlay.hidden) close(); });
    return overlay;
  }

  function showPopup(kind, heading, detail) {
    const title = String(heading || t('Notice','تنبيه')).trim();
    const body = String(detail || '').trim();
    const signature = `${kind}|${title}|${body}`;
    const now = Date.now();
    if (signature === popupLastSignature && now - popupLastAt < 1800) return;
    popupLastSignature = signature;
    popupLastAt = now;

    const overlay = ensurePopup();
    const dialog = overlay.querySelector('#p135MessageDialog');
    if (!dialog) return;
    dialog.dataset.kind = kind || 'info';
    const titleEl = overlay.querySelector('#p135PopupTitle');
    const detailEl = overlay.querySelector('#p135PopupDetail');
    if (titleEl) titleEl.textContent = title;
    if (detailEl) detailEl.textContent = body;
    const icon = overlay.querySelector('.p135-popup-icon i');
    if (icon) icon.className = `bi ${kind === 'success' ? 'bi-check-circle' : kind === 'error' ? 'bi-x-circle' : kind === 'warning' ? 'bi-exclamation-triangle' : 'bi-info-circle'}`;
    overlay.hidden = false;
    overlay.classList.add('is-open');
    popupSequence += 1;
    dialog.dataset.sequence = String(popupSequence);
    setTimeout(() => dialog.focus({ preventScroll: true }), 0);
  }

  window.mamPopup = Object.freeze({
    success: (heading, detail) => showPopup('success', heading, detail),
    error: (heading, detail) => showPopup('error', heading, detail),
    warning: (heading, detail) => showPopup('warning', heading, detail),
    info: (heading, detail) => showPopup('info', heading, detail)
  });

  const nativeAlert = window.alert.bind(window);
  window.alert = message => {
    try { showPopup('info', t('Notice','تنبيه'), String(message ?? '')); }
    catch { nativeAlert(message); }
  };

  function classifyState(node) {
    if (!(node instanceof HTMLElement)) return null;
    if (node.classList.contains('loading') || node.classList.contains('empty')) return null;
    if (node.classList.contains('error') || node.classList.contains('denied') || node.classList.contains('degraded')) return 'error';
    if (node.classList.contains('warning')) return 'warning';
    if (node.classList.contains('success')) return 'success';
    return null;
  }

  function promoteInlineMessages(root = document) {
    const candidates = [];
    if (root instanceof HTMLElement && root.matches('.state,[id$="State"],[id$="ActionState"]')) candidates.push(root);
    root.querySelectorAll?.('.state,[id$="State"],[id$="ActionState"]').forEach(node => candidates.push(node));
    for (const candidate of candidates) {
      if (!(candidate instanceof HTMLElement) || candidate.dataset.p135PopupHandled === '1') continue;
      if (candidate.id === 'p04AssetState' || candidate.id === 'p04QueueState' || candidate.id === 'p12SearchHost') continue;
      const stateNode = candidate.classList.contains('state') ? candidate : candidate.querySelector(':scope > .state');
      if (!stateNode) continue;
      const explicitKind = classifyState(stateNode);
      const actionHost = /(?:ActionState|MutationState|detail-state|visual-segment-state|mamVisualState)$/i.test(candidate.id || '') ||
        candidate.matches('[data-p133-detail-state],[data-visual-segment-state]');
      const textValue = (stateNode.textContent || '').replace(/\s+/g, ' ').trim();
      if (!textValue || /loading|جاري|انتظار|waiting/i.test(textValue)) continue;
      let kind = explicitKind;
      if (!kind && actionHost) {
        kind = /fail|error|denied|تعذر|فشل|خطأ|تعارض|غير مسموح/i.test(textValue) ? 'error' : 'success';
      }
      if (!kind) continue;
      const heading = stateNode.querySelector('strong')?.textContent?.trim() || (kind === 'success' ? t('Completed','تم') : t('Notice','تنبيه'));
      const detail = textValue.startsWith(heading) ? textValue.slice(heading.length).trim() : textValue;
      candidate.dataset.p135PopupHandled = '1';
      showPopup(kind, heading, detail);
      if (actionHost) candidate.classList.add('p135-inline-promoted');
    }
  }

  async function json(url, options = {}) {
    const response = await fetch(url, {
      cache: 'no-store',
      headers: { Accept: 'application/json', ...(options.headers || {}) },
      ...options
    });
    if (!response.ok) {
      const error = new Error(`HTTP ${response.status}`);
      error.status = response.status;
      try { error.payload = await response.json(); } catch {}
      throw error;
    }
    return response.status === 204 ? null : response.json();
  }

  async function environmentDescriptor() {
    if (descriptorCache) return descriptorCache;
    descriptorCache = await json('/client-environment.json');
    return descriptorCache;
  }

  function installerDownloadName(apiBaseUrl) {
    const url = new URL(apiBaseUrl);
    const scheme = url.protocol.replace(':','').toLowerCase();
    const host = url.hostname;
    const port = url.port || (scheme === 'https' ? '443' : '80');
    return `DiwanMAM-Desktop-Setup__MAMENV__${scheme}__${host}__${port}.exe`;
  }

  async function enhanceDashboard() {
    if (currentRoute() !== 'dashboard') return;
    const host = document.getElementById('p128DashboardHost');
    if (!host || host.querySelector('[data-p135-desktop-download]')) return;
    const anchorPoint = host.querySelector('.p128-metrics') || host.firstElementChild;
    const section = document.createElement('section');
    section.className = 'p135-desktop-download';
    section.dataset.p135DesktopDownload = '1';
    section.innerHTML = `
      <div class="p135-desktop-icon"><i class="bi bi-windows"></i></div>
      <div class="p135-desktop-copy">
        <strong>${safe(t('MAM Desktop Application','تطبيق MAM لسطح المكتب'))}</strong>
        <span>${safe(t('Download the installer already configured for this environment. Install with Next → Next and start working immediately.','حمّل برنامج التثبيت مجهزًا مسبقًا لنفس هذه البيئة. نفّذ Next ← Next ثم ابدأ العمل مباشرة.'))}</span>
        <small data-p135-env-label>${safe(t('Reading environment configuration…','جاري قراءة إعدادات البيئة…'))}</small>
      </div>
      <a class="p135-download-button is-disabled" aria-disabled="true" data-p135-download href="#"><i class="bi bi-download"></i><span>${safe(t('Download Desktop App','تحميل تطبيق سطح المكتب'))}</span></a>`;
    if (anchorPoint?.parentNode) anchorPoint.parentNode.insertBefore(section, anchorPoint);
    else host.appendChild(section);

    const link = section.querySelector('[data-p135-download]');
    const label = section.querySelector('[data-p135-env-label]');
    try {
      const descriptor = await environmentDescriptor();
      if (!section.isConnected || currentRoute() !== 'dashboard') return;
      const apiBaseUrl = String(descriptor?.apiBaseUrl || '').trim();
      if (!/^https?:\/\//i.test(apiBaseUrl)) throw new Error('apiBaseUrl missing');
      const downloadName = installerDownloadName(apiBaseUrl);
      link.href = '/downloads/DiwanMAM-Desktop-Setup-current-x64.exe';
      link.setAttribute('download', downloadName);
      link.classList.remove('is-disabled');
      link.removeAttribute('aria-disabled');
      if (label) label.textContent = `${descriptor.environmentName || t('Current environment','البيئة الحالية')} · ${apiBaseUrl}`;
      link.addEventListener('click', event => {
        if (link.classList.contains('is-disabled')) event.preventDefault();
        else showPopup('success', t('Download started','بدأ التحميل'), t('The Desktop installer is configured for this environment. Run it and continue with Next → Next.','برنامج سطح المكتب مجهز لهذه البيئة. شغّله وأكمل التثبيت باستخدام Next ← Next.'));
      });
    } catch {
      link.addEventListener('click', event => event.preventDefault());
      if (label) label.textContent = t('Environment-bound installer is currently unavailable.','برنامج التثبيت المرتبط بالبيئة غير متاح حاليًا.');
      showPopup('error', t('Desktop download unavailable','تعذر تحميل تطبيق سطح المكتب'), t('The server did not expose a valid environment descriptor. No generic or incorrectly configured installer will be offered.','الخادم لم يوفر وصفًا صالحًا للبيئة، لذلك لن يتم عرض برنامج تثبيت عام أو بإعدادات غير صحيحة.'));
    }
  }

  function libraryKindLabel(kind) {
    const ar = { Video:'فيديو', Audio:'صوت', Image:'صورة', Document:'مستند', Other:'أخرى' };
    return t(kind || 'Other', ar[kind] || kind || 'أخرى');
  }

  function libraryKindIcon(kind) {
    return ({ Video:'bi-play-circle', Audio:'bi-music-note-beamed', Image:'bi-image', Document:'bi-file-earmark-text', Other:'bi-file-earmark' })[kind] || 'bi-file-earmark';
  }

  function libraryTabs() {
    return [
      ['all', t('All Media','كل الوسائط')],
      ['upload', t('By Upload Date','حسب تاريخ الرفع')],
      ['production', t('By Production Date','حسب تاريخ الإنتاج الفعلي')],
      ['category', t('By Category','حسب التصنيف')]
    ];
  }

  function installLibraryTabs(section) {
    if (!section) return null;
    let tabbar = section.querySelector('.p135-library-tabs');
    if (!tabbar) {
      tabbar = document.createElement('div');
      tabbar.className = 'p135-library-tabs';
      tabbar.setAttribute('role','tablist');
      const toolbar = section.querySelector('.p133-toolbar');
      section.insertBefore(tabbar, toolbar || section.firstChild);
    }
    tabbar.innerHTML = libraryTabs().map(([key,label]) => `<button type="button" role="tab" data-p135-library-tab="${key}" aria-selected="${libraryMode === key}">${safe(label)}</button>`).join('');
    tabbar.querySelectorAll('[data-p135-library-tab]').forEach(button => button.addEventListener('click', () => {
      const next = button.dataset.p135LibraryTab || 'all';
      libraryMode = next;
      window.mamP135LibraryMode = next;
      if (next === 'all') {
        void renderAllMedia(section);
      } else {
        if (window.MamMediaLibraryTrees?.selectTab) window.MamMediaLibraryTrees.selectTab(next);
      }
    }));
    return tabbar;
  }

  function curationParams() {
    const params = new URLSearchParams({ page: String(libraryState.page), pageSize: String(libraryState.pageSize) });
    if (libraryState.query) params.set('query', libraryState.query);
    if (libraryState.lifecycle) params.set('lifecycle', libraryState.lifecycle);
    if (libraryState.category) params.set('category', libraryState.category);
    if (libraryState.collectionId) params.set('collectionId', libraryState.collectionId);
    return params;
  }

  function assetCard(asset, kind) {
    const id = asset.id || asset.assetId || '';
    const title = (typeof arabic !== 'undefined' && arabic && asset.titleAr) ? asset.titleAr : (asset.title || asset.titleEn || '—');
    return `<article class="p128-asset-card p135-library-card">
      <div class="p128-thumb ${safe(String(kind || 'Other').toLowerCase())}"><span class="p128-type-pill"><i class="bi ${libraryKindIcon(kind)}"></i> ${safe(libraryKindLabel(kind))}</span><i class="bi ${libraryKindIcon(kind)}"></i></div>
      <div class="p128-card-title">${safe(title)} <i class="bi bi-star" style="float:left;color:#647b92"></i></div>
      <div class="p128-id">${safe(id)}</div>
      <div class="p128-card-meta"><span class="p128-dot"></span><span>v${safe(asset.version ?? 1)} · ${safe(asset.lifecycle || 'Draft')}</span></div>
      <div class="p128-card-actions">
        <button data-mam-open-asset="${safe(id)}"><i class="bi bi-eye"></i> ${safe(t('Asset Details','تفاصيل الأصل'))}</button>
        <button data-p05-edit="${safe(id)}"><i class="bi bi-pencil-square"></i> ${safe(t('Edit Metadata','تعديل البيانات'))}</button>
        <button class="danger" data-mam-delete-asset="${safe(id)}" data-mam-delete-title="${safe(title)}"><i class="bi bi-trash3"></i> ${safe(t('Delete','حذف نهائي'))}</button>
      </div>
    </article>`;
  }

  async function renderAllMedia(section) {
    if (currentRoute() !== 'library' || libraryMode !== 'all' || !section?.isConnected) return;
    const token = ++libraryRenderToken;
    installLibraryTabs(section);
    const nativeToolbar = section.querySelector('.p133-toolbar');
    const nativeTreeCard = section.querySelector('.p133-tree')?.closest('.card');
    const mutation = section.querySelector('#p133MutationState');
    const context = document.getElementById('p133Context');
    if (nativeToolbar) nativeToolbar.hidden = true;
    if (nativeTreeCard) nativeTreeCard.hidden = true;
    if (mutation) mutation.hidden = true;
    if (context) context.hidden = true;

    let host = section.querySelector('[data-p135-all-media]');
    if (!host) {
      host = document.createElement('div');
      host.dataset.p135AllMedia = '1';
      section.appendChild(host);
    }
    host.hidden = false;
    host.innerHTML = `<div class="state loading"><strong>${safe(t('Loading Media Library','جاري تحميل مكتبة الوسائط'))}</strong><br>${safe(t('Loading the authoritative asset catalog…','جاري تحميل كتالوج الأصول الموثوق…'))}</div>`;

    try {
      const [result, collections, snapshot] = await Promise.all([
        json(`/client-api/curation/search?${curationParams()}`),
        json('/client-api/curation/collections').catch(() => []),
        json('/client-api/media-library/snapshot').catch(() => ({ assets: [], categories: [] }))
      ]);
      if (token !== libraryRenderToken || currentRoute() !== 'library' || libraryMode !== 'all' || !host.isConnected) return;
      let items = Array.isArray(result?.items) ? [...result.items] : [];
      const snapshotAssets = Array.isArray(snapshot?.assets) ? snapshot.assets : [];
      const byId = new Map(snapshotAssets.map(asset => [String(asset.assetId || asset.id || '').toLowerCase(), asset]));
      items = items.map(asset => {
        const meta = byId.get(String(asset.id || asset.assetId || '').toLowerCase());
        return { ...asset, mediaKind: meta?.mediaKind || asset.mediaKind || 'Other' };
      });
      if (libraryState.kind) items = items.filter(asset => asset.mediaKind === libraryState.kind);
      if (libraryState.sort === 'title') items.sort((a,b) => String(a.title || '').localeCompare(String(b.title || ''), (typeof arabic !== 'undefined' && arabic) ? 'ar' : 'en'));
      const totalCount = Number(result?.totalCount || 0);
      const totalPages = Math.max(1, Math.ceil(totalCount / libraryState.pageSize));
      const facets = result?.facets || {};
      host.innerHTML = `
        <section class="p128-library-hero p135-library-hero"><div class="p128-library-copy"><span class="p128-kicker">SEARCH & CURATION <i class="bi bi-headphones"></i></span><h2>${safe(t('Media Library','مكتبة الوسائط'))}</h2><p>${safe(totalCount)} ${safe(t('matching assets · authoritative results','أصل مطابق · نتائج مركزية'))}</p><p>${safe(t('Search, browse and manage all media assets in one place','ابحث واستعرض وأدر جميع الأصول الإعلامية في مكان واحد'))}</p><div class="p128-library-actions"><button class="p128-btn primary" data-p135-focus-search><i class="bi bi-search"></i>${safe(t('Search','البحث'))}</button><button class="p128-btn" data-p135-create-category><i class="bi bi-tag"></i>${safe(t('Create category','إنشاء تصنيف'))}</button></div></div></section>
        <section class="p128-filter-panel"><div class="p128-filter-title"><i class="bi bi-funnel"></i> ${safe(t('Advanced search filters','تصفية البحث المتقدم'))}</div><div class="p128-filters">
          <input data-p135-lib-query class="p128-control" value="${safe(libraryState.query)}" placeholder="${safe(t('Arabic or English search','بحث عربي أو إنجليزي'))}"/>
          <select data-p135-lib-kind class="p128-control"><option value="">${safe(t('All media types','كل أنواع الميديا'))}</option>${['Video','Audio','Image','Document','Other'].map(kind => `<option value="${kind}" ${libraryState.kind===kind?'selected':''}>${safe(libraryKindLabel(kind))}</option>`).join('')}</select>
          <select data-p135-lib-life class="p128-control"><option value="">${safe(t('All states','كل الحالات'))}</option>${(facets.lifecycles || []).map(item => `<option value="${safe(item.value)}" ${libraryState.lifecycle===item.value?'selected':''}>${safe(item.value)}</option>`).join('')}</select>
          <select data-p135-lib-cat class="p128-control"><option value="">${safe(t('All categories','كل التصنيفات'))}</option>${(facets.categories || []).map(item => `<option value="${safe(item.value)}" ${libraryState.category===item.value?'selected':''}>${safe(item.value)}</option>`).join('')}</select>
          <select data-p135-lib-collection class="p128-control"><option value="">${safe(t('All collections','كل المجموعات'))}</option>${(Array.isArray(collections)?collections:[]).map(item => `<option value="${safe(item.collectionId)}" ${libraryState.collectionId===item.collectionId?'selected':''}>${safe((typeof arabic !== 'undefined' && arabic && item.nameAr) ? item.nameAr : item.nameEn)}</option>`).join('')}</select>
          <button class="p128-filter-btn primary" data-p135-lib-apply><i class="bi bi-search"></i> ${safe(t('Search','بحث'))}</button>
          <button class="p128-filter-btn" data-p135-lib-reset><i class="bi bi-arrow-clockwise"></i> ${safe(t('Reset','مسح'))}</button>
        </div></section>
        <section class="p128-result-head"><strong><i class="bi bi-list-ul"></i> ${safe(totalCount)} ${safe(t('matching assets · authoritative results','أصل مطابق · نتائج مركزية'))}</strong><div class="p135-library-result-controls"><select data-p135-lib-sort class="p128-control"><option value="newest" ${libraryState.sort==='newest'?'selected':''}>${safe(t('Newest first','الأحدث أولاً'))}</option><option value="title" ${libraryState.sort==='title'?'selected':''}>${safe(t('By title','حسب الاسم'))}</option></select><div class="p128-view-switch"><button type="button" data-p135-lib-grid class="${libraryState.grid?'active':''}"><i class="bi bi-grid"></i></button><button type="button" data-p135-lib-list class="${!libraryState.grid?'active':''}"><i class="bi bi-list"></i></button></div></div></section>
        <section>${items.length ? (libraryState.grid ? `<div class="p128-asset-grid">${items.map(asset => assetCard(asset, asset.mediaKind)).join('')}</div>` : `<div class="p135-library-list">${items.map(asset => `<button type="button" data-mam-open-asset="${safe(asset.id || asset.assetId)}"><strong>${safe(asset.title || '—')}</strong><span>${safe(libraryKindLabel(asset.mediaKind))} · v${safe(asset.version ?? 1)} · ${safe(asset.lifecycle || 'Draft')}</span></button>`).join('')}</div>`) : `<div class="state empty"><strong>${safe(t('No results','لا توجد نتائج'))}</strong><br>${safe(t('No assets match the current filters.','لا توجد أصول تطابق عوامل التصفية الحالية.'))}</div>`}</section>
        <div class="p127-pager"><button type="button" data-p135-lib-prev ${libraryState.page<=1?'disabled':''}><i class="bi bi-chevron-right"></i></button><span class="p127-page-info">${safe(t('Page','صفحة'))} ${libraryState.page} / ${totalPages}</span><button type="button" data-p135-lib-next ${libraryState.page>=totalPages?'disabled':''}><i class="bi bi-chevron-left"></i></button></div>`;

      const apply = () => {
        libraryState.query = host.querySelector('[data-p135-lib-query]')?.value.trim() || '';
        libraryState.kind = host.querySelector('[data-p135-lib-kind]')?.value || '';
        libraryState.lifecycle = host.querySelector('[data-p135-lib-life]')?.value || '';
        libraryState.category = host.querySelector('[data-p135-lib-cat]')?.value || '';
        libraryState.collectionId = host.querySelector('[data-p135-lib-collection]')?.value || '';
        libraryState.page = 1;
        void renderAllMedia(section);
      };
      host.querySelector('[data-p135-lib-apply]')?.addEventListener('click', apply);
      host.querySelector('[data-p135-lib-query]')?.addEventListener('keydown', event => { if (event.key === 'Enter') apply(); });
      host.querySelector('[data-p135-focus-search]')?.addEventListener('click', () => host.querySelector('[data-p135-lib-query]')?.focus());
      host.querySelector('[data-p135-create-category]')?.addEventListener('click', () => setRoute('categories'));
      host.querySelector('[data-p135-lib-reset]')?.addEventListener('click', () => {
        Object.assign(libraryState, { page:1, query:'', kind:'', lifecycle:'', category:'', collectionId:'', sort:'newest', grid:true });
        void renderAllMedia(section);
      });
      host.querySelector('[data-p135-lib-sort]')?.addEventListener('change', event => { libraryState.sort = event.target.value; void renderAllMedia(section); });
      host.querySelector('[data-p135-lib-grid]')?.addEventListener('click', () => { libraryState.grid = true; void renderAllMedia(section); });
      host.querySelector('[data-p135-lib-list]')?.addEventListener('click', () => { libraryState.grid = false; void renderAllMedia(section); });
      host.querySelector('[data-p135-lib-prev]')?.addEventListener('click', () => { if (libraryState.page > 1) { libraryState.page -= 1; void renderAllMedia(section); } });
      host.querySelector('[data-p135-lib-next]')?.addEventListener('click', () => { if (libraryState.page < totalPages) { libraryState.page += 1; void renderAllMedia(section); } });
      host.querySelectorAll('[data-mam-open-asset]').forEach(button => button.addEventListener('click', () => openAsset(button.dataset.mamOpenAsset || '', 0)));
      if (typeof window.p05BindAssetActions === 'function') window.p05BindAssetActions(Array.isArray(collections) ? collections : []);
      else {
        try { if (typeof p05BindAssetActions === 'function') p05BindAssetActions(Array.isArray(collections) ? collections : []); } catch {}
      }
    } catch (error) {
      if (token !== libraryRenderToken || !host.isConnected) return;
      host.innerHTML = `<div class="state error"><strong>${safe(t('Media Library failed to load','تعذر تحميل مكتبة الوسائط'))}</strong><br>${safe(error?.message || '')}</div>`;
      showPopup('error', t('Media Library failed to load','تعذر تحميل مكتبة الوسائط'), t('The authoritative library could not be loaded.','تعذر تحميل مكتبة الوسائط الموثوقة.'));
    }
  }

  function enhanceLibrary() {
    if (currentRoute() !== 'library') return;
    const section = contentHost.querySelector('.p133-library');
    if (!section) return;
    installLibraryTabs(section);
    const allHost = section.querySelector('[data-p135-all-media]');
    const nativeToolbar = section.querySelector('.p133-toolbar');
    const nativeTreeCard = section.querySelector('.p133-tree')?.closest('.card');
    const mutation = section.querySelector('#p133MutationState');
    if (libraryMode === 'all') {
      void renderAllMedia(section);
      return;
    }
    if (allHost) allHost.hidden = true;
    if (nativeToolbar) nativeToolbar.hidden = false;
    if (nativeTreeCard) nativeTreeCard.hidden = false;
    if (mutation) mutation.hidden = false;
    section.querySelectorAll('[data-p135-library-tab]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.p135LibraryTab === libraryMode)));
  }

  function enhanceSearch() {
    if (currentRoute() !== 'search') return;
    const host = document.getElementById('p12SearchHost');
    const modebar = host?.querySelector('.mam-visual-modebar');
    if (!host || !modebar) return;
    let combined = modebar.querySelector('[data-p135-combined-mode]');
    if (!combined) {
      combined = document.createElement('button');
      combined.type = 'button';
      combined.className = 'action';
      combined.dataset.visualMode = 'combined';
      combined.dataset.p135CombinedMode = '1';
      combined.innerHTML = `<i class="bi bi-card-image"></i> ${safe(t('Text + Image','نص + صورة'))}`;
      const imageButton = modebar.querySelector('[data-visual-mode="image"]');
      modebar.insertBefore(combined, imageButton || null);
      combined.addEventListener('click', () => { searchMode = 'combined'; applySearchMode(); });
      modebar.querySelector('[data-visual-mode="text"]')?.addEventListener('click', () => { searchMode = 'text'; setTimeout(applySearchMode,0); });
      modebar.querySelector('[data-visual-mode="image"]')?.addEventListener('click', () => { searchMode = 'image'; setTimeout(applySearchMode,0); });
      searchMode = 'combined';
    }
    applySearchMode();
  }

  function applySearchMode() {
    if (currentRoute() !== 'search') return;
    const host = document.getElementById('p12SearchHost');
    if (!host) return;
    host.classList.toggle('p135-combined-search', searchMode === 'combined');
    host.querySelectorAll('[data-visual-mode]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.visualMode === searchMode)));
    const textPanel = host.querySelector('[data-visual-text-panel]');
    const imagePanel = host.querySelector('[data-visual-image-panel]');
    if (searchMode === 'combined') {
      if (textPanel) textPanel.hidden = false;
      if (imagePanel) imagePanel.hidden = false;
    } else if (searchMode === 'text') {
      if (textPanel) textPanel.hidden = false;
      if (imagePanel) imagePanel.hidden = true;
    } else {
      if (textPanel) textPanel.hidden = true;
      if (imagePanel) imagePanel.hidden = false;
    }
  }

  function visualResultCard(item) {
    const assetId = item.assetId || '';
    const hasThumb = item.hasThumbnail && item.segmentId;
    const preview = hasThumb
      ? `<img loading="lazy" src="/client-api/discovery/assets/${encodeURIComponent(assetId)}/visual-segments/${encodeURIComponent(item.segmentId)}/thumbnail" alt="${safe(t('Matched segment thumbnail','صورة المقطع المطابق'))}"/>`
      : `<span class="p135-result-placeholder"><i class="bi bi-image"></i></span>`;
    const context = item.startMs !== null && item.startMs !== undefined
      ? `${formatMs(item.startMs)} → ${formatMs(item.endMs)}`
      : item.pageNumber ? `${t('Page','صفحة')} ${safe(item.pageNumber)}` : safe(item.sourceKind || 'visual');
    const score = Math.round(Number(item.combinedScore ?? item.score ?? 0) * 100);
    return `<article class="card mam-visual-result p135-combined-result"><div class="mam-visual-result-media">${preview}</div><div class="mam-visual-result-body"><div class="mam-visual-segments-header"><strong>${safe(item.title || item.textItem?.title || '')}</strong><span class="mam-visual-score">${score}%</span></div><div class="mam-visual-context">${safe(item.mediaKind || item.textItem?.mediaKind || '')} · ${context}</div><small>${safe(t('Combined text + visual relevance','صلة مشتركة للنص والصورة'))}</small><div class="mam-visual-actions"><button type="button" class="action" data-p135-open-result="${safe(assetId)}" data-p135-seek-ms="${safe(item.startMs ?? 0)}">${safe(item.startMs !== null && item.startMs !== undefined ? t('Open at match','فتح عند موضع التطابق') : t('Open asset','فتح الأصل'))}</button></div></div></article>`;
  }

  function formatMs(ms) {
    const total = Math.max(0, Math.floor(Number(ms || 0) / 1000));
    const h = Math.floor(total / 3600), m = Math.floor((total % 3600) / 60), s = total % 60;
    return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
  }

  async function runCombinedSearch() {
    const query = document.getElementById('p12SearchQuery')?.value.trim() || '';
    const file = document.getElementById('mamVisualFile')?.files?.[0] || null;
    const stateHost = document.getElementById('mamVisualState');
    const resultsHost = document.getElementById('mamVisualResults');
    if (!stateHost || !resultsHost) return;
    if (query.length < 2) {
      showPopup('warning', t('Search text required','نص البحث مطلوب'), t('Enter at least two searchable characters for Text + Image mode.','أدخل حرفين على الأقل لاستخدام وضع نص + صورة.'));
      document.getElementById('p12SearchQuery')?.focus();
      return;
    }
    if (!file) {
      showPopup('warning', t('Search image required','صورة البحث مطلوبة'), t('Choose or drop an image for Text + Image mode.','اختر أو اسحب صورة لاستخدام وضع نص + صورة.'));
      return;
    }
    stateHost.innerHTML = `<div class="state loading"><strong>${safe(t('Searching','جاري البحث'))}</strong><br>${safe(t('Combining the authoritative text index with visual similarity…','جاري دمج الفهرس النصي الموثوق مع التشابه البصري…'))}</div>`;
    resultsHost.innerHTML = '';
    try {
      const params = new URLSearchParams({ query, page:'1', pageSize:'100' });
      const kind = document.getElementById('p12SearchKind')?.value || '';
      const category = document.getElementById('p12SearchCategory')?.value || '';
      if (kind) params.set('mediaKind', kind);
      if (category) params.set('categoryId', category);
      const [textResult, visualResult] = await Promise.all([
        json(`/client-api/discovery/search?${params}`),
        json('/client-api/discovery/image-search?limit=100', { method:'POST', headers:{ 'Content-Type': file.type }, body:file })
      ]);
      if (currentRoute() !== 'search' || searchMode !== 'combined') return;
      const textItems = Array.isArray(textResult?.items) ? textResult.items : [];
      const visualItems = Array.isArray(visualResult?.items) ? visualResult.items : [];
      const textRank = new Map();
      textItems.forEach((item,index) => {
        const key = String(item.assetId || '').toLowerCase();
        if (!key || textRank.has(key)) return;
        const normalized = textItems.length <= 1 ? 1 : 1 - (index / (textItems.length - 1));
        textRank.set(key, { score:normalized, item });
      });
      const merged = [];
      const seen = new Set();
      for (const visual of visualItems) {
        const key = String(visual.assetId || '').toLowerCase();
        const textHit = textRank.get(key);
        if (!textHit || seen.has(key)) continue;
        seen.add(key);
        const visualScore = Math.max(0, Math.min(1, Number(visual.score || 0)));
        merged.push({ ...visual, textItem:textHit.item, title:visual.title || textHit.item.title, mediaKind:visual.mediaKind || textHit.item.mediaKind, combinedScore:(visualScore * 0.65) + (textHit.score * 0.35) });
      }
      merged.sort((a,b) => b.combinedScore - a.combinedScore);
      stateHost.innerHTML = '';
      if (!merged.length) {
        resultsHost.innerHTML = `<div class="state empty"><strong>${safe(t('No combined matches','لا توجد نتائج مشتركة'))}</strong><br>${safe(t('No asset matched both the text query and the image query.','لا يوجد أصل طابق النص والصورة معًا.'))}</div>`;
        showPopup('info', t('No combined matches','لا توجد نتائج مشتركة'), t('No asset matched both the text query and the selected image.','لا يوجد أصل طابق نص البحث والصورة المختارة معًا.'));
        return;
      }
      resultsHost.innerHTML = `<div class="mam-visual-results">${merged.slice(0,30).map(visualResultCard).join('')}</div>`;
      resultsHost.querySelectorAll('[data-p135-open-result]').forEach(button => button.addEventListener('click', () => openAsset(button.dataset.p135OpenResult || '', Number(button.dataset.p135SeekMs || 0))));
      showPopup('success', t('Search completed','اكتمل البحث'), `${merged.length} ${t('combined matches found.','نتيجة مشتركة تم العثور عليها.')}`);
    } catch (error) {
      stateHost.innerHTML = '';
      showPopup('error', t('Combined search failed','فشل البحث المشترك'), error?.payload?.detail || error?.message || t('The search could not be completed.','تعذر إكمال البحث.'));
    }
  }

  function openAsset(assetId, seekMs = 0) {
    if (!assetId) return;
    if (seekMs > 0) sessionStorage.setItem('mam.p135.pendingSeekMs', String(seekMs));
    try { p12SelectedAssetId = assetId; } catch {}
    window.p135SelectedAssetId = assetId;
    setRoute('asset');
  }

  function extractSeekMs(button) {
    const attrs = ['p135SeekMs','visualSeek','startMs','seekMs','playFromMs'];
    for (const name of attrs) {
      const value = button?.dataset?.[name];
      if (value !== undefined && value !== '' && Number.isFinite(Number(value))) return Number(value);
    }
    let node = button?.parentElement;
    for (let depth=0; node && depth<5; depth++,node=node.parentElement) {
      for (const name of attrs) {
        const value = node.dataset?.[name];
        if (value !== undefined && value !== '' && Number.isFinite(Number(value))) return Number(value);
      }
    }
    return 0;
  }

  async function seekPreview(ms, announce = true) {
    const host = document.getElementById('p04AssetState');
    if (!host) return false;
    const videos = [...host.querySelectorAll('video')];
    const video = videos.find(item => item.offsetParent !== null) || videos[0];
    if (!video) {
      if (announce) showPopup('warning', t('Video preview unavailable','معاينة الفيديو غير متاحة'), t('No playable video preview is available for this asset.','لا توجد معاينة فيديو قابلة للتشغيل لهذا الأصل.'));
      return false;
    }
    const seconds = Math.max(0, Number(ms || 0) / 1000);
    const apply = () => {
      try { video.currentTime = Math.min(seconds, Number.isFinite(video.duration) && video.duration > 0 ? Math.max(0, video.duration - 0.05) : seconds); } catch {}
      video.scrollIntoView({ behavior:'smooth', block:'center' });
      try { video.focus({ preventScroll:true }); } catch {}
      video.play().catch(() => {});
    };
    if (video.readyState >= 1) apply();
    else video.addEventListener('loadedmetadata', apply, { once:true });
    if (announce) showPopup('success', t('Preview positioned','تم تحديد موضع المعاينة'), t('The preview was moved to the selected time and brought into view.','تم نقل المعاينة إلى الوقت المحدد وإظهار الفيديو أمامك.'));
    return true;
  }

  function localizePlayFromHere(root = document) {
    const candidates = root.querySelectorAll?.('button,a') || [];
    candidates.forEach(button => {
      const raw = (button.textContent || '').replace(/\s+/g,' ').trim().toLowerCase();
      if (!raw) return;
      const marked = button.hasAttribute('data-play-from-here') || button.hasAttribute('data-visual-play') || raw === 'play from here' || raw === 'تشغيل من هنا';
      if (!marked) return;
      button.dataset.p135PlayFromHere = '1';
      const icon = button.querySelector('i')?.outerHTML || '';
      button.innerHTML = `${icon}${icon ? ' ' : ''}${safe(t('Play from here','تشغيل من هنا'))}`;
    });
  }

  function enhanceAssetDetails() {
    if (currentRoute() !== 'asset') return;
    const host = document.getElementById('p04AssetState');
    if (!host || !host.querySelector('#p12AssetDiscovery')) return;
    localizePlayFromHere(host);
    if (host.dataset.p135AssetTabs === '1') {
      applyPendingSeek();
      return;
    }

    const direct = [...host.children];
    const topCard = direct.find(node => node.classList?.contains('card') && !node.classList.contains('p135-asset-tabs'));
    const grid = direct.find(node => node.classList?.contains('grid') && node.classList.contains('two'));
    const technicalCard = grid?.children?.[0] || null;
    const previewCard = grid?.children?.[1] || null;
    const discovery = host.querySelector('#p12AssetDiscovery');
    const documentPreview = direct.find(node => node.classList?.contains('card') && node !== topCard && node !== grid && node.querySelector('iframe'));
    if (!topCard || !discovery) return;

    host.dataset.p135AssetTabs = '1';
    const tabShell = document.createElement('section');
    tabShell.className = 'p135-asset-tabs';
    tabShell.innerHTML = `<div class="p135-asset-tabbar" role="tablist"></div><div class="p135-asset-panels"></div>`;
    host.insertBefore(tabShell, topCard);
    const tabbar = tabShell.querySelector('.p135-asset-tabbar');
    const panels = tabShell.querySelector('.p135-asset-panels');

    const definitions = [
      ['overview', t('Overview','نظرة عامة'), [topCard]],
      ['technical', t('Technical Metadata','البيانات الفنية'), technicalCard ? [technicalCard] : []],
      ['preview', t('Preview & Derivatives','المعاينة والمشتقات'), [previewCard, documentPreview].filter(Boolean)],
      ['discovery', t('Search & Indexing','البحث والفهرسة'), [discovery]]
    ].filter(item => item[2].length);

    if (grid) grid.remove();
    definitions.forEach(([key,label,nodes],index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.p135AssetTab = key;
      button.setAttribute('role','tab');
      button.setAttribute('aria-selected', String(index === 0));
      button.textContent = label;
      tabbar.appendChild(button);
      const panel = document.createElement('div');
      panel.className = 'p135-asset-panel';
      panel.dataset.p135AssetPanel = key;
      panel.setAttribute('role','tabpanel');
      panel.hidden = index !== 0;
      nodes.forEach(node => panel.appendChild(node));
      panels.appendChild(panel);
    });

    const activate = key => {
      tabbar.querySelectorAll('[data-p135-asset-tab]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.p135AssetTab === key)));
      panels.querySelectorAll('[data-p135-asset-panel]').forEach(panel => { panel.hidden = panel.dataset.p135AssetPanel !== key; });
      if (key === 'preview') setTimeout(() => applyPendingSeek(), 0);
    };
    tabbar.querySelectorAll('[data-p135-asset-tab]').forEach(button => button.addEventListener('click', () => activate(button.dataset.p135AssetTab)));

    const discoveryObserver = new MutationObserver(() => {
      const organization = discovery.querySelector('[data-p133-organization]');
      if (!organization || tabbar.querySelector('[data-p135-asset-tab="organization"]')) return;
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.p135AssetTab = 'organization';
      button.setAttribute('role','tab');
      button.setAttribute('aria-selected','false');
      button.textContent = t('Organization','التنظيم');
      tabbar.appendChild(button);
      const panel = document.createElement('div');
      panel.className = 'p135-asset-panel';
      panel.dataset.p135AssetPanel = 'organization';
      panel.setAttribute('role','tabpanel');
      panel.hidden = true;
      panel.appendChild(organization);
      panels.appendChild(panel);
      button.addEventListener('click', () => activate('organization'));
    });
    discoveryObserver.observe(discovery, { childList:true, subtree:true });
    applyPendingSeek();
  }

  async function applyPendingSeek() {
    const value = sessionStorage.getItem('mam.p135.pendingSeekMs');
    if (!value) return;
    const ms = Number(value);
    if (!Number.isFinite(ms)) { sessionStorage.removeItem('mam.p135.pendingSeekMs'); return; }
    for (let attempt=0; attempt<30; attempt++) {
      if (currentRoute() !== 'asset') return;
      if (document.querySelector('#p04AssetState video')) {
        sessionStorage.removeItem('mam.p135.pendingSeekMs');
        const previewTab = document.querySelector('[data-p135-asset-tab="preview"]');
        previewTab?.click();
        await seekPreview(ms, false);
        return;
      }
      await sleep(100);
    }
  }

  function handleGlobalClick(event) {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;
    const play = target.closest('[data-p135-play-from-here],button,a');
    if (play?.dataset?.p135PlayFromHere === '1') {
      event.preventDefault();
      event.stopImmediatePropagation();
      const ms = extractSeekMs(play);
      const previewTab = document.querySelector('[data-p135-asset-tab="preview"]');
      previewTab?.click();
      void seekPreview(ms, true);
      return;
    }
    if (currentRoute() === 'search' && searchMode === 'combined') {
      const action = target.closest('#p12SearchButton,#mamVisualRun');
      if (action) {
        event.preventDefault();
        event.stopImmediatePropagation();
        void runCombinedSearch();
      }
    }
    if (currentRoute() === 'library') {
      const nativeTab = target.closest('[data-p133-tab]');
      if (nativeTab) {
        libraryMode = nativeTab.dataset.p133Tab || 'upload';
        window.mamP135LibraryMode = libraryMode;
      }
    }
  }

  document.addEventListener('click', handleGlobalClick, true);
  document.addEventListener('keydown', event => {
    if (currentRoute() === 'search' && searchMode === 'combined' && event.key === 'Enter' && event.target?.id === 'p12SearchQuery') {
      event.preventDefault();
      event.stopImmediatePropagation();
      void runCombinedSearch();
    }
  }, true);

  function reconcile() {
    const activeRoute = currentRoute();
    if (activeRoute !== previousRoute) {
      if (activeRoute === 'library') {
        libraryMode = 'all';
        window.mamP135LibraryMode = 'all';
        libraryState.page = 1;
      }
      if (activeRoute === 'search') searchMode = 'combined';
      previousRoute = activeRoute;
    }
    if (activeRoute === 'dashboard') void enhanceDashboard();
    if (activeRoute === 'library') enhanceLibrary();
    if (activeRoute === 'search') enhanceSearch();
    if (activeRoute === 'asset') enhanceAssetDetails();
    localizePlayFromHere(contentHost);
    promoteInlineMessages(contentHost);
  }

  const observer = new MutationObserver(() => reconcile());
  observer.observe(contentHost, { childList:true, subtree:true });

  try {
    const priorRender = render;
    render = function() {
      priorRender();
      setTimeout(reconcile, 0);
    };
  } catch {}

  window.mamP135 = Object.freeze({
    version: 'p135-complete-ux-1',
    popup: showPopup,
    reconcile,
    get libraryMode() { return libraryMode; },
    get searchMode() { return searchMode; },
    diagnose: () => ({ route:currentRoute(), libraryMode, searchMode, descriptorLoaded:!!descriptorCache })
  });

  reconcile();
})();
