(() => {
'use strict';

const tr = (en, ar) => (window.arabic ? ar : en);
const esc = value => typeof window.esc === 'function'
  ? window.esc(value ?? '')
  : String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const notify = (message, kind = 'info', title = '') => {
  const text = String(message || '').trim();
  if (!text) return;
  if (window.MamPopup?.notify) window.MamPopup.notify(text, kind, title);
  else console[kind === 'error' ? 'error' : 'info'](text);
};

async function api(path, options = {}) {
  const headers = { Accept:'application/json', ...(options.headers || {}) };
  if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, { cache:'no-store', ...options, headers });
  if (!response.ok) {
    let payload = {};
    try { payload = await response.json(); } catch {}
    const detail = payload.detail || payload.error || `HTTP ${response.status}`;
    const error = new Error(detail);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  if (response.status === 204) return null;
  return (response.headers.get('content-type') || '').includes('json') ? response.json() : response.text();
}

function normalizeSettingsRoute() {
  try {
    if (typeof route === 'undefined' || route !== 'settings') return false;
    localStorage.setItem('mam.p127.adminTab', 'settings');
    route = 'admin';
    const url = new URL(location.href);
    const hash = new URLSearchParams(url.hash.replace(/^#/, ''));
    hash.set('route', 'admin');
    hash.delete('tab');
    url.hash = hash.toString();
    history.replaceState(history.state, '', url.href);
    return true;
  } catch {
    return false;
  }
}

function navigationRow(item) {
  return `<tr data-p142-nav-key="${esc(item.navigationKey)}">
    <td><code>${esc(item.navigationKey)}</code></td>
    <td><input data-p142-nav-en maxlength="100" value="${esc(item.labelEn || '')}"/></td>
    <td><input data-p142-nav-ar maxlength="100" dir="rtl" value="${esc(item.labelAr || '')}"/></td>
    <td><input data-p142-nav-order type="number" min="0" max="9999" value="${Number(item.sortOrder || 0)}"/></td>
    <td class="p142-center"><input data-p142-nav-enabled type="checkbox" ${item.isEnabled ? 'checked' : ''}/></td>
  </tr>`;
}

function navigationTable(items, title, icon) {
  return `<section class="card p142-nav-editor">
    <h3><i class="bi ${icon}"></i> ${esc(title)}</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Key</th><th>English</th><th>العربية</th><th>${tr('Order','الترتيب')}</th><th>${tr('Visible','ظاهر')}</th></tr></thead>
      <tbody>${items.map(navigationRow).join('')}</tbody>
    </table></div>
  </section>`;
}

async function renderAdminSettings(panel) {
  if (!panel) return;
  panel.innerHTML = `<div class="state loading"><strong>${esc(tr('Loading menu settings…','جاري تحميل إعدادات القوائم…'))}</strong></div>`;
  try {
    const payload = await api('/client-api/admin/navigation');
    if (!panel.isConnected) return;
    const all = Array.isArray(payload?.items) ? payload.items : [];
    const desktop = all.filter(x => String(x.navigationKey || '').startsWith('desktop-'));
    const web = all.filter(x => !String(x.navigationKey || '').startsWith('desktop-'));

    panel.innerHTML = `
      <div class="p127-tabs p142-settings-tabs" role="tablist">
        <button type="button" class="p127-tab active" data-p142-settings-tab="web"><i class="bi bi-globe2"></i> ${esc(tr('Web menu settings','إعدادات قائمة الويب'))}</button>
        <button type="button" class="p127-tab" data-p142-settings-tab="desktop"><i class="bi bi-windows"></i> ${esc(tr('Desktop application menu','إعدادات قائمة تطبيق الديسكتوب'))}</button>
      </div>
      <div data-p142-settings-panel="web">${navigationTable(web, tr('Web navigation','إدارة قائمة الويب'), 'bi-globe2')}</div>
      <div data-p142-settings-panel="desktop" hidden>${navigationTable(desktop, tr('Desktop navigation','إدارة قائمة تطبيق الديسكتوب'), 'bi-windows')}</div>
      <section class="card p142-settings-save">
        <div class="toolbar">
          <button type="button" class="action" id="p142SaveNavigation"><i class="bi bi-check2-circle"></i> ${esc(tr('Save menu settings','حفظ إعدادات القوائم'))}</button>
        </div>
        <div id="p142NavigationState" aria-live="polite"></div>
      </section>`;

    panel.querySelectorAll('[data-p142-settings-tab]').forEach(button => button.addEventListener('click', () => {
      panel.querySelectorAll('[data-p142-settings-tab]').forEach(x => x.classList.toggle('active', x === button));
      panel.querySelectorAll('[data-p142-settings-panel]').forEach(x => {
        x.hidden = x.dataset.p142SettingsPanel !== button.dataset.p142SettingsTab;
      });
    }));

    panel.querySelector('#p142SaveNavigation')?.addEventListener('click', async event => {
      const button = event.currentTarget;
      const items = [...panel.querySelectorAll('[data-p142-nav-key]')].map(row => ({
        navigationKey: row.dataset.p142NavKey,
        labelEn: row.querySelector('[data-p142-nav-en]')?.value.trim() || '',
        labelAr: row.querySelector('[data-p142-nav-ar]')?.value.trim() || '',
        isEnabled: row.querySelector('[data-p142-nav-enabled]')?.checked === true,
        sortOrder: Number(row.querySelector('[data-p142-nav-order]')?.value || 0)
      }));
      if (items.some(x => !x.labelEn || !x.labelAr || !Number.isInteger(x.sortOrder) || x.sortOrder < 0 || x.sortOrder > 9999)) {
        notify(tr('Check menu names and display order before saving.','راجع أسماء القوائم والترتيب قبل الحفظ.'), 'error');
        return;
      }
      button.disabled = true;
      try {
        await api('/client-api/admin/navigation', { method:'PUT', body:JSON.stringify({items}) });
        notify(tr('Web and Desktop menu settings were saved.','تم حفظ إعدادات قائمة الويب وتطبيق الديسكتوب.'), 'success');
        await renderAdminSettings(panel);
      } catch (error) {
        notify(error.message, 'error', tr('Menu settings','إعدادات القوائم'));
      } finally {
        button.disabled = false;
      }
    });
  } catch (error) {
    panel.innerHTML = `<div class="state error"><strong>${esc(tr('Menu settings failed to load','تعذر تحميل إعدادات القوائم'))}</strong><br>${esc(error.message)}</div>`;
    notify(error.message, 'error');
  }
}
window.MamAdminOwnerSettings = Object.freeze({ render: renderAdminSettings });

function installGlobalSearch() {
  const form = document.getElementById('p126GlobalSearch');
  const input = document.getElementById('p126GlobalSearchInput');
  if (!form || !input || form.dataset.p142SearchBound === '1') return;
  form.dataset.p142SearchBound = '1';

  form.addEventListener('submit', event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    const query = input.value.trim();
    if (query.length < 2) {
      notify(tr('Enter at least two characters to search.','اكتب حرفين على الأقل للبحث.'), 'info');
      input.focus();
      return;
    }

    window.p126PendingSearch = query;
    try {
      const url = new URL(location.href);
      const hash = new URLSearchParams(url.hash.replace(/^#/, ''));
      hash.set('route', 'search');
      hash.set('q', query);
      hash.set('searched', '1');
      url.hash = hash.toString();
      history.replaceState(history.state, '', url.href);
    } catch {}

    try {
      route = 'search';
      render();
    } catch (error) {
      notify(error.message || error, 'error');
      return;
    }
    void runGlobalSearch(query);
  }, true);
}

async function runGlobalSearch(query) {
  for (let attempt = 0; attempt < 60; attempt++) {
    if (typeof route !== 'undefined' && route !== 'search') return;

    const unified = document.getElementById('mamUnifiedQuery');
    const unifiedRun = document.getElementById('mamUnifiedRun');
    if (unified && unifiedRun) {
      unified.value = query;
      document.querySelector('[data-mam-search-mode="text"]')?.click();
      window.p126PendingSearch = '';
      unifiedRun.click();
      return;
    }

    const legacy = document.getElementById('p12SearchQuery');
    if (legacy) {
      legacy.value = query;
      window.p126PendingSearch = '';
      if (typeof p12RunSearch === 'function') await p12RunSearch();
      else document.getElementById('p12SearchButton')?.click();
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  notify(tr('Search page did not become ready. Try again.','صفحة البحث لم تصبح جاهزة. حاول مرة أخرى.'), 'error');
}

function resumeHashSearch() {
  try {
    if (typeof route === 'undefined' || route !== 'search') return;
    const hash = new URLSearchParams(location.hash.replace(/^#/, ''));
    const query = (hash.get('q') || window.p126PendingSearch || '').trim();
    if (query.length >= 2 && hash.get('searched') === '1') void runGlobalSearch(query);
  } catch {}
}

function ensureDownloadButtons() {
  const mac = document.getElementById('p128MacUploaderDownload');
  if (mac) {
    mac.classList.add('mam-download-action');
    wrapDownloadLabel(mac);
  }
  const desktop = document.querySelector('[data-mam-desktop-download],.mam-desktop-download');
  if (desktop) {
    desktop.classList.add('mam-download-action');
    wrapDownloadLabel(desktop);
  }
}

function wrapDownloadLabel(control) {
  if (!control || control.querySelector(':scope > span')) return;
  const textNodes = [...control.childNodes].filter(x => x.nodeType === Node.TEXT_NODE && x.textContent.trim());
  if (!textNodes.length) return;
  const span = document.createElement('span');
  span.textContent = textNodes.map(x => x.textContent.trim()).join(' ');
  textNodes.forEach(x => x.remove());
  control.appendChild(span);
}

function ensureCurationCollectionButton() {
  if (typeof route === 'undefined' || route !== 'curation-actions') return;
  const host = document.getElementById('p12CurationHost');
  if (!host) return;
  const cards = [...host.children].filter(x => x.classList?.contains('card'));
  const collectionCard = cards[1];
  if (!collectionCard || collectionCard.querySelector('#p142ManageCollections')) return;

  const toolbar = collectionCard.querySelector('.toolbar');
  if (!toolbar) return;
  const button = document.createElement('button');
  button.id = 'p142ManageCollections';
  button.type = 'button';
  button.className = 'action';
  button.innerHTML = `<i class="bi bi-collection"></i><span>${esc(tr('Add / manage collections','إضافة / إدارة المجموعات'))}</span>`;
  button.addEventListener('click', () => void openCollectionManager());
  toolbar.prepend(button);
}

async function openCollectionManager() {
  if (document.getElementById('p142CollectionOverlay')) return;
  const overlay = document.createElement('div');
  overlay.id = 'p142CollectionOverlay';
  overlay.className = 'p142-modal-overlay';
  overlay.innerHTML = `<section class="p142-modal" role="dialog" aria-modal="true" aria-labelledby="p142CollectionTitle">
    <header><div><h3 id="p142CollectionTitle">${esc(tr('Collection management','إدارة المجموعات'))}</h3><p>${esc(tr('Create, edit, or delete collections from one place.','إضافة وتعديل وحذف المجموعات من مكان واحد.'))}</p></div><button type="button" data-p142-close aria-label="${esc(tr('Close','إغلاق'))}"><i class="bi bi-x-lg"></i></button></header>
    <div class="p142-collection-editor">
      <input type="hidden" id="p142CollectionId"/>
      <input type="hidden" id="p142CollectionVersion"/>
      <label>English<input id="p142CollectionEn" maxlength="200"/></label>
      <label>العربية<input id="p142CollectionAr" maxlength="200" dir="rtl"/></label>
      <div class="toolbar"><button type="button" class="action" id="p142CollectionSave"><i class="bi bi-check2"></i> ${esc(tr('Save','حفظ'))}</button><button type="button" id="p142CollectionNew">${esc(tr('New','جديد'))}</button></div>
    </div>
    <div id="p142CollectionList"><div class="state loading"><strong>${esc(tr('Loading collections…','جاري تحميل المجموعات…'))}</strong></div></div>
  </section>`;
  document.body.appendChild(overlay);
  const close = () => overlay.remove();
  overlay.querySelector('[data-p142-close]')?.addEventListener('click', close);
  overlay.addEventListener('click', event => { if (event.target === overlay) close(); });
  overlay.querySelector('#p142CollectionNew')?.addEventListener('click', () => fillCollectionEditor(null));
  overlay.querySelector('#p142CollectionSave')?.addEventListener('click', () => void saveCollection());
  await reloadCollectionManager();
}

function fillCollectionEditor(collection) {
  const root = document.getElementById('p142CollectionOverlay');
  if (!root) return;
  root.querySelector('#p142CollectionId').value = collection?.collectionId || '';
  root.querySelector('#p142CollectionVersion').value = collection?.version || '';
  root.querySelector('#p142CollectionEn').value = collection?.nameEn || '';
  root.querySelector('#p142CollectionAr').value = collection?.nameAr || '';
  root.querySelector('#p142CollectionEn').focus();
}

async function reloadCollectionManager() {
  const list = document.getElementById('p142CollectionList');
  if (!list) return;
  try {
    const rows = await api('/client-api/curation/collections');
    if (!list.isConnected) return;
    list.innerHTML = `<div class="p142-collection-list">${(rows || []).map(row => `<div class="p142-collection-row" data-p142-collection="${esc(row.collectionId)}"><div><strong>${esc(window.arabic && row.nameAr ? row.nameAr : row.nameEn)}</strong><small>${Number(row.memberCount || 0)} ${esc(tr('media','وسائط'))} · v${Number(row.version || 0)}</small></div><div class="toolbar"><button type="button" class="action" data-p142-edit><i class="bi bi-pencil-square"></i> ${esc(tr('Edit','تعديل'))}</button><button type="button" class="action p127-danger" data-p142-delete><i class="bi bi-trash3"></i> ${esc(tr('Delete','حذف'))}</button></div></div>`).join('') || `<div class="state empty"><strong>${esc(tr('No collections','لا توجد مجموعات'))}</strong></div>`}</div>`;
    list.querySelectorAll('[data-p142-collection]').forEach(row => {
      const item = (rows || []).find(x => String(x.collectionId) === row.dataset.p142Collection);
      row.querySelector('[data-p142-edit]')?.addEventListener('click', () => fillCollectionEditor(item));
      row.querySelector('[data-p142-delete]')?.addEventListener('click', () => void deleteCollection(item));
    });
    const select = document.getElementById('p12Collection');
    if (select) {
      select.innerHTML = (rows || []).map(x => `<option value="${esc(x.collectionId)}" data-version="${Number(x.version)}">${esc(window.arabic && x.nameAr ? x.nameAr : x.nameEn)} · v${Number(x.version)}</option>`).join('');
    }
  } catch (error) {
    list.innerHTML = `<div class="state error"><strong>${esc(tr('Collections failed to load','تعذر تحميل المجموعات'))}</strong><br>${esc(error.message)}</div>`;
  }
}

async function saveCollection() {
  const root = document.getElementById('p142CollectionOverlay');
  if (!root) return;
  const id = root.querySelector('#p142CollectionId').value;
  const expectedVersion = Number(root.querySelector('#p142CollectionVersion').value || 0);
  const nameEn = root.querySelector('#p142CollectionEn').value.trim();
  const nameAr = root.querySelector('#p142CollectionAr').value.trim();
  if (!nameEn) {
    notify(tr('English collection name is required.','اسم المجموعة بالإنجليزية مطلوب.'), 'error');
    return;
  }
  try {
    if (id) {
      await api(`/client-api/curation/collections/${encodeURIComponent(id)}`, { method:'PUT', body:JSON.stringify({expectedVersion,nameEn,nameAr:nameAr || null}) });
      notify(tr('Collection updated.','تم تعديل المجموعة.'), 'success');
    } else {
      await api('/client-api/curation/collections', { method:'POST', body:JSON.stringify({nameEn,nameAr:nameAr || null}) });
      notify(tr('Collection created.','تمت إضافة المجموعة.'), 'success');
    }
    fillCollectionEditor(null);
    await reloadCollectionManager();
  } catch (error) {
    notify(error.message, 'error');
  }
}

async function deleteCollection(item) {
  if (!item) return;
  const name = window.arabic && item.nameAr ? item.nameAr : item.nameEn;
  if (!confirm(tr(`Delete collection "${name}"?`, `حذف المجموعة "${name}"؟`))) return;
  try {
    await api(`/client-api/curation/collections/${encodeURIComponent(item.collectionId)}?expectedVersion=${Number(item.version)}`, { method:'DELETE' });
    notify(tr('Collection deleted.','تم حذف المجموعة.'), 'success');
    await reloadCollectionManager();
  } catch (error) {
    notify(error.message, 'error');
  }
}

function promoteMessages() {
  document.querySelectorAll('#content .state.error,#content .state.denied,#content .state.degraded,#content .state.info,[data-mam-message-kind="error"],[data-mam-message-kind="info"]').forEach(node => {
    if (!(node instanceof HTMLElement) || node.classList.contains('loading') || node.dataset.p142Popup === '1') return;
    const text = node.textContent?.replace(/\s+/g, ' ').trim();
    if (!text || text.length > 1000) return;
    node.dataset.p142Popup = '1';
    const error = node.classList.contains('error') || node.classList.contains('denied') || node.classList.contains('degraded') || node.dataset.mamMessageKind === 'error';
    notify(text, error ? 'error' : 'info');
  });
}

function activateAdminSettingsTab() {
  if (typeof route === 'undefined' || route !== 'admin' || localStorage.getItem('mam.p127.adminTab') !== 'settings') return;
  const button = document.querySelector('#p127AdminTabs [data-admin-tab="settings"]');
  if (!button || button.classList.contains('active') || button.dataset.p142Clicked === '1') return;
  button.dataset.p142Clicked = '1';
  button.click();
}

function enhance() {
  installGlobalSearch();
  ensureDownloadButtons();
  ensureCurationCollectionButton();
  activateAdminSettingsTab();
  promoteMessages();
  resumeHashSearch();
}

try {
  const previousRender = render;
  render = function(...args) {
    normalizeSettingsRoute();
    const result = previousRender.apply(this, args);
    requestAnimationFrame(enhance);
    return result;
  };
} catch {}

window.addEventListener('error', event => {
  const message = event?.error?.message || event?.message;
  if (message) notify(message, 'error', tr('Application error','خطأ في التطبيق'));
});
window.addEventListener('unhandledrejection', event => {
  const reason = event?.reason;
  const message = reason?.message || (typeof reason === 'string' ? reason : '');
  if (message) notify(message, 'error', tr('Application error','خطأ في التطبيق'));
});

const observer = new MutationObserver(() => requestAnimationFrame(enhance));
observer.observe(document.body, { childList:true, subtree:true, characterData:true });

if (normalizeSettingsRoute()) {
  try { render(); } catch {}
} else {
  enhance();
}
})();
