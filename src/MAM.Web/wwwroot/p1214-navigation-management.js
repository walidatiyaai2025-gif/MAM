(() => {
'use strict';

const defaults = [
  {navigationKey:'dashboard',routeKey:'dashboard',parentKey:null,itemType:'route',labelEn:'Dashboard',labelAr:'لوحة التحكم',isEnabled:true,sortOrder:10},
  {navigationKey:'library',routeKey:'library',parentKey:null,itemType:'route',labelEn:'Media Library',labelAr:'مكتبة الوسائط',isEnabled:true,sortOrder:20},
  {navigationKey:'curation-actions',routeKey:'curation-actions',parentKey:null,itemType:'route',labelEn:'Curation Actions',labelAr:'إجراءات التهيئة',isEnabled:true,sortOrder:30},
  {navigationKey:'ingest',routeKey:'ingest',parentKey:null,itemType:'route',labelEn:'New Ingest',labelAr:'إدخال جديد',isEnabled:true,sortOrder:40},
  {navigationKey:'upload',routeKey:'upload',parentKey:null,itemType:'route',labelEn:'Add Media',labelAr:'إضافة ميديا',isEnabled:true,sortOrder:50},
  {navigationKey:'queue',routeKey:'queue',parentKey:null,itemType:'route',labelEn:'Processing Queue',labelAr:'قائمة المعالجة',isEnabled:true,sortOrder:60},
  {navigationKey:'reports',routeKey:'reports',parentKey:null,itemType:'route',labelEn:'Reports',labelAr:'التقارير',isEnabled:true,sortOrder:70},
  {navigationKey:'protection',routeKey:'protection',parentKey:null,itemType:'route',labelEn:'Backup Protection',labelAr:'حماية النسخة الاحتياطية',isEnabled:true,sortOrder:80},
  {navigationKey:'system-admin',routeKey:null,parentKey:null,itemType:'group',labelEn:'System Administrator Settings',labelAr:'إعدادات مسؤول النظام',isEnabled:true,sortOrder:90},
  {navigationKey:'admin',routeKey:'admin',parentKey:'system-admin',itemType:'route',labelEn:'Administration',labelAr:'الإدارة',isEnabled:true,sortOrder:10},
  {navigationKey:'settings',routeKey:'settings',parentKey:'system-admin',itemType:'route',labelEn:'Settings',labelAr:'الإعدادات',isEnabled:true,sortOrder:20},
  {navigationKey:'categories',routeKey:'categories',parentKey:'system-admin',itemType:'route',labelEn:'Categories',labelAr:'التصنيفات',isEnabled:true,sortOrder:30},
  {navigationKey:'references',routeKey:'references',parentKey:'system-admin',itemType:'route',labelEn:'Reference Library',labelAr:'مكتبة المراجع',isEnabled:true,sortOrder:40},
  {navigationKey:'mediaPermissions',routeKey:'mediaPermissions',parentKey:'system-admin',itemType:'route',labelEn:'Media Type Permissions',labelAr:'صلاحيات أنواع الوسائط',isEnabled:true,sortOrder:50},
  {navigationKey:'admin-actions',routeKey:'admin-actions',parentKey:null,itemType:'route',labelEn:'Administration Actions',labelAr:'إجراءات الإدارة',isEnabled:true,sortOrder:100},
  {navigationKey:'search',routeKey:'search',parentKey:null,itemType:'route',labelEn:'Content Search',labelAr:'البحث في المحتوى',isEnabled:true,sortOrder:110}
];

let items = defaults.map(x => ({...x}));
let loaded = false;
let loadError = null;
let applying = false;
let enhanceScheduled = false;

const nav = document.getElementById('nav');
const content = document.getElementById('content');
const isArabic = () => typeof arabic !== 'undefined' ? !!arabic : document.documentElement.lang === 'ar';
const text = (en, ar) => isArabic() ? ar : en;
const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));

function mergeItems(serverItems) {
  const byKey = new Map(defaults.map(x => [x.navigationKey, {...x}]));
  (Array.isArray(serverItems) ? serverItems : []).forEach(row => {
    const key = row?.navigationKey;
    if (!byKey.has(key)) return;
    byKey.set(key, {...byKey.get(key), ...row});
  });
  items = [...byKey.values()];
}

