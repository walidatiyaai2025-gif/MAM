(() => {
  'use strict';

  /*
    Language controls are owned by legacy render layers that can rewrite the
    hash/query during the same click. Capture the full deep link at the control
    before its bubble handler runs, then restore it after the locale is applied.
    This file is loaded before p132 so this reference is the native History API.
  */
  const nativeReplaceState = history.replaceState.bind(history);

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
          const language = document.documentElement.lang === 'ar' ? 'ar' : 'en';
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
