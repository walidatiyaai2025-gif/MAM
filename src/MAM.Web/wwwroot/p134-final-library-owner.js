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
    One explicit navigation intent owns both the runtime route and the URL for a
    short bounded transition. Legacy async renderers may write a stale hash or a
    stale route variable after the new page has already rendered. Guard History
    itself while the intent is active, and reject conflicting synthetic nav
    clicks during that same window. Real user clicks remain free to supersede the
    lock immediately.
  */
  const nativeReplaceState = History.prototype.replaceState;
  const nativePushState = History.prototype.pushState;
  let explicitRouteLock = null;
  let routeNavigationGeneration = 0;
  let routeEnforcementTimer = 0;

  function activeLock() {
    const lock = explicitRouteLock;
    if (!lock) return null;
    if (performance.now() > lock.expiresAt) {
      explicitRouteLock = null;
      return null;
    }
    return lock;
  }

  function guardHistoryUrl(value) {
    const lock = activeLock();
    if (!lock || value === null || value === undefined || value === '') return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin !== location.origin || url.pathname !== location.pathname) return value;
      const state = new URLSearchParams(url.hash.replace(/^#/, ''));
      state.set('route', lock.route);
      url.hash = state.toString();
      url.searchParams.set('lang', 'ar');
      return url.href;
    } catch {
      return value;
    }
  }

  History.prototype.replaceState = function (state, title, url) {
    return nativeReplaceState.call(this, state, title, guardHistoryUrl(url));
  };
  History.prototype.pushState = function (state, title, url) {
    return nativePushState.call(this, state, title, guardHistoryUrl(url));
  };

  function runtimeRoute(fallback = '') {
    const lock = activeLock();
    if (lock?.route) return lock.route;
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

    const currentLock = activeLock();
    if (currentLock && !event.isTrusted && clickedRoute !== currentLock.route) {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      return;
    }

    const generation = ++routeNavigationGeneration;
    explicitRouteLock = {
      route: clickedRoute,
      generation,
      expiresAt: performance.now() + 1600
    };

    const enforceExplicitRoute = () => {
      const lock = activeLock();
      if (!lock || lock.generation !== generation || generation !== routeNavigationGeneration) return;
      try {
        if (typeof route !== 'undefined' && route !== lock.route) route = lock.route;
      } catch { }
      try {
        const url = new URL(location.href);
        const state = new URLSearchParams(url.hash.replace(/^#/, ''));
        state.set('route', lock.route);
        url.hash = state.toString();
        url.searchParams.set('lang', 'ar');
        nativeReplaceState.call(history, history.state, '', url.href);
        localStorage.setItem('mam.p127.route', lock.route);
      } catch { }
    };

    if (routeEnforcementTimer) {
      clearInterval(routeEnforcementTimer);
      routeEnforcementTimer = 0;
    }

    /* The URL may be corrected immediately, while route-global enforcement is
       repeated after the event to defeat late stale assignments deterministically. */
    enforceExplicitRoute();
    queueMicrotask(enforceExplicitRoute);
    setTimeout(enforceExplicitRoute, 0);
    routeEnforcementTimer = setInterval(enforceExplicitRoute, 10);
    [40, 80, 120, 180, 240, 400, 700, 1000, 1300].forEach(delay => setTimeout(enforceExplicitRoute, delay));
    setTimeout(() => {
      const lock = explicitRouteLock;
      if (!lock || lock.generation !== generation) return;
      enforceExplicitRoute();
      if (routeEnforcementTimer) {
        clearInterval(routeEnforcementTimer);
        routeEnforcementTimer = 0;
      }
      explicitRouteLock = null;
    }, 1600);
  }, true);

  const content = document.getElementById('content');
  if (!content) return;

  let p133InFlight = false;
  let reconcileTimer = 0;

  function isLibraryRoute() {
    const lock = activeLock();
    if (lock) return lock.route === 'library';
    try {
      if (typeof route !== 'undefined') {
        const value = String(route || '').trim();
        if (value) return value === 'library';
      }
    } catch { }
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
    version: 'p134-final-owner-3-explicit-route-lock',
    reconcile: () => reconcileLibrary(true),
    diagnose: () => ({
      route: runtimeRoute(new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '') || 'unknown',
      lockedRoute: activeLock()?.route || '',
      p133Ready: p133Ready(),
      p128Surface: hasP128LibrarySurface(),
      p133Surface: hasP133LibrarySurface(),
      p133InFlight
    })
  });
})();
