(() => {
  'use strict';

  window.mamP131InitialHash = location.hash;

  const initialHashState = new URLSearchParams(window.mamP131InitialHash.replace(/^#/, ''));
  const bootstrapDeepLinkState = new Map(
    [...initialHashState.entries()].filter(([key]) => key !== 'route')
  );
  let bootstrapDeepLinkUntil = bootstrapDeepLinkState.size ? performance.now() + 5000 : 0;

  const nativeReplaceState = History.prototype.replaceState;
  const nativePushState = History.prototype.pushState;
  let authoritativeRoute = new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  let guardUntil = authoritativeRoute ? performance.now() + 5000 : 0;
  let navigationLockUntil = 0;
  let routeGeneration = 0;
  let routeEnforcementTimer = 0;
  let instanceGuardInstalled = false;
  let lastHistoryWrite = null;

  function guardActive() {
    return !!authoritativeRoute && performance.now() <= guardUntil;
  }

  function navigationLockActive() {
    return !!authoritativeRoute && performance.now() <= navigationLockUntil;
  }

  function bootstrapDeepLinkActive() {
    return bootstrapDeepLinkState.size > 0 && performance.now() <= bootstrapDeepLinkUntil;
  }

  function releaseBootstrapDeepLinkAuthority() {
    bootstrapDeepLinkUntil = 0;
    bootstrapDeepLinkState.clear();
  }

  function guardedUrl(value) {
    if (!value || (!guardActive() && !bootstrapDeepLinkActive())) return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin !== location.origin || url.pathname !== location.pathname) return value;
      const state = new URLSearchParams(url.hash.replace(/^#/, ''));

      if (guardActive()) state.set('route', authoritativeRoute);

      /*
        During initial hydration, the incoming deep link is authoritative.
        Late Library/bootstrap writers may normalize their own state, but they
        must not erase or rewrite caller-supplied hash fields before the user
        explicitly changes content state or navigates to another route.
      */
      if (bootstrapDeepLinkActive()) {
        for (const [key, initialValue] of bootstrapDeepLinkState) {
          state.set(key, initialValue);
        }
      }

      url.hash = state.toString();
      return url.href;
    } catch {
      return value;
    }
  }

  function replaceCanonicalState(state, title, value) {
    const input = value === null || value === undefined ? value : String(value);
    const guardWasActive = guardActive();
    const routeAtWrite = authoritativeRoute;
    const before = location.href;
    const canonical = guardedUrl(value);
    const result = nativeReplaceState.call(history, state, title, canonical);
    const afterNative = location.href;
    let repairUrl = null;

    /*
      Never delegate route authority to a later legacy history wrapper. A stale
      wrapper can accept the canonical URL and then synchronously restore its own
      pre-navigation hash. Verify the browser location before returning so an
      explicit route transition is synchronous and observable to callers.
    */
    if (guardActive()) {
      try {
        const actual = new URLSearchParams(location.hash.replace(/^#/, ''));
        if (actual.get('route') !== authoritativeRoute) {
          const repaired = new URL(location.href);
          actual.set('route', authoritativeRoute);
          repaired.hash = actual.toString();
          repairUrl = repaired.href;
          nativeReplaceState.call(history, state, title, repaired.href);

          /*
            Chromium can occasionally leave the visible same-document fragment
            unchanged even though replaceState returned normally. If the native
            repair did not move the authoritative route, use fragment-safe
            location.replace as a final synchronous repair. Restrict this to the
            exact same origin/path/query so it can never turn route authority
            into a document reload or cross-document navigation.
          */
          const afterNativeRepair = new URL(location.href);
          const afterNativeState = new URLSearchParams(afterNativeRepair.hash.replace(/^#/, ''));
          if (afterNativeState.get('route') !== authoritativeRoute &&
              afterNativeRepair.origin === repaired.origin &&
              afterNativeRepair.pathname === repaired.pathname &&
              afterNativeRepair.search === repaired.search) {
            location.replace(repaired.href);
          }
        }
      } catch { }
    }

    lastHistoryWrite = {
      input,
      canonical: canonical === null || canonical === undefined ? canonical : String(canonical),
      before,
      afterNative,
      repairUrl,
      afterRepair: location.href,
      guardWasActive,
      guardActiveAfter: guardActive(),
      routeAtWrite,
      authoritativeRoute
    };

    return result;
  }

  /*
    Own the prototype before every legacy runtime layer loads. Some layers cache
    History.prototype.replaceState directly, so guarding only history.replaceState
    is bypassable. During a route transition we protect only the route key; all
    filters, tabs and other deep-link fields remain free to change.
  */
  History.prototype.replaceState = function (state, title, url) {
    return replaceCanonicalState(state, title, url);
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

    /*
      Once an explicit route transition begins, the bootstrap authority becomes
      the final history writer for the lifetime of this document. Do not compose
      around or defer to a later language/navigation wrapper: those wrappers may
      legitimately carry stale pre-navigation state. The writer remains dynamic
      because it reads authoritativeRoute on every call, so later user route
      changes continue to work without replacing the function again.
    */
    const finalRouteWriter = function (state, title, url) {
      return replaceCanonicalState(state, title, url);
    };
    Object.defineProperty(finalRouteWriter, '__mamRouteAuthorityFinal', {
      value: true,
      configurable: false,
      enumerable: false,
      writable: false
    });
    Object.defineProperty(finalRouteWriter, '__mamRouteAuthorityOwner', {
      value: 'p131-route-authority-7',
      configurable: false,
      enumerable: false,
      writable: false
    });

    /*
      Seal both lookup paths. Packaged Chromium runs can observe History through
      a different wrapper path than source-run acceptance. Locking only the
      instance leaves a prototype lookup able to bypass route canonicalization.
      The final writer is route-dynamic, so sealing it does not freeze navigation.
    */
    try {
      Object.defineProperty(History.prototype, 'replaceState', {
        value: finalRouteWriter,
        configurable: false,
        enumerable: false,
        writable: false
      });
    } catch { }

    try {
      Object.defineProperty(history, 'replaceState', {
        value: finalRouteWriter,
        configurable: false,
        enumerable: false,
        writable: false
      });
    } catch {
      try { history.replaceState = finalRouteWriter; } catch { }
    }

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

    if (routeKey !== authoritativeRoute) releaseBootstrapDeepLinkAuthority();

    authoritativeRoute = routeKey;
    navigationLockUntil = performance.now() + 2500;
    guardUntil = navigationLockUntil;
    routeGeneration += 1;
    installInstanceHistoryGuard();
    scheduleRouteEnforcement(routeGeneration);
    try { localStorage.setItem('mam.p127.route', routeKey); } catch { }
  }

  window.addEventListener('click', beginRouteNavigation, true);

  const releaseOnTrustedContentInteraction = event => {
    if (!bootstrapDeepLinkActive() || !event.isTrusted || !(event.target instanceof Element)) return;
    if (event.target.closest('#nav,[data-p128-language],#languageButton')) return;
    if (event.target.closest('#content,main,form,input,select,textarea,button')) {
      releaseBootstrapDeepLinkAuthority();
    }
  };
  window.addEventListener('click', releaseOnTrustedContentInteraction, true);
  window.addEventListener('input', releaseOnTrustedContentInteraction, true);
  window.addEventListener('change', releaseOnTrustedContentInteraction, true);

  window.mamRouteAuthority = Object.freeze({
    version: 'p131-route-authority-7',
    canonicalizeUrl: value => guardedUrl(value),
    replaceState: (state, title, value) =>
      replaceCanonicalState(state, title, value),
    enforce: () => enforceAuthoritativeRoute(routeGeneration),
    diagnose: () => ({
      route: authoritativeRoute,
      guardActive: guardActive(),
      navigationLockActive: navigationLockActive(),
      generation: routeGeneration,
      instanceGuardInstalled,
      bootstrapDeepLinkActive: bootstrapDeepLinkActive(),
      bootstrapDeepLink: Object.fromEntries(bootstrapDeepLinkState),
      historyWriterFinal: history.replaceState?.__mamRouteAuthorityFinal === true,
      historyWriterOwner: history.replaceState?.__mamRouteAuthorityOwner || '',
      lastHistoryWrite
    })
  });
})();
