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
  const platformPushState = History.prototype.pushState;
  let generation = 0;
  let activeLanguageTarget = '';
  let activeLanguageSnapshot = null;

  const languageControl = event => event.target instanceof Element
    ? event.target.closest('[data-p128-language],#languageButton')
    : null;

  function authoritativeLanguage() {
    return document.documentElement.lang === 'ar' ? 'ar' : 'en';
  }

  function syncUnifiedLocaleBridge(language = authoritativeLanguage()) {
    try { window.arabic = language === 'ar'; } catch { }
  }

  function frozenLanguageUrl(snapshot = activeLanguageSnapshot, language = activeLanguageTarget) {
    if (!snapshot || (language !== 'ar' && language !== 'en')) return null;
    const url = new URL(snapshot.href);
    url.searchParams.set('lang', language);
    return url.href;
  }

  function canonicalizeCurrentLocaleUrl(language = authoritativeLanguage()) {
    try {
      const url = new URL(location.href);
      if (url.searchParams.get('lang') === language) return;
      url.searchParams.set('lang', language);
      platformReplaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.language', language);
    } catch { }
  }

  /*
    p132 is loaded after this file and captures history.replaceState as its
    native writer. Guard both history writers here first so later/legacy owners
    cannot replace the target locale or drop the user's deep-link state while a
    language transition is active.
  */
  history.replaceState = function (state, title, url) {
    const frozen = frozenLanguageUrl();
    return platformReplaceState.call(history, state, title, frozen ?? url);
  };
  history.pushState = function (state, title, url) {
    const frozen = frozenLanguageUrl();
    return platformPushState.call(history, state, title, frozen ?? url);
  };

  syncUnifiedLocaleBridge();
  canonicalizeCurrentLocaleUrl();

  /*
    Older render layers can write a stale ?lang= value before their DOM locale
    reconciliation finishes. Observe the authoritative html lang/dir instead of
    guessing writer timing. The correction is queued so every legacy mutation
    observer for the same DOM change runs first; the final owner then makes the
    URL agree with the language actually rendered on screen while preserving all
    other query/hash deep-link state.
  */
  const localeObserver = new MutationObserver(mutations => {
    if (!mutations.some(mutation => mutation.type === 'attributes' &&
      (mutation.attributeName === 'lang' || mutation.attributeName === 'dir'))) return;
    const reconcile = () => {
      const language = activeLanguageTarget === 'ar' || activeLanguageTarget === 'en'
        ? activeLanguageTarget
        : authoritativeLanguage();
      syncUnifiedLocaleBridge(language);
      canonicalizeCurrentLocaleUrl(language);
    };
    queueMicrotask(reconcile);
    requestAnimationFrame(reconcile);
    setTimeout(reconcile, 0);
    setTimeout(reconcile, 40);
    setTimeout(reconcile, 100);
    setTimeout(reconcile, 180);
  });
  localeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['lang', 'dir']
  });

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

  function restore(snapshot, expectedGeneration, language) {
    if (expectedGeneration !== generation) return;
    try {
      syncUnifiedLocaleBridge(language);
      const url = new URL(snapshot.href);
      url.searchParams.set('lang', language);
      /* Use the platform primitive directly. Older runtime layers captured their
         own History writers before the final owner existed; the final owner must
         therefore be able to correct the authoritative URL without re-entering
         any legacy wrapper. */
      platformReplaceState.call(history, history.state, '', url.href);
      localStorage.setItem('mam.language', language);
      localStorage.setItem('mam.p128.language.initialized', '1');
    } catch { }
  }

  window.addEventListener('click', event => {
    const control = languageControl(event);
    if (!control) return;

    const snapshot = new URL(location.href);
    const currentLanguage = authoritativeLanguage();
    const targetLanguage = currentLanguage === 'ar' ? 'en' : 'ar';
    const currentGeneration = ++generation;
    const transitionStartedAt = performance.now();
    activeLanguageSnapshot = snapshot;
    activeLanguageTarget = targetLanguage;

    /* Own this interaction completely. p132 and the legacy control handler must
       not also toggle or write language state for the same click. */
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();

    try {
      syncUnifiedLocaleBridge(targetLanguage);
      arabic = targetLanguage === 'ar';
      localStorage.setItem('mam.p128.language.initialized', '1');
      if (typeof render === 'function') render();
      syncUnifiedLocaleBridge(targetLanguage);
    } catch { }

    const enforce = () => {
      restore(snapshot, currentGeneration, targetLanguage);
      reconcilePassivePromotion();
    };

    /* Legacy owners can complete on different microtask/timer frames after a
       render. Reassert the immutable pre-click deep link + target locale every
       animation frame through the transition window instead of relying on a
       handful of timing guesses. */
    const enforceFrame = now => {
      if (currentGeneration !== generation) return;
      enforce();
      if (now - transitionStartedAt < 800) requestAnimationFrame(enforceFrame);
    };

    enforce();
    queueMicrotask(enforce);
    requestAnimationFrame(enforceFrame);
    setTimeout(enforce, 0);
    setTimeout(enforce, 240);
    setTimeout(enforce, 500);
    setTimeout(enforce, 900);
    setTimeout(() => {
      if (currentGeneration !== generation) return;
      enforce();
      activeLanguageSnapshot = null;
      activeLanguageTarget = '';
      syncUnifiedLocaleBridge();
      canonicalizeCurrentLocaleUrl();
    }, 1400);
  }, true);
})();