function routeElement(item) {
  if (!nav) return null;
  if (item.itemType === 'group' && item.navigationKey === 'system-admin')
    return nav.querySelector(':scope > .p127-admin-menu');
  if (!item.routeKey) return null;
  return nav.querySelector(`[data-route="${CSS.escape(item.routeKey)}"]`);
}

function labelElement(item, element) {
  if (!element) return null;
  if (item.itemType === 'group')
    return element.querySelector(':scope > .p127-admin-trigger .p127-nav-label') || element.querySelector(':scope > .p127-admin-trigger');
  return element.querySelector('.p127-nav-label') || element;
}

function applyNavigation() {
  if (!nav || applying) return;
  applying = true;
  try {
    const ar = isArabic();
    for (const item of items) {
      const element = routeElement(item);
      if (!element) continue;

      element.classList.toggle('p1214-config-hidden', !item.isEnabled);
      element.style.order = String(Number(item.sortOrder) || 0);

      const label = labelElement(item, element);
      const value = ar ? item.labelAr : item.labelEn;
      if (label && label.textContent !== value) label.textContent = value;

      element.dataset.p1214NavigationKey = item.navigationKey;
    }
  } finally {
    applying = false;
  }
}

async function loadNavigation() {
  try {
    const response = await fetch('/client-api/admin/navigation', {headers:{Accept:'application/json'}, cache:'no-store'});
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const payload = await response.json();
    mergeItems(payload.items);
    loaded = true;
    loadError = null;
  } catch (error) {
    loaded = true;
    loadError = error;
    mergeItems([]);
  }
  applyNavigation();
  scheduleEnhance();
}

function settingsTabsMarkup() {
  return `
    <div class="p127-tabs p1214-settings-tabs" id="p1214SettingsTabs" role="tablist">
      <button type="button" class="p127-tab active" data-tab="general" role="tab" aria-selected="true">${text('General','عام')}</button>
      <button type="button" class="p127-tab" data-tab="navigation" role="tab" aria-selected="false">${text('Menu management','إدارة القائمة')}</button>
    </div>`;
}

function rowMarkup(item) {
  const group = item.parentKey === 'system-admin' ? text('System Administrator submenu','القائمة الفرعية لمسؤول النظام') : text('Main menu','القائمة الرئيسية');
  const kind = item.itemType === 'group' ? text('Menu group','مجموعة قائمة') : text('Link','رابط');
  return `
    <div class="p1214-nav-row" data-navigation-key="${esc(item.navigationKey)}" data-parent-key="${esc(item.parentKey || '')}">
      <div class="p1214-nav-identity">
        <strong>${esc(isArabic() ? item.labelAr : item.labelEn)}</strong>
        <small>${esc(group)} · ${esc(kind)}</small>
      </div>
      <label class="p1214-switch-field">
        <span>${text('Visible','ظاهر')}</span>
        <input type="checkbox" data-field="enabled" ${item.isEnabled ? 'checked' : ''} />
      </label>
      <label class="p1214-field">
        <span>${text('English label','الاسم الإنجليزي')}</span>
        <input type="text" dir="ltr" data-field="labelEn" maxlength="100" value="${esc(item.labelEn)}" />
      </label>
      <label class="p1214-field">
        <span>${text('Arabic label','الاسم العربي')}</span>
        <input type="text" dir="rtl" data-field="labelAr" maxlength="100" value="${esc(item.labelAr)}" />
      </label>
      <div class="p1214-order-field">
        <span>${text('Order','الترتيب')}</span>
        <div class="p1214-order-controls">
          <button type="button" data-move="up" title="${text('Move up','تحريك لأعلى')}" aria-label="${text('Move up','تحريك لأعلى')}"><i class="bi bi-chevron-up"></i></button>
          <input type="number" min="0" max="9999" step="10" data-field="sortOrder" value="${Number(item.sortOrder) || 0}" />
          <button type="button" data-move="down" title="${text('Move down','تحريك لأسفل')}" aria-label="${text('Move down','تحريك لأسفل')}"><i class="bi bi-chevron-down"></i></button>
        </div>
      </div>
    </div>`;
}

