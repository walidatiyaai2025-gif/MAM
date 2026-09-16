(() => {
  'use strict';

  function enhance(root = document) {
    root.querySelectorAll?.('.mam-visual-result:not([data-asset-thumb-enhanced])').forEach(card => {
      card.dataset.assetThumbEnhanced = '1';
      const media = card.querySelector('.mam-visual-result-media');
      const button = card.querySelector('[data-visual-open]');
      if (!media || !button || media.querySelector('img')) return;
      const assetId = button.dataset.visualOpen || '';
      if (!assetId) return;
      const fallback = media.innerHTML;
      const image = document.createElement('img');
      image.loading = 'lazy';
      image.alt = document.documentElement.dir === 'rtl' ? 'صورة مصغرة للأصل المطابق' : 'Matched asset thumbnail';
      image.src = `/client-api/discovery/assets/${encodeURIComponent(assetId)}/visual-thumbnail`;
      image.addEventListener('error', () => { media.innerHTML = fallback; }, { once: true });
      media.innerHTML = '';
      media.appendChild(image);
    });
  }

  const observer = new MutationObserver(records => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node.nodeType !== Node.ELEMENT_NODE) continue;
        enhance(node);
        if (node.matches?.('.mam-visual-result')) enhance(node.parentElement || document);
      }
    }
  });

  observer.observe(document.getElementById('content') || document.body, { childList: true, subtree: true });
  enhance();
})();
