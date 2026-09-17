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
  window.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;
    const control = event.target.closest('#nav [data-route]');
    const key = control?.dataset.route || '';
    if (!key || key === 'asset') return;

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

    queueMicrotask(enforceRoute);
    [0, 40, 80, 120, 180, 240, 400, 700, 1000].forEach(delay => setTimeout(enforceRoute, delay));
  }, true);

  const content = document.getElementById('content');
  if (!content) return;

  let p133InFlight = false;
  let reconcileTimer = 0;

  function isLibraryRoute() {
    const hashRoute = new URLSearchParams(location.hash.replace(/^#/, '')).get('route');
    if (hashRoute) return hashRoute === 'library';
    try {
      if (typeof route !== 'undefined') return route === 'library';
    } catch {}
    return false;
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
    version: 'p134-final-owner-1',
    reconcile: () => reconcileLibrary(true),
    diagnose: () => ({
      route: isLibraryRoute() ? 'library' : 'other',
      p133Ready: p133Ready(),
      p128Surface: hasP128LibrarySurface(),
      p133Surface: hasP133LibrarySurface(),
      p133InFlight
    })
  });
})();
