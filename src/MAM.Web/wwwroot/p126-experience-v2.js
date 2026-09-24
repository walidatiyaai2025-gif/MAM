(() => {
  'use strict';

  const html = value => typeof esc === 'function'
    ? esc(value)
    : String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));

  let mediaKindFilter = '';
  let libraryTab = 'search';
  let authState = { loaded:false, permissions:new Set(), roles:new Set(), capabilities:[] };
  const kindCache = new Map();

  pages.upload = ['Add Media', 'إضافة ميديا'];
  pages.reports = ['Reports', 'التقارير'];

  const previousShellPage = shellPage;
  shellPage = function () {
    if (route === 'reports') {
      return `${lead(
        arabic ? 'التقارير' : 'Reports',
        arabic ? 'مؤشرات مباشرة عن أنواع الوسائط ونشاط الرفع حسب المستخدم.' : 'Live indicators for media types and upload activity by user.',
        'LIVE REPORTING')}
        <div id="p126ReportsHost" class="p126-report-host">${state('loading', arabic ? 'جارٍ التحميل' : 'Loading', arabic ? 'جاري تحميل التقارير…' : 'Loading reports…')}</div>`;
    }
    return previousShellPage();
  };

  function has(permission) { return authState.permissions.has(permission); }
  function canUploadAny() { return authState.capabilities.some(x => x.canUpload); }

  function routeAllowed(key) {
    if (key === 'asset') return false;
    if (!authState.loaded) return true;
    if (['admin', 'settings', 'mediaPermissions'].includes(key)) return has('administration.manage');
    if (key === 'reports') return has('audit.read');
    if (['upload', 'ingest'].includes(key)) return has('catalog.write') && canUploadAny();
    if (['queue', 'categories', 'references'].includes(key)) return has('catalog.write');
    if (['dashboard', 'library', 'search'].includes(key)) return has('catalog.read');
    return true;
  }

  async function loadIdentity() {
    try {
      const sessionResponse = await fetch('/client-api/session', { headers:{ Accept:'application/json' }, cache:'no-store' });
      if (!sessionResponse.ok) return;
      const session = await sessionResponse.json();
      authState.permissions = new Set(session.permissions || []);
      authState.roles = new Set(session.roles || []);

      try {
        const capResponse = await fetch('/client-api/discovery/my-media-capabilities', { headers:{ Accept:'application/json' }, cache:'no-store' });
        if (capResponse.ok) authState.capabilities = await capResponse.json();
      } catch { }

      authState.loaded = true;
      applyPermissionVisibility();
      if (route === 'dashboard') {
        decorateDashboard();
        void renderDashboardCharts();
      }
    } catch { }
  }

  function applyPermissionVisibility() {
    const host = document.getElementById('nav');
    if (!host) return;

    const assetButton = host.querySelector('[data-route="asset"]');
    if (assetButton) {
      assetButton.hidden = true;
      assetButton.classList.add('p126-permission-hidden');
      assetButton.setAttribute('aria-hidden', 'true');
      assetButton.tabIndex = -1;
    }

    const upload = host.querySelector('[data-route="upload"]');
    const desiredUploadLabel = arabic ? 'إضافة ميديا' : 'Add Media';
    if (upload && upload.textContent !== desiredUploadLabel) upload.textContent = desiredUploadLabel;

    host.querySelectorAll('[data-route]').forEach(button => {
      if (button.dataset.route === 'asset') return;
      const allowed = routeAllowed(button.dataset.route || '');
      if (button.hidden === allowed) button.hidden = !allowed;
      button.classList.toggle('p126-permission-hidden', !allowed);
      button.setAttribute('aria-hidden', allowed ? 'false' : 'true');
      if (!allowed) button.tabIndex = -1;
      else button.removeAttribute('tabindex');
    });

    document.querySelectorAll('[data-p05-edit],[data-p12-cat-edit],[data-p12-cat-delete],[data-p12-ref-edit],[data-p12-reference-add],[data-p12-asset-reference-add]').forEach(el => {
      if (authState.loaded) el.hidden = !has('catalog.write');
    });
    document.querySelectorAll('[data-mam-delete-asset]').forEach(el => {
      if (authState.loaded) el.hidden = !has('catalog.delete');
    });

    if (authState.loaded && !routeAllowed(route) && route !== 'asset') {
      route = 'dashboard';
      render();
    }
  }

  function sidebarUtilities() {
    return document.getElementById('mamSidebarUtilities');
  }

  function ensureSidebarUtilityRow() {
    const host=sidebarUtilities();
    if(!host)return null;
    let row=host.querySelector('.mam-sidebar-utility-row');
    if(!row){
      row=document.createElement('div');
      row.className='mam-sidebar-utility-row';
      host.appendChild(row);
    }
    return row;
  }

  function ensureGlobalSearch() {
    if (document.getElementById('p126SidebarSearch')) return;
    const host = sidebarUtilities();
    if (!host) return;

    const form = document.createElement('form');
    form.id = 'p126SidebarSearch';
    form.className = 'p126-sidebar-search';
    form.setAttribute('role', 'search');
    form.innerHTML = `<span class="p126-sidebar-search-icon" aria-hidden="true"><i class="bi bi-search"></i></span><input id="p126GlobalSearchInput" type="search" maxlength="300" autocomplete="off" placeholder="${arabic ? 'ابحث في المحتوى…' : 'Search content…'}" aria-label="${arabic ? 'البحث في المحتوى' : 'Search content'}"/><button type="submit" class="p126-sidebar-search-submit" aria-label="${arabic ? 'تنفيذ البحث' : 'Run search'}" title="${arabic ? 'بحث' : 'Search'}"><i class="bi bi-arrow-left"></i></button>`;
    host.prepend(form);

    form.addEventListener('submit', event => {
      event.preventDefault();
      const query = form.querySelector('input')?.value.trim() || '';
      if (query.length < 2) {
        window.MamPopup?.notify?.(arabic ? 'أدخل حرفين على الأقل للبحث.' : 'Enter at least two characters to search.', 'warning');
        return;
      }
      window.p126PendingSearch = query;

      const activated = window.mamNavigationRuntime?.activateRoute?.('search') === true;
      if (!activated) {
        route = 'search';
        render();
      }

      queueMicrotask(() => {
        try {
          const url = new URL(location.href);
          const hash = new URLSearchParams(url.hash.replace(/^#/, ''));
          hash.set('route', 'search');
          hash.set('q', query);
          hash.set('searched', '1');
          url.hash = hash.toString();
          history.replaceState(history.state, '', url.href);
        } catch { }
      });
    });
  }

  function ensureActiveUsersBadge() {
    const row = ensureSidebarUtilityRow();
    if (!row) return null;
    let badge = document.getElementById('p126ActiveUsers');
    if (badge) return badge;

    badge = document.createElement('span');
    badge.id = 'p126ActiveUsers';
    badge.className = 'p126-active-users-badge is-loading';
    badge.setAttribute('role', 'status');
    badge.setAttribute('aria-live', 'polite');
    badge.innerHTML = `<span class="p126-active-users-dot" aria-hidden="true"></span><i class="bi bi-people-fill" aria-hidden="true"></i><strong data-p126-active-count>—</strong><span class="p126-active-users-label">${arabic ? 'نشط الآن' : 'active now'}</span>`;
    row.prepend(badge);
    return badge;
  }

  let presenceRefreshInFlight = false;
  let presenceTimer = 0;

  async function refreshActiveUsers() {
    if (presenceRefreshInFlight) return;
    const badge = ensureActiveUsersBadge();
    if (!badge) return;

    presenceRefreshInFlight = true;
    try {
      const response = await fetch('/presence/heartbeat', {
        method:'POST',
        headers:{ Accept:'application/json' },
        cache:'no-store'
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      const count = Math.max(0, Number(payload?.activeUsers || 0));
      const windowSeconds = Math.max(1, Number(payload?.windowSeconds || 90));
      const countNode = badge.querySelector('[data-p126-active-count]');
      const labelNode = badge.querySelector('.p126-active-users-label');
      if (countNode) countNode.textContent = String(count);
      if (labelNode) labelNode.textContent = arabic ? 'نشط الآن' : 'active now';
      badge.classList.remove('is-loading','is-offline');
      badge.title = arabic
        ? `عدد المستخدمين الفريدين الذين لديهم النظام مفتوحًا خلال آخر ${windowSeconds} ثانية`
        : `Unique users with the system open within the last ${windowSeconds} seconds`;
      badge.setAttribute('aria-label', arabic ? `${count} مستخدم نشط الآن` : `${count} active users now`);
    } catch {
      badge.classList.remove('is-loading');
      badge.classList.add('is-offline');
      const countNode = badge.querySelector('[data-p126-active-count]');
      if (countNode) countNode.textContent = '—';
      badge.title = arabic ? 'تعذر قراءة عدد المستخدمين النشطين' : 'Active-user count is unavailable';
    } finally {
      presenceRefreshInFlight = false;
    }
  }

  function startPresenceHeartbeat() {
    ensureActiveUsersBadge();
    void refreshActiveUsers();
    if (!presenceTimer) presenceTimer = window.setInterval(() => void refreshActiveUsers(), 30000);
    window.addEventListener('pageshow', () => void refreshActiveUsers());
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) void refreshActiveUsers();
    });
  }

  async function runPendingGlobalSearch() {
    const query = window.p126PendingSearch;
    if (!query) return;
    for (let attempt = 0; attempt < 40; attempt++) {
      const unified = document.getElementById('mamUnifiedQuery');
      if (unified) {
        unified.value = query;
        // Sidebar search is intentionally text-only. The unified Search page
        // defaults to Text + Image, which requires an uploaded image.
        document.querySelector('[data-mam-search-mode="text"]')?.click();
        window.p126PendingSearch = '';
        document.getElementById('mamUnifiedRun')?.click();
        return;
      }

      const legacy = document.getElementById('p12SearchQuery');
      if (legacy) {
        legacy.value = query;
        window.p126PendingSearch = '';
        if (typeof p12RunSearch === 'function') await p12RunSearch();
        return;
      }

      await new Promise(resolve => setTimeout(resolve, 50));
    }

    // Do not silently discard a query if a renderer is temporarily late.
    window.p126PendingSearch = query;
  }

  function quickButton(routeKey, icon, en, ar, tone) {
    if (!routeAllowed(routeKey)) return '';
    return `<button type="button" class="p126-quick-action ${tone}" data-p126-go="${routeKey}"><i class="bi ${icon}"></i><span>${arabic ? ar : en}</span></button>`;
  }

  function decorateDashboard() {
    if (route !== 'dashboard') return;
    const leadNode = content.querySelector('.lead');
    if (leadNode) {
      leadNode.classList.add('p126-dashboard-lead');
      let quick = document.getElementById('p126DashboardQuickActions');
      const markup = `${quickButton('upload', 'bi-plus-circle', 'Add Media', 'إضافة ميديا', 'media')}${quickButton('settings', 'bi-gear', 'Settings', 'الإعدادات', 'settings')}${quickButton('reports', 'bi-bar-chart-line', 'Reports', 'التقارير', 'reports')}`;
      if (!quick) {
        quick = document.createElement('div');
        quick.id = 'p126DashboardQuickActions';
        quick.className = 'p126-quick-actions';
        leadNode.appendChild(quick);
      }
      if (quick.dataset.markup !== markup) {
        quick.dataset.markup = markup;
        quick.innerHTML = markup;
        quick.querySelectorAll('[data-p126-go]').forEach(button => button.addEventListener('click', () => {
          route = button.dataset.p126Go;
          render();
        }));
      }
    }

    const icons = ['bi-collection-play', 'bi-shield-check', 'bi-cpu', 'bi-diagram-3', 'bi-search', 'bi-person-badge'];
    content.querySelectorAll('.card.metric').forEach((card, index) => {
      if (card.querySelector('.p126-metric-icon')) return;
      card.classList.add('p126-metric', `p126-metric-${(index % 6) + 1}`);
      card.insertAdjacentHTML('afterbegin', `<span class="p126-metric-icon"><i class="bi ${icons[index % icons.length]}"></i></span>`);
    });
  }

  async function resolveKind(assetId) {
    if (kindCache.has(assetId)) return kindCache.get(assetId);
    try {
      const response = await fetch(`/client-api/discovery/lookups/assets?query=${encodeURIComponent(assetId)}&limit=2`, { headers:{ Accept:'application/json' } });
      if (!response.ok) return 'Other';
      const rows = await response.json();
      const row = (rows || []).find(x => String(x.assetId).toLowerCase() === String(assetId).toLowerCase()) || rows?.[0];
      const kind = row?.mediaKind || 'Other';
      kindCache.set(assetId, kind);
      return kind;
    } catch { return 'Other'; }
  }

  async function mapLimit(items, limit, worker) {
    const result = new Array(items.length);
    let cursor = 0;
    const runners = Array.from({ length:Math.min(limit, items.length) }, async () => {
      while (true) {
        const index = cursor++;
        if (index >= items.length) return;
        result[index] = await worker(items[index], index);
      }
    });
    await Promise.all(runners);
    return result;
  }

  async function dashboardData() {
    const mediaCounts = new Map([['Video',0],['Audio',0],['Image',0],['Document',0],['Other',0]]);
    const activeAssetIds = new Set();
    try {
      const assetsResponse = await fetch('/client-api/catalog/assets',{headers:{Accept:'application/json'}});
      if (assetsResponse.ok) {
        const allAssets = await assetsResponse.json();
        const assets = (Array.isArray(allAssets)?allAssets:[]).filter(asset => String(asset.lifecycle||'').toLowerCase() !== 'deleted');
        assets.forEach(asset => activeAssetIds.add(String(asset.id||'').toLowerCase()));
        const kinds = await mapLimit(assets,8,asset=>resolveKind(asset.id));
        kinds.forEach(kind => mediaCounts.set(kind,(mediaCounts.get(kind)||0)+1));
      }
    } catch { }

    const uploads = new Map();
    if (has('audit.read')) {
      try {
        const [auditResponse,usersResponse] = await Promise.all([
          fetch('/client-api/admin/audit?limit=5000',{headers:{Accept:'application/json'}}),
          fetch('/client-api/admin/users',{headers:{Accept:'application/json'}})
        ]);
        const userNames = new Map();
        if (usersResponse.ok) {
          const users=await usersResponse.json();
          (users||[]).forEach(u=>{
            const id=String(u.userId||'').toLowerCase();
            const userName=String(u.userName||'').toLowerCase();
            const external=String(u.externalSubject||'').toLowerCase();
            const display=u.displayName||u.userName||(arabic ? 'مستخدم غير معروف' : 'Unknown user');
            if(id)userNames.set(id,display);
            if(userName)userNames.set(userName,display);
            if(external)userNames.set(external,display);
          });
        }
        if (auditResponse.ok) {
          const audit=await auditResponse.json();
          const events=Array.isArray(audit)?audit:(audit.items||[]);
          events.filter(x => ['upload.primary.committed','upload.session.finalized'].includes(String(x.action)) && String(x.outcome).toLowerCase()==='success').forEach(x=>{
            let assetId='';
            if(String(x.action)==='upload.session.finalized' && String(x.entityType||'').toLowerCase()==='mediaasset'){
              assetId=String(x.entityId||'').toLowerCase();
            } else {
              const match=String(x.detail||'').match(/(?:^|;)asset=([0-9a-f-]{36})(?:;|$)/i);
              assetId=String(match?.[1]||'').toLowerCase();
            }
            if(!assetId||!activeAssetIds.has(assetId))return;
            const raw=String(x.actorId||'').trim();
            const label=userNames.get(raw.toLowerCase()) || (/^[0-9a-f-]{36}$/i.test(raw)?(arabic ? 'مستخدم غير معروف' : 'Unknown user'):(raw||(arabic ? 'مستخدم غير معروف' : 'Unknown user')));
            uploads.set(label,(uploads.get(label)||0)+1);
          });
        }
      } catch { }
    }
    return { media:[...mediaCounts].map(([name,count])=>({name,count})), users:[...uploads].map(([name,count])=>({name,count})).sort((a,b)=>b.count-a.count) };
  }

  function chartMarkup(data) {
    const mediaMax = Math.max(1, ...data.media.map(x => x.count));
    const userMax = Math.max(1, ...data.users.map(x => x.count));
    const mediaLabels = { Video:arabic ? 'فيديو' : 'Video', Audio:arabic ? 'صوت' : 'Audio', Image:arabic ? 'صور' : 'Image', Document:arabic ? 'وثائق' : 'Document', Other:arabic ? 'أخرى' : 'Other' };
    const mediaBars = data.media.map(x => `<div class="p126-bar-item"><span class="p126-bar-value">${html(x.count)}</span><div class="p126-bar" style="height:${Math.max(3, (x.count / mediaMax) * 155)}px"></div><span class="p126-bar-label">${html(mediaLabels[x.name] || x.name)}</span></div>`).join('');
    const userRows = data.users.length
      ? data.users.slice(0, 12).map(x => `<div class="p126-user-row"><span class="p126-user-name" title="${html(x.name)}">${html(x.name)}</span><div class="p126-user-track"><div class="p126-user-fill" style="width:${Math.max(3, (x.count / userMax) * 100)}%"></div></div><span class="p126-user-count">${html(x.count)}</span></div>`).join('')
      : `<p>${arabic ? 'لا توجد بيانات رفع متاحة لهذا المستخدم أو لا توجد عمليات رفع مكتملة بعد.' : 'No upload activity is available to this user, or no uploads have completed yet.'}</p>`;
    return `<div class="p126-chart-grid"><div class="card p126-chart-card"><h3><i class="bi bi-bar-chart-fill"></i>${arabic ? 'عدد الملفات حسب نوع الميديا' : 'Files by media type'}</h3><div class="p126-bars">${mediaBars}</div></div><div class="card p126-chart-card"><h3><i class="bi bi-people-fill"></i>${arabic ? 'عدد الملفات المرفوعة بواسطة كل مستخدم' : 'Uploaded files by user'}</h3><div class="p126-user-bars">${userRows}</div></div></div>`;
  }

  async function renderDashboardCharts() {
    if (route !== 'dashboard' || document.getElementById('p126DashboardCharts')) return;
    const host = document.createElement('div');
    host.id = 'p126DashboardCharts';
    host.innerHTML = `<div class="card">${state('loading', arabic ? 'جارٍ التحميل' : 'Loading', arabic ? 'جاري تجهيز الرسوم البيانية…' : 'Preparing charts…')}</div>`;
    content.appendChild(host);
    const data = await dashboardData();
    if (route !== 'dashboard') return;
    host.innerHTML = chartMarkup(data);
    decorateDashboard();
  }

  async function renderReports() {
    const host = document.getElementById('p126ReportsHost');
    if (!host) return;
    if (authState.loaded && !has('audit.read')) {
      route = 'dashboard';
      render();
      return;
    }
    const data = await dashboardData();
    if (route !== 'reports') return;
    host.innerHTML = chartMarkup(data);
  }

  function mediaOptions() {
    const labels = { Video:arabic ? 'فيديو' : 'Video', Audio:arabic ? 'صوت' : 'Audio', Image:arabic ? 'صور' : 'Image', Document:arabic ? 'وثائق' : 'Document', Other:arabic ? 'أخرى' : 'Other' };
    return `<option value="">${arabic ? 'كل أنواع الميديا' : 'All media types'}</option>${Object.entries(labels).map(([value,label]) => `<option value="${value}" ${mediaKindFilter === value ? 'selected' : ''}>${label}</option>`).join('')}`;
  }

  p05SearchCard = function (result, collections) {
    const lifecycleOptions = ['', ...(result.facets?.lifecycles || []).map(x => x.value)];
    const categoryOptions = ['', ...(result.facets?.categories || []).map(x => x.value)];
    return `<div class="card"><h3>${arabic ? 'البحث' : 'Search'}</h3><div class="p126-search-row">
      <input id="p05Query" value="${html(p05Query)}" maxlength="300" placeholder="${arabic ? 'بحث عربي أو إنجليزي' : 'Arabic or English search'}"/>
      <select id="p126MediaKind">${mediaOptions()}</select>
      <select id="p05Lifecycle">${lifecycleOptions.map(x => `<option value="${html(x)}" ${x === p05Lifecycle ? 'selected' : ''}>${html(x || (arabic ? 'كل الحالات' : 'All states'))}</option>`).join('')}</select>
      <select id="p05Category">${categoryOptions.map(x => `<option value="${html(x)}" ${x === p05Category ? 'selected' : ''}>${html(x || (arabic ? 'كل التصنيفات' : 'All categories'))}</option>`).join('')}</select>
      <select id="p05Collection"><option value="">${arabic ? 'كل المجموعات' : 'All collections'}</option>${(collections || []).map(c => `<option value="${html(c.collectionId)}" ${c.collectionId === p05CollectionId ? 'selected' : ''}>${html(arabic && c.nameAr ? c.nameAr : c.nameEn)}</option>`).join('')}</select>
      <button id="p05Search" class="action">${arabic ? 'بحث' : 'Search'}</button>
      <button id="p05Reset" class="action">${arabic ? 'مسح' : 'Clear'}</button>
    </div><div class="p126-reference-tags">${(result.facets?.tags || []).slice(0, 12).map(t => `<button class="action" data-p05-tag="${html(t.value)}">${html(t.value)} · ${html(t.count)}</button>`).join('')}</div></div>`;
  };

  function createPanel(categories) {
    const parentOptions = `<option value="">${arabic ? 'بدون تصنيف رئيسي' : 'No parent category'}</option>${(categories || []).filter(c => !c.isSystem).map(c => `<option value="${html(c.categoryId)}">${html(arabic && c.nameAr ? c.nameAr : c.nameEn)}</option>`).join('')}`;
    return `<div class="card"><h3>${arabic ? 'إنشاء تصنيف' : 'Create category'}</h3><div class="toolbar"><input id="p126CategoryEn" maxlength="200" placeholder="Category name (English)"/><input id="p126CategoryAr" maxlength="200" dir="rtl" placeholder="اسم التصنيف بالعربية"/><select id="p126CategoryParent">${parentOptions}</select><button id="p126CreateCategory" class="action">${arabic ? 'إنشاء تصنيف' : 'Create category'}</button></div><div id="p126CategoryState"></div></div>
      <div class="card"><h3>${arabic ? 'إنشاء مرجع' : 'Create reference'}</h3><div class="toolbar"><input id="p126ReferenceEn" maxlength="200" placeholder="Reference name (English)"/><input id="p126ReferenceAr" maxlength="200" dir="rtl" placeholder="اسم المرجع بالعربية"/><button id="p126CreateReference" class="action">${arabic ? 'إنشاء مرجع' : 'Create reference'}</button></div><div id="p126ReferenceState"></div></div>`;
  }

  p05LoadLibrary = async function () {
    const languageAtRequest = arabic;
    try {
      const params = new URLSearchParams({ page:'1', pageSize:'100' });
      if (p05Query) params.set('query', p05Query);
      if (p05Lifecycle) params.set('lifecycle', p05Lifecycle);
      if (p05Category) params.set('category', p05Category);
      if (p05Tag) params.set('tag', p05Tag);
      if (p05CollectionId) params.set('collectionId', p05CollectionId);

      const [searchResponse, collectionsResponse, policyResponse, categoriesResponse] = await Promise.all([
        fetch(`/client-api/curation/search?${params}`, { headers:{ Accept:'application/json' } }),
        fetch('/client-api/curation/collections', { headers:{ Accept:'application/json' } }),
        fetch('/client-api/curation/policy', { headers:{ Accept:'application/json' } }),
        fetch('/client-api/discovery/categories', { headers:{ Accept:'application/json' } })
      ]);

      if (route !== 'library' || languageAtRequest !== arabic) return;
      if (!searchResponse.ok || !collectionsResponse.ok || !policyResponse.ok) {
        return p05LibraryFailure(!searchResponse.ok ? searchResponse.status : !collectionsResponse.ok ? collectionsResponse.status : policyResponse.status);
      }

      const result = await searchResponse.json();
      const collections = await collectionsResponse.json();
      const categories = categoriesResponse.ok ? await categoriesResponse.json() : [];
      let items = Array.isArray(result.items) ? result.items : [];
      if (mediaKindFilter) {
        const kinds = await mapLimit(items, 8, a => resolveKind(a.id));
        items = items.filter((_, i) => kinds[i] === mediaKindFilter);
      }
      const shown = { ...result, items, totalCount:mediaKindFilter ? items.length : result.totalCount };
      const canCreate = !authState.loaded || has('catalog.write');

      content.innerHTML = `${lead(arabic ? 'مكتبة الوسائط' : 'Media Library', arabic ? `${shown.totalCount} أصل مطابق · نتائج مركزية` : `${shown.totalCount} matching assets · authoritative results`, 'SEARCH & CURATION')}
        <div class="p126-library-tabs">
          <button class="p126-library-tab ${libraryTab === 'search' ? 'active' : ''}" data-p126-library-tab="search">${arabic ? 'البحث' : 'Search'}</button>
          ${canCreate ? `<button class="p126-library-tab ${libraryTab === 'create' ? 'active' : ''}" data-p126-library-tab="create">${arabic ? 'إنشاء تصنيف' : 'Create'}</button>` : ''}
        </div>
        <div class="p126-search-panel" ${libraryTab === 'search' ? '' : 'hidden'}>${p05SearchCard(shown, collections)}<div id="p05Results">${p05Results(shown, collections)}</div></div>
        ${canCreate ? `<div class="p126-create-panel" ${libraryTab === 'create' ? '' : 'hidden'}>${createPanel(categories)}</div>` : ''}`;

      bindLibraryTabs();
      p05BindSearch(shown);
      p05BindAssetActions(collections);
      bindCreation();
      replaceArabicTerms(content);
      applyPermissionVisibility();
    } catch {
      if (route === 'library' && languageAtRequest === arabic) {
        content.innerHTML = `${lead(arabic ? 'مكتبة الوسائط' : 'Media Library', '', 'SEARCH & CURATION')}${state('error', arabic ? 'خطأ في واجهة API' : 'API error', arabic ? 'تعذر تحميل البحث.' : 'Search could not be loaded.')}`;
      }
    }
  };
  loadLiveLibrary = p05LoadLibrary;

  p05BindSearch = function () {
    const run = () => {
      p05Query = document.getElementById('p05Query')?.value.trim() || '';
      p05Lifecycle = document.getElementById('p05Lifecycle')?.value || '';
      p05Category = document.getElementById('p05Category')?.value || '';
      p05CollectionId = document.getElementById('p05Collection')?.value || '';
      mediaKindFilter = document.getElementById('p126MediaKind')?.value || '';
      p05Page = 1;
      void p05LoadLibrary();
    };
    document.getElementById('p05Search')?.addEventListener('click', run);
    document.getElementById('p05Query')?.addEventListener('keydown', e => { if (e.key === 'Enter') run(); });
    document.getElementById('p05Reset')?.addEventListener('click', () => {
      p05Query = '';
      p05Lifecycle = '';
      p05Category = '';
      p05Tag = '';
      p05CollectionId = '';
      mediaKindFilter = '';
      p05Page = 1;
      void p05LoadLibrary();
    });
    content.querySelectorAll('[data-p05-tag]').forEach(button => button.addEventListener('click', () => {
      p05Tag = button.dataset.p05Tag || '';
      void p05LoadLibrary();
    }));
  };

  function bindLibraryTabs() {
    content.querySelectorAll('[data-p126-library-tab]').forEach(button => button.addEventListener('click', () => {
      libraryTab = button.dataset.p126LibraryTab || 'search';
      content.querySelectorAll('[data-p126-library-tab]').forEach(x => x.classList.toggle('active', x === button));
      const searchPanel = content.querySelector('.p126-search-panel');
      const create = content.querySelector('.p126-create-panel');
      if (searchPanel) searchPanel.hidden = libraryTab !== 'search';
      if (create) create.hidden = libraryTab !== 'create';
    }));
  }

  function bindCreation() {
    document.getElementById('p126CreateCategory')?.addEventListener('click', async () => {
      const out = document.getElementById('p126CategoryState');
      const nameEn = document.getElementById('p126CategoryEn')?.value.trim() || '';
      const nameAr = document.getElementById('p126CategoryAr')?.value.trim() || '';
      const parent = document.getElementById('p126CategoryParent')?.value || null;
      if (!nameEn) {
        out.innerHTML = state('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'الاسم الإنجليزي مطلوب.' : 'English name is required.');
        return;
      }
      try {
        const response = await fetch('/client-api/discovery/categories', {
          method:'POST', headers:{ 'Content-Type':'application/json', Accept:'application/json' },
          body:JSON.stringify({ parentCategoryId:parent, nameEn, nameAr:nameAr || null, sortOrder:0 })
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        out.innerHTML = state('empty', arabic ? 'تم الإنشاء' : 'Created', arabic ? 'تم إنشاء التصنيف.' : 'Category created.');
        setTimeout(() => void p05LoadLibrary(), 500);
      } catch (error) {
        out.innerHTML = state('error', arabic ? 'خطأ في واجهة API' : 'API error', error.message);
      }
    });

    document.getElementById('p126CreateReference')?.addEventListener('click', async () => {
      const out = document.getElementById('p126ReferenceState');
      const nameEn = document.getElementById('p126ReferenceEn')?.value.trim() || '';
      const nameAr = document.getElementById('p126ReferenceAr')?.value.trim() || '';
      if (!nameEn) {
        out.innerHTML = state('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'الاسم الإنجليزي مطلوب.' : 'English name is required.');
        return;
      }
      try {
        const response = await fetch('/client-api/discovery/references', {
          method:'POST', headers:{ 'Content-Type':'application/json', Accept:'application/json' },
          body:JSON.stringify({ nameEn, nameAr:nameAr || null, descriptionEn:null, descriptionAr:null, tags:[] })
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        out.innerHTML = state('empty', arabic ? 'تم الإنشاء' : 'Created', arabic ? 'تم إنشاء المرجع.' : 'Reference created.');
      } catch (error) {
        out.innerHTML = state('error', arabic ? 'خطأ في واجهة API' : 'API error', error.message);
      }
    });
  }

  function setupTranscriptWorkspace() {
    if (route !== 'asset') return;
    const review = document.getElementById('mamTranscriptReview');
    const discovery = document.getElementById('p12AssetDiscovery');
    if (!review || !discovery) return;

    const oldTranscript = discovery.querySelector('[data-p12-tab="transcript"]');
    if (oldTranscript) oldTranscript.style.display = 'none';

    let tabs = document.getElementById('p126TranscriptTabs');
    if (tabs) return;

    tabs = document.createElement('div');
    tabs.id = 'p126TranscriptTabs';
    tabs.className = 'p126-transcript-tabs';
    tabs.innerHTML = `<button class="p126-transcript-tab active" data-p126-transcript="timeline">${arabic ? 'التفريغ الزمني' : 'Transcript timeline'}</button><button class="p126-transcript-tab" data-p126-transcript="manual">${arabic ? 'المراجعة اليدوية للتفريغ' : 'Manual review'}</button><button class="p126-transcript-tab" data-p126-transcript="revisions">${arabic ? 'سجل النسخ' : 'Revision history'}</button>`;
    review.parentNode.insertBefore(tabs, review);
    tabs.querySelectorAll('[data-p126-transcript]').forEach(button => button.addEventListener('click', () => showTranscriptMode(button.dataset.p126Transcript)));
    showTranscriptMode('timeline');
  }

  function showTranscriptMode(mode) {
    const discovery = document.getElementById('p12AssetDiscovery');
    const review = document.getElementById('mamTranscriptReview');
    const tabs = document.getElementById('p126TranscriptTabs');
    if (!discovery || !review || !tabs) return;

    tabs.dataset.mode = mode;
    tabs.querySelectorAll('[data-p126-transcript]').forEach(button => button.classList.toggle('active', button.dataset.p126Transcript === mode));
    review.classList.remove('p126-revisions-only', 'p126-manual-only');
    const body = document.getElementById('p12AssetTab');

    if (mode === 'timeline') {
      discovery.querySelector('[data-p12-tab="transcript"]')?.click();
      if (body) body.classList.remove('p126-transcript-pane-hidden');
      review.classList.add('p126-transcript-pane-hidden');
    } else {
      if (body) body.classList.add('p126-transcript-pane-hidden');
      review.classList.remove('p126-transcript-pane-hidden');
      review.classList.add(mode === 'revisions' ? 'p126-revisions-only' : 'p126-manual-only');
    }
  }

  function replaceArabicTerms(root) {
    if (!arabic || !root) return;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walker.nextNode())) {
      if (node.parentElement?.matches('script,style,code,input,textarea,option')) continue;
      const current = node.nodeValue || '';
      const next = current.replaceAll('البحث والمرشحات', 'البحث').replaceAll('مسح المرشحات', 'مسح').replaceAll('مرشحات', 'مرجعات').replaceAll('رفع الملفات', 'إضافة ميديا');
      if (next !== current) node.nodeValue = next;
    }
  }

  function enhanceCurrentRoute() {
    ensureGlobalSearch();
    ensureActiveUsersBadge();
    applyPermissionVisibility();
    replaceArabicTerms(content);
    if (route === 'dashboard') decorateDashboard();
    if (route === 'asset') setupTranscriptWorkspace();
  }

  const previousRender = render;
  render = function () {
    previousRender();
    enhanceCurrentRoute();
    if (route === 'dashboard') setTimeout(() => void renderDashboardCharts(), 0);
    if (route === 'reports') void renderReports();
    if (route === 'asset') setTimeout(setupTranscriptWorkspace, 80);
    if (window.p126PendingSearch && route === 'search') void runPendingGlobalSearch();
  };

  let observerQueued = false;
  const observer = new MutationObserver(() => {
    if (observerQueued) return;
    observerQueued = true;
    requestAnimationFrame(() => {
      observerQueued = false;
      replaceArabicTerms(document.body);
      applyPermissionVisibility();
      if (route === 'dashboard' && !document.getElementById('p126DashboardQuickActions')) decorateDashboard();
      if (route === 'asset' && !document.getElementById('p126TranscriptTabs')) setupTranscriptWorkspace();
    });
  });
  observer.observe(document.body, { childList:true, subtree:true });

  ensureGlobalSearch();
  startPresenceHeartbeat();
  enhanceCurrentRoute();
  void loadIdentity();
})();