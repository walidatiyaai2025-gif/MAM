(() => {
  'use strict';

  /*
    Single final owner for language switching. This file is loaded immediately
    before p132. It intercepts a language-control click at window capture before
    legacy language handlers can run, applies the locale once, renders once, and
    restores the complete pre-click deep link with the new locale.

    It also bridges the lexical runtime locale to the unified P135 layer, which
    intentionally reads window.arabic, and keeps passive page-load/status states
    from being promoted into action popups while preserving their original inline
    visibility. Genuine action-state popups remain owned by P135.
  */
  const platformReplaceState = History.prototype.replaceState;
  let generation = 0;

  const languageControl = event => event.target instanceof Element
    ? event.target.closest('[data-p128-language],#languageButton')
    : null;

  function authoritativeLanguage() {
    return document.documentElement.lang === 'ar' ? 'ar' : 'en';
  }

  function syncUnifiedLocaleBridge() {
    try { window.arabic = authoritativeLanguage() === 'ar'; } catch { }
  }

  syncUnifiedLocaleBridge();

  const actionStateSelector = '#p04ActionState,#p04QueueActionState,[id$="ActionState"],#p133MutationState';
  const passiveDisplay = new WeakMap();
  const normalize = value => String(value || '').replace(/\s+/g, ' ').trim();

  function rememberPassiveStates(root = document) {
    const states = root.querySelectorAll?.('#content .state') || [];
    for (const state of states) {
      if (!(state instanceof HTMLElement) || state.closest(actionStateSelector)) continue;
      if (!passiveDisplay.has(state)) passiveDisplay.set(state, state.style.display);
    }
  }

  function reconcilePassivePromotion() {
    rememberPassiveStates();
    const passiveMessages = new Set();

    document.querySelectorAll('#content .state[data-mam-popup-promoted="1"]').forEach(state => {
      if (!(state instanceof HTMLElement) || state.closest(actionStateSelector)) return;
      const message = normalize(state.textContent);
      if (message) passiveMessages.add(message);
      if (passiveDisplay.has(state)) state.style.display = passiveDisplay.get(state) || '';
    });

    if (!passiveMessages.size) return;
    document.querySelectorAll('#mamPopupStack .mam-popup').forEach(popup => {
      const message = normalize(popup.querySelector('.mam-popup-copy p')?.textContent);
      if (message && passiveMessages.has(message)) popup.remove();
    });

    const stack = document.getElementById('mamPopupStack');
    if (stack && !stack.children.length) stack.remove();
  }

  rememberPassiveStates();
  const passiveObserver = new MutationObserver(() => {
    rememberPassiveStates();
    queueMicrotask(reconcilePassivePromotion);
  });
  passiveObserver.observe(document.body, { childList: true, subtree: true, characterData: true });
  queueMicrotask(reconcilePassivePromotion);
  setTimeout(reconcilePassivePromotion, 0);

  function restore(snapshot, expectedGeneration) {
    if (expectedGeneration !== generation) return;
    try {
      const language = authoritativeLanguage();
      syncUnifiedLocaleBridge();
      const url = new URL(snapshot.href);
      url.searchParams.set('lang', language);
      platformReplaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.language', language);
      localStorage.setItem('mam.p128.language.initialized', '1');
    } catch { }
  }

  window.addEventListener('click', event => {
    const control = languageControl(event);
    if (!control) return;

    const snapshot = new URL(location.href);
    const currentGeneration = ++generation;

    /* Own this interaction completely. p132 and the legacy control handler must
       not also toggle or write language state for the same click. */
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();

    try {
      arabic = !arabic;
      localStorage.setItem('mam.p128.language.initialized', '1');
      if (typeof render === 'function') render();
      syncUnifiedLocaleBridge();
    } catch { }

    const enforce = () => {
      restore(snapshot, currentGeneration);
      reconcilePassivePromotion();
    };
    enforce();
    queueMicrotask(enforce);
    requestAnimationFrame(enforce);
    setTimeout(enforce, 0);
    setTimeout(enforce, 40);
    setTimeout(enforce, 90);
    setTimeout(enforce, 140);
    setTimeout(enforce, 240);
  }, true);
})();