function editorMarkup() {
  const ordered = [...items].sort((a,b) => {
    const ag = a.parentKey || '';
    const bg = b.parentKey || '';
    if (ag !== bg) return ag.localeCompare(bg);
    return Number(a.sortOrder) - Number(b.sortOrder) || a.navigationKey.localeCompare(b.navigationKey);
  });

  return `
    <div class="p1214-editor-head">
      <div>
        <h3><i class="bi bi-list-nested"></i> ${text('Menu management','إدارة القائمة')}</h3>
        <p>${text('Control visibility, Arabic and English labels, and menu order. This changes navigation presentation only; existing permissions still control access.','تحكم في الإظهار والإخفاء، ومسميات العربية والإنجليزية، وترتيب القائمة. هذا يغيّر عرض القائمة فقط وتظل الصلاحيات الحالية هي المسؤولة عن الوصول.')}</p>
      </div>
      <div class="p1214-editor-actions">
        <button type="button" class="action" data-p1214-reset><i class="bi bi-arrow-counterclockwise"></i> ${text('Reset form','استعادة الافتراضي')}</button>
        <button type="button" class="action p1214-save" data-p1214-save><i class="bi bi-floppy"></i> ${text('Save menu','حفظ القائمة')}</button>
      </div>
    </div>
    <div class="p1214-status" data-p1214-status aria-live="polite">${loadError ? text('The saved configuration could not be loaded; defaults are shown until the service is available.','تعذر تحميل الإعدادات المحفوظة؛ يتم عرض القيم الافتراضية حتى تتوفر الخدمة.') : ''}</div>
    <div class="p1214-nav-list">${ordered.map(rowMarkup).join('')}</div>`;
}

function readEditor(panel) {
  return [...panel.querySelectorAll('.p1214-nav-row')].map(row => ({
    navigationKey: row.dataset.navigationKey,
    labelEn: row.querySelector('[data-field="labelEn"]')?.value.trim() || '',
    labelAr: row.querySelector('[data-field="labelAr"]')?.value.trim() || '',
    isEnabled: !!row.querySelector('[data-field="enabled"]')?.checked,
    sortOrder: Math.max(0, Math.min(9999, Number(row.querySelector('[data-field="sortOrder"]')?.value || 0)))
  }));
}

function setStatus(panel, message, kind = '') {
  const host = panel.querySelector('[data-p1214-status]');
  if (!host) return;
  host.className = `p1214-status ${kind}`.trim();
  host.textContent = message;
}

function moveRow(panel, row, direction) {
  const parentKey = row.dataset.parentKey || '';
  const rows = [...panel.querySelectorAll(`.p1214-nav-row[data-parent-key="${CSS.escape(parentKey)}"]`)]
    .sort((a,b) => Number(a.querySelector('[data-field="sortOrder"]').value) - Number(b.querySelector('[data-field="sortOrder"]').value));
  const index = rows.indexOf(row);
  const otherIndex = direction === 'up' ? index - 1 : index + 1;
  if (index < 0 || otherIndex < 0 || otherIndex >= rows.length) return;
  const a = row.querySelector('[data-field="sortOrder"]');
  const b = rows[otherIndex].querySelector('[data-field="sortOrder"]');
  const av = a.value;
  a.value = b.value;
  b.value = av;
}

