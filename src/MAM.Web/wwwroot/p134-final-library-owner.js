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

  /* Legacy async renderers can write a stale pre-navigation hash after p132 has
     already rendered the requested page. Observe the route intent before p132
     consumes the click, then keep only the route key authoritative for a short,
     bounded transition window while preserving every other deep-link field. */
  const platformReplaceState = History.prototype.replaceState;
  let routeNavigationGeneration = 0;
  let routeEnforcementTimer = 0;

  function isInteractiveRouteControl(control) {
    if (!(control instanceof HTMLElement)) return false;
    if (control.hidden || control.disabled || control.getAttribute('aria-hidden') === 'true') return false;
    const style = getComputedStyle(control);
    if (style.display === 'none' || style.visibility === 'hidden' || style.pointerEvents === 'none') return false;
    const rect = control.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && rect.right > 0 && rect.bottom > 0 && rect.left < innerWidth && rect.top < innerHeight;
  }

  window.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const key = control?.dataset.route || '';
    if (!key || key === 'asset' || !isInteractiveRouteControl(control)) return;

    /* Reconciliation code can programmatically click stale/duplicate route
       controls. Only the currently interactive canonical control is allowed to
       become URL authority; otherwise UI and hash can diverge. */
    const canonical = [...document.querySelectorAll(`#nav [data-route="${CSS.escape(key)}"]`)]
      .find(candidate => isInteractiveRouteControl(candidate));
    if (canonical !== control) return;

    const generation = ++routeNavigationGeneration;
    const enforceRoute = () => {
      if (generation !== routeNavigationGeneration) return;
      try {
        const url = new URL(location.href);
        const state = new URLSearchParams(url.hash.replace(/^#/, ''));
        state.set('route', key);
        url.hash = state.toString();
        url.searchParams.set('lang', 'ar');
        platformReplaceState.call(history, history.state, '', url.href);
        localStorage.setItem('mam.p127.route', key);
      } catch { }
    };

    if (routeEnforcementTimer) {
      clearInterval(routeEnforcementTimer);
      routeEnforcementTimer = 0;
    }
    enforceRoute();
    queueMicrotask(enforceRoute);
    routeEnforcementTimer = setInterval(enforceRoute, 10);
    [0, 40, 80, 120, 180, 240, 400, 700, 1000, 1300].forEach(delay => setTimeout(enforceRoute, delay));
    setTimeout(() => {
      if (generation !== routeNavigationGeneration) return;
      enforceRoute();
      if (routeEnforcementTimer) {
        clearInterval(routeEnforcementTimer);
        routeEnforcementTimer = 0;
      }
    }, 1500);
  }, true);

  const content = document.getElementById('content');
  if (!content) return;

  let finalOwnerInFlight = false;
  let reconcileTimer = 0;

  function authoritativeRuntimeRoute() {
    try {
      const authority = window.mamRouteAuthority?.diagnose?.();
      if (authority?.navigationLockActive && authority.route) return String(authority.route);
    } catch { }
    try {
      if (typeof route !== 'undefined') {
        const value = String(route || '').trim();
        if (value) return value;
      }
    } catch { }
    return new URLSearchParams(location.hash.replace(/^#/, '')).get('route') || '';
  }

  function isLibraryRoute() {
    return authoritativeRuntimeRoute() === 'library';
  }

  function finalOwnerReady() {
    return !!window.mamAuthoritativeMediaLibrary &&
      typeof window.mamAuthoritativeMediaLibrary.render === 'function';
  }

  function hasFinalLibrarySurface() {
    const host = document.getElementById('p128LibraryHost');
    return !!host &&
      host.dataset.mamLibraryOwner === 'p140-authoritative-pagination' &&
      !!host.querySelector('[data-p140-final="1"],.state.loading');
  }

  async function reconcileLibrary(force = false) {
    if (!isLibraryRoute() || finalOwnerInFlight) return;
    if (!finalOwnerReady()) {
      if (force) scheduleReconcile(true, 40);
      return;
    }
    if (!force && hasFinalLibrarySurface() && !content.querySelector('.p133-library')) return;

    finalOwnerInFlight = true;
    try {
      await window.mamAuthoritativeMediaLibrary.render();
    } finally {
      finalOwnerInFlight = false;
    }
  }

  function scheduleReconcile(force = false, delay = 0) {
    clearTimeout(reconcileTimer);
    reconcileTimer = setTimeout(() => void reconcileLibrary(force), delay);
  }

  const observer = new MutationObserver(() => {
    if (!isLibraryRoute()) return;
    if (!hasFinalLibrarySurface() || content.querySelector('.p133-library')) scheduleReconcile(false);
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

  // P140 is the only Media Library renderer allowed to own the final surface.
  // P134 now acts purely as a repair coordinator and never invokes legacy P133.
  scheduleReconcile(true);

  window.mamFinalLibraryOwner = Object.freeze({
    version: 'p134-final-owner-3',
    reconcile: () => reconcileLibrary(true),
    diagnose: () => ({
      route: isLibraryRoute() ? 'library' : 'other',
      finalOwnerReady: finalOwnerReady(),
      finalSurface: hasFinalLibrarySurface(),
      legacyP133Surface: !!content.querySelector('.p133-library'),
      finalOwnerInFlight
    })
  });
})();
