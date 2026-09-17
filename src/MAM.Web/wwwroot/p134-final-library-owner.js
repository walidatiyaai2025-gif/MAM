(() => {
  'use strict';

  const content = document.getElementById('content');
  if (!content) return;

  let p133InFlight = false;
  let reconcileTimer = 0;

  function isLibraryRoute() {
    try {
      if (typeof route !== 'undefined') return route === 'library';
    } catch {}
    return new URLSearchParams(location.hash.replace(/^#/, '')).get('route') === 'library';
  }

  function preferReferenceLibrary() {
    // P135 makes the screenshot-matched grid/list library the default. The
    // organization trees are opt-in tabs inside the same Media Library page.
    return window.__mamP135PreferReferenceLibrary !== false;
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
    if (preferReferenceLibrary()) return;
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
    if (!isLibraryRoute() || preferReferenceLibrary()) return;
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

  function loadP135() {
    if (document.querySelector('script[data-p135-loader]')) return;
    const script = document.createElement('script');
    script.src = '/p135-unified-experience.js?v=0.12.20-p135-1';
    script.async = false;
    script.dataset.p135Loader = '1';
    document.head.appendChild(script);
  }

  keepRuntimeShaInsideProductionCard();
  setTimeout(keepRuntimeShaInsideProductionCard, 100);
  setTimeout(keepRuntimeShaInsideProductionCard, 500);

  // Reference Media Library is now the default. P133 is reconciled only after
  // the user explicitly selects one of the organization tabs.
  if (!preferReferenceLibrary()) scheduleReconcile(true);
  loadP135();

  window.mamFinalLibraryOwner = Object.freeze({
    version: 'p134-final-owner-2',
    reconcile: () => reconcileLibrary(true),
    diagnose: () => ({
      route: isLibraryRoute() ? 'library' : 'other',
      defaultView: preferReferenceLibrary() ? 'all-media' : 'organization',
      p133Ready: p133Ready(),
      p128Surface: hasP128LibrarySurface(),
      p133Surface: hasP133LibrarySurface(),
      p133InFlight
    })
  });
})();