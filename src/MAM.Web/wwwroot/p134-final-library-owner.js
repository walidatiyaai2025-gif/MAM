(() => {
  'use strict';

  /* Web is intentionally Arabic-only in this release. Register this capture
     owner before p132-navigation-final.js loads so even synthetic/programmatic
     clicks on legacy hidden language controls cannot reach bilingual handlers. */
  window.addEventListener('click', event => {
    if (!(event.target instanceof Element) || !event.target.closest('[data-p128-language],#languageButton')) return;
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
    if (typeof window.mamForceArabicState === 'function') window.mamForceArabicState();
  }, true);

  /*
    Navigation ownership must follow the route the runtime actually committed,
    not whichever DOM button happened to emit a click. Reconciliation layers can
    programmatically click stale/duplicate buttons and legacy async writers can
    later restore an old hash. After any navigation click, wait until the event
    has committed the global route, then keep that runtime route authoritative in
    the URL for a short bounded window. This preserves every non-route deep-link
    field and makes UI route and URL route converge deterministically.
  */
  const platformReplaceState = History.prototype.replaceState;
  let routeNavigationGeneration = 0;
  let routeEnforcementTimer = 0;

  function runtimeRoute(fallback = '') {
    try {
      if (typeof route !== 'undefined') {
        const value = String(route || '').trim();
        if (value) return value;
      }
    } catch { }
    return String(fallback || '').trim();
  }

  window.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const clickedRoute = control?.dataset.route || '';
    if (!clickedRoute || clickedRoute === 'asset') return;

    const generation = ++routeNavigationGeneration;
    const enforceRuntimeRoute = () => {
      if (generation !== routeNavigationGeneration) return;
      const activeRoute = runtimeRoute(clickedRoute);
      if (!activeRoute || activeRoute === 'asset') return;
      try {
        const url = new URL(location.href);
        const state = new URLSearchParams(url.hash.replace(/^#/, ''));
        state.set('route', activeRoute);
        url.hash = state.toString();
        url.searchParams.set('lang', 'ar');
        platformReplaceState.call(history, history.state, '', url.href);
        localStorage.setItem('mam.p127.route', activeRoute);
      } catch { }
    };

    if (routeEnforcementTimer) {
      clearInterval(routeEnforcementTimer);
      routeEnforcementTimer = 0;
    }

    /* Do not write synchronously: at window-capture time the clicked route has
       not necessarily reached the runtime yet. Microtask/timer execution occurs
       after p132/p130 route ownership has committed the authoritative route. */
    queueMicrotask(enforceRuntimeRoute);
    setTimeout(enforceRuntimeRoute, 0);
    routeEnforcementTimer = setInterval(enforceRuntimeRoute, 10);
    [40, 80, 120, 180, 240, 400, 700, 1000, 1300].forEach(delay => setTimeout(enforceRuntimeRoute, delay));
    setTimeout(() => {
      if (generation !== routeNavigationGeneration) return;
      enforceRuntimeRoute();
      if (routeEnforcementTimer) {
        clearInterval(routeEnforcementTimer);
        routeEnforcementTimer = 0;
      }
    }, 1500);
  }, true);

  const content = document.getElementById('content');
  if (!content) return;

  let p133InFlight = false;
  let reconcileTimer = 0;

  function isLibraryRoute() {
    /* Runtime route is authoritative after navigation. Only fall back to the
       hash during bootstrap before the route global is available. */
    const activeRoute = runtimeRoute('');
    if (activeRoute) return activeRoute === 'library';
    const hashRoute = new URLSearchParams(location.hash.replace(/^#/, '')).get('route');
    return hashRoute === 'library';
  }

  function p133Ready() {
    return !!window.MamMediaLibraryTrees && typeof window.MamMediaLibraryTrees.reload === 'function';
  }

  function hasP128LibrarySurface() {
    return !!content.querySelector('#p128LibraryHost,.p128-library-hero,.p128-filter-panel');
  }

  function hasP133LibrarySurface() {
    return !!content.querySelector('.p133-library');
  }

  async function reconcileLibrary(force = false) {
    if (!isLibraryRoute() || !p133Ready() || p133InFlight || hasP133LibrarySurface()) return;
    if (!force && !hasP128LibrarySurface()) return;

    p133InFlight = true;
    try {
      await window.MamMediaLibraryTrees.reload();
    } finally {
      p133InFlight = false;
    }
  }

  function scheduleReconcile(force = false) {
    clearTimeout(reconcileTimer);
    reconcileTimer = setTimeout(() => void reconcileLibrary(force), 0);
  }

  const observer = new MutationObserver(() => {
    if (!isLibraryRoute()) return;
    if (hasP128LibrarySurface() && !hasP133LibrarySurface()) scheduleReconcile(false);
  });

  observer.observe(content, { childList: true, subtree: true });

  function keepRuntimeShaInsideProductionCard() {
    const card = document.querySelector('.sidebar > .nonprod');
    const shaLine = document.getElementById('runtimeCommitSha');
    if (!card || !shaLine) return;

    card.style.setProperty('height', '92px', 'important');
    card.style.setProperty('bottom', '18px', 'important');
    card.style.setProperty('grid-template-rows', 'auto auto auto', 'important');
    card.style.setProperty('align-content', 'center', 'important');

    shaLine.style.setProperty('display', 'block', 'important');
    shaLine.style.setProperty('grid-column', '2', 'important');
    shaLine.style.setProperty('grid-row', '3', 'important');
    shaLine.style.setProperty('margin-top', '2px', 'important');
    shaLine.style.setProperty('font-family', 'Consolas, monospace', 'important');
    shaLine.style.setProperty('font-size', '9px', 'important');
    shaLine.style.setProperty('line-height', '1.2', 'important');
    shaLine.style.setProperty('color', '#F6D77B', 'important');
    shaLine.style.setProperty('white-space', 'nowrap', 'important');
    shaLine.style.setProperty('overflow', 'visible', 'important');
    shaLine.style.setProperty('text-overflow', 'clip', 'important');
  }

  keepRuntimeShaInsideProductionCard();
  setTimeout(keepRuntimeShaInsideProductionCard, 100);
  setTimeout(keepRuntimeShaInsideProductionCard, 500);

  // Direct navigation can arrive on #route=library before P133 is loaded.
  // Force one authoritative P133 render now that every UI layer is present.
  scheduleReconcile(true);

  window.mamFinalLibraryOwner = Object.freeze({
    version: 'p134-final-owner-2-route-authority',
    reconcile: () => reconcileLibrary(true),
    diagnose: () => ({
      route: runtimeRoute(new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '') || 'unknown',
      p133Ready: p133Ready(),
      p128Surface: hasP128LibrarySurface(),
      p133Surface: hasP133LibrarySurface(),
      p133InFlight
    })
  });
})();
