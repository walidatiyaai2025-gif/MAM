(() => {
'use strict';

const PRIMARY_ROUTES = ['dashboard','library','curation-actions','ingest','upload','queue','reports','protection'];
const ADMIN_ROUTES = ['admin','settings','categories','references','mediaPermissions','capabilities'];
const TRAILING_ROUTES = ['admin-actions','search'];
const VALID_ROUTES = new Set([...PRIMARY_ROUTES,...ADMIN_ROUTES,...TRAILING_ROUTES,'asset','myPermissions']);
const HASH_KEYS = new Set(['route','asset','q','kind','lifecycle','category','collection','tag','page','view','sort','tab','libraryTab','assetTab','searched']);

let internalNavigation = false;
let navScheduled = false;
let domScheduled = false;
let pendingRestore = null;

const isArabic = () => document.documentElement.lang === 'ar';
const text = (en, ar) => isArabic() ? ar : en;
const hashParams = () => new URLSearchParams(location.hash.replace(/^#/, ''));
const hashRoute = params => params.get('route') || 'dashboard';
const byId = id => document.getElementById(id);
const firstValue = (...ids) => {
  for (const id of ids) {
    const element = byId(id);
    if (element && 'value' in element) return String(element.value || '').trim();
  }
  return '';
};
const setParam = (params, key, value, defaultValue = '') => {
  const normalized = String(value ?? '').trim();
  if (!normalized || normalized === defaultValue) params.delete(key);
  else params.set(key, normalized);
};

function captureRouteState(routeKey, source = hashParams()) {
  const state = new URLSearchParams();
  state.set('route', routeKey);

  if (hashRoute(source) === routeKey) {
    source.forEach((value, key) => {
      if (HASH_KEYS.has(key) && key !== 'route') state.set(key, value);
    });
  }

  if (routeKey === 'asset') {
    const assetId = typeof p12SelectedAssetId !== 'undefined' ? p12SelectedAssetId : state.get('asset');
    setParam(state, 'asset', assetId);
  }

  if (routeKey === 'library') {
    const query = firstValue('p128Query','p05Query') || (typeof p05Query !== 'undefined' ? p05Query : '');
    const lifecycle = firstValue('p128Life','p05Lifecycle') || (typeof p05Lifecycle !== 'undefined' ? p05Lifecycle : '');
    const category = firstValue('p128Cat','p05Category') || (typeof p05Category !== 'undefined' ? p05Category : '');
    const collection = firstValue('p128Collection','p05Collection') || (typeof p05CollectionId !== 'undefined' ? p05CollectionId : '');
    const tag = typeof p05Tag !== 'undefined' ? p05Tag : state.get('tag');
    const page = typeof p05Page !== 'undefined' ? p05Page : state.get('page');
    const grid = typeof p05Grid !== 'undefined' ? p05Grid : state.get('view') !== 'list';
    setParam(state, 'q', query);
    setParam(state, 'kind', firstValue('p128Kind') || (typeof p128MediaKind !== 'undefined' ? p128MediaKind : state.get('kind')));
    setParam(state, 'lifecycle', lifecycle);
    setParam(state, 'category', category);
    setParam(state, 'collection', collection);
    setParam(state, 'tag', tag);
    setParam(state, 'page', Number(page) > 1 ? page : '');
    setParam(state, 'view', grid ? '' : 'list');
    setParam(state, 'sort', firstValue('p128Sort') || (typeof p128Sort !== 'undefined' ? p128Sort : state.get('sort')), 'newest');
    const libraryTab = document.querySelector('[data-mam-library-tab][aria-selected="true"]')?.dataset.mamLibraryTab;
    setParam(state, 'libraryTab', libraryTab, 'browse');
  }

  if (routeKey === 'search') {
    setParam(state, 'q', firstValue('p12SearchQuery','p126GlobalSearchInput') || state.get('q'));
    setParam(state, 'kind', firstValue('p12SearchKind') || state.get('kind'));
    setParam(state, 'category', firstValue('p12SearchCategory') || state.get('category'));
    if (byId('p12SearchResults')?.textContent.trim()) state.set('searched','1');
  }

  const activeTab = document.querySelector(
    routeKey === 'admin' ? '#p127AdminTabs [data-admin-tab].active' :
    routeKey === 'asset' ? '#p126TranscriptTabs [data-p126-transcript].active' :
    '#content .p127-tabs [data-tab].active'
  );
  const tabValue = activeTab?.dataset.adminTab || activeTab?.dataset.p126Transcript || activeTab?.dataset.tab;
  setParam(state, 'tab', tabValue);

  if (routeKey === 'asset') {
    const assetTab = document.querySelector('[data-mam-asset-tab][aria-selected="true"]')?.dataset.mamAssetTab;
    setParam(state, 'assetTab', assetTab, 'preview');
  }

  return state;
}

function applyStateToGlobals(params) {
  if (typeof p12SelectedAssetId !== 'undefined' && params.get('asset')) p12SelectedAssetId = params.get('asset');
  if (typeof p05Query !== 'undefined') p05Query = params.get('q') || '';
  if (typeof p05Lifecycle !== 'undefined') p05Lifecycle = params.get('lifecycle') || '';
  if (typeof p05Category !== 'undefined') p05Category = params.get('category') || '';
  if (typeof p05CollectionId !== 'undefined') p05CollectionId = params.get('collection') || '';
  if (typeof p05Tag !== 'undefined') p05Tag = params.get('tag') || '';
  if (typeof p05Page !== 'undefined') p05Page = Math.max(1, Number(params.get('page') || 1));
  if (typeof p05Grid !== 'undefined') p05Grid = params.get('view') !== 'list';
  if (typeof p128MediaKind !== 'undefined') p128MediaKind = params.get('kind') || '';
  if (typeof p128Sort !== 'undefined') p128Sort = params.get('sort') || 'newest';
  pendingRestore = { route:hashRoute(params), params:new URLSearchParams(params), attempts:0, searched:false };
}

function writeLocation(state) {
  const url = new URL(location.href);
  url.searchParams.set('lang', isArabic() ? 'ar' : 'en');
  url.hash = state.toString();
  internalNavigation = true;
  history.replaceState(history.state, '', url);
  internalNavigation = false;
  try {
    localStorage.setItem('mam.language', isArabic() ? 'ar' : 'en');
    localStorage.setItem('mam.p127.route', state.get('route') || 'dashboard');
  } catch { }
}

function currentCanonicalButton(key) {
  const nodes = [...document.querySelectorAll(`#nav [data-route="${CSS.escape(key)}"]`)];
  return nodes.find(node => !node.classList.contains('p129-duplicate-route') && !node.hidden) ||
    nodes.find(node => !node.classList.contains('p129-duplicate-route')) || nodes[0] || null;
}

function reconcileNavigation() {
  const nav = byId('nav');
  if (!nav) return;
  const menu = nav.querySelector(':scope > .p127-admin-menu');
  const submenu = menu?.querySelector(':scope > .p127-admin-submenu');

  if (submenu) {
    ADMIN_ROUTES.forEach(key => {
      const button = currentCanonicalButton(key);
      if (button && button.parentElement !== submenu) submenu.appendChild(button);
    });
  }

  const primary = PRIMARY_ROUTES.map(currentCanonicalButton).filter(Boolean);
  const trailing = TRAILING_ROUTES.map(currentCanonicalButton).filter(Boolean);
  const asset = ['asset'].map(currentCanonicalButton).filter(Boolean);
  const ordered = [...primary, ...(menu ? [menu] : []), ...trailing, ...asset];
  const extras = [...nav.children].filter(node => !ordered.includes(node));
  const desired = [...primary, ...extras, ...(menu ? [menu] : []), ...trailing, ...asset];
  const current = [...nav.children];
  if (current.length !== desired.length || current.some((node, index) => node !== desired[index])) {
    const fragment = document.createDocumentFragment();
    desired.forEach(node => fragment.appendChild(node));
    nav.appendChild(fragment);
  }

  const currentRoute = typeof route !== 'undefined' ? route : hashRoute(hashParams());
  nav.querySelectorAll('[data-route]').forEach(button => {
    const active = button.dataset.route === currentRoute;
    button.classList.toggle('active', active);
    if (active) button.setAttribute('aria-current','page');
    else button.removeAttribute('aria-current');
    if (!button.type) button.type = 'button';
  });
  if (menu) {
    const inAdmin = ADMIN_ROUTES.includes(currentRoute);
    menu.classList.toggle('open', inAdmin || menu.classList.contains('open'));
    const trigger = menu.querySelector(':scope > .p127-admin-trigger');
    trigger?.classList.toggle('active', inAdmin);
    trigger?.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');
  }
}

function scheduleNavigation() {
  if (navScheduled) return;
  navScheduled = true;
  requestAnimationFrame(() => {
    navScheduled = false;
    reconcileNavigation();
  });
}

function applyDeferredState() {
  const pending = pendingRestore;
  if (!pending || pending.route !== (typeof route !== 'undefined' ? route : hashRoute(hashParams()))) return;
  const params = pending.params;

  const setValue = (id, value) => {
    const element = byId(id);
    if (!element || value === null || value === undefined) return false;
    const optionExists = element.tagName !== 'SELECT' || [...element.options].some(option => option.value === value);
    if (optionExists && element.value !== value) element.value = value;
    return true;
  };

  if (pending.route === 'library') {
    /* A render can recreate Library controls and reset its legacy globals before
       deferred hydration runs. Keep the pending URL state authoritative until
       hydration completes instead of letting defaults erase the deep link. */
    if (typeof p05Query !== 'undefined') p05Query = params.get('q') || '';
    if (typeof p05Lifecycle !== 'undefined') p05Lifecycle = params.get('lifecycle') || '';
    if (typeof p05Category !== 'undefined') p05Category = params.get('category') || '';
    if (typeof p05CollectionId !== 'undefined') p05CollectionId = params.get('collection') || '';
    if (typeof p05Tag !== 'undefined') p05Tag = params.get('tag') || '';
    if (typeof p05Page !== 'undefined') p05Page = Math.max(1, Number(params.get('page') || 1));
    if (typeof p05Grid !== 'undefined') p05Grid = params.get('view') !== 'list';
    if (typeof p128MediaKind !== 'undefined') p128MediaKind = params.get('kind') || '';
    if (typeof p128Sort !== 'undefined') p128Sort = params.get('sort') || 'newest';
    setValue('p128Query', params.get('q') || '');
    setValue('p128Kind', params.get('kind') || '');
    setValue('p128Life', params.get('lifecycle') || '');
    setValue('p128Cat', params.get('category') || '');
    setValue('p128Collection', params.get('collection') || '');
    setValue('p128Sort', params.get('sort') || 'newest');
    const libraryTab = params.get('libraryTab');
    if (libraryTab) {
      const libraryTabButton = document.querySelector('[data-mam-library-tab="' + CSS.escape(libraryTab) + '"]');
      if (libraryTabButton?.getAttribute('aria-selected') !== 'true') libraryTabButton?.click();
    }
  }
  if (pending.route === 'search') {
    const ready = setValue('p12SearchQuery', params.get('q') || '');
    setValue('p12SearchKind', params.get('kind') || '');
    setValue('p12SearchCategory', params.get('category') || '');
    if (ready && params.get('searched') === '1' && !pending.searched && (params.get('q') || '').length >= 2) {
      pending.searched = true;
      byId('p12SearchButton')?.click();
    }
  }

  const assetTab = params.get('assetTab');
  if (pending.route === 'asset' && assetTab) {
    const assetTabButton = document.querySelector('[data-mam-asset-tab="' + CSS.escape(assetTab) + '"]');
    if (assetTabButton?.getAttribute('aria-selected') !== 'true') assetTabButton?.click();
  }

  const tab = params.get('tab');
  if (tab) {
    const tabButton = pending.route === 'admin'
      ? document.querySelector(`#p127AdminTabs [data-admin-tab="${CSS.escape(tab)}"]`)
      : pending.route === 'asset'
        ? document.querySelector(`#p126TranscriptTabs [data-p126-transcript="${CSS.escape(tab)}"]`)
        : document.querySelector(`#content .p127-tabs [data-tab="${CSS.escape(tab)}"]`);
    if (tabButton && !tabButton.classList.contains('active')) tabButton.click();
  }

  pending.attempts += 1;
  if (pending.attempts > 80) pendingRestore = null;
}

function enhanceAccessibility() {
  const globalSearch = byId('p126GlobalSearchInput');
  if (globalSearch) {
    const label = text('Quick search within content','بحث سريع داخل المحتوى');
    globalSearch.placeholder = label;
    globalSearch.setAttribute('aria-label', label);
  }
  const dynamicLabels = [
    ['.p127-profile-button', text('Open account menu','فتح قائمة الحساب')],
    ['.p128-mobile-menu', text('Open navigation','فتح القائمة')],
    ['.brand > button', text('Collapse navigation','طي القائمة')]
  ];
  dynamicLabels.forEach(([selector, label]) => document.querySelector(selector)?.setAttribute('aria-label', label));
  const landingLink = document.querySelector('.topbar .actions > a[href="/"]');
  if (landingLink) landingLink.title = text('Landing page','الصفحة الرئيسية');

  document.querySelectorAll('.state').forEach(element => {
    const urgent = element.classList.contains('error') || element.classList.contains('denied');
    element.setAttribute('role', urgent ? 'alert' : 'status');
  });
  document.querySelectorAll('input,select,textarea').forEach(element => {
    if (element.type === 'hidden' || element.getAttribute('aria-label') || element.getAttribute('aria-labelledby')) return;
    const label = element.labels?.[0]?.textContent.trim() || element.placeholder || element.options?.[0]?.textContent.trim();
    if (label) element.setAttribute('aria-label', label);
  });
  document.querySelectorAll('button').forEach(button => {
    if (!button.type) button.type = 'button';
    if (button.getAttribute('aria-label') || button.textContent.trim()) return;
    const title = button.title || text('Action','إجراء');
    button.setAttribute('aria-label', title);
  });

  document.querySelectorAll('#content .state.error,#content .state.degraded').forEach(element => {
    if (element.querySelector('.p131-retry') || element.closest('.p127-modal')) return;
    const retry = document.createElement('button');
    retry.type = 'button';
    retry.className = 'p131-retry';
    retry.innerHTML = `<i class="bi bi-arrow-clockwise"></i><span>${text('Retry','إعادة المحاولة')}</span>`;
    retry.addEventListener('click', () => {
      if (typeof render === 'function') render();
    });
    element.appendChild(retry);
  });
}

function ensureMobileScrim() {
  const shell = document.querySelector('.app-shell');
  if (!shell || shell.querySelector(':scope > .p131-sidebar-scrim')) return;
  const scrim = document.createElement('button');
  scrim.type = 'button';
  scrim.className = 'p131-sidebar-scrim';
  scrim.setAttribute('aria-label', text('Close navigation','إغلاق القائمة'));
  scrim.addEventListener('click', () => shell.classList.remove('p128-mobile-open'));
  shell.insertBefore(scrim, shell.querySelector('main'));
}

function enhanceDom() {
  enhanceAccessibility();
  ensureMobileScrim();
  applyDeferredState();
  scheduleNavigation();
}

function scheduleDom() {
  if (domScheduled) return;
  domScheduled = true;
  requestAnimationFrame(() => {
    domScheduled = false;
    enhanceDom();
  });
}

if (typeof render === 'function') {
  const previousRender = render;
  render = function () {
    const before = hashParams();
    const targetRoute = typeof route !== 'undefined' ? route : hashRoute(before);
    const restoring = pendingRestore && pendingRestore.route === targetRoute && pendingRestore.attempts <= 80;
    const state = restoring
      ? new URLSearchParams(pendingRestore.params)
      : hashRoute(before) === targetRoute
        ? captureRouteState(targetRoute, before)
        : new URLSearchParams({ route:targetRoute });
    previousRender();
    writeLocation(state);
    if (!restoring) pendingRestore = { route:targetRoute, params:new URLSearchParams(state), attempts:0, searched:false };
    scheduleDom();
  };
}

/*
  This listener is registered before p132's final navigation capture owner. An
  explicit nav click must supersede any deferred hydration before the route owner
  renders, otherwise a later restore can put the old hash back under the new UI.
*/
window.addEventListener('click', event => {
  if (!(event.target instanceof Element)) return;
  const navButton = event.target.closest('#nav [data-route]');
  const requestedRoute = navButton?.dataset.route || '';
  if (!requestedRoute || requestedRoute === 'asset' || !VALID_ROUTES.has(requestedRoute)) return;

  pendingRestore = null;
  const reconcileExplicitRoute = () => {
    const activeRoute = typeof route !== 'undefined' ? route : hashRoute(hashParams());
    if (activeRoute !== requestedRoute) return;
    const params = hashParams();
    if (hashRoute(params) === requestedRoute) return;
    const url = new URL(location.href);
    params.set('route', requestedRoute);
    url.hash = params.toString();
    url.searchParams.set('lang', isArabic() ? 'ar' : 'en');
    internalNavigation = true;
    try {
      History.prototype.replaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.p127.route', requestedRoute);
    } finally {
      internalNavigation = false;
    }
  };
  [0,40,120,220].forEach(delay => setTimeout(reconcileExplicitRoute, delay));
}, true);

window.addEventListener('hashchange', () => {
  if (internalNavigation || typeof route === 'undefined' || typeof render !== 'function') return;
  const params = hashParams();
  const requestedRoute = hashRoute(params);
  if (!VALID_ROUTES.has(requestedRoute)) return;
  applyStateToGlobals(params);
  if (route !== requestedRoute) route = requestedRoute;
  render();
});

document.addEventListener('click', event => {
  if (!(event.target instanceof Element)) return;
  const navButton = event.target.closest('#nav [data-route]');
  if (navButton && matchMedia('(max-width: 800px)').matches) {
    document.querySelector('.app-shell')?.classList.remove('p128-mobile-open');
  }
  if (event.target.closest('[data-p128-language],#languageButton')) {
    const currentRoute = typeof route !== 'undefined' ? route : hashRoute(hashParams());
    pendingRestore = { route:currentRoute, params:captureRouteState(currentRoute), attempts:0, searched:false };
  }
  if (event.target.closest('#p128Apply,#p128Reset,#p128Grid,#p128List,#p128Prev,#p128Next,#p12SearchButton,[data-admin-tab],[data-tab],[data-p126-transcript],[data-mam-library-tab],[data-mam-asset-tab]')) {
    pendingRestore = null;
    setTimeout(() => {
      const currentRoute = typeof route !== 'undefined' ? route : hashRoute(hashParams());
      writeLocation(captureRouteState(currentRoute));
    }, 0);
  }
}, true);

document.addEventListener('change', event => {
  if (!event.target.closest('#content')) return;
  pendingRestore = null;
  setTimeout(() => {
    const currentRoute = typeof route !== 'undefined' ? route : hashRoute(hashParams());
    writeLocation(captureRouteState(currentRoute));
  }, 0);
}, true);

document.addEventListener('keydown', event => {
  if (event.key === 'Escape') document.querySelector('.app-shell')?.classList.remove('p128-mobile-open');
});

const initial = hashParams();
const bootstrap = new URLSearchParams(String(window.mamP131InitialHash || '').replace(/^#/, ''));
if (hashRoute(bootstrap) === hashRoute(initial)) {
  bootstrap.forEach((value, key) => {
    if (HASH_KEYS.has(key) && !initial.has(key)) initial.set(key, value);
  });
}
delete window.mamP131InitialHash;
if (VALID_ROUTES.has(hashRoute(initial))) applyStateToGlobals(initial);
writeLocation(captureRouteState(typeof route !== 'undefined' ? route : hashRoute(initial), initial));

const navObserver = new MutationObserver(scheduleNavigation);
const nav = byId('nav');
if (nav) navObserver.observe(nav, { childList:true, subtree:true, attributes:true, attributeFilter:['hidden','aria-hidden'] });
const domObserver = new MutationObserver(scheduleDom);
domObserver.observe(document.body, { childList:true,subtree:true });

enhanceDom();
})();