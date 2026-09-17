(() => {
  'use strict';

  /*
    Language controls are owned by legacy render layers that can rewrite the
    hash/query during the same click. This owner is loaded before p132 so it can
    preserve the platform History API and make every same-page replaceState
    converge on the locale that is actually applied to the DOM.
  */
  const nativeReplaceState = history.replaceState.bind(history);
  const currentLanguage = () => document.documentElement.lang === 'ar' ? 'ar' : 'en';

  function normalizeHistoryUrl(value) {
    if (value === null || value === undefined || value === '') return value;
    try {
      const url = new URL(String(value), location.href);
      if (url.origin === location.origin && url.pathname === location.pathname) {
        url.searchParams.set('lang', currentLanguage());
        return url.href;
      }
    } catch { }
    return value;
  }

  /*
    Install this before p132 captures replaceState. Any later legacy writer can
    still update route/filter state, but it cannot persist a locale that no
    longer matches the authoritative html[lang] state.
  */
  history.replaceState = function (state, title, url) {
    return nativeReplaceState(state, title, normalizeHistoryUrl(url));
  };

  function bindLanguageGuard(control) {
    if (!(control instanceof Element) || control.dataset.mamLanguageStateGuard === '1') return;
    control.dataset.mamLanguageStateGuard = '1';

    let snapshot = null;
    let generation = 0;

    control.addEventListener('click', () => {
      snapshot = new URL(location.href);
      generation += 1;
    }, true);

    control.addEventListener('click', () => {
      const captured = snapshot ? new URL(snapshot.href) : new URL(location.href);
      const currentGeneration = generation;
      const enforce = () => {
        if (currentGeneration !== generation) return;
        try {
          const language = currentLanguage();
          const restored = new URL(captured.href);
          restored.searchParams.set('lang', language);
          nativeReplaceState(history.state, '', restored);
          localStorage.setItem('mam.language', language);
        } catch { }
      };

      enforce();
      queueMicrotask(enforce);
      requestAnimationFrame(enforce);
      setTimeout(enforce, 0);
      setTimeout(enforce, 40);
      setTimeout(enforce, 80);
      setTimeout(enforce, 120);
      setTimeout(enforce, 180);
      setTimeout(enforce, 320);
      setTimeout(enforce, 700);
      setTimeout(enforce, 1250);
    });
  }

  function bindLanguageGuards() {
    document.querySelectorAll('[data-p128-language],#languageButton').forEach(bindLanguageGuard);
  }

  bindLanguageGuards();
  new MutationObserver(bindLanguageGuards).observe(document.body, { childList: true, subtree: true });
})();
