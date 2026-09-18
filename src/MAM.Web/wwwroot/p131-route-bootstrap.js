(() => {
  'use strict';

  window.mamP131InitialHash = location.hash;

  const nativeReplaceState = History.prototype.replaceState;
  const nativePushState = History.prototype.pushState;
  let authoritativeRoute = new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  let guardUntil = authoritativeRoute ? performance.now() + 5000 : 0;
  let navigationLockUntil = 0;
  let routeGeneration = 0;
  let routeEnforcementTimer = 0;
  let downstreamReplaceState = null;
  let instanceGuardInstalled = false;

  function guardActive() {
    return !!authoritativeRoute && performance.now() <= guardUntil;
  }

  function navigationLockActive() {
    return !!authoritativeRoute && performance.now() <= navigationLockUntil;
  }

  function guardedUrl(value) {
    if (!value || !guardActive()) return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin !== location.origin || url.pathname !== location.pathname) return value;
      const state = new URLSearchParams(url.hash.replace(/^#/, ''));
      state.set('route', authoritativeRoute);
      url.hash = state.toString();
      return url.href;
    } catch {
      return value;
    }
  }

  /*
    Own the prototype before every legacy runtime layer loads. Some layers cache
    History.prototype.replaceState directly, so guarding only history.replaceState
    is bypassable. During a route transition we protect only the route key; all
    filters, tabs and other deep-link fields remain free to change.
  */
  History.prototype.replaceState = function (state, title, url) {
    return nativeReplaceState.call(this, state, title, guardedUrl(url));
  };
  History.prototype.pushState = function (state, title, url) {
    return nativePushState.call(this, state, title, guardedUrl(url));
  };

  /*
    Direct location.hash writes cannot be intercepted synchronously. Reject a
    stale route on hashchange before later legacy handlers can copy it back into
    the runtime route variable. History-based writers are corrected synchronously
    by the prototype guards above.
  */
  window.addEventListener('hashchange', event => {
    if (!guardActive()) return;
    const current = new URLSearchParams(location.hash.replace(/^#/, ''));
    if (current.get('route') === authoritativeRoute) return;

    event.stopImmediatePropagation();
    event.stopPropagation();
    try {
      const url = new URL(location.href);
      current.set('route', authoritativeRoute);
      url.hash = current.toString();
      nativeReplaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.p127.route', authoritativeRoute);
    } catch { }
  }, true);

  function installInstanceHistoryGuard() {
    if (instanceGuardInstalled) return;
    downstreamReplaceState = history.replaceState.bind(history);
    history.replaceState = function (state, title, url) {
      return downstreamReplaceState(state, title, guardedUrl(url));
    };
    instanceGuardInstalled = true;
  }

  function enforceAuthoritativeRoute(generation = routeGeneration) {
    if (generation !== routeGeneration || !navigationLockActive()) return;

    try {
      if (typeof route !== 'undefined' && route !== authoritativeRoute) route = authoritativeRoute;
    } catch { }

    try {
      const url = new URL(location.href);
      const state = new URLSearchParams(url.hash.replace(/^#/, ''));
      if (state.get('route') !== authoritativeRoute) {
        state.set('route', authoritativeRoute);
        url.hash = state.toString();
        nativeReplaceState.call(history, history.state, '', url.href);
      }
      localStorage.setItem('mam.p127.route', authoritativeRoute);
    } catch { }
  }

  function scheduleRouteEnforcement(generation) {
    if (routeEnforcementTimer) {
      clearInterval(routeEnforcementTimer);
      routeEnforcementTimer = 0;
    }

    enforceAuthoritativeRoute(generation);
    queueMicrotask(() => enforceAuthoritativeRoute(generation));
    requestAnimationFrame(() => enforceAuthoritativeRoute(generation));

    routeEnforcementTimer = setInterval(() => {
      if (generation !== routeGeneration || !navigationLockActive()) {
        clearInterval(routeEnforcementTimer);
        routeEnforcementTimer = 0;
        return;
      }
      enforceAuthoritativeRoute(generation);
    }, 16);

    [40, 80, 120, 180, 240, 400, 700, 1000, 1400, 1800].forEach(delay => {
      setTimeout(() => enforceAuthoritativeRoute(generation), delay);
    });

    setTimeout(() => {
      enforceAuthoritativeRoute(generation);
      if (generation === routeGeneration && routeEnforcementTimer) {
        clearInterval(routeEnforcementTimer);
        routeEnforcementTimer = 0;
      }
    }, 2100);
  }

  function beginRouteNavigation(event) {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const routeKey = control?.dataset.route || '';
    if (!routeKey || routeKey === 'asset') return;

    /*
      A real user click may always supersede the current route. During the
      bounded transition, however, legacy reconciliation layers can emit
      synthetic clicks on stale/duplicate navigation controls. Reject only
      those conflicting synthetic clicks before any later listener can copy
      the stale route back into runtime or URL state.
    */
    if (navigationLockActive() && !event.isTrusted && routeKey !== authoritativeRoute) {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      enforceAuthoritativeRoute(routeGeneration);
      return;
    }

    authoritativeRoute = routeKey;
    navigationLockUntil = performance.now() + 2500;
    guardUntil = navigationLockUntil;
    routeGeneration += 1;
    installInstanceHistoryGuard();
    scheduleRouteEnforcement(routeGeneration);
    try { localStorage.setItem('mam.p127.route', routeKey); } catch { }
  }

  window.addEventListener('click', beginRouteNavigation, true);

  window.mamRouteAuthority = Object.freeze({
    version: 'p131-route-authority-2',
    diagnose: () => ({
      route: authoritativeRoute,
      guardActive: guardActive(),
      navigationLockActive: navigationLockActive(),
      generation: routeGeneration,
      instanceGuardInstalled
    })
  });
})();
