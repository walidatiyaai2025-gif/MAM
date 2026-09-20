(() => {
  'use strict';

  let visualFile = null;
  let visualPreviewUrl = '';
  let visualMode = 'text';
  const VISUAL_MIN_SCORE = 0.90;

  const text = (en, ar) => (window.arabic ? ar : en);
  const safe = value => typeof window.esc === 'function' ? window.esc(value ?? '') : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const time = ms => typeof window.p12Time === 'function' ? window.p12Time(ms) : formatTime(ms);

  function formatTime(ms) {
    if (ms === null || ms === undefined) return '—';
    const total = Math.max(0, Math.floor(Number(ms) / 1000));
    const h = Math.floor(total / 3600), m = Math.floor((total % 3600) / 60), s = total % 60;
    return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
  }

  async function json(url, options = {}) {
    if (typeof window.p12Json === 'function') return window.p12Json(url, options);
    const response = await fetch(url, { headers: { Accept: 'application/json', ...(options.headers || {}) }, ...options });
    if (!response.ok) {
      const error = new Error(`HTTP ${response.status}`);
      error.status = response.status;
      try { error.payload = await response.json(); } catch {}
      throw error;
    }
    return response.status === 204 ? null : response.json();
  }

  function failure(error, fallbackEn, fallbackAr) {
    const detail = error?.payload?.detail || error?.payload?.error || text(fallbackEn, fallbackAr);
    if (typeof window.state === 'function') return window.state('error', text('Error','خطأ'), safe(detail));
    return `<div class="card"><strong>${safe(text('Error','خطأ'))}</strong><p>${safe(detail)}</p></div>`;
  }

  function addSearchModes() {
    const host = document.getElementById('p12SearchHost');
    if (!host || host.dataset.visualEnhanced === '1') return;
    host.dataset.visualEnhanced = '1';

    const textCard = host.firstElementChild;
    if (textCard) textCard.dataset.visualTextPanel = '1';
    const controls = document.createElement('div');
    controls.className = 'mam-visual-modebar';
    controls.innerHTML = `
      <button class="action" type="button" data-visual-mode="text" aria-pressed="true">${safe(text('Text Search','البحث بالنص'))}</button>
      <button class="action" type="button" data-visual-mode="image" aria-pressed="false">${safe(text('Search by Image','البحث بالصورة'))}</button>`;
    host.prepend(controls);

    const imagePanel = document.createElement('div');
    imagePanel.className = 'card';
    imagePanel.dataset.visualImagePanel = '1';
    imagePanel.hidden = true;
    imagePanel.innerHTML = `
      <div class="mam-visual-segments-header">
        <div><h3>${safe(text('Search by Image','البحث بالصورة'))}</h3><p>${safe(text('Drop or choose an image. MAM creates the visual query on the server and returns only media you are permitted to view.','اسحب أو اختر صورة. ينشئ النظام بصمة البحث على الخادم ويعرض فقط الوسائط المسموح لك بعرضها.'))}</p></div>
        <small>${safe(text('JPEG, PNG, BMP, GIF, TIFF or WebP · max 16 MB','JPEG أو PNG أو BMP أو GIF أو TIFF أو WebP · بحد أقصى 16 MB'))}</small>
      </div>
      <div class="mam-visual-policy" role="note">
        <strong>${safe(text('Image Search Policy','سياسة البحث بالصور'))}</strong>
        <span>${safe(text('Only matches with 90% similarity or higher are shown. Results below 90% are suppressed.','يتم عرض النتائج التي تبلغ نسبة تطابقها 90% أو أكثر فقط. أي نتيجة أقل من 90% لا يتم عرضها.'))}</span>
      </div>
      <div id="mamVisualDrop" class="mam-visual-drop" tabindex="0" role="button" aria-label="${safe(text('Choose query image','اختر صورة البحث'))}">
        <div id="mamVisualPreview" class="mam-visual-query-empty">${safe(text('No image selected','لم يتم اختيار صورة'))}</div>
        <div>
          <div class="mam-visual-actions">
            <input id="mamVisualFile" type="file" accept="image/jpeg,image/png,image/bmp,image/gif,image/tiff,image/webp" hidden />
            <button id="mamVisualChoose" class="action" type="button">${safe(text('Choose image','اختيار صورة'))}</button>
            <button id="mamVisualRun" class="action" type="button" disabled>${safe(text('Find similar media','ابحث عن وسائط مشابهة'))}</button>
            <button id="mamVisualClear" class="action" type="button" disabled>${safe(text('Clear','مسح'))}</button>
          </div>
          <p id="mamVisualFileInfo">${safe(text('You can also drag and drop an image here.','يمكنك أيضًا سحب الصورة وإفلاتها هنا.'))}</p>
          <div id="mamVisualState"></div>
        </div>
      </div>
      <div id="mamVisualResults"></div>`;
    host.appendChild(imagePanel);

    controls.querySelectorAll('[data-visual-mode]').forEach(button => button.addEventListener('click', () => switchMode(button.dataset.visualMode || 'text')));
    const input = document.getElementById('mamVisualFile');
    document.getElementById('mamVisualChoose')?.addEventListener('click', event => { event.stopPropagation(); input?.click(); });
    document.getElementById('mamVisualRun')?.addEventListener('click', event => { event.stopPropagation(); void runImageSearch(); });
    document.getElementById('mamVisualClear')?.addEventListener('click', event => { event.stopPropagation(); clearImage(); });
    input?.addEventListener('change', () => selectImage(input.files?.[0] || null));
    const drop = document.getElementById('mamVisualDrop');
    drop?.addEventListener('click', event => { if (event.target === drop) input?.click(); });
    drop?.addEventListener('keydown', event => { if ((event.key === 'Enter' || event.key === ' ') && event.target === drop) { event.preventDefault(); input?.click(); } });
    ['dragenter','dragover'].forEach(name => drop?.addEventListener(name, event => { event.preventDefault(); drop.classList.add('is-dragover'); }));
    ['dragleave','drop'].forEach(name => drop?.addEventListener(name, event => { event.preventDefault(); drop.classList.remove('is-dragover'); }));
    drop?.addEventListener('drop', event => selectImage(event.dataTransfer?.files?.[0] || null));
    switchMode(visualMode);
  }

  function switchMode(mode) {
    visualMode = mode === 'image' ? 'image' : 'text';
    const host = document.getElementById('p12SearchHost');
    if (!host) return;
    host.querySelectorAll('[data-visual-mode]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.visualMode === visualMode)));
    const textPanel = host.querySelector('[data-visual-text-panel]');
    const imagePanel = host.querySelector('[data-visual-image-panel]');
    if (textPanel) textPanel.hidden = visualMode !== 'text';
    if (imagePanel) imagePanel.hidden = visualMode !== 'image';
  }

  function selectImage(file) {
    const status = document.getElementById('mamVisualState');
    if (!file) { clearImage(); return; }
    const allowed = new Set(['image/jpeg','image/png','image/bmp','image/gif','image/tiff','image/webp']);
    if (!allowed.has((file.type || '').toLowerCase())) {
      if (status) status.innerHTML = failure({ payload: { detail: text('Choose a supported image file.','اختر ملف صورة مدعومًا.') } }, '', '');
      return;
    }
    if (file.size <= 0 || file.size > 16 * 1024 * 1024) {
      if (status) status.innerHTML = failure({ payload: { detail: text('The image must be between 1 byte and 16 MB.','يجب أن يكون حجم الصورة بين 1 بايت و16 ميجابايت.') } }, '', '');
      return;
    }
    if (visualPreviewUrl) URL.revokeObjectURL(visualPreviewUrl);
    visualFile = file;
    visualPreviewUrl = URL.createObjectURL(file);
    const preview = document.getElementById('mamVisualPreview');
    if (preview) { preview.className = ''; preview.innerHTML = `<img class="mam-visual-query-preview" src="${safe(visualPreviewUrl)}" alt="${safe(text('Query image preview','معاينة صورة البحث'))}" />`; }
    const info = document.getElementById('mamVisualFileInfo');
    if (info) info.textContent = `${file.name} · ${(file.size / 1024 / 1024).toFixed(2)} MB`;
    const run = document.getElementById('mamVisualRun'); if (run) run.disabled = false;
    const clear = document.getElementById('mamVisualClear'); if (clear) clear.disabled = false;
    if (status) status.innerHTML = '';
  }

  function clearImage() {
    if (visualPreviewUrl) URL.revokeObjectURL(visualPreviewUrl);
    visualPreviewUrl = '';
    visualFile = null;
    const input = document.getElementById('mamVisualFile'); if (input) input.value = '';
    const preview = document.getElementById('mamVisualPreview');
    if (preview) { preview.className = 'mam-visual-query-empty'; preview.textContent = text('No image selected','لم يتم اختيار صورة'); }
    const info = document.getElementById('mamVisualFileInfo'); if (info) info.textContent = text('You can also drag and drop an image here.','يمكنك أيضًا سحب الصورة وإفلاتها هنا.');
    const run = document.getElementById('mamVisualRun'); if (run) run.disabled = true;
    const clear = document.getElementById('mamVisualClear'); if (clear) clear.disabled = true;
    const results = document.getElementById('mamVisualResults'); if (results) results.innerHTML = '';
    const status = document.getElementById('mamVisualState'); if (status) status.innerHTML = '';
  }

  async function runImageSearch() {
    const status = document.getElementById('mamVisualState');
    const results = document.getElementById('mamVisualResults');
    if (!visualFile || !status || !results) return;
    status.innerHTML = typeof window.state === 'function' ? window.state('loading','Loading',text('Creating visual query and comparing indexed media…','جارٍ إنشاء بصمة الصورة ومقارنتها بالوسائط المفهرسة…')) : `<p>${safe(text('Searching…','جارٍ البحث…'))}</p>`;
    results.innerHTML = '';
    try {
      const response = await json('/client-api/discovery/image-search?limit=30', { method: 'POST', headers: { 'Content-Type': visualFile.type }, body: visualFile });
      const minimumScore = Math.max(VISUAL_MIN_SCORE, Number(response?.minimumScore ?? VISUAL_MIN_SCORE));
      const items = (Array.isArray(response?.items) ? response.items : [])
        .filter(item => Number(item?.score ?? 0) >= minimumScore);
      status.innerHTML = '';
      if (!items.length) {
        const threshold = `${Math.round(minimumScore * 100)}%`;
        results.innerHTML = typeof window.state === 'function'
          ? window.state('empty',text('No qualifying matches','لا توجد نتائج مؤهلة'),text(`No visual match reached the required ${threshold} similarity threshold.`,`لم تصل أي نتيجة إلى حد التطابق المطلوب ${threshold}.`))
          : `<p>${safe(text(`No visual match reached the required ${threshold} similarity threshold.`,`لم تصل أي نتيجة إلى حد التطابق المطلوب ${threshold}.`))}</p>`;
        return;
      }
      results.innerHTML = `<div class="mam-visual-results">${items.map(resultCard).join('')}</div>`;
      results.querySelectorAll('[data-visual-open]').forEach(button => button.addEventListener('click', () => openAsset(button.dataset.visualOpen || '', Number(button.dataset.visualSeek || 0))));
    } catch (error) {
      status.innerHTML = failure(error, 'Image search failed.', 'فشل البحث بالصورة.');
    }
  }

  function resultCard(item) {
    const hasSegmentThumb = item.hasThumbnail && item.segmentId;
    const image = hasSegmentThumb
      ? `<img loading="lazy" src="/client-api/discovery/assets/${encodeURIComponent(item.assetId)}/visual-segments/${encodeURIComponent(item.segmentId)}/thumbnail" alt="${safe(text('Matched segment thumbnail','صورة المقطع المطابق'))}" />`
      : `<span>${safe(item.mediaKind === 'Image' ? text('Indexed image','صورة مفهرسة') : text('Visual match','تطابق بصري'))}</span>`;
    const context = item.startMs !== null && item.startMs !== undefined
      ? `${time(item.startMs)} → ${time(item.endMs)}`
      : item.pageNumber ? `${text('Page','صفحة')} ${safe(item.pageNumber)}` : safe(item.sourceKind || 'visual');
    const score = `${Math.round(Number(item.score || 0) * 100)}%`;
    return `<article class="card mam-visual-result">
      <div class="mam-visual-result-media">${image}</div>
      <div class="mam-visual-result-body">
        <div class="mam-visual-segments-header"><strong>${safe(item.title)}</strong><span class="mam-visual-score">${score}</span></div>
        <div class="mam-visual-context">${safe(item.mediaKind)} · ${context}</div>
        <small>${safe(text('Model','النموذج'))}: ${safe(item.provider)} / ${safe(item.modelId)} v${safe(item.modelVersion)}</small>
        <div class="mam-visual-actions"><button type="button" class="action" data-visual-open="${safe(item.assetId)}" data-visual-seek="${safe(item.startMs ?? 0)}">${safe(item.startMs !== null && item.startMs !== undefined ? text('Open at match','فتح عند موضع التطابق') : text('Open asset','فتح الأصل'))}</button></div>
      </div></article>`;
  }

  async function attachVisualSegments(assetId, technical) {
    const host = document.getElementById('p12AssetDiscovery');
    if (!host || host.querySelector('[data-visual-segment-experience]')) return;
    const section = document.createElement('div');
    section.className = 'card';
    section.dataset.visualSegmentExperience = '1';
    section.innerHTML = `<div class="mam-visual-segments-header"><div><h3>${safe(text('Transcript & Visual Segments','التفريغ والمقاطع المرئية'))}</h3><p>${safe(text('Representative frames stay linked to their exact transcript time. Audio-only segments remain explicit without fake imagery.','تظل الصور الممثلة مرتبطة بالوقت الدقيق في التفريغ، بينما تظهر المقاطع الصوتية بوضوح دون صور وهمية.'))}</p></div><button class="action" type="button" data-visual-reindex>${safe(text('Rebuild visual index','إعادة بناء الفهرس المرئي'))}</button></div><div data-visual-segment-state></div><div data-visual-segment-list></div>`;
    host.appendChild(section);
    section.querySelector('[data-visual-reindex]')?.addEventListener('click', () => void reindex(assetId, section));
    await loadSegments(assetId, technical, section);
  }

  async function loadSegments(assetId, technical, section) {
    const status = section.querySelector('[data-visual-segment-state]');
    const list = section.querySelector('[data-visual-segment-list]');
    if (!list) return;
    const kind = String(technical?.mediaType || '').toLowerCase();
    const source = (kind === 'video' || kind === 'audio') ? 'transcript' : 'ocr';
    try {
      const segments = await json(`/client-api/discovery/assets/${encodeURIComponent(assetId)}/visual-segments?sourceKind=${encodeURIComponent(source)}`);
      if (!Array.isArray(segments) || !segments.length) {
        list.innerHTML = typeof window.state === 'function' ? window.state('empty',text('No segments','لا توجد مقاطع'),source === 'transcript' ? text('Create a timestamped transcript, then rebuild the visual index.','أنشئ تفريغًا زمنيًا ثم أعد بناء الفهرس المرئي.') : text('No indexed OCR segments are available.','لا توجد مقاطع OCR مفهرسة.')) : `<p>${safe(text('No segments.','لا توجد مقاطع.'))}</p>`;
        return;
      }
      if (status) status.innerHTML = '';
      list.innerHTML = `<div class="mam-visual-segments">${segments.map(segment => segmentCard(segment)).join('')}</div>`;
      list.querySelectorAll('[data-segment-seek]').forEach(button => button.addEventListener('click', () => seekCurrent(Number(button.dataset.segmentSeek || 0))));
    } catch (error) {
      if (error?.status === 404) { list.innerHTML = ''; return; }
      if (status) status.innerHTML = failure(error, 'Visual segments could not be loaded.', 'تعذر تحميل المقاطع المرئية.');
    }
  }

  function segmentCard(segment) {
    const visual = segment.hasThumbnail
      ? `<img loading="lazy" src="/client-api/discovery/assets/${encodeURIComponent(segment.assetId)}/visual-segments/${encodeURIComponent(segment.segmentId)}/thumbnail" alt="${safe(text('Representative segment frame','الصورة الممثلة للمقطع'))}" />`
      : `<span>${safe(segment.visualState === 'Unavailable' ? text('Audio / no visual frame','صوت / لا توجد صورة') : text('Thumbnail pending','الصورة قيد المعالجة'))}</span>`;
    const context = segment.startMs !== null && segment.startMs !== undefined ? `${time(segment.startMs)} → ${time(segment.endMs)}` : segment.pageNumber ? `${text('Page','صفحة')} ${safe(segment.pageNumber)}` : `#${safe(segment.segmentIndex + 1)}`;
    const canSeek = segment.startMs !== null && segment.startMs !== undefined;
    return `<article class="mam-visual-segment">
      <div class="mam-visual-segment-media">${visual}</div>
      <div><div class="mam-visual-segments-header"><strong>${context}</strong><span class="mam-visual-status" data-state="${safe(segment.visualState)}">${safe(localState(segment.visualState))}</span></div><p class="mam-visual-segment-text">${safe(segment.text || '')}</p>${segment.lastError ? `<small>${safe(segment.lastError)}</small>` : ''}${canSeek ? `<div class="mam-visual-actions"><button type="button" class="action" data-segment-seek="${safe(segment.startMs)}">${safe(text('Play from here','تشغيل من هنا'))}</button></div>` : ''}</div>
    </article>`;
  }

  function localState(value) {
    const states = {
      Ready: ['Ready','جاهز'], Pending: ['Pending','قيد المعالجة'], Failed: ['Failed','فشل'], Unavailable: ['No visual frame','لا توجد صورة']
    };
    const pair = states[value] || [value || 'Pending', value || 'قيد المعالجة'];
    return window.arabic ? pair[1] : pair[0];
  }

  async function reindex(assetId, section) {
    const status = section.querySelector('[data-visual-segment-state]');
    if (status) status.innerHTML = typeof window.state === 'function' ? window.state('loading','Loading',text('Queuing visual rebuild…','جارٍ إضافة إعادة البناء إلى قائمة المعالجة…')) : '';
    try {
      const result = await json(`/client-api/discovery/assets/${encodeURIComponent(assetId)}/visual-reindex`, { method: 'POST' });
      if (status) status.innerHTML = typeof window.state === 'function' ? window.state('empty',text('Queued','تمت الإضافة'), result?.state === 'Succeeded' ? text('Visual index was rebuilt locally.','تمت إعادة بناء الفهرس المرئي محليًا.') : text('Visual processing job was queued. Refresh after processing completes.','تمت إضافة مهمة المعالجة المرئية. حدّث الصفحة بعد اكتمالها.')) : '';
    } catch (error) {
      if (status) status.innerHTML = failure(error, 'Visual rebuild could not be queued.', 'تعذر إضافة إعادة البناء المرئي.');
    }
  }

  function seekCurrent(ms) {
    const media = document.querySelector('#content video, #content audio');
    if (!media) return;
    const apply = () => { try { media.currentTime = Math.max(0, ms / 1000); void media.play?.().catch?.(() => {}); } catch {} };
    if (media.readyState >= 1) apply(); else media.addEventListener('loadedmetadata', apply, { once: true });
  }

  function openAsset(assetId, seekMs) {
    if (!assetId) return;
    window.mamVisualPendingSeekMs = Number.isFinite(seekMs) ? seekMs : 0;
    if ('p12SelectedAssetId' in window) window.p12SelectedAssetId = assetId;
    else try { p12SelectedAssetId = assetId; } catch {}
    try { route = 'asset'; render(); } catch { location.hash = `asset=${encodeURIComponent(assetId)}`; }
    schedulePendingSeek();
  }

  function schedulePendingSeek() {
    let attempts = 0;
    const timer = setInterval(() => {
      attempts++;
      const media = document.querySelector('#content video, #content audio');
      if (media) {
        clearInterval(timer);
        const ms = Number(window.mamVisualPendingSeekMs || 0);
        const apply = () => { try { media.currentTime = Math.max(0, ms / 1000); } catch {} };
        if (media.readyState >= 1) apply(); else media.addEventListener('loadedmetadata', apply, { once: true });
      } else if (attempts >= 30) clearInterval(timer);
    }, 200);
  }

  function installHooks() {
    if (typeof window.p12LoadSearch === 'function' && !window.p12LoadSearch.__visualWrapped) {
      const previous = window.p12LoadSearch;
      const wrapped = async function(...args) { await previous.apply(this, args); addSearchModes(); };
      wrapped.__visualWrapped = true;
      window.p12LoadSearch = wrapped;
      try { p12LoadSearch = wrapped; } catch {}
    }
    if (typeof window.p12AttachAssetDiscovery === 'function' && !window.p12AttachAssetDiscovery.__visualWrapped) {
      const previous = window.p12AttachAssetDiscovery;
      const wrapped = async function(assetId, technical, ...rest) { await previous.call(this, assetId, technical, ...rest); await attachVisualSegments(assetId, technical); if (window.mamVisualPendingSeekMs !== undefined) schedulePendingSeek(); };
      wrapped.__visualWrapped = true;
      window.p12AttachAssetDiscovery = wrapped;
      try { p12AttachAssetDiscovery = wrapped; } catch {}
    }
    if (typeof window.render === 'function' && !window.render.__visualWrapped) {
      const previous = window.render;
      const wrapped = function(...args) { const result = previous.apply(this, args); setTimeout(() => { if (typeof route !== 'undefined' && route === 'search') addSearchModes(); }, 0); return result; };
      wrapped.__visualWrapped = true;
      window.render = wrapped;
      try { render = wrapped; } catch {}
    }
  }

  installHooks();
})();
