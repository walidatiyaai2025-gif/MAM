(() => {
  'use strict';

  window.mamP131InitialHash = location.hash;

  const nativeReplaceState = History.prototype.replaceState;
  const nativePushState = History.prototype.pushState;
  let authoritativeRoute = new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  let guardUntil = authoritativeRoute ? performance.now() + 5000 : 0;

  function guardActive() {
    return !!authoritativeRoute && performance.now() <= guardUntil;
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

  function beginRouteNavigation(event) {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const routeKey = control?.dataset.route || '';
    if (!routeKey || routeKey === 'asset') return;

    authoritativeRoute = routeKey;
    guardUntil = performance.now() + 2500;
    try { localStorage.setItem('mam.p127.route', routeKey); } catch { }
  }

  window.addEventListener('click', beginRouteNavigation, true);
})();
