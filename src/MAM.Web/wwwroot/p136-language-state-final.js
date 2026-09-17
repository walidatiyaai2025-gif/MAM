(() => {
  'use strict';

  /*
    Final locale/deep-link owner. p132 intentionally runs first and may perform
    legacy pre-toggle URL writes. This script runs after p132 and reconciles the
    URL from deferred work scheduled in window capture, so it does not depend on
    the language-control click being allowed to bubble back to window.

    Use History.prototype.replaceState directly so no older instance wrapper can
    re-apply a stale locale. One page-wide generation cancels timers from older
    control instances when render layers recreate the profile-language action.
  */
  const platformReplaceState = History.prototype.replaceState;
  let languageGeneration = 0;

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

  /*
    p132's window-capture handler is registered before this one and preserves all
    non-language query/hash state. Capture that complete URL, then schedule only
    deferred reconciliation. Every deferred callback runs after the synchronous
    language handler/render has had the opportunity to apply html[lang]/dir.
  */
  window.addEventListener('click', event => {
    if (!isLanguageControl(event)) return;

    const snapshot = new URL(location.href);
    const generation = ++languageGeneration;
    const enforce = () => restore(snapshot, generation);

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
  }, true);
})();
