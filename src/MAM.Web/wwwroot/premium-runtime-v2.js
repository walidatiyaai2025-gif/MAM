(() => {
  'use strict';

  const nativeFetch = window.fetch.bind(window);
  let lastApiError = null;
  const h = value => typeof esc === 'function' ? esc(value) : String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const txt = value => value === null || value === undefined ? '' : String(value);
  const compact = (value, max = 1200) => {
    const normalized = txt(value).replace(/\s+/g, ' ').trim();
    return normalized.length > max ? `${normalized.slice(0, max)}…` : normalized;
  };

  function toastHost() {
    let host = document.getElementById('mamToastStack');
    if (!host) {
      host = document.createElement('div');
      host.id = 'mamToastStack';
      host.className = 'mam-toast-stack';
      document.body.appendChild(host);
    }
    return host;
  }

  function toast(kind, heading, detail) {
    const item = document.createElement('div');
    item.className = `mam-toast ${kind || 'info'}`;
    item.innerHTML = `<strong>${h(heading || '')}</strong><div>${h(detail || '')}</div>`;
    toastHost().appendChild(item);
    setTimeout(() => item.remove(), kind === 'error' ? 11000 : 4500);
  }
  window.mamToast = toast;

  async function readFailure(response) {
    let payload = null;
    try {
      const clone = response.clone();
      const type = clone.headers.get('content-type') || '';
      payload = type.includes('application/json') ? await clone.json() : await clone.text();
    } catch { }
    const obj = payload && typeof payload === 'object' ? payload : null;
    return {
      status: response.status,
      code: compact(obj?.error || obj?.code || ''),
      detail: compact(obj?.detail || obj?.message || (typeof payload === 'string' ? payload : '')),
      technicalDetail: compact(obj?.technicalDetail || ''),
      correlationId: compact(obj?.correlationId || response.headers.get('x-correlation-id') || ''),
      at: Date.now()
    };
  }

  function errorText(info = lastApiError, fallback = '') {
    if (!info) return fallback || (arabic ? 'حدث خطأ غير معروف.' : 'An unknown error occurred.');
    const parts = [];
    if (info.status) parts.push(`HTTP ${info.status}`);
    if (info.code) parts.push(info.code);
    if (info.detail) parts.push(info.detail);
    if (info.technicalDetail && info.technicalDetail !== info.detail) parts.push(info.technicalDetail);
    if (info.correlationId) parts.push(`Ref ${info.correlationId}`);
    return parts.length ? parts.join(' · ') : (fallback || 'Request failed');
  }
  window.mamApiErrorText = errorText;

  window.fetch = async (...args) => {
    try {
      const response = await nativeFetch(...args);
      if (!response.ok) {
        lastApiError = await readFailure(response);
        const method = String((args[1] || {}).method || 'GET').toUpperCase();
        if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method))
          toast('error', arabic ? 'فشل الإجراء' : 'Action failed', errorText());
      }
      return response;
    } catch (error) {
      lastApiError = { status: 0, code: 'network_error', detail: compact(error?.message || 'Network request failed.'), technicalDetail: '', correlationId: '', at: Date.now() };
      toast('error', arabic ? 'خطأ اتصال' : 'Network error', errorText());
      throw error;
    }
  };

  async function apiJson(url, options = {}) {
    const response = await fetch(url, { ...options, headers: { Accept: 'application/json', ...(options.headers || {}) } });
    if (!response.ok) {
      const ex = new Error(errorText(null, `HTTP ${response.status}`));
      ex.status = response.status;
      ex.info = lastApiError;
      throw ex;
    }
    if (response.status === 204) return null;
    return await response.json();
  }

  function failureHtml(heading, fallback) {
    return `<div class="state error"><strong>${h(heading)}</strong><br><span>${h(errorText(null, fallback))}</span></div>`;
  }

  function decorateElement(element) {
    if (!(element instanceof Element)) return;
    const decorate = root => {
      root.querySelectorAll?.('.action').forEach(item => item.classList.add('btn', 'btn-sm'));
      root.querySelectorAll?.('input:not([type="checkbox"]):not([type="radio"]):not([type="hidden"])').forEach(item => item.classList.add('form-control'));
      root.querySelectorAll?.('select').forEach(item => item.classList.add('form-select'));
      root.querySelectorAll?.('textarea').forEach(item => item.classList.add('form-control'));
      root.querySelectorAll?.('.table-wrap table').forEach(item => item.classList.add('table', 'table-hover', 'align-middle'));
    };
    decorate(element);
    upgradeLookups(element);
  }

  const observer = new MutationObserver(records => records.forEach(record => record.addedNodes.forEach(node => decorateElement(node))));
  observer.observe(document.body, { childList: true, subtree: true });

  function openAssetDetails(assetId) {
    p12SelectedAssetId = assetId || '';
    route = 'asset';
    render();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function deleteButton(asset) {
    const titleValue = arabic && asset.titleAr ? asset.titleAr : asset.title;
    return `<button class="action mam-btn-danger" data-mam-delete-asset="${h(asset.id)}" data-mam-delete-title="${h(titleValue)}"><i class="bi bi-trash3"></i> ${arabic ? 'حذف نهائي' : 'Delete permanently'}</button>`;
  }

  if (typeof p05AssetCard === 'function' && typeof p05Results === 'function' && typeof p05BindAssetActions === 'function') {
    p05AssetCard = function (asset, collections) {
      const titleValue = arabic && asset.titleAr ? asset.titleAr : asset.title;
      const tags = Array.isArray(asset.tags) ? asset.tags.slice(0, 8).join(' · ') : '';
      const collectionAction = Array.isArray(collections) && collections.length
        ? `<button class="action mam-btn-secondary" data-p05-add="${h(asset.id)}" data-p05-collection="${h(collections[0].collectionId)}" data-p05-version="${h(collections[0].version)}">${arabic ? 'أضف للمجموعة' : 'Add to collection'}</button>`
        : '';
      return `<div class="card mam-asset-card"><h3>${h(titleValue)}</h3><div class="mam-asset-id">${h(asset.id)}</div><p>v${h(asset.version)} · ${h(asset.lifecycle)} · ${h(asset.category || '—')}<br>${h(tags)}</p><div class="mam-asset-actions"><button class="action mam-btn-info" data-mam-open-asset="${h(asset.id)}"><i class="bi bi-box-arrow-up-right"></i> ${arabic ? 'تفاصيل الأصل' : 'Asset details'}</button><button class="action mam-btn-secondary" data-p05-edit="${h(asset.id)}"><i class="bi bi-pencil-square"></i> ${arabic ? 'تعديل البيانات' : 'Edit metadata'}</button>${collectionAction}${deleteButton(asset)}</div></div>`;
    };

    p05Results = function (result, collections) {
      const items = Array.isArray(result.items) ? result.items : [];
      if (!items.length) return state('empty', 'Empty', arabic ? 'لا توجد نتائج تطابق البحث والمرشحات الحالية.' : 'No assets match the current search and filters.');
      if (p05Grid) return `<div class="grid three">${items.map(asset => p05AssetCard(asset, collections)).join('')}</div>`;
      return `<div class="list">${items.map(asset => `<div class="row"><b>${h(String(asset.id).slice(0, 13))}</b><span><strong>${h(arabic && asset.titleAr ? asset.titleAr : asset.title)}</strong><br><small>${h(asset.id)}</small></span><span>v${h(asset.version)} · ${h(asset.lifecycle)} · ${h(asset.category || '—')}</span><span class="mam-asset-actions"><button class="action mam-btn-info" data-mam-open-asset="${h(asset.id)}">${arabic ? 'التفاصيل' : 'Details'}</button><button class="action mam-btn-secondary" data-p05-edit="${h(asset.id)}">${arabic ? 'تعديل' : 'Edit'}</button>${deleteButton(asset)}</span></div>`).join('')}</div>`;
    };

    const originalBindAssetActions = p05BindAssetActions;
    p05BindAssetActions = function (collections) {
      originalBindAssetActions(collections);
      content.querySelectorAll('[data-mam-open-asset]').forEach(button => button.addEventListener('click', () => openAssetDetails(button.dataset.mamOpenAsset)));
      content.querySelectorAll('[data-mam-delete-asset]').forEach(button => button.addEventListener('click', () => void confirmDeleteAsset(button.dataset.mamDeleteAsset, button.dataset.mamDeleteTitle || '')));
    };
  }

  function confirmDeleteAsset(assetId, titleValue) {
    document.getElementById('mamDeleteModal')?.remove();
    const backdrop = document.createElement('div');
    backdrop.id = 'mamDeleteModal';
    backdrop.className = 'mam-modal-backdrop';
    backdrop.innerHTML = `<div class="mam-modal" role="dialog" aria-modal="true"><div class="mam-modal-header"><h3>${arabic ? 'حذف الأصل نهائيًا' : 'Permanently delete asset'}</h3></div><div class="mam-modal-body"><p>${arabic ? 'سيتم حذف الأصل والمشتقات والنسخة الاحتياطية وOCR والتفريغ والفهرسة والتصنيفات والوسوم وسجلات التشغيل المرتبطة. يبقى سجل التدقيق فقط.' : 'This deletes the original, derivatives, Backup copy, OCR, transcript, indexes, categories, tags and related operational records. Only the immutable audit event is retained.'}</p><div class="mam-delete-summary"><strong>${h(titleValue || assetId)}</strong><br><code>${h(assetId)}</code></div><label>${arabic ? 'اكتب DELETE للتأكيد' : 'Type DELETE to confirm'}<input id="mamDeleteConfirmText" autocomplete="off" placeholder="DELETE" /></label></div><div class="mam-modal-footer"><button id="mamDeleteCancel" class="action mam-btn-secondary">${arabic ? 'إلغاء' : 'Cancel'}</button><button id="mamDeleteConfirm" class="action mam-btn-danger" disabled>${arabic ? 'حذف كل البيانات' : 'Delete all data'}</button></div></div>`;
    document.body.appendChild(backdrop);
    const input = backdrop.querySelector('#mamDeleteConfirmText');
    const confirm = backdrop.querySelector('#mamDeleteConfirm');
    input?.addEventListener('input', () => { confirm.disabled = input.value.trim() !== 'DELETE'; });
    backdrop.querySelector('#mamDeleteCancel')?.addEventListener('click', () => backdrop.remove());
    confirm?.addEventListener('click', async () => {
      confirm.disabled = true;
      try {
        const response = await fetch(`/client-api/admin/assets/${encodeURIComponent(assetId)}`, { method: 'DELETE', headers: { Accept: 'application/json' } });
        const payload = await response.json().catch(() => null);
        if (!response.ok) {
          toast('error', arabic ? 'تعذر حذف الأصل' : 'Asset deletion failed', payload?.detail || errorText(null, `HTTP ${response.status}`));
          confirm.disabled = false;
          return;
        }
        backdrop.remove();
        toast('success', arabic ? 'تم الحذف' : 'Asset deleted', payload?.detail || (arabic ? 'تم حذف الأصل وكل البيانات المرتبطة.' : 'Asset and related data were deleted.'));
        if (route === 'library') await p05LoadLibrary();
      } catch (error) {
        toast('error', arabic ? 'تعذر حذف الأصل' : 'Asset deletion failed', errorText(null, error?.message || 'Delete failed'));
        confirm.disabled = false;
      }
    });
    setTimeout(() => input?.focus(), 0);
  }

  function sanitizeRichHtml(html) {
    const template = document.createElement('template');
    template.innerHTML = txt(html);
    const allowed = new Set(['P','DIV','BR','B','STRONG','I','EM','U','UL','OL','LI','H2','H3','BLOCKQUOTE','A','SPAN']);
    [...template.content.querySelectorAll('*')].forEach(node => {
      if (!allowed.has(node.tagName)) { node.replaceWith(...node.childNodes); return; }
      const href = node.tagName === 'A' ? node.getAttribute('href') || '' : '';
      const direction = node.getAttribute('dir') || '';
      const styleText = node.getAttribute('style') || '';
      [...node.attributes].forEach(attribute => node.removeAttribute(attribute.name));
      if (node.tagName === 'A' && /^(https?:|mailto:)/i.test(href.trim())) { node.setAttribute('href', href.trim()); node.setAttribute('target', '_blank'); node.setAttribute('rel', 'noopener noreferrer'); }
      if (/^(rtl|ltr)$/i.test(direction)) node.setAttribute('dir', direction.toLowerCase());
      const safeStyles = styleText.split(';').map(x => x.trim()).filter(x => /^(text-align\s*:\s*(left|right|center|justify)|direction\s*:\s*(rtl|ltr))$/i.test(x));
      if (safeStyles.length) node.setAttribute('style', safeStyles.join('; '));
    });
    return template.innerHTML.trim();
  }

  function initialRichHtml(value) {
    const source = txt(value);
    if (!source) return '';
    return /<\/?(p|div|br|b|strong|i|em|u|ul|ol|li|h2|h3|blockquote|a|span)(\s|>|\/)/i.test(source) ? sanitizeRichHtml(source) : h(source).replace(/\r?\n/g, '<br>');
  }

  function richToolbar() {
    const button = (cmd, label, value = '') => `<button type="button" class="action mam-editor-btn" data-mam-editor-cmd="${cmd}" data-mam-editor-value="${h(value)}">${label}</button>`;
    return `<div class="mam-editor-toolbar">${button('bold','<b>B</b>')}${button('italic','<i>I</i>')}${button('underline','<u>U</u>')}${button('formatBlock','H2','H2')}${button('formatBlock','H3','H3')}${button('insertUnorderedList','• List')}${button('insertOrderedList','1. List')}${button('justifyRight','⇥')}${button('justifyCenter','↔')}${button('justifyLeft','⇤')}<button type="button" id="mamEditorRtl" class="action">RTL</button><button type="button" id="mamEditorLtr" class="action">LTR</button>${button('undo','↶')}${button('redo','↷')}${button('removeFormat','Clear')}</div>`;
  }

  function bindRichEditor() {
    const editor = document.getElementById('p05RichNotes');
    if (!editor) return;
    document.querySelectorAll('[data-mam-editor-cmd]').forEach(button => button.addEventListener('click', () => {
      editor.focus();
      document.execCommand(button.dataset.mamEditorCmd, false, button.dataset.mamEditorValue || null);
      updateRichCount();
    }));
    document.getElementById('mamEditorRtl')?.addEventListener('click', () => { editor.dir = 'rtl'; editor.style.textAlign = 'right'; editor.focus(); });
    document.getElementById('mamEditorLtr')?.addEventListener('click', () => { editor.dir = 'ltr'; editor.style.textAlign = 'left'; editor.focus(); });
    editor.addEventListener('input', updateRichCount);
    updateRichCount();
  }

  function updateRichCount() {
    const editor = document.getElementById('p05RichNotes');
    const counter = document.getElementById('mamEditorCount');
    if (!editor || !counter) return;
    const length = sanitizeRichHtml(editor.innerHTML).length;
    counter.textContent = `${length}/2000`;
    counter.classList.toggle('danger', length > 2000);
  }

  if (typeof p05OpenEditor === 'function') {
    p05OpenEditor = async function (assetId) {
      try {
        const metadata = await apiJson(`/client-api/curation/assets/${assetId}/metadata`);
        content.innerHTML = `${lead(arabic ? 'تهيئة البيانات الوصفية' : 'Metadata Curation', `${h(assetId)} · v${h(metadata.version)} · ${h(metadata.lifecycle)}`, 'P05 · CURATION')}
          <div class="grid two"><div class="card"><h3>${arabic ? 'العنوان الإنجليزي' : 'English title'}</h3><input id="p05EditTitle" maxlength="300" value="${h(metadata.titleEn)}"/></div><div class="card"><h3>${arabic ? 'العنوان العربي' : 'Arabic title'}</h3><input id="p05EditTitleAr" maxlength="300" value="${h(metadata.titleAr || '')}" dir="rtl"/></div></div>
          <div class="grid two"><div class="card"><h3>${arabic ? 'التصنيف' : 'Category'}</h3><input id="p05EditCategory" maxlength="120" value="${h(metadata.category || '')}"/></div><div class="card"><h3>${arabic ? 'الوسوم' : 'Tags'}</h3><input id="p05EditTags" maxlength="1000" value="${h((metadata.tags || []).join(', '))}"/></div></div>
          <div class="card"><div class="mam-editor-title"><h3>${arabic ? 'ملاحظات الحفظ' : 'Preservation notes'}</h3><small id="mamEditorCount"></small></div>${richToolbar()}<div id="p05RichNotes" class="mam-rich-editor" contenteditable="true" role="textbox" aria-multiline="true">${initialRichHtml(metadata.preservationNotes || '')}</div></div>
          <div class="card"><div class="toolbar"><button id="p05SaveMeta" class="action mam-btn-primary">${arabic ? 'حفظ البيانات' : 'Save metadata'}</button><button id="p05Lifecycle" class="action mam-btn-secondary">${metadata.lifecycle === 'Archived' ? (arabic ? 'استعادة' : 'Restore') : (arabic ? 'أرشفة' : 'Archive')}</button><button id="p05Back" class="action mam-btn-secondary">${arabic ? 'رجوع للمكتبة' : 'Back to library'}</button></div><div id="p05EditState" aria-live="polite"></div></div>`;
        bindRichEditor();
        document.getElementById('p05Back')?.addEventListener('click', () => void p05LoadLibrary());
        document.getElementById('p05SaveMeta')?.addEventListener('click', () => void p05SaveMetadata(assetId, metadata));
        document.getElementById('p05Lifecycle')?.addEventListener('click', () => void p05SetLifecycle(assetId, metadata));
      } catch (error) {
        content.innerHTML = `${lead(arabic ? 'تهيئة البيانات' : 'Metadata Curation', 'MAM')}${failureHtml(arabic ? 'تعذر التحميل' : 'Load failed', error?.message)}`;
      }
    };

    p05SaveMetadata = async function (assetId, metadata) {
      const output = document.getElementById('p05EditState');
      const rich = sanitizeRichHtml(document.getElementById('p05RichNotes')?.innerHTML || '');
      if (rich.length > 2000) { if (output) output.innerHTML = `<div class="state error"><strong>${arabic ? 'النص طويل' : 'Text too long'}</strong><br>${arabic ? 'يجب ألا يتجاوز المحتوى المنسق 2000 حرف تخزين.' : 'Formatted content must not exceed 2000 stored characters.'}</div>`; return; }
      const body = { expectedVersion: metadata.version, schemaKey: 'core-media-v1', titleEn: document.getElementById('p05EditTitle')?.value.trim() || '', titleAr: document.getElementById('p05EditTitleAr')?.value.trim() || '', eventDate: metadata.eventDate, category: document.getElementById('p05EditCategory')?.value.trim() || '', tags: (document.getElementById('p05EditTags')?.value || '').split(',').map(x => x.trim()).filter(Boolean), preservationNotes: rich };
      try {
        const response = await fetch(`/client-api/curation/assets/${assetId}/metadata`, { method: 'PUT', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify(body) });
        if (!response.ok) { if (output) output.innerHTML = failureHtml(arabic ? 'تعذر الحفظ' : 'Save failed', `HTTP ${response.status}`); return; }
        toast('success', arabic ? 'تم الحفظ' : 'Saved', arabic ? 'تم حفظ البيانات والنص المنسق.' : 'Metadata and rich text were saved.');
        await p05OpenEditor(assetId);
      } catch (error) { if (output) output.innerHTML = failureHtml(arabic ? 'تعذر الحفظ' : 'Save failed', error?.message); }
    };
  }

  function upgradeLookups(root = document) {
    const scopes = root instanceof Element ? root : document;
    const targets = [];
    const byId = id => document.getElementById(id);
    [['p06AssetId', false, ''], ['p12CollectionAsset', false, ''], ['p12BulkIds', true, '']].forEach(([id, multiple, kind]) => { const el = byId(id); if (el) targets.push([el, multiple, kind]); });
    scopes.querySelectorAll?.('[data-p12-ref-image-input]').forEach(el => targets.push([el, false, 'Image']));
    scopes.querySelectorAll?.('input[placeholder*="Asset GUID"],input[placeholder*="Image Asset ID"],input[placeholder*="معرّف الأصل"],textarea[placeholder*="asset GUID"],textarea[placeholder*="معرّف أصل"]').forEach(el => {
      if (el.id !== 'p12UserId' && !targets.some(x => x[0] === el)) targets.push([el, el.tagName === 'TEXTAREA', /image/i.test(el.placeholder || '') ? 'Image' : '']);
    });
    targets.forEach(([el, multiple, kind]) => mountAssetLookup(el, { multiple, mediaKind: kind }));

    const userId = document.getElementById('p12UserId');
    if (userId && !userId.dataset.mamManagedId) {
      userId.dataset.mamManagedId = '1';
      userId.type = 'hidden';
      const badge = document.createElement('span');
      badge.className = 'mam-managed-id';
      badge.textContent = arabic ? 'المعرّف يُدار تلقائيًا بواسطة النظام' : 'System ID is managed automatically';
      userId.insertAdjacentElement('afterend', badge);
    }
  }

  function mountAssetLookup(input, options = {}) {
    if (!input || input.dataset.mamLookupMounted) return;
    input.dataset.mamLookupMounted = '1';
    input.style.display = 'none';
    input.setAttribute('aria-hidden', 'true');
    const multiple = options.multiple === true;
    let selected = multiple ? txt(input.value).split(/[\r\n,;]+/).map(x => x.trim()).filter(Boolean) : (input.value ? [input.value.trim()] : []);
    const wrapper = document.createElement('div');
    wrapper.className = 'mam-lookup';
    wrapper.innerHTML = `<div class="mam-lookup-search"><i class="bi bi-search"></i><input type="search" class="form-control" placeholder="${arabic ? 'ابحث باسم الأصل أو اسم الملف…' : 'Search by asset title or file name…'}" autocomplete="off"/><span class="mam-lookup-kind">${h(options.mediaKind || (arabic ? 'كل الوسائط' : 'All media'))}</span></div><div class="mam-lookup-selected"></div><div class="mam-lookup-results" hidden></div>`;
    input.insertAdjacentElement('afterend', wrapper);
    const search = wrapper.querySelector('input[type="search"]');
    const results = wrapper.querySelector('.mam-lookup-results');
    const selectedHost = wrapper.querySelector('.mam-lookup-selected');
    let timer = null;

    const sync = () => {
      input.value = multiple ? selected.join('\n') : (selected[0] || '');
      input.dispatchEvent(new Event('change', { bubbles: true }));
      selectedHost.innerHTML = selected.length ? selected.map((id, index) => `<span class="mam-lookup-chip"><span>${h(id.slice(0, 8))}…</span><button type="button" data-remove="${index}" aria-label="Remove">×</button></span>`).join('') : `<small>${arabic ? 'لم يتم اختيار أصل بعد.' : 'No asset selected yet.'}</small>`;
      selectedHost.querySelectorAll('[data-remove]').forEach(button => button.addEventListener('click', () => { selected.splice(Number(button.dataset.remove), 1); sync(); }));
    };

    const run = async () => {
      const q = search.value.trim();
      try {
        const params = new URLSearchParams({ query: q, limit: '30' });
        if (options.mediaKind) params.set('mediaKind', options.mediaKind);
        const items = await apiJson(`/client-api/discovery/lookups/assets?${params}`);
        results.hidden = false;
        results.innerHTML = items.length ? items.map(item => `<button type="button" class="mam-lookup-result" data-id="${h(item.assetId)}"><strong>${h(item.title)}</strong><span>${h(item.mediaKind)}${item.originalFileName ? ` · ${h(item.originalFileName)}` : ''}</span><small>${h(item.assetId)}</small></button>`).join('') : `<div class="mam-lookup-empty">${arabic ? 'لا توجد نتائج.' : 'No matching assets.'}</div>`;
        results.querySelectorAll('[data-id]').forEach(button => button.addEventListener('click', () => {
          const id = button.dataset.id;
          if (multiple) { if (!selected.includes(id)) selected.push(id); } else selected = [id];
          sync(); results.hidden = true; search.value = '';
        }));
      } catch (error) { results.hidden = false; results.innerHTML = `<div class="mam-lookup-empty error">${h(errorText(null, error?.message))}</div>`; }
    };

    search.addEventListener('focus', () => { if (!search.value) void run(); });
    search.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(run, 280); });
    sync();
  }

  if (typeof p04ShowFailure === 'function') {
    p04ShowFailure = function (host, statusCode, surface) {
      const kind = statusCode === 401 || statusCode === 403 ? 'denied' : statusCode === 503 ? 'degraded' : 'error';
      const heading = kind === 'denied' ? (arabic ? 'الوصول مرفوض' : 'Permission denied') : kind === 'degraded' ? (arabic ? 'الخدمة غير جاهزة' : 'Degraded') : (arabic ? 'فشل المعالجة' : 'Processing failed');
      host.innerHTML = `<div class="state ${kind}"><strong>${h(heading)}</strong><br>${h(errorText(null, `${surface} · HTTP ${statusCode}`))}</div>`;
    };
  }

  if (typeof p04LoadQueue === 'function') {
    p04LoadQueue = async function () {
      const languageAtRequest = arabic;
      const host = document.getElementById('p04QueueState'); if (!host) return;
      try {
        const jobs = await apiJson('/client-api/processing/jobs?limit=100');
        if (route !== 'queue' || languageAtRequest !== arabic) return;
        if (!Array.isArray(jobs) || !jobs.length) { host.innerHTML = state('empty', 'Empty', arabic ? 'لا توجد وظائف معالجة.' : 'No processing jobs are queued.'); return; }
        const label = value => ({0: arabic?'معلقة':'Pending',1:arabic?'قيد التنفيذ':'Running',2:arabic?'نجحت':'Succeeded',3:arabic?'فشلت':'Failed'})[Number(value)] || txt(value);
        host.innerHTML = `<div id="p04QueueActionState"></div><div class="list mam-processing-list">${jobs.map(j => {
          const failed = Number(j.state) === 3;
          const reason = failed ? (j.lastError || (arabic ? 'لم تُرجع الخدمة سببًا تفصيليًا.' : 'The service did not return a detailed failure reason.')) : '';
          return `<div class="row mam-processing-row ${failed ? 'is-failed' : ''}"><b>${h(String(j.jobId).slice(0,13))}</b><span><strong>${h(j.profileId)}</strong> v${h(j.profileVersion)}<br><small>${h(j.assetId)}</small></span><span><strong>${h(label(j.state))}</strong> · attempt ${h(j.attemptCount)}<span data-p12-progress="${h(j.assetId)}|${h(j.profileId)}"></span></span><span>${failed ? `<div class="mam-failure-reason"><strong>${arabic ? 'سبب الفشل' : 'Failure reason'}</strong><p>${h(reason)}</p></div><button class="action mam-btn-secondary" data-p04-retry="${h(j.jobId)}">${arabic ? 'إعادة المحاولة' : 'Retry'}</button>` : h(j.lastError || '')}</span></div>`;
        }).join('')}</div>`;
        host.querySelectorAll('[data-p04-retry]').forEach(button => button.addEventListener('click', async () => {
          const output = document.getElementById('p04QueueActionState'); button.disabled = true;
          try { await apiJson(`/client-api/processing/jobs/${button.dataset.p04Retry}/retry`, { method: 'POST' }); toast('success', arabic ? 'تمت إعادة المحاولة' : 'Retry queued', arabic ? 'تمت إعادة الوظيفة إلى قائمة المعالجة.' : 'The job was re-queued.'); await p04LoadQueue(); }
          catch (error) { if (output) output.innerHTML = failureHtml(arabic ? 'فشلت إعادة المحاولة' : 'Retry failed', error?.message); }
          finally { button.disabled = false; }
        }));
        if (typeof p12DecorateProcessingQueue === 'function') void p12DecorateProcessingQueue(host, jobs);
      } catch (error) { if (route === 'queue') host.innerHTML = failureHtml(arabic ? 'تعذر تحميل قائمة المعالجة' : 'Queue load failed', error?.message); }
    };
  }

  if (typeof p04LoadAsset === 'function') {
    const originalLoadAsset = p04LoadAsset;
    p04LoadAsset = async function () {
      await originalLoadAsset();
      if (route !== 'asset') return;
      try {
        const assets = await apiJson('/client-api/catalog/assets');
        const selected = p12SelectedAssetId ? assets.find(item => String(item.id).toLowerCase() === String(p12SelectedAssetId).toLowerCase()) : null;
        const asset = selected || assets[0]; if (!asset) return;
        const jobs = await apiJson('/client-api/processing/jobs?limit=100');
        const failed = (jobs || []).filter(j => String(j.assetId).toLowerCase() === String(asset.id).toLowerCase() && Number(j.state) === 3).slice(0, 5);
        if (!failed.length || document.getElementById('mamAssetFailures')) return;
        const card = document.createElement('div'); card.id = 'mamAssetFailures'; card.className = 'card mam-failure-card';
        card.innerHTML = `<h3>${arabic ? 'آخر حالات فشل المعالجة' : 'Latest processing failures'}</h3><p>${arabic ? 'السبب الفعلي محفوظ مع الوظيفة لتسهيل التشخيص.' : 'The persisted job error is shown for diagnosis.'}</p>${failed.map(j => `<div class="mam-failure-reason"><strong>${h(j.profileId)}</strong><p>${h(j.lastError || (arabic ? 'لا يوجد تفصيل إضافي.' : 'No additional detail was returned.'))}</p></div>`).join('')}`;
        document.getElementById('p04AssetState')?.appendChild(card);
      } catch { }
    };
  }

  if (typeof p12AttachAssetDiscovery === 'function') {
    const originalAttachDiscovery = p12AttachAssetDiscovery;
    p12AttachAssetDiscovery = async function (assetId, technical, host) {
      await originalAttachDiscovery(assetId, technical, host);
      if (route === 'asset' && (technical?.mediaType === 'Video' || technical?.mediaType === 'Audio')) await mountTranscriptReview(assetId);
    };
  }

  async function mountTranscriptReview(assetId) {
    const discoveryHost = document.getElementById('p12AssetDiscovery');
    if (!discoveryHost || document.getElementById('mamTranscriptReview')) return;
    const card = document.createElement('div'); card.id = 'mamTranscriptReview'; card.className = 'card mam-transcript-review';
    card.innerHTML = `<h3>${arabic ? 'المراجعة اليدوية للتفريغ' : 'Manual transcript review'}</h3><div class="state loading"><strong>${arabic ? 'جاري التحميل' : 'Loading'}</strong><br>${arabic ? 'تحميل التفريغ الأصلي وسجل النسخ…' : 'Loading machine transcript and revision history…'}</div>`;
    discoveryHost.appendChild(card);
    try {
      const original = await apiJson(`/client-api/discovery/assets/${assetId}/text/transcript`);
      const revisions = await apiJson(`/client-api/discovery/assets/${assetId}/transcript-revisions`);
      renderTranscriptEditor(card, assetId, original, revisions, null);
    } catch (error) {
      card.innerHTML = `<h3>${arabic ? 'المراجعة اليدوية للتفريغ' : 'Manual transcript review'}</h3>${error?.status === 404 ? `<p>${arabic ? 'لا يوجد تفريغ صوتي ناجح بعد. شغّل التفريغ أولًا ثم راجعه هنا.' : 'No successful machine transcript exists yet. Run transcription first, then review it here.'}</p>` : failureHtml(arabic ? 'تعذر تحميل التفريغ' : 'Transcript load failed', error?.message)}`;
    }
  }

  function renderTranscriptEditor(card, assetId, original, revisions, loadedRevision) {
    const source = loadedRevision ? { language: loadedRevision.revision.language, segments: loadedRevision.segments } : original;
    const segments = Array.isArray(source?.segments) && source.segments.length ? source.segments : [{ segmentIndex: 0, startMs: null, endMs: null, text: source?.text || '' }];
    card.innerHTML = `<div class="mam-transcript-heading"><div><h3>${arabic ? 'المراجعة اليدوية للتفريغ' : 'Manual transcript review'}</h3><p>${arabic ? 'التفريغ الآلي الأصلي لا يتم استبداله. كل حفظ ينشئ نسخة جديدة مستقلة ومفهرسة.' : 'The machine transcript is never overwritten. Every save creates a new immutable, indexed revision.'}</p></div><span id="mamTranscriptDirty" class="badge" hidden>${arabic ? 'تغييرات غير محفوظة' : 'Unsaved changes'}</span></div>
      <div class="mam-transcript-controls"><input id="mamTranscriptNote" maxlength="500" placeholder="${arabic ? 'ملاحظة على النسخة (اختياري)' : 'Revision note (optional)'}"/><button id="mamSaveTranscript" class="action mam-btn-primary">${arabic ? 'حفظ كنسخة جديدة' : 'Save new revision'}</button><button id="mamFinalizeTranscript" class="action mam-btn-success">${arabic ? 'حفظ واعتماد كنسخة نهائية' : 'Save as final'}</button><button id="mamLoadMachineTranscript" class="action mam-btn-secondary">${arabic ? 'تحميل الأصل الآلي' : 'Load machine original'}</button></div><div id="mamTranscriptSaveState"></div>
      <div class="mam-transcript-segments">${segments.map((s, index) => `<div class="mam-transcript-segment" data-index="${index}" data-start="${s.startMs ?? ''}" data-end="${s.endMs ?? ''}"><div class="mam-transcript-time">${formatTime(s.startMs)} → ${formatTime(s.endMs)}</div><textarea rows="3">${h(s.text || '')}</textarea></div>`).join('')}</div>
      <div class="mam-revision-history"><h4>${arabic ? 'سجل النسخ' : 'Revision history'}</h4><div class="mam-revision-row machine"><div><strong>${arabic ? 'الأصل الآلي' : 'Machine original'}</strong><small>${h(original.updatedAtUtc ? new Date(original.updatedAtUtc).toLocaleString() : '')}</small></div><span class="badge">AI</span></div>${(revisions || []).map(r => `<button type="button" class="mam-revision-row" data-revision="${h(r.revisionId)}"><div><strong>${arabic ? `نسخة #${r.revisionNumber}` : `Revision #${r.revisionNumber}`}${r.isFinal ? ` · ${arabic ? 'نهائية' : 'FINAL'}` : ''}</strong><small>${h(r.createdBy)} · ${h(new Date(r.createdAtUtc).toLocaleString())}${r.note ? ` · ${h(r.note)}` : ''}</small></div><span class="badge ${r.isFinal ? 'final' : ''}">${r.isFinal ? (arabic ? 'نهائية' : 'Final') : (arabic ? 'فتح' : 'Open')}</span></button>`).join('') || `<p>${arabic ? 'لا توجد مراجعات محفوظة بعد.' : 'No manual revisions have been saved yet.'}</p>`}</div>`;

    card.querySelectorAll('.mam-transcript-segment textarea').forEach(textarea => textarea.addEventListener('input', () => { const dirty = document.getElementById('mamTranscriptDirty'); if (dirty) dirty.hidden = false; }));
    document.getElementById('mamLoadMachineTranscript')?.addEventListener('click', () => renderTranscriptEditor(card, assetId, original, revisions, null));
    card.querySelectorAll('[data-revision]').forEach(button => button.addEventListener('click', async () => {
      try { const loaded = await apiJson(`/client-api/discovery/assets/${assetId}/transcript-revisions/${button.dataset.revision}`); renderTranscriptEditor(card, assetId, original, revisions, loaded); }
      catch (error) { toast('error', arabic ? 'تعذر تحميل النسخة' : 'Revision load failed', errorText(null, error?.message)); }
    }));
    const save = isFinal => void saveTranscriptRevision(card, assetId, original, isFinal);
    document.getElementById('mamSaveTranscript')?.addEventListener('click', () => save(false));
    document.getElementById('mamFinalizeTranscript')?.addEventListener('click', () => save(true));
  }

  async function saveTranscriptRevision(card, assetId, original, isFinal) {
    const output = document.getElementById('mamTranscriptSaveState');
    const segmentNodes = [...card.querySelectorAll('.mam-transcript-segment')];
    const segments = segmentNodes.map((node, index) => ({ segmentIndex: index, startMs: numberOrNull(node.dataset.start), endMs: numberOrNull(node.dataset.end), text: node.querySelector('textarea')?.value || '' }));
    if (!segments.some(x => x.text.trim())) { if (output) output.innerHTML = `<div class="state error"><strong>${arabic ? 'لا يوجد نص' : 'No text'}</strong><br>${arabic ? 'أدخل نصًا قبل الحفظ.' : 'Enter transcript text before saving.'}</div>`; return; }
    if (output) output.innerHTML = `<div class="state loading"><strong>${arabic ? 'جاري الحفظ' : 'Saving'}</strong><br>${arabic ? 'إنشاء نسخة جديدة وفهرستها…' : 'Creating and indexing a new revision…'}</div>`;
    try {
      const saved = await apiJson(`/client-api/discovery/assets/${assetId}/transcript-revisions`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ language: original.language || null, isFinal, note: document.getElementById('mamTranscriptNote')?.value.trim() || null, segments }) });
      toast('success', isFinal ? (arabic ? 'تم اعتماد النسخة النهائية' : 'Final transcript saved') : (arabic ? 'تم حفظ نسخة جديدة' : 'Revision saved'), arabic ? 'تم حفظ النسخة وفهرستها دون حذف أي نسخة سابقة.' : 'The revision was saved and indexed without replacing earlier versions.');
      const revisions = await apiJson(`/client-api/discovery/assets/${assetId}/transcript-revisions`);
      renderTranscriptEditor(card, assetId, original, revisions, saved);
    } catch (error) { if (output) output.innerHTML = failureHtml(arabic ? 'تعذر حفظ النسخة' : 'Revision save failed', error?.message); }
  }

  function formatTime(ms) {
    if (ms === null || ms === undefined || ms === '') return '--:--:--';
    const total = Math.max(0, Math.floor(Number(ms) / 1000));
    const hh = Math.floor(total / 3600), mm = Math.floor((total % 3600) / 60), ss = total % 60;
    return `${String(hh).padStart(2,'0')}:${String(mm).padStart(2,'0')}:${String(ss).padStart(2,'0')}`;
  }
  function numberOrNull(value) { return value === '' || value === null || value === undefined ? null : Number(value); }

  decorateElement(document.body);
})();
