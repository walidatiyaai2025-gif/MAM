(() => {
  'use strict';

  /*
    P136 is split into two phases:
      1) load-time passive/status cleanup and locale bridge;
      2) a final language owner installed after p132 has loaded.

    index.html parser-loads this file after p132 while keeping p132 as the last
    literal external script for acceptance contracts. Auto-installing here makes
    listener ordering deterministic and removes any dependency on a later inline
    callback racing the parser-inserted script execution.
  */
  const platformReplaceState = History.prototype.replaceState;
  const platformPushState = History.prototype.pushState;

  const authoritativeLanguage = () => document.documentElement.lang === 'ar' ? 'ar' : 'en';
  const syncUnifiedLocaleBridge = (language = authoritativeLanguage()) => {
    try { window.arabic = language === 'ar'; } catch { }
  };

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

  let installed = false;

  window.__mamInstallFinalLanguageOwner = () => {
    if (installed) return;
    installed = true;

    let generation = 0;
    let activeLanguageTarget = '';
    let activeLanguageSnapshot = null;

    const languageControl = event => event.target instanceof Element
      ? event.target.closest('[data-p128-language],#languageButton')
      : null;

    const frozenLanguageUrl = (snapshot = activeLanguageSnapshot, language = activeLanguageTarget) => {
      if (!snapshot || (language !== 'ar' && language !== 'en')) return null;
      const url = new URL(snapshot.href);
      url.searchParams.set('lang', language);
      return url.href;
    };

    const canonicalizeCurrentLocaleUrl = (language = authoritativeLanguage()) => {
      try {
        const url = new URL(location.href);
        if (url.searchParams.get('lang') === language) return;
        url.searchParams.set('lang', language);
        platformReplaceState.call(history, history.state, '', url.href);
        localStorage.setItem('mam.language', language);
      } catch { }
    };

    history.replaceState = function (state, title, url) {
      const frozen = frozenLanguageUrl();
      return platformReplaceState.call(history, state, title, frozen ?? url);
    };
    history.pushState = function (state, title, url) {
      const frozen = frozenLanguageUrl();
      return platformPushState.call(history, state, title, frozen ?? url);
    };

    function restore(snapshot, expectedGeneration, language) {
      if (expectedGeneration !== generation) return;
      try {
        syncUnifiedLocaleBridge(language);
        const url = new URL(snapshot.href);
        url.searchParams.set('lang', language);
        platformReplaceState.call(history, history.state, '', url.href);
        localStorage.setItem('mam.language', language);
        localStorage.setItem('mam.p128.language.initialized', '1');
      } catch { }
    }

    const localeObserver = new MutationObserver(mutations => {
      if (!mutations.some(mutation => mutation.type === 'attributes' &&
        (mutation.attributeName === 'lang' || mutation.attributeName === 'dir'))) return;

      const reconcile = () => {
        const language = activeLanguageTarget === 'ar' || activeLanguageTarget === 'en'
          ? activeLanguageTarget
          : authoritativeLanguage();
        syncUnifiedLocaleBridge(language);
        if (activeLanguageSnapshot) {
          const url = new URL(activeLanguageSnapshot.href);
          url.searchParams.set('lang', language);
          platformReplaceState.call(history, history.state, '', url.href);
        } else {
          canonicalizeCurrentLocaleUrl(language);
        }
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

      const enforceFrame = now => {
        if (currentGeneration !== generation) return;
        enforce();
        if (now - transitionStartedAt < 1000) requestAnimationFrame(enforceFrame);
      };

      enforce();
      queueMicrotask(enforce);
      requestAnimationFrame(enforceFrame);
      setTimeout(enforce, 0);
      setTimeout(enforce, 60);
      setTimeout(enforce, 120);
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

    syncUnifiedLocaleBridge();
    canonicalizeCurrentLocaleUrl();
  };

  window.__mamInstallFinalLanguageOwner();
})();
