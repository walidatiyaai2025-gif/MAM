(() => {
  'use strict';

  /*
    Final locale/deep-link owner. p132 intentionally runs first and may perform
    legacy pre-toggle URL writes. This script runs after p132 and reconciles the
    URL only after the real language control has applied html[lang]/dir.

    Use History.prototype.replaceState directly so no older instance wrapper can
    re-apply a stale locale. One page-wide generation cancels timers from older
    control instances when render layers recreate the profile-language action.
  */
  const platformReplaceState = History.prototype.replaceState;
  let languageGeneration = 0;
  let languageSnapshot = null;

  const isLanguageControl = event => event.target instanceof Element &&
    !!event.target.closest('[data-p128-language],#languageButton');
  const currentLanguage = () => document.documentElement.lang === 'ar' ? 'ar' : 'en';

  function restore(snapshot, generation) {
    if (!snapshot || generation !== languageGeneration) return;
    try {
      const restored = new URL(snapshot.href);
      const language = currentLanguage();
      restored.searchParams.set('lang', language);
      platformReplaceState.call(history, history.state, '', restored.href);
      localStorage.setItem('mam.language', language);
    } catch { }
  }

  /* Capture after p132's capture handler. p132 preserves every non-language
     query/hash key, so this remains a complete deep-link snapshot. */
  window.addEventListener('click', event => {
    if (!isLanguageControl(event)) return;
    languageSnapshot = new URL(location.href);
    languageGeneration += 1;
  }, true);

  /* Bubble on window runs after the actual control handler and render(), so the
     DOM locale is authoritative here. Deferred reconciliations outlast legacy
     p132 timers while remaining cancelled by any newer language switch. */
  window.addEventListener('click', event => {
    if (!isLanguageControl(event)) return;
    const snapshot = languageSnapshot ? new URL(languageSnapshot.href) : new URL(location.href);
    const generation = languageGeneration;
    const enforce = () => restore(snapshot, generation);

    enforce();
    queueMicrotask(enforce);
    requestAnimationFrame(enforce);
    setTimeout(enforce, 0);
    setTimeout(enforce, 40);
    setTimeout(enforce, 90);
    setTimeout(enforce, 130);
    setTimeout(enforce, 200);
    setTimeout(enforce, 360);
    setTimeout(enforce, 760);
    setTimeout(enforce, 1280);
    setTimeout(enforce, 1400);
  });
})();
