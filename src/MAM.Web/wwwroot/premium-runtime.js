(() => {
  'use strict';

  const nativeFetch = window.fetch.bind(window);
  const baseState = typeof state === 'function' ? state : null;
  let lastApiError = null;

  const text = value => value === null || value === undefined ? '' : String(value);
  const compact = (value, max = 900) => {
    const normalized = text(value).replace(/\s+/g, ' ').trim();
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

  function mamToast(kind, heading, detail) {
    const item = document.createElement('div');
    item.className = `mam-toast ${kind || 'info'}`;
    item.innerHTML = `<strong>${esc(heading || '')}</strong><div>${esc(detail || '')}</div>`;
    toastHost().appendChild(item);
    setTimeout(() => item.remove(), kind === 'error' ? 9000 : 4500);
  }
  window.mamToast = mamToast;

  async function readFailure(response) {
    let payload = null;
    try {
      const clone = response.clone();
      const type = clone.headers.get('content-type') || '';
      payload = type.includes('application/json') ? await clone.json() : await clone.text();
    } catch { }
    const objectPayload = payload && typeof payload === 'object' ? payload : null;
    return {
      status: response.status,
      code: compact(objectPayload?.error || objectPayload?.code || ''),
      detail: compact(objectPayload?.detail || objectPayload?.message || (typeof payload === 'string' ? payload : '')),
      technicalDetail: compact(objectPayload?.technicalDetail || ''),
      correlationId: compact(objectPayload?.correlationId || response.headers.get('x-correlation-id') || ''),
      at: Date.now()
    };
  }

  function apiErrorText(info = lastApiError, fallback = '') {
    if (!info) return fallback || (arabic ? 'حدث خطأ غير معروف.' : 'An unknown error occurred.');
    const parts = [];
    if (info.status) parts.push(`HTTP ${info.status}`);
    if (info.code) parts.push(info.code);
    if (info.detail) parts.push(info.detail);
    if (info.technicalDetail && info.technicalDetail !== info.detail) parts.push(info.technicalDetail);
    if (info.correlationId) parts.push(`Ref ${info.correlationId}`);
    return parts.length ? parts.join(' · ') : (fallback || 'Request failed');
  }
  window.mamApiErrorText = apiErrorText;

  window.fetch = async (...args) => {
    try {
      const response = await nativeFetch(...args);
      if (!response.ok) {
        lastApiError = await readFailure(response);
        const method = String((args[1] || {}).method || 'GET').toUpperCase();
        if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
          mamToast('error', arabic ? 'فشل الإجراء' : 'Action failed', apiErrorText());
        }
      }
      return response;
    } catch (error) {
      lastApiError = { status: 0, code: 'network_error', detail: compact(error?.message || 'Network request failed.'), technicalDetail: '', correlationId: '', at: Date.now() };
      mamToast('error', arabic ? 'خطأ اتصال' : 'Network error', apiErrorText());
      throw error;
    }
  };

  if (baseState) {
    state = function (kind, heading, detail) {
      const html = baseState(kind, heading, detail);
      if (!lastApiError || Date.now() - lastApiError.at > 3500 || !['error', 'denied', 'degraded'].includes(kind)) return html;
      return html.replace(/<\/div><\/div>$/, `<span class="mam-diagnostic">${esc(apiErrorText())}</span></div></div>`);
    };
  }

  function decorateElement(element) {
    if (!(element instanceof Element)) return;
    if (element.matches('.action')) element.classList.add('btn', 'btn-sm');
    if (element.matches('input:not([type="checkbox"]):not([type="radio"]):not([type="hidden"])')) element.classList.add('form-control');
    if (element.matches('select')) element.classList.add('form-select');
    if (element.matches('textarea')) element.classList.add('form-control');
    if (element.matches('.table-wrap table')) element.classList.add('table', 'table-hover', 'align-middle');
    element.querySelectorAll('.action').forEach(item => item.classList.add('btn', 'btn-sm'));
    element.querySelectorAll('input:not([type="checkbox"]):not([type="radio"]):not([type="hidden"])').forEach(item => item.classList.add('form-control'));
    element.querySelectorAll('select').forEach(item => item.classList.add('form-select'));
    element.querySelectorAll('textarea').forEach(item => item.classList.add('form-control'));
    element.querySelectorAll('.table-wrap table').forEach(item => item.classList.add('table', 'table-hover', 'align-middle'));
  }

  const uiObserver = new MutationObserver(records => records.forEach(record => record.addedNodes.forEach(node => decorateElement(node))));
  uiObserver.observe(document.body, { childList: true, subtree: true });
  decorateElement(document.body);

  function openAssetDetails(assetId) {
    p12SelectedAssetId = assetId || '';
    route = 'asset';
    render();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function deleteButton(asset) {
    const titleValue = arabic && asset.titleAr ? asset.titleAr : asset.title;
    return `<button class="action mam-btn-danger" data-mam-delete-asset="${esc(asset.id)}" data-mam-delete-title="${esc(titleValue)}">${arabic ? 'حذف نهائي' : 'Delete permanently'}</button>`;
  }

  if (typeof p05AssetCard === 'function' && typeof p05Results === 'function' && typeof p05BindAssetActions === 'function') {
    p05AssetCard = function (asset, collections) {
      const titleValue = arabic && asset.titleAr ? asset.titleAr : asset.title;
      const tags = Array.isArray(asset.tags) ? asset.tags.slice(0, 8).join(' · ') : '';
      const collectionAction = Array.isArray(collections) && collections.length
        ? `<button class="action mam-btn-secondary" data-p05-add="${esc(asset.id)}" data-p05-collection="${esc(collections[0].collectionId)}" data-p05-version="${esc(collections[0].version)}">${arabic ? 'أضف لأول مجموعة' : 'Add to first collection'}</button>`
        : '';
      return `<div class="card mam-asset-card"><h3>${esc(titleValue)}</h3><div class="mam-asset-id">${esc(asset.id)}</div><p>v${esc(asset.version)} · ${esc(asset.lifecycle)} · ${esc(asset.category || '—')}<br>${esc(tags)}</p><div class="mam-asset-actions"><button class="action mam-btn-info" data-mam-open-asset="${esc(asset.id)}">${arabic ? 'تفاصيل الأصل' : 'Asset details'}</button><button class="action mam-btn-secondary" data-p05-edit="${esc(asset.id)}">${arabic ? 'تعديل البيانات' : 'Edit metadata'}</button>${collectionAction}${deleteButton(asset)}</div></div>`;
    };

    p05Results = function (result, collections) {
      const items = Array.isArray(result.items) ? result.items : [];
      if (!items.length) return state('empty', 'Empty', arabic ? 'لا توجد نتائج تطابق البحث والمرشحات الحالية.' : 'No assets match the current search and filters.');
      if (p05Grid) return `<div class="grid three">${items.map(asset => p05AssetCard(asset, collections)).join('')}</div>`;
      return `<div class="list">${items.map(asset => `<div class="row"><b>${esc(String(asset.id).slice(0, 13))}</b><span><strong>${esc(arabic && asset.titleAr ? asset.titleAr : asset.title)}</strong><br><small>${esc(asset.id)}</small></span><span>v${esc(asset.version)} · ${esc(asset.lifecycle)} · ${esc(asset.category || '—')}</span><span class="mam-asset-actions"><button class="action mam-btn-info" data-mam-open-asset="${esc(asset.id)}">${arabic ? 'تفاصيل الأصل' : 'Details'}</button><button class="action mam-btn-secondary" data-p05-edit="${esc(asset.id)}">${arabic ? 'تعديل' : 'Edit'}</button>${deleteButton(asset)}</span></div>`).join('')}</div>`;
    };

    const originalBindAssetActions = p05BindAssetActions;
    p05BindAssetActions = function (collections) {
      originalBindAssetActions(collections);
      content.querySelectorAll('[data-mam-open-asset]').forEach(button => button.addEventListener('click', () => openAssetDetails(button.dataset.mamOpenAsset)));
      content.querySelectorAll('[data-mam-delete-asset]').forEach(button => button.addEventListener('click', () => void confirmDeleteAsset(button.dataset.mamDeleteAsset, button.dataset.mamDeleteTitle || '')));
    };
  }

  function confirmDeleteAsset(assetId, titleValue) {
    return new Promise(resolve => {
      document.getElementById('mamDeleteModal')?.remove();
      const backdrop = document.createElement('div');
      backdrop.id = 'mamDeleteModal';
      backdrop.className = 'mam-modal-backdrop';
      backdrop.innerHTML = `<div class="mam-modal" role="dialog" aria-modal="true" aria-labelledby="mamDeleteTitle">
        <div class="mam-modal-header"><h3 id="mamDeleteTitle">${arabic ? 'حذف الأصل نهائيًا' : 'Permanently delete asset'}</h3></div>
        <div class="mam-modal-body"><p>${arabic ? 'سيتم حذف الأصل من Primary والمشتقات والنسخة الاحتياطية وOCR والتفريغ والفهرسة والتصنيفات والوسوم وكل سجلات التشغيل المرتبطة. يبقى سجل التدقيق غير القابل للتعديل فقط.' : 'This deletes the Primary original, derivatives, Backup copy, OCR, transcript, indexes, categories, tags and related operational records. Only the immutable audit event is retained.'}</p><div class="mam-delete-summary"><strong>${esc(titleValue || assetId)}</strong><br><code>${esc(assetId)}</code></div><label>${arabic ? 'اكتب DELETE للتأكيد' : 'Type DELETE to confirm'}<input id="mamDeleteConfirmText" autocomplete="off" placeholder="DELETE" /></label></div>
        <div class="mam-modal-footer"><button id="mamDeleteCancel" class="action mam-btn-secondary">${arabic ? 'إلغاء' : 'Cancel'}</button><button id="mamDeleteConfirm" class="action mam-btn-danger" disabled>${arabic ? 'حذف كل البيانات' : 'Delete all data'}</button></div>
      </div>`;
      document.body.appendChild(backdrop);
      decorateElement(backdrop);
      const input = backdrop.querySelector('#mamDeleteConfirmText');
      const confirm = backdrop.querySelector('#mamDeleteConfirm');
      input?.addEventListener('input', () => { if (confirm) confirm.disabled = input.value.trim() !== 'DELETE'; });
      backdrop.querySelector('#mamDeleteCancel')?.addEventListener('click', () => { backdrop.remove(); resolve(false); });
      confirm?.addEventListener('click', async () => {
        confirm.disabled = true;
        confirm.textContent = arabic ? 'جارٍ الحذف…' : 'Deleting…';
        try {
          const response = await fetch(`/client-api/admin/assets/${encodeURIComponent(assetId)}`, { method: 'DELETE', headers: { Accept: 'application/json' } });
          const payload = await response.json().catch(() => null);
          if (!response.ok) {
            mamToast('error', arabic ? 'تعذر حذف الأصل' : 'Asset deletion failed', payload?.detail || apiErrorText(null, `HTTP ${response.status}`));
            confirm.disabled = false;
            confirm.textContent = arabic ? 'حذف كل البيانات' : 'Delete all data';
            return;
          }
          backdrop.remove();
          mamToast('success', arabic ? 'تم الحذف' : 'Asset deleted', payload?.detail || (arabic ? 'تم حذف الأصل وكل البيانات المرتبطة.' : 'The asset and all related data were deleted.'));
          if (route === 'library') await p05LoadLibrary();
          resolve(true);
        } catch (error) {
          mamToast('error', arabic ? 'تعذر حذف الأصل' : 'Asset deletion failed', apiErrorText(null, error?.message || 'Delete failed'));
          confirm.disabled = false;
          confirm.textContent = arabic ? 'حذف كل البيانات' : 'Delete all data';
        }
      });
      setTimeout(() => input?.focus(), 0);
    });
  }

  function sanitizeRichHtml(html) {
    const template = document.createElement('template');
    template.innerHTML = text(html);
    const allowedTags = new Set(['P', 'DIV', 'BR', 'B', 'STRONG', 'I', 'EM', 'U', 'UL', 'OL', 'LI', 'H2', 'H3', 'BLOCKQUOTE', 'A', 'SPAN']);
    [...template.content.querySelectorAll('*')].forEach(node => {
      if (!allowedTags.has(node.tagName)) {
        node.replaceWith(...node.childNodes);
        return;
      }
      const href = node.tagName === 'A' ? node.getAttribute('href') || '' : '';
      const direction = node.getAttribute('dir') || '';
      const styleText = node.getAttribute('style') || '';
      [...node.attributes].forEach(attribute => node.removeAttribute(attribute.name));
      if (node.tagName === 'A' && /^(https?:|mailto:)/i.test(href.trim())) {
        node.setAttribute('href', href.trim());
        node.setAttribute('target', '_blank');
        node.setAttribute('rel', 'noopener noreferrer');
      }
      if (/^(rtl|ltr)$/i.test(direction)) node.setAttribute('dir', direction.toLowerCase());
      const safeStyles = styleText.split(';').map(value => value.trim()).filter(Boolean).filter(value => /^(text-align\s*:\s*(left|right|center|justify)|direction\s*:\s*(rtl|ltr))$/i.test(value));
      if (safeStyles.length) node.setAttribute('style', safeStyles.join('; '));
    });
    return template.innerHTML.trim();
  }

  function initialRichHtml(value) {
    const source = text(value);
    if (!source) return '';
    return /<\/?(p|div|br|b|strong|i|em|u|ul|ol|li|h2|h3|blockquote|a|span)(\s|>|\/)/i.test(source)
      ? sanitizeRichHtml(source)
      : esc(source).replace(/\r?\n/g, '<br>');
  }

  function updateEditorStatus() {
    const editor = document.getElementById('p05RichNotes');
    const counter = document.getElementById('mamEditorCount');
    if (!editor || !counter) return;
    const htmlLength = sanitizeRichHtml(editor.innerHTML).length;
    const textLength = (editor.innerText || '').trim().length;
    counter.textContent = `${textLength} ${arabic ? 'حرف نصي' : 'text chars'} · ${htmlLength}/2000 ${arabic ? 'تخزين' : 'stored'}`;
    counter.style.color = htmlLength > 2000 ? '#b42318' : '';
  }

  function execEditor(command, value = null) {
    const editor = document.getElementById('p05RichNotes');
    if (!editor) return;
    editor.focus();
    document.execCommand(command, false, value);
    updateEditorStatus();
  }

  function bindRichEditor() {
    document.querySelectorAll('[data-mam-editor-command]').forEach(button => button.addEventListener('click', () => {
      const command = button.dataset.mamEditorCommand;
      if (command === 'createLink') {
        const value = window.prompt(arabic ? 'أدخل رابطًا يبدأ بـ https:// أو mailto:' : 'Enter a link beginning with https:// or mailto:');
        if (value && /^(https?:|mailto:)/i.test(value.trim())) execEditor('createLink', value.trim());
        return;
      }
      execEditor(command, button.dataset.mamEditorValue || null);
    }));
    const editor = document.getElementById('p05RichNotes');
    editor?.addEventListener('input', updateEditorStatus);
    document.getElementById('mamEditorRtl')?.addEventListener('click', () => { if (editor) { editor.dir = 'rtl'; editor.style.textAlign = 'right'; editor.focus(); } });
    document.getElementById('mamEditorLtr')?.addEventListener('click', () => { if (editor) { editor.dir = 'ltr'; editor.style.textAlign = 'left'; editor.focus(); } });
    updateEditorStatus();
  }

  if (typeof p05OpenEditor === 'function') {
    p05OpenEditor = async function (assetId) {
      try {
        const response = await fetch(`/client-api/curation/assets/${assetId}/metadata`, { headers: { Accept: 'application/json' } });
        if (!response.ok) {
          content.innerHTML = `${lead(arabic ? 'تهيئة البيانات' : 'Metadata Curation', 'MAM')}${state('error', arabic ? 'تعذر التحميل' : 'Load failed', apiErrorText(null, `HTTP ${response.status}`))}`;
          return;
        }
        const metadata = await response.json();
        content.innerHTML = `${lead(arabic ? 'تهيئة البيانات الوصفية' : 'Metadata Curation', `${esc(assetId)} · v${esc(metadata.version)} · ${esc(metadata.lifecycle)}`, 'MAM · PREMIUM CURATION')}
          <div class="grid two"><div class="card"><h3>${arabic ? 'العنوان الإنجليزي' : 'English title'}</h3><input id="p05EditTitle" maxlength="300" value="${esc(metadata.titleEn)}" /></div><div class="card"><h3>${arabic ? 'العنوان العربي' : 'Arabic title'}</h3><input id="p05EditTitleAr" maxlength="300" value="${esc(metadata.titleAr || '')}" dir="rtl" /></div></div>
          <div class="grid two"><div class="card"><h3>${arabic ? 'التصنيف' : 'Category'}</h3><input id="p05EditCategory" maxlength="120" value="${esc(metadata.category || '')}" /></div><div class="card"><h3>${arabic ? 'الوسوم' : 'Tags'}</h3><input id="p05EditTags" maxlength="1000" value="${esc((metadata.tags || []).join(', '))}" /></div></div>
          <div class="card"><h3>${arabic ? 'ملاحظات الحفظ والمحتوى النصي' : 'Preservation notes & rich text'}</h3><div class="mam-rich-editor-shell"><div class="mam-rich-toolbar" role="toolbar" aria-label="Rich text editor"><button type="button" data-mam-editor-command="bold"><b>B</b></button><button type="button" data-mam-editor-command="italic"><i>I</i></button><button type="button" data-mam-editor-command="underline"><u>U</u></button><span class="sep"></span><button type="button" data-mam-editor-command="formatBlock" data-mam-editor-value="p">P</button><button type="button" data-mam-editor-command="formatBlock" data-mam-editor-value="h2">H2</button><button type="button" data-mam-editor-command="formatBlock" data-mam-editor-value="h3">H3</button><span class="sep"></span><button type="button" data-mam-editor-command="insertUnorderedList">• List</button><button type="button" data-mam-editor-command="insertOrderedList">1. List</button><span class="sep"></span><button type="button" data-mam-editor-command="justifyLeft">⇤</button><button type="button" data-mam-editor-command="justifyCenter">↔</button><button type="button" data-mam-editor-command="justifyRight">⇥</button><button type="button" id="mamEditorRtl">RTL</button><button type="button" id="mamEditorLtr">LTR</button><span class="sep"></span><button type="button" data-mam-editor-command="createLink">🔗</button><button type="button" data-mam-editor-command="undo">↶</button><button type="button" data-mam-editor-command="redo">↷</button><button type="button" data-mam-editor-command="removeFormat">Tx</button></div><div id="p05RichNotes" class="mam-rich-editor" contenteditable="true" role="textbox" aria-multiline="true" dir="${arabic ? 'rtl' : 'ltr'}">${initialRichHtml(metadata.preservationNotes || '')}</div><div class="mam-editor-status"><span>${arabic ? 'محرر نص منسق شبيه ببرامج معالجة النصوص' : 'Word-like rich text editor'}</span><span id="mamEditorCount"></span></div></div></div>
          <div class="card"><div class="mam-asset-actions"><button id="p05SaveMeta" class="action">${arabic ? 'حفظ البيانات' : 'Save metadata'}</button><button id="p05LifecycleAction" class="action mam-btn-secondary">${metadata.lifecycle === 'Archived' ? (arabic ? 'استعادة' : 'Restore') : (arabic ? 'أرشفة' : 'Archive')}</button><button id="p05Details" class="action mam-btn-info">${arabic ? 'تفاصيل الأصل' : 'Asset details'}</button><button id="p05Back" class="action mam-btn-secondary">${arabic ? 'رجوع للمكتبة' : 'Back to library'}</button></div><div id="p05EditState" aria-live="polite"></div></div>`;
        decorateElement(content);
        bindRichEditor();
        document.getElementById('p05Back')?.addEventListener('click', () => void p05LoadLibrary());
        document.getElementById('p05Details')?.addEventListener('click', () => openAssetDetails(assetId));
        document.getElementById('p05SaveMeta')?.addEventListener('click', () => void saveRichMetadata(assetId, metadata));
        document.getElementById('p05LifecycleAction')?.addEventListener('click', () => void p05SetLifecycle(assetId, metadata));
      } catch (error) {
        content.innerHTML = `${lead(arabic ? 'تهيئة البيانات' : 'Metadata Curation', 'MAM')}${state('error', arabic ? 'تعذر التحميل' : 'Load failed', apiErrorText(null, error?.message || 'Load failed'))}`;
      }
    };
  }

  async function saveRichMetadata(assetId, metadata) {
    const output = document.getElementById('p05EditState');
    const notes = sanitizeRichHtml(document.getElementById('p05RichNotes')?.innerHTML || '');
    if (notes.length > 2000) {
      const detail = arabic ? `النص المنسق حجمه ${notes.length} حرفًا بعد التنسيق، والحد الحالي 2000. اختصر النص أو التنسيق.` : `Formatted content is ${notes.length} characters; the current storage limit is 2000. Shorten the text or formatting.`;
      if (output) output.innerHTML = state('error', arabic ? 'النص أطول من المسموح' : 'Content too long', detail);
      mamToast('error', arabic ? 'تعذر الحفظ' : 'Save failed', detail);
      return;
    }
    const body = {
      expectedVersion: metadata.version,
      schemaKey: 'core-media-v1',
      titleEn: document.getElementById('p05EditTitle')?.value.trim() || '',
      titleAr: document.getElementById('p05EditTitleAr')?.value.trim() || '',
      eventDate: metadata.eventDate,
      category: document.getElementById('p05EditCategory')?.value.trim() || '',
      tags: (document.getElementById('p05EditTags')?.value || '').split(',').map(value => value.trim()).filter(Boolean),
      preservationNotes: notes
    };
    try {
      if (output) output.innerHTML = state('loading', arabic ? 'جارٍ الحفظ' : 'Saving', arabic ? 'يتم حفظ البيانات المنسقة…' : 'Saving formatted metadata…');
      const response = await fetch(`/client-api/curation/assets/${assetId}/metadata`, { method: 'PUT', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify(body) });
      if (!response.ok) {
        if (output) output.innerHTML = state('error', arabic ? 'تعذر الحفظ' : 'Save failed', apiErrorText(null, `HTTP ${response.status}`));
        return;
      }
      mamToast('success', arabic ? 'تم الحفظ' : 'Saved', arabic ? 'تم حفظ البيانات الوصفية والنص المنسق.' : 'Metadata and rich text were saved.');
      await p05OpenEditor(assetId);
    } catch (error) {
      if (output) output.innerHTML = state('error', arabic ? 'تعذر الحفظ' : 'Save failed', apiErrorText(null, error?.message || 'Save failed'));
    }
  }
})();
