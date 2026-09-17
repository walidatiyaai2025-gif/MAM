(() => {
  'use strict';

  window.mamP131InitialHash = location.hash;

  const nativeReplaceState = history.replaceState.bind(history);
  const nativePushState = history.pushState.bind(history);
  let authoritative = new URLSearchParams(location.hash.replace(/^#/, ''));
  let guardUntil = authoritative.size ? performance.now() + 5000 : 0;

  function guardedUrl(value) {
    if (!value || !authoritative.size || performance.now() > guardUntil) return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin !== location.origin || url.pathname !== location.pathname) return value;
      const state = new URLSearchParams(url.hash.replace(/^#/, ''));
      authoritative.forEach((entryValue, key) => {
        if (key === 'route' || !state.has(key)) state.set(key, entryValue);
      });
      url.hash = state.toString();
      return url.href;
    } catch {
      return value;
    }
  }

  history.replaceState = function (state, title, url) {
    return nativeReplaceState(state, title, guardedUrl(url));
  };
  history.pushState = function (state, title, url) {
    return nativePushState(state, title, guardedUrl(url));
  };

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