function bindEditor(panel) {
  panel.querySelectorAll('[data-move]').forEach(button => button.addEventListener('click', () => {
    const row = button.closest('.p1214-nav-row');
    if (row) moveRow(panel, row, button.dataset.move);
  }));

  panel.querySelector('[data-p1214-reset]')?.addEventListener('click', () => {
    const savedItems = items;
    items = defaults.map(x => ({...x}));
    panel.innerHTML = editorMarkup();
    items = savedItems;
    bindEditor(panel);
    setStatus(panel, text('Default values restored in the form. Select Save menu to apply them.','تمت استعادة القيم الافتراضية في النموذج. اضغط حفظ القائمة لتطبيقها.'), 'info');
  });

  panel.querySelector('[data-p1214-save]')?.addEventListener('click', async event => {
    const button = event.currentTarget;
    const updates = readEditor(panel);
    if (updates.some(x => !x.labelEn || !x.labelAr)) {
      setStatus(panel, text('Arabic and English labels are required for every menu item.','يجب إدخال الاسم العربي والإنجليزي لكل عنصر في القائمة.'), 'error');
      return;
    }

    button.disabled = true;
    setStatus(panel, text('Saving menu configuration…','جاري حفظ إعدادات القائمة…'), 'loading');
    try {
      const response = await fetch('/client-api/admin/navigation', {
        method:'PUT',
        headers:{'Content-Type':'application/json','Accept':'application/json'},
        body:JSON.stringify({items:updates})
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.detail || `HTTP ${response.status}`);

      const currentByKey = new Map(items.map(x => [x.navigationKey, x]));
      items = updates.map(update => ({...currentByKey.get(update.navigationKey), ...update}));
      applyNavigation();
      setStatus(panel, text('Menu configuration saved and applied.','تم حفظ إعدادات القائمة وتطبيقها.'), 'success');
      panel.querySelectorAll('.p1214-nav-row').forEach(row => {
        const item = items.find(x => x.navigationKey === row.dataset.navigationKey);
        const strong = row.querySelector('.p1214-nav-identity strong');
        if (strong && item) strong.textContent = isArabic() ? item.labelAr : item.labelEn;
      });
    } catch (error) {
      setStatus(panel, `${text('Save failed','فشل الحفظ')}: ${error.message}`, 'error');
    } finally {
      button.disabled = false;
    }
  });
}

function bindTabs(root, general, navigation) {
  root.querySelectorAll('[data-tab]').forEach(button => button.addEventListener('click', () => {
    const key = button.dataset.tab;
    root.querySelectorAll('[data-tab]').forEach(x => {
      const active = x === button;
      x.classList.toggle('active', active);
      x.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    general.hidden = key !== 'general';
    navigation.hidden = key !== 'navigation';
  }));
}

function enhanceSettingsPage() {
  if (!loaded || !content || typeof route === 'undefined' || route !== 'settings') return;
  if (content.dataset.p1214Settings === '1') return;

  const lead = content.querySelector(':scope > .lead');
  const currentChildren = [...content.children].filter(x => x !== lead);
  if (!currentChildren.length) return;

  content.dataset.p1214Settings = '1';
  const tabsHost = document.createElement('div');
  tabsHost.innerHTML = settingsTabsMarkup().trim();
  const tabs = tabsHost.firstElementChild;

  const general = document.createElement('section');
  general.className = 'p1214-settings-panel';
  general.dataset.panel = 'general';
  currentChildren.forEach(node => general.appendChild(node));

  const navigation = document.createElement('section');
  navigation.className = 'p1214-settings-panel';
  navigation.dataset.panel = 'navigation';
  navigation.hidden = true;
  navigation.innerHTML = editorMarkup();

  if (lead) lead.after(tabs, general, navigation);
  else content.prepend(tabs, general, navigation);

  bindTabs(tabs, general, navigation);
  bindEditor(navigation);
}

function scheduleEnhance() {
  if (!loaded || enhanceScheduled) return;
  enhanceScheduled = true;
  requestAnimationFrame(() => {
    enhanceScheduled = false;
    applyNavigation();
    enhanceSettingsPage();
  });
}

if (nav) {
  const navObserver = new MutationObserver(scheduleEnhance);
  navObserver.observe(nav, {subtree:true, childList:true, characterData:true, attributes:true, attributeFilter:['class','hidden','aria-hidden']});
}
if (content) {
  const contentObserver = new MutationObserver(scheduleEnhance);
  contentObserver.observe(content, {childList:true, subtree:false});
}

document.getElementById('languageButton')?.addEventListener('click', () => requestAnimationFrame(scheduleEnhance));
window.addEventListener('hashchange', scheduleEnhance);

window.mamNavigationPreferences = Object.freeze({
  version:'p12.14',
  apply:applyNavigation,
  reload:loadNavigation,
  getItems:() => items.map(x => ({...x}))
});

loadNavigation();
})();
