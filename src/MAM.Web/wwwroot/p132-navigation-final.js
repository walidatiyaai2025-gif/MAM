(() => {
'use strict';

const nav = document.getElementById('nav');
if (!nav) return;

const adminRoutes = new Set(['admin','settings','categories','collections','tags','references','mediaPermissions']);
let lastInteractionSignature = '';
let lastInteractionAt = 0;
let hardenScheduled = false;
let renderInProgress = false;
let languageSwitchTarget = '';
let languageSwitchSnapshot = null;
let languageSwitchGeneration = 0;

/*
  Several legacy render owners write the current URL after rendering. During a
  language switch the route/query/hash are user state and must be immutable: the
  only permitted URL change is ?lang=. Freeze the pre-click URL for the lifetime
  of the locale transition so stale asynchronous owners cannot drop filters or
  write the previous language. Outside a locale transition, only normalize the
  language key and preserve the caller's URL state.
*/
const nativeReplaceState = history.replaceState.bind(history);
function canonicalLanguage() {
  return languageSwitchTarget === 'ar' || languageSwitchTarget === 'en'
    ? languageSwitchTarget
    : (document.documentElement.lang === 'ar' ? 'ar' : 'en');
}
function activeRouteAuthority() {
  try {
    const diagnosis = window.mamRouteAuthority?.diagnose?.();
    if (diagnosis?.navigationLockActive && diagnosis.route) return String(diagnosis.route);
  } catch { }
  return '';
}

function applyActiveRouteAuthority(url) {
  const key = activeRouteAuthority();
  if (!key) return url;
  const state = new URLSearchParams(url.hash.replace(/^#/, ''));
  state.set('route', key);
  url.hash = state.toString();
  return url;
}

function frozenLanguageUrl(snapshot = languageSwitchSnapshot, language = canonicalLanguage()) {
  if (!snapshot) return null;
  const url = new URL(snapshot.href);
  url.searchParams.set('lang', language);
  return applyActiveRouteAuthority(url);
}
function canonicalizeHistoryUrl(value) {
  if (languageSwitchSnapshot) return frozenLanguageUrl()?.href ?? value;
  if (value === null || value === undefined || value === '') return value;
  try {
    const url = new URL(String(value), location.href);
    if (url.origin === location.origin && url.pathname === location.pathname) {
      url.searchParams.set('lang', canonicalLanguage());
      return applyActiveRouteAuthority(url).href;
    }
  } catch { }
  return value;
}
function canonicalizeWithRouteAuthority(value) {
  try {
    const canonicalize = window.mamRouteAuthority?.canonicalizeUrl;
    if (typeof canonicalize === 'function') return canonicalize(value);
  } catch { }
  return value;
}

const mamRouteAwareReplaceState = function (state, title, url) {
  const languageCanonical = canonicalizeHistoryUrl(url);
  const routeCanonical = canonicalizeWithRouteAuthority(languageCanonical);
  try {
    const authoritativeReplaceState = window.mamRouteAuthority?.replaceState;
    if (typeof authoritativeReplaceState === 'function') {
      return authoritativeReplaceState(state, title, routeCanonical);
    }
  } catch { }
  return nativeReplaceState(state, title, routeCanonical);
};
Object.defineProperty(mamRouteAwareReplaceState, '__mamRouteAuthorityFinal', {
  value: true,
  configurable: false,
  enumerable: false,
  writable: false
});
history.replaceState = mamRouteAwareReplaceState;

/*
  P135 can ask for a Library reconciliation from inside the legacy
  loadLiveLibrary call that is itself running during render(). The final runtime
  owner rejects only synchronous re-entry so a stale Library render cannot race
  the next hash route. Normal later renders and hashchange renders remain valid.
*/
const delegatedRender = typeof render === 'function' ? render : null;
if (delegatedRender && !delegatedRender.__mamNonReentrant) {
  const guardedRender = function (...args) {
    if (renderInProgress) return;
    renderInProgress = true;
    try { return delegatedRender.apply(this, args); }
    finally { renderInProgress = false; }
  };
  guardedRender.__mamNonReentrant = true;
  window.render = guardedRender;
  try { render = guardedRender; } catch { }
}

function closeMobileNavigation() {
  const shell = document.querySelector('.app-shell');
  shell?.classList.remove('p128-mobile-open');
  shell?.classList.remove('p131-sidebar-open');
}

function adminMenu() {
  return nav.querySelector(':scope > .p127-admin-menu');
}

function setAdminOpen(open, activeRoute = '') {
  const menu = adminMenu();
  if (!menu || menu.hidden) return;
  const trigger = menu.querySelector(':scope > .p127-admin-trigger');
  menu.classList.toggle('open', !!open);
  trigger?.setAttribute('aria-expanded', open ? 'true' : 'false');
  const inAdmin = adminRoutes.has(activeRoute);
  trigger?.classList.toggle('active', inAdmin);
  menu.querySelectorAll('.p127-admin-submenu button[data-route]').forEach(button => {
    const active = button.dataset.route === activeRoute;
    button.classList.toggle('active', active);
    if (active) button.setAttribute('aria-current','page');
    else button.removeAttribute('aria-current');
  });
}

function syncAdminMenu(key) {
  setAdminOpen(adminRoutes.has(key), key);
}

function syncRouteLocation(key) {
  try {
    const url = new URL(location.href);
    const state = new URLSearchParams(url.hash.replace(/^#/, ''));
    state.set('route', key);
    url.hash = state.toString();
    history.replaceState(history.state, '', url);
    localStorage.setItem('mam.p127.route', key);
  } catch { }
}

function syncLanguageLocation(languageOverride = '') {
  try {
    const language = languageOverride === 'ar' || languageOverride === 'en'
      ? languageOverride
      : (document.documentElement.lang === 'ar' ? 'ar' : 'en');
    const frozen = frozenLanguageUrl(languageSwitchSnapshot, language);
    if (frozen) nativeReplaceState(history.state, '', frozen);
    else {
      const url = new URL(location.href);
      url.searchParams.set('lang', language);
      nativeReplaceState(history.state, '', url);
    }
    localStorage.setItem('mam.language', language);
  } catch { }
}

/*
  Locale application is asynchronous in the legacy shell. Synchronize the URL
  from the authoritative DOM locale mutation and, while a transition is active,
  always restore from the immutable pre-click snapshot.
*/
const languageLocationObserver = new MutationObserver(mutations => {
  if (mutations.some(mutation =>
    mutation.type === 'attributes' &&
    (mutation.attributeName === 'lang' || mutation.attributeName === 'dir'))) {
    syncLanguageLocation(languageSwitchTarget);
  }
});
languageLocationObserver.observe(document.documentElement, {
  attributes: true,
  attributeFilter: ['lang','dir']
});

/*
  Some legacy owners bypass the wrapped history writer and can rewrite only the
  URL after the DOM locale has already settled. MutationObserver cannot see that
  URL-only race, so keep a cheap runtime invariant: the URL locale must always
  equal the active transition target (or, outside a transition, the DOM locale).
  During a transition the full pre-click deep link is restored, not just ?lang=.
*/
function reconcileLanguageInvariant() {
  try {
    const language = canonicalLanguage();
    const desired = frozenLanguageUrl(languageSwitchSnapshot, language) || applyActiveRouteAuthority(new URL(location.href));
    desired.searchParams.set('lang', language);
    applyActiveRouteAuthority(desired);
    if (desired.href !== location.href) {
      nativeReplaceState(history.state, '', desired.href);
    }
    localStorage.setItem('mam.language', language);
  } catch { }
}
setInterval(reconcileLanguageInvariant, 20);
window.addEventListener('pageshow', reconcileLanguageInvariant);
setTimeout(reconcileLanguageInvariant, 0);

function beginLanguageSwitch(event) {
  if (!(event.target instanceof Element)) return;
  if (!event.target.closest('[data-p128-language],#languageButton')) return;

  const snapshot = new URL(location.href);
  const currentLanguage = document.documentElement.lang === 'ar' ? 'ar' : 'en';
  const targetLanguage = currentLanguage === 'ar' ? 'en' : 'ar';
  const targetArabic = targetLanguage === 'ar';
  const generation = ++languageSwitchGeneration;
  const transitionStartedAt = performance.now();
  languageSwitchSnapshot = snapshot;
  languageSwitchTarget = targetLanguage;

  /* p132 is the single language owner. Stop every legacy element/document
     language handler so one click produces exactly one locale mutation. */
  event.preventDefault();
  event.stopPropagation();
  event.stopImmediatePropagation();

  try {
    window.arabic = targetArabic;
    arabic = targetArabic;
    localStorage.setItem('mam.p128.language.initialized', '1');
    localStorage.setItem('mam.language', targetLanguage);
    if (typeof render === 'function') render();
    window.arabic = targetArabic;
  } catch { }

  const enforce = () => {
    if (generation !== languageSwitchGeneration) return;
    try {
      window.arabic = targetArabic;
      syncLanguageLocation(targetLanguage);
      localStorage.setItem('mam.p128.language.initialized', '1');
      localStorage.setItem('mam.language', targetLanguage);
    } catch { }
  };

  const enforceFrame = now => {
    if (generation !== languageSwitchGeneration) return;
    enforce();
    if (now - transitionStartedAt < 1000) requestAnimationFrame(enforceFrame);
  };

  /* Defend the immutable deep link across synchronous render, mutation
     observers, async library reconciliation and deferred legacy URL writers. */
  enforce();
  queueMicrotask(enforce);
  requestAnimationFrame(enforceFrame);
  setTimeout(enforce, 0);
  setTimeout(enforce, 40);
  setTimeout(enforce, 80);
  setTimeout(enforce, 120);
  setTimeout(enforce, 180);
  setTimeout(enforce, 240);
  setTimeout(enforce, 400);
  setTimeout(enforce, 700);
  setTimeout(enforce, 1000);
  setTimeout(() => {
    if (generation !== languageSwitchGeneration) return;
    enforce();
    languageSwitchSnapshot = null;
    languageSwitchTarget = '';
    reconcileLanguageInvariant();
  }, 1400);
}

/*
  A deliberate route change supersedes the temporary language deep-link freeze.
  Invalidate all deferred locale enforcers before rendering the new route so a
  post-language navigation cannot be restored back to the pre-switch route.
*/
function finishLanguageTransitionForNavigation() {
  if (!languageSwitchSnapshot && !languageSwitchTarget) return;
  ++languageSwitchGeneration;
  languageSwitchSnapshot = null;
  languageSwitchTarget = '';
  reconcileLanguageInvariant();
}

function activateRoute(key) {
  if (!key || key === 'asset' || typeof render !== 'function' || typeof route === 'undefined') return false;
  if (key === 'settings') {
    try { localStorage.setItem('mam.p142.adminTab','web-menu'); } catch { }
    key = 'admin';
  }
  try {
    if (window.mamNavigationPreferences?.isRouteEnabled?.(key) === false) return false;
  } catch { }
  finishLanguageTransitionForNavigation();
  route = key;
  syncAdminMenu(key);
  render();
  syncRouteLocation(key);
  if (!adminRoutes.has(key)) setAdminOpen(false, key);
  closeMobileNavigation();
  return true;
}

function visibleControl(element) {
  if (!element || element.hidden || element.disabled || element.getAttribute('aria-hidden') === 'true' ||
      element.dataset.p1214Enabled === '0' || element.classList.contains('p1214-config-hidden')) return false;
  const style = getComputedStyle(element);
  if (style.display === 'none' || style.visibility === 'hidden') return false;
  const rect = element.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0 && rect.right > 0 && rect.bottom > 0 && rect.left < window.innerWidth && rect.top < window.innerHeight;
}

function pointInside(element, x, y) {
  if (!visibleControl(element)) return false;
  const rect = element.getBoundingClientRect();
  return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
}

function controlFromTarget(target) {
  if (!(target instanceof Element)) return null;

  const routeButton = target.closest('#nav button[data-route]');
  if (routeButton && nav.contains(routeButton) && visibleControl(routeButton)) {
    return { type:'route', element:routeButton, key:routeButton.dataset.route || '' };
  }

  const adminTrigger = target.closest('#nav .p127-admin-trigger');
  if (adminTrigger && nav.contains(adminTrigger) && visibleControl(adminTrigger)) {
    return { type:'admin-trigger', element:adminTrigger, key:'' };
  }

  return null;
}

/*
  Production-proven fallback: do not trust event.target alone. Chrome/Edge may
  deliver the pointer to a transparent/stacked overlay even though the visible
  sidebar button is underneath it. Resolve the intended control from the pointer
  coordinates against the current button rectangles.
*/
function controlAtPoint(x, y) {
  for (const button of nav.querySelectorAll('button[data-route]')) {
    if (pointInside(button, x, y)) {
      return { type:'route', element:button, key:button.dataset.route || '' };
    }
  }

  const trigger = nav.querySelector('.p127-admin-trigger');
  if (trigger && pointInside(trigger, x, y)) {
    return { type:'admin-trigger', element:trigger, key:'' };
  }

  return null;
}

function toggleAdmin() {
  const menu = adminMenu();
  if (!menu || menu.hidden) return false;
  const open = !menu.classList.contains('open');
  setAdminOpen(open, typeof route !== 'undefined' ? route : '');
  return true;
}

function interactionSignature(control) {
  return control.type === 'route' ? `route:${control.key}` : control.type;
}

function handlePointerInteraction(event) {
  let control = controlFromTarget(event.target);
  if (!control && Number.isFinite(event.clientX) && Number.isFinite(event.clientY)) {
    control = controlAtPoint(event.clientX, event.clientY);
  }
  if (!control) return;

  event.preventDefault();
  event.stopPropagation();
  event.stopImmediatePropagation();

  const signature = interactionSignature(control);
  const now = performance.now();
  const duplicateClick = event.type === 'click' && signature === lastInteractionSignature && now - lastInteractionAt < 500;
  if (duplicateClick) return;

  lastInteractionSignature = signature;
  lastInteractionAt = now;

  if (control.type === 'route') {
    if (!control.key || control.key === 'asset') return;
    activateRoute(control.key);
    return;
  }

  if (control.type === 'admin-trigger') toggleAdmin();
}

/*
  P12.16 final navigation owner.
  Window capture runs before document/element handlers. pointerup is the primary
  physical interaction owner; click remains a mouse/keyboard compatibility path.
  Coordinate hit-testing makes the sidebar resilient to transparent overlays.
*/
window.addEventListener('pointerup', handlePointerInteraction, { capture:true, passive:false });
window.addEventListener('click', handlePointerInteraction, { capture:true, passive:false });
window.addEventListener('click', beginLanguageSwitch, true);
window.addEventListener('keydown', event => {
  if (event.key !== 'Enter' && event.key !== ' ') return;
  const control = controlFromTarget(document.activeElement);
  if (!control) return;
  event.preventDefault();
  event.stopPropagation();
  event.stopImmediatePropagation();
  if (control.type === 'route') activateRoute(control.key);
  else if (control.type === 'admin-trigger') toggleAdmin();
}, true);

const style = document.createElement('style');
style.dataset.mamNavigationFinal = 'p12.16';
style.textContent = `
.app-shell{position:relative!important}
.sidebar{display:flex!important;flex-direction:column!important;box-sizing:border-box!important;position:relative!important;z-index:2147483000!important;isolation:isolate!important;pointer-events:auto!important}
.sidebar>.brand{flex:0 0 auto!important}
.sidebar::before,.sidebar::after,.brand::before,.brand::after,.app-shell::before,.app-shell::after{pointer-events:none!important}
#nav{position:relative!important;z-index:2147483001!important;flex:1 1 auto!important;min-height:0!important;max-height:none!important;padding-block-end:8px!important;overflow-y:auto!important;overflow-x:hidden!important;pointer-events:auto!important;isolation:isolate!important;touch-action:manipulation!important}
#nav>button,#nav button[data-route],#nav .p127-admin-menu,#nav .p127-admin-trigger,#nav .p127-admin-submenu,#nav .p127-admin-submenu button{position:relative!important;z-index:2147483002!important;pointer-events:auto!important}
#nav button[data-route] *,#nav .p127-admin-trigger *{pointer-events:none!important}
.p127-admin-menu{width:100%!important;margin:0!important}
.p127-admin-trigger{width:100%!important;min-height:48px!important;cursor:pointer!important}
.p127-admin-trigger .bi-chevron-down{margin-inline-start:auto!important;transition:transform .18s ease!important}
.p127-admin-menu.open>.p127-admin-trigger .bi-chevron-down{transform:rotate(180deg)!important}
.p127-admin-submenu{display:none!important;position:relative!important;inset:auto!important;width:auto!important;margin:4px 8px 8px!important;padding:5px!important;border:1px solid rgba(255,255,255,.12)!important;border-inline-start:3px solid #d7ad49!important;border-radius:10px!important;background:rgba(0,0,0,.14)!important;box-shadow:none!important;overflow:visible!important}
.p127-admin-menu.open>.p127-admin-submenu{display:grid!important;gap:3px!important}
.p127-admin-submenu button[data-route]{width:100%!important;min-height:40px!important;padding:7px 10px!important;border-radius:8px!important;font-size:12px!important;background:transparent!important;text-align:start!important}
.p127-admin-submenu button[data-route]:hover{background:rgba(255,255,255,.08)!important}
.p127-admin-submenu button[data-route].active{background:rgba(201,152,47,.16)!important;color:#f6d77b!important;box-shadow:inset -3px 0 0 #d7ad49!important}
.app-shell.p127-sidebar-collapsed .sidebar{overflow:visible!important}
.app-shell.p127-sidebar-collapsed #nav{overflow:visible!important}
.app-shell.p127-sidebar-collapsed .p127-admin-menu.open>.p127-admin-submenu{position:absolute!important;inset-inline-start:66px!important;top:0!important;width:240px!important;margin:0!important;z-index:2147483003!important;background:#062b4c!important;box-shadow:0 14px 40px rgba(0,0,0,.28)!important}
.sidebar>.nonprod{position:relative!important;inset:auto!important;left:auto!important;right:auto!important;top:auto!important;bottom:auto!important;width:auto!important;flex:0 0 auto!important;margin:8px 8px 0!important;z-index:1!important;pointer-events:none!important}
.sidebar>.nonprod *{pointer-events:none!important}
.p128-mobile-scrim,.p131-sidebar-scrim{pointer-events:none!important}
.app-shell.p128-mobile-open>.p128-mobile-scrim,.app-shell.p128-mobile-open>.p131-sidebar-scrim{pointer-events:auto!important}
`;
document.head.appendChild(style);

function hardenNavigation() {
  const desired = [
    [nav, 'pointerEvents', 'auto'],
    [nav, 'position', 'relative'],
    [nav, 'zIndex', '2147483001']
  ];
  desired.forEach(([element, property, value]) => {
    if (element.style[property] !== value) element.style[property] = value;
  });
  nav.querySelectorAll('button,.p127-admin-menu,.p127-admin-submenu').forEach(element => {
    if (element.style.pointerEvents !== 'auto') element.style.pointerEvents = 'auto';
  });
}

function scheduleHarden() {
  if (hardenScheduled) return;
  hardenScheduled = true;
  requestAnimationFrame(() => {
    hardenScheduled = false;
    hardenNavigation();
  });
}

const observer = new MutationObserver(scheduleHarden);
observer.observe(nav, { childList:true, subtree:true, attributes:true, attributeFilter:['class','style','hidden','aria-hidden','disabled'] });
hardenNavigation();

function hitTestReport() {
  return [...nav.querySelectorAll('button[data-route]')]
    .filter(visibleControl)
    .map(button => {
      const rect = button.getBoundingClientRect();
      const x = Math.min(window.innerWidth - 1, Math.max(0, rect.left + rect.width / 2));
      const y = Math.min(window.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
      const stack = document.elementsFromPoint(x, y).slice(0, 5);
      const resolved = controlAtPoint(x, y);
      return {
        route: button.dataset.route || '',
        visible: true,
        resolvedRoute: resolved?.type === 'route' ? resolved.key : null,
        topElements: stack.map(element => ({ tag:element.tagName, id:element.id || '', className:String(element.className || '') })),
        coordinateFallbackClickable: resolved?.element === button
      };
    });
}

window.mamNavigationRuntime = Object.freeze({
  version: 'p12.16-window-coordinate',
  owner: 'window-capture-coordinate-fallback',
  activateRoute,
  hitTestReport,
  diagnose:hitTestReport
});

/*
  p132 is the final external navigation owner. Seal the route-aware history
  writer after every runtime layer has loaded so stale async code cannot replace
  it after an explicit navigation. The wrapper still delegates to the original
  platform chain and preserves language/deep-link normalization.
*/
Object.defineProperty(history, 'replaceState', {
  value: mamRouteAwareReplaceState,
  configurable: true,
  enumerable: false,
  writable: true
});
})();