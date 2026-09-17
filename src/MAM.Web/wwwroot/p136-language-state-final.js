(() => {
  'use strict';

  /*
    Single final owner for language switching. This file is loaded immediately
    before p132. It intercepts a language-control click at window capture before
    legacy language handlers can run, applies the locale once, renders once, and
    restores the complete pre-click deep link with the new locale.

    This deliberately leaves p132 as the navigation owner while preventing its
    older language-switch timers from participating in a language click at all.
  */
  const platformReplaceState = History.prototype.replaceState;
  let generation = 0;

  const languageControl = event => event.target instanceof Element
    ? event.target.closest('[data-p128-language],#languageButton')
    : null;

  function authoritativeLanguage() {
    return document.documentElement.lang === 'ar' ? 'ar' : 'en';
  }

  function restore(snapshot, expectedGeneration) {
    if (expectedGeneration !== generation) return;
    try {
      const language = authoritativeLanguage();
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
    } catch { }

    const enforce = () => restore(snapshot, currentGeneration);
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
