(() => {
  if (typeof p05BindAssetActions !== 'function') return;

  // Replace the legacy P05 asset-card binding because the original collection shortcut only
  // reloaded on HTTP success and swallowed transport errors. Keep the shortcut, but make its
  // Central API outcome explicit. Full collection selection/Add/Remove remains in Curation Actions.
  p05BindAssetActions = function () {
    content.querySelectorAll('[data-p05-edit]').forEach(button =>
      button.addEventListener('click', () => void p05OpenEditor(button.dataset.p05Edit)));

    content.querySelectorAll('[data-p05-add]').forEach(button => button.addEventListener('click', async () => {
      const toolbar = button.closest('.toolbar') || button.parentElement;
      let output = toolbar?.querySelector('[data-p12-legacy-collection-state]');
      if (!output && toolbar) {
        output = document.createElement('div');
        output.dataset.p12LegacyCollectionState = 'true';
        output.setAttribute('aria-live', 'polite');
        output.style.width = '100%';
        output.style.marginTop = '8px';
        toolbar.appendChild(output);
      }

      button.disabled = true;
      if (output) output.innerHTML = state('loading', 'Loading', arabic ? 'جاري إضافة الأصل للمجموعة…' : 'Adding asset to collection…');
      try {
        const response = await fetch(`/client-api/curation/collections/${button.dataset.p05Collection}/assets/${button.dataset.p05Add}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({ expectedVersion: Number(button.dataset.p05Version) })
        });
        if (!response.ok) {
          if (output) p05InlineFailure(output, response.status);
          return;
        }
        const updated = await response.json();
        if (output) output.innerHTML = state('empty', arabic ? 'تمت الإضافة' : 'Added', arabic ? `تمت الإضافة · إصدار المجموعة ${updated.version}` : `Added · collection version ${updated.version}`);
        setTimeout(() => { if (route === 'library') void p05LoadLibrary(); }, 450);
      } catch {
        if (output) output.innerHTML = state('error', 'API error', arabic ? 'تعذر إضافة الأصل للمجموعة.' : 'Asset could not be added to the collection.');
      } finally {
        button.disabled = false;
      }
    }));
  };
})();
