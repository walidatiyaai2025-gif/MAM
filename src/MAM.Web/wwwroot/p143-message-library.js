(() => {
'use strict';

let runtimeEntries = [];
let runtimeLoad = null;
let adminPage = 1;
let adminQuery = '';
let adminScope = '';
const adminPageSize = 10;
const rawByElement = new WeakMap();

const tr = (en, ar) => (window.arabic ? ar : en);
const escHtml = value => typeof window.esc === 'function'
  ? window.esc(value ?? '')
  : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function api(path, options = {}) {
  const headers = { Accept:'application/json', ...(options.headers || {}) };
  if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, { ...options, headers, cache:options.cache || 'no-store' });
  let payload = null;
  if ((response.headers.get('content-type') || '').includes('json')) {
    try { payload = await response.json(); } catch {}
  }
  if (!response.ok) {
    const error = new Error(payload?.detail || payload?.error || `HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function normalized(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function localized(entry) {
  const ar = !!window.arabic;
  const title = normalized(ar ? entry?.titleAr : entry?.titleEn) || normalized(ar ? entry?.titleEn : entry?.titleAr);
  const message = normalized(ar ? entry?.messageAr : entry?.messageEn) || normalized(ar ? entry?.messageEn : entry?.messageAr);
  const reason = normalized(ar ? entry?.reasonAr : entry?.reasonEn) || normalized(ar ? entry?.reasonEn : entry?.reasonAr);
  const reasonLabel = ar ? 'سبب الفشل' : 'Failure reason';
  return {
    title,
    message,
    reason,
    text: reason ? `${message} (${reasonLabel}: ${reason})` : message,
    severity: normalized(entry?.severity || 'error').toLowerCase(),
    showRetry: entry?.showRetry === true,
    key: entry?.messageKey || ''
  };
}

function matchMessage(raw, scope = 'processing') {
  const value = normalized(raw);
  if (!value) return null;
  const lower = value.toLocaleLowerCase();
  return runtimeEntries.find(entry =>
    entry?.isEnabled !== false &&
    (!scope || String(entry.scope || '').toLocaleLowerCase() === String(scope).toLocaleLowerCase()) &&
    normalized(entry.matchPattern) &&
    lower.includes(normalized(entry.matchPattern).toLocaleLowerCase())
  ) || null;
}

function retryButtons(root) {
  if (!(root instanceof Element)) return [];
  return [...root.querySelectorAll(
    '[data-p04-retry],.p131-retry,[data-retry],button'
  )].filter(button => {
    if (button.matches('[data-p04-retry],.p131-retry,[data-retry]')) return true;
    const text = normalized(button.textContent).toLocaleLowerCase();
    return text === 'retry' || text.includes('إعادة المحاولة');
  });
}

function enforceRetryVisibility(container, showRetry) {
  if (!(container instanceof Element)) return;
  retryButtons(container).forEach(button => {
    button.hidden = !showRetry;
    button.setAttribute('aria-hidden', showRetry ? 'false' : 'true');
    if (!showRetry) button.tabIndex = -1;
  });
}

function applyEntryToElement(element, entry, raw) {
  if (!(element instanceof Element) || !entry) return;
  const view = localized(entry);
  const container = element.closest('.state,.row,.card,article,[role="alert"],[aria-live]') || element;
  container.dataset.mamMessageKey = view.key;
  container.dataset.mamMessageSeverity = view.severity;
  rawByElement.set(container, raw);

  container.classList.remove(
    'mam-message-info','mam-message-success','mam-message-warning','mam-message-error'
  );
  container.classList.add(`mam-message-${['info','success','warning','error'].includes(view.severity) ? view.severity : 'error'}`);

  let replaced = false;
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const matched = matchMessage(node.nodeValue);
    if (!matched || matched.messageKey !== entry.messageKey) continue;
    node.nodeValue = view.text;
    replaced = true;
  }
  if (!replaced) {
    const messageNode = document.createElement('span');
    messageNode.className = 'mam-friendly-message';
    messageNode.textContent = view.text;
    element.appendChild(messageNode);
  }
  const stateBox = container.matches('.state.error,.state.degraded,.state.denied,[role="alert"]') ? container : null;
  if (stateBox && !stateBox.querySelector(':scope > strong') && view.title) {
    const title = document.createElement('strong');
    title.textContent = view.title;
    stateBox.prepend(document.createElement('br'));
    stateBox.prepend(title);
  }
  enforceRetryVisibility(container, view.showRetry);
}

function sanitizeNode(root = document) {
  if (!runtimeEntries.length) return;
  const scopeRoot = root instanceof Element || root instanceof Document ? root : document;
  const walker = document.createTreeWalker(scopeRoot, NodeFilter.SHOW_TEXT);
  const matches = [];
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const raw = normalized(node.nodeValue);
    if (!raw || raw.length < 3 || raw.length > 4000) continue;
    const entry = matchMessage(raw);
    if (!entry) continue;
    const parent = node.parentElement;
    if (!parent || parent.closest('#p127AdminPanel [data-message-library-editor]')) continue;
    matches.push({ node, parent, raw, entry });
  }

  for (const match of matches) {
    const target = match.parent.matches('.state.error,.state.degraded,.state.denied,[role="alert"]')
      ? match.parent
      : match.parent;
    applyEntryToElement(target, match.entry, match.raw);
  }

  scopeRoot.querySelectorAll?.('[data-mam-message-key]').forEach(container => {
    const entry = runtimeEntries.find(x => x.messageKey === container.dataset.mamMessageKey);
    if (entry) enforceRetryVisibility(container, entry.showRetry === true);
  });
}

async function loadRuntime(force = false) {
  if (runtimeLoad && !force) return runtimeLoad;
  runtimeLoad = (async () => {
    try {
      const rows = await api('/client-api/messages?scope=processing');
      runtimeEntries = Array.isArray(rows) ? rows : [];
      document.querySelectorAll('[data-mam-message-key]').forEach(element => {
        const raw = rawByElement.get(element);
        const entry = raw ? matchMessage(raw) : runtimeEntries.find(x => x.messageKey === element.dataset.mamMessageKey);
        if (entry && raw) applyEntryToElement(element, entry, raw);
      });
      sanitizeNode(document);
      return runtimeEntries;
    } catch {
      runtimeEntries = [];
      return runtimeEntries;
    }
  })();
  try { return await runtimeLoad; }
  finally { if (force) runtimeLoad = null; }
}

function severityLabel(value) {
  const map = {
    info:tr('Information','معلومة'),
    success:tr('Success','نجاح'),
    warning:tr('Warning','تنبيه'),
    error:tr('Error','خطأ')
  };
  return map[value] || value;
}

function messagePreview(row) {
  const view = localized(row);
  return `<div class="mam-message-preview mam-message-${escHtml(view.severity)}">
    <strong>${escHtml(view.title)}</strong>
    <span>${escHtml(view.text)}</span>
  </div>`;
}

async function editMessage(row) {
  if (!window.p127OpenModal) return;
  const body = `
    <div class="p127-user-form" data-message-library-editor>
      <div class="p127-field"><label>${escHtml(tr('Key','المفتاح'))}</label><input value="${escHtml(row.messageKey)}" disabled></div>
      <div class="p127-field"><label>${escHtml(tr('Scope','النطاق'))}</label><input id="mlScope" maxlength="80" value="${escHtml(row.scope)}"></div>
      <div class="p127-field full"><label>${escHtml(tr('Technical match pattern','نمط مطابقة الخطأ التقني'))}</label><input id="mlPattern" maxlength="600" dir="ltr" value="${escHtml(row.matchPattern)}"></div>
      <div class="p127-field"><label>English title</label><input id="mlTitleEn" maxlength="240" value="${escHtml(row.titleEn)}"></div>
      <div class="p127-field"><label>العنوان العربي</label><input id="mlTitleAr" maxlength="240" dir="rtl" value="${escHtml(row.titleAr)}"></div>
      <div class="p127-field full"><label>English message</label><textarea id="mlMessageEn" rows="3" maxlength="1200">${escHtml(row.messageEn)}</textarea></div>
      <div class="p127-field full"><label>الرسالة العربية</label><textarea id="mlMessageAr" rows="3" maxlength="1200" dir="rtl">${escHtml(row.messageAr)}</textarea></div>
      <div class="p127-field full"><label>English failure reason</label><textarea id="mlReasonEn" rows="2" maxlength="1000">${escHtml(row.reasonEn || '')}</textarea></div>
      <div class="p127-field full"><label>سبب الفشل بالعربية</label><textarea id="mlReasonAr" rows="2" maxlength="1000" dir="rtl">${escHtml(row.reasonAr || '')}</textarea></div>
      <div class="p127-field"><label>${escHtml(tr('Message style','شكل الرسالة'))}</label>
        <select id="mlSeverity">
          ${['info','success','warning','error'].map(x => `<option value="${x}" ${row.severity === x ? 'selected' : ''}>${escHtml(severityLabel(x))}</option>`).join('')}
        </select>
      </div>
      <div class="p127-field"><label>${escHtml(tr('Order','الترتيب'))}</label><input id="mlSort" type="number" min="0" max="100000" value="${Number(row.sortOrder || 0)}"></div>
      <div class="p127-field"><label><input id="mlRetry" type="checkbox" ${row.showRetry ? 'checked' : ''}> ${escHtml(tr('Show Retry button','إظهار زر إعادة المحاولة'))}</label></div>
      <div class="p127-field"><label><input id="mlEnabled" type="checkbox" ${row.isEnabled !== false ? 'checked' : ''}> ${escHtml(tr('Enabled','مفعلة'))}</label></div>
      <div class="p127-field full">${messagePreview(row)}</div>
    </div>`;

  const modal = await window.p127OpenModal({
    title:tr('Edit message','تعديل الرسالة'),
    confirmText:tr('Save','حفظ'),
    body
  });
  if (!modal) return;

  const payload = {
    expectedVersion:Number(row.version),
    scope:modal.querySelector('#mlScope')?.value.trim() || '',
    matchPattern:modal.querySelector('#mlPattern')?.value.trim() || '',
    titleEn:modal.querySelector('#mlTitleEn')?.value.trim() || '',
    titleAr:modal.querySelector('#mlTitleAr')?.value.trim() || '',
    messageEn:modal.querySelector('#mlMessageEn')?.value.trim() || '',
    messageAr:modal.querySelector('#mlMessageAr')?.value.trim() || '',
    reasonEn:modal.querySelector('#mlReasonEn')?.value.trim() || null,
    reasonAr:modal.querySelector('#mlReasonAr')?.value.trim() || null,
    severity:modal.querySelector('#mlSeverity')?.value || 'error',
    showRetry:!!modal.querySelector('#mlRetry')?.checked,
    isEnabled:!!modal.querySelector('#mlEnabled')?.checked,
    sortOrder:Number(modal.querySelector('#mlSort')?.value || 0)
  };

  try {
    await api(`/client-api/admin/messages/${encodeURIComponent(row.messageKey)}`, {
      method:'PUT',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify(payload)
    });
    window.MamPopup?.notify?.(tr('Message settings were saved.','تم حفظ إعدادات الرسالة.'),'success',tr('Message Library','مكتبة الرسائل'));
    await loadRuntime(true);
    await renderAdminPage();
  } catch (error) {
    window.MamPopup?.notify?.(error.message,'error',tr('Save failed','فشل الحفظ'));
  }
}

function pager(payload) {
  const current = Number(payload.page || 1);
  const pages = Math.max(1, Number(payload.totalPages || 1));
  return `<div class="mam-message-pager">
    <button type="button" data-ml-prev ${current <= 1 ? 'disabled' : ''}>${escHtml(tr('Previous','السابق'))}</button>
    <span>${escHtml(tr('Page','صفحة'))} ${current} / ${pages} · ${Number(payload.totalCount || 0)}</span>
    <button type="button" data-ml-next ${current >= pages ? 'disabled' : ''}>${escHtml(tr('Next','التالي'))}</button>
  </div>`;
}

async function renderAdminPage() {
  const host = document.getElementById('mamMessageLibraryRows');
  if (!host) return;
  host.innerHTML = `<div class="state loading"><strong>${escHtml(tr('Loading messages…','جاري تحميل الرسائل…'))}</strong></div>`;

  try {
    const params = new URLSearchParams({
      page:String(adminPage),
      pageSize:String(adminPageSize)
    });
    if (adminQuery) params.set('query', adminQuery);
    if (adminScope) params.set('scope', adminScope);
    const payload = await api(`/client-api/admin/messages?${params}`);
    const rows = Array.isArray(payload?.items) ? payload.items : [];

    host.innerHTML = rows.length ? `
      <div class="mam-message-list">
        ${rows.map(row => `<article class="mam-message-row" data-message-key="${escHtml(row.messageKey)}">
          <div class="mam-message-row-head">
            <div><strong>${escHtml(window.arabic ? row.titleAr : row.titleEn)}</strong><small>${escHtml(row.messageKey)} · ${escHtml(row.scope)}</small></div>
            <div class="mam-message-badges">
              <span class="mam-message-badge ${escHtml(row.severity)}">${escHtml(severityLabel(row.severity))}</span>
              <span>${row.showRetry ? escHtml(tr('Retry visible','إعادة المحاولة ظاهرة')) : escHtml(tr('No retry','بدون إعادة محاولة'))}</span>
              <span>${row.isEnabled ? escHtml(tr('Enabled','مفعلة')) : escHtml(tr('Disabled','معطلة'))}</span>
            </div>
          </div>
          ${messagePreview(row)}
          <div class="mam-message-pattern"><b>${escHtml(tr('Match','المطابقة'))}:</b> <code>${escHtml(row.matchPattern)}</code></div>
          <div class="mam-message-row-actions"><button type="button" class="action" data-ml-edit="${escHtml(row.messageKey)}"><i class="bi bi-pencil"></i> ${escHtml(tr('Edit','تعديل'))}</button></div>
        </article>`).join('')}
      </div>
      ${pager(payload)}
    ` : `<div class="state empty"><strong>${escHtml(tr('No messages found','لا توجد رسائل مطابقة'))}</strong></div>`;

    host.querySelectorAll('[data-ml-edit]').forEach(button => button.addEventListener('click', () => {
      const row = rows.find(x => x.messageKey === button.dataset.mlEdit);
      if (row) void editMessage(row);
    }));
    host.querySelector('[data-ml-prev]')?.addEventListener('click', () => { adminPage = Math.max(1, adminPage - 1); void renderAdminPage(); });
    host.querySelector('[data-ml-next]')?.addEventListener('click', () => { adminPage += 1; void renderAdminPage(); });
  } catch (error) {
    host.innerHTML = `<div class="state error"><strong>${escHtml(tr('Message Library could not be loaded','تعذر تحميل مكتبة الرسائل'))}</strong><br>${escHtml(error.message)}</div>`;
  }
}

async function loadAdmin() {
  const panel = document.getElementById('p127AdminPanel');
  if (!panel) return;
  panel.innerHTML = `
    <section class="mam-message-library-admin">
      <header class="mam-message-library-head">
        <div><h3><i class="bi bi-chat-square-text"></i> ${escHtml(tr('Message Library','مكتبة الرسائل'))}</h3>
        <p>${escHtml(tr('Control user-facing system messages without exposing technical database or server errors.','تحكم في رسائل النظام التي تظهر للمستخدم بدون كشف أخطاء قاعدة البيانات أو الخادم.'))}</p></div>
      </header>
      <div class="mam-message-toolbar">
        <input id="mamMessageQuery" value="${escHtml(adminQuery)}" placeholder="${escHtml(tr('Search key, text or technical pattern','بحث بالمفتاح أو النص أو النمط التقني'))}">
        <select id="mamMessageScope"><option value="">${escHtml(tr('All scopes','كل الأنواع'))}</option><option value="processing" ${adminScope === 'processing' ? 'selected' : ''}>${escHtml(tr('Processing','المعالجة'))}</option></select>
        <button type="button" class="action" id="mamMessageSearch"><i class="bi bi-search"></i> ${escHtml(tr('Search','بحث'))}</button>
      </div>
      <div id="mamMessageLibraryRows"></div>
    </section>`;
  panel.querySelector('#mamMessageSearch')?.addEventListener('click', () => {
    adminQuery = panel.querySelector('#mamMessageQuery')?.value.trim() || '';
    adminScope = panel.querySelector('#mamMessageScope')?.value || '';
    adminPage = 1;
    void renderAdminPage();
  });
  panel.querySelector('#mamMessageQuery')?.addEventListener('keydown', event => {
    if (event.key !== 'Enter') return;
    adminQuery = event.currentTarget.value.trim();
    adminScope = panel.querySelector('#mamMessageScope')?.value || '';
    adminPage = 1;
    void renderAdminPage();
  });
  panel.querySelector('#mamMessageScope')?.addEventListener('change', event => {
    adminScope = event.currentTarget.value || '';
    adminPage = 1;
    void renderAdminPage();
  });
  await renderAdminPage();
}

const observer = new MutationObserver(records => {
  if (!runtimeEntries.length) return;
  for (const record of records) {
    record.addedNodes.forEach(node => {
      if (node.nodeType === Node.ELEMENT_NODE) sanitizeNode(node);
      else if (node.nodeType === Node.TEXT_NODE && node.parentElement) sanitizeNode(node.parentElement);
    });
  }
  document.querySelectorAll('[data-mam-message-key]').forEach(container => {
    const entry = runtimeEntries.find(x => x.messageKey === container.dataset.mamMessageKey);
    if (entry) enforceRetryVisibility(container, entry.showRetry === true);
  });
});
observer.observe(document.body, { childList:true, subtree:true, characterData:true });

window.mamMessageLibrary = Object.freeze({
  version:'p143-message-library-1',
  reload:() => loadRuntime(true),
  match:(raw, scope='processing') => matchMessage(raw, scope),
  format:raw => {
    const entry = matchMessage(raw);
    return entry ? localized(entry) : null;
  },
  sanitize:root => sanitizeNode(root || document)
});

window.mamMessageLibraryAdmin = Object.freeze({
  load:loadAdmin
});

void loadRuntime(false);
})();