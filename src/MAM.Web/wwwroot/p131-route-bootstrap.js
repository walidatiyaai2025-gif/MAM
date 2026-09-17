(() => {
  'use strict';

  window.mamP131InitialHash = location.hash;

  const nativeReplaceState = History.prototype.replaceState;
  const nativePushState = History.prototype.pushState;
  let authoritative = new URLSearchParams(location.hash.replace(/^#/, ''));
  let guardUntil = authoritative.size ? performance.now() + 5000 : 0;

  function mergeAuthoritativeHash(source) {
    const state = new URLSearchParams(source);
    authoritative.forEach((entryValue, key) => {
      if (key === 'route' || !state.has(key)) state.set(key, entryValue);
    });
    return state;
  }

  function guardedUrl(value) {
    if (!value || !authoritative.size || performance.now() > guardUntil) return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin !== location.origin || url.pathname !== location.pathname) return value;
      url.hash = mergeAuthoritativeHash(new URLSearchParams(url.hash.replace(/^#/, ''))).toString();
      return url.href;
    } catch {
      return value;
    }
  }

  /*
    Guard the prototype itself, not only the history instance. Older runtime
    layers cache History.prototype.replaceState directly, so an instance-only
    wrapper can be bypassed. Loading this bootstrap from <head> makes every
    later direct or cached history writer pass through the same route invariant.
  */
  History.prototype.replaceState = function (state, title, url) {
    return nativeReplaceState.call(this, state, title, guardedUrl(url));
  };
  History.prototype.pushState = function (state, title, url) {
    return nativePushState.call(this, state, title, guardedUrl(url));
  };

  /*
    A direct location.hash assignment cannot be wrapped like replaceState.
    Reject a stale hash transition here, before later legacy hashchange handlers
    can copy the stale route back into runtime state.
  */
  window.addEventListener('hashchange', event => {
    if (!authoritative.size || performance.now() > guardUntil) return;
    const expectedRoute = authoritative.get('route');
    if (!expectedRoute) return;

    const current = new URLSearchParams(location.hash.replace(/^#/, ''));
    if (current.get('route') === expectedRoute) return;

    event.stopImmediatePropagation();
    event.stopPropagation();
    try {
      const url = new URL(location.href);
      url.hash = mergeAuthoritativeHash(current).toString();
      nativeReplaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.p127.route', expectedRoute);
    } catch { }
  }, true);

  function beginRouteNavigation(event) {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const routeKey = control?.dataset.route || '';
    if (!routeKey || routeKey === 'asset') return;

    const current = new URLSearchParams(location.hash.replace(/^#/, ''));
    authoritative.forEach((entryValue, key) => {
      if (!current.has(key)) current.set(key, entryValue);
    });
    current.set('route', routeKey);
    authoritative = current;
    guardUntil = performance.now() + 2000;
  }

  function releaseForExplicitStateChange(event) {
    if (!(event.target instanceof Element)) return;
    if (event.target.closest('#p128Apply,#p128Reset,#p128Grid,#p128List,#p128Prev,#p128Next,#p12SearchButton,[data-admin-tab],[data-tab],[data-p126-transcript]')) {
      guardUntil = 0;
    }
  }

  window.addEventListener('click', beginRouteNavigation, true);
  window.addEventListener('click', releaseForExplicitStateChange, true);
  window.addEventListener('change', event => {
    if (event.target instanceof Element && event.target.closest('#content')) guardUntil = 0;
  }, true);
})();
