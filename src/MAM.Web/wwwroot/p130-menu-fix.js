(() => {
'use strict';

const nav = document.getElementById('nav');
if (!nav) return;

const adminRoutes = ['admin','settings','categories','references','mediaPermissions'];
const primaryOrder = ['dashboard','library','curation-actions','ingest','upload','queue','reports','protection'];
const trailingOrder = ['admin-actions','search'];
const labels = {
  dashboard:['Dashboard','لوحة التحكم'],
  library:['Media Library','مكتبة الوسائط'],
  'curation-actions':['Curation Actions','إجراءات التهيئة'],
  ingest:['New Ingest','إدخال جديد'],
  upload:['Add Media','إضافة ميديا'],
  queue:['Processing Queue','قائمة المعالجة'],
  reports:['Reports','التقارير'],
  protection:['Backup Protection','حماية النسخة الاحتياطية'],
  admin:['Administration','إدارة النظام'],
  settings:['System Settings','إعدادات النظام'],
  categories:['Categories','التصنيفات'],
  references:['Reference Library','مكتبة المراجع'],
  mediaPermissions:['Media Permissions','صلاحيات أنواع الوسائط'],
  'admin-actions':['Management Actions','إجراءات الإدارة'],
  search:['Content Search','البحث في المحتوى'],
  asset:['Asset Details','تفاصيل الأصل']
};

let reconciling = false;
let scheduled = false;
let observer = null;

const isArabic = () => (typeof arabic !== 'undefined' ? arabic : document.documentElement.lang === 'ar');
const labelFor = key => labels[key]?.[isArabic() ? 1 : 0] || key;

function preferred(nodes) {
  return nodes.find(x => !x.hidden && !x.classList.contains('p129-duplicate-route')) ||
         nodes.find(x => !x.classList.contains('p129-duplicate-route')) ||
         nodes[0] || null;
}

function setButtonLabel(button, key) {
  if (!button) return;
  const text = labelFor(key);
  const span = button.querySelector('.p127-nav-label');
  if (span) span.textContent = text;
  else if (!button.hidden) button.textContent = text;
  button.setAttribute('aria-label', text);
  button.setAttribute('title', text);
  button.type = 'button';
}

function ensureAdminMenu() {
  const menus = [...nav.querySelectorAll(':scope > .p127-admin-menu')];
  let menu = menus[0] || null;

  if (!menu) {
    menu = document.createElement('div');
    menu.className = 'p127-admin-menu';
    menu.innerHTML = '<button type="button" class="p127-admin-trigger"><span class="p127-nav-icon"><i class="bi bi-sliders"></i></span><span class="p127-nav-label"></span><i class="bi bi-chevron-down"></i></button><div class="p127-admin-submenu"></div>';
    nav.appendChild(menu);
  }

  menus.slice(1).forEach(extra => extra.remove());

  let trigger = menu.querySelector(':scope > .p127-admin-trigger');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'p127-admin-trigger';
    trigger.innerHTML = '<span class="p127-nav-icon"><i class="bi bi-sliders"></i></span><span class="p127-nav-label"></span><i class="bi bi-chevron-down"></i>';
    menu.prepend(trigger);
  }

  let submenu = menu.querySelector(':scope > .p127-admin-submenu');
  if (!submenu) {
    submenu = document.createElement('div');
    submenu.className = 'p127-admin-submenu';
    menu.appendChild(submenu);
  }

  const menuLabel = isArabic() ? 'إعدادات مسؤول النظام' : 'System Administrator Settings';
  const label = trigger.querySelector('.p127-nav-label');
  if (label) label.textContent = menuLabel;
  trigger.type = 'button';
  trigger.setAttribute('aria-label', menuLabel);
  trigger.setAttribute('title', menuLabel);
  trigger.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');

  return {menu, trigger, submenu};
}

function reconcile() {
  if (reconciling) return;
  reconciling = true;
  if (observer) observer.disconnect();

  try {
    const byRoute = new Map();
    [...nav.querySelectorAll('button[data-route]')].forEach(button => {
      const key = button.dataset.route;
      if (!key) return;
      if (!byRoute.has(key)) byRoute.set(key, []);
      byRoute.get(key).push(button);
    });

    const canonical = new Map();
    byRoute.forEach((nodes, key) => {
      const keep = preferred(nodes);
      if (!keep) return;
      canonical.set(key, keep);
      nodes.forEach(node => {
        if (node !== keep) node.remove();
      });
      keep.classList.remove('p129-duplicate-route');
      keep.removeAttribute('aria-hidden');
      if (key === 'asset') {
        keep.hidden = true;
        keep.setAttribute('aria-hidden','true');
        keep.tabIndex = -1;
      } else if (keep.tabIndex < 0) {
        keep.tabIndex = 0;
      }
      setButtonLabel(keep, key);
    });

    const {menu, trigger, submenu} = ensureAdminMenu();

    adminRoutes.forEach(key => {
      const button = canonical.get(key);
      if (button && button.parentElement !== submenu) submenu.appendChild(button);
    });

    const visibleAdmin = adminRoutes.some(key => {
      const button = canonical.get(key);
      return button && !button.hidden;
    });
    menu.hidden = !visibleAdmin;

    primaryOrder.forEach(key => {
      const button = canonical.get(key);
      if (button && !button.hidden) nav.appendChild(button);
    });

    if (!menu.hidden) nav.appendChild(menu);

    trailingOrder.forEach(key => {
      const button = canonical.get(key);
      if (button && !button.hidden) nav.appendChild(button);
    });

    const asset = canonical.get('asset');
    if (asset && asset.parentElement === nav) nav.appendChild(asset);

    const currentRoute = typeof route !== 'undefined' ? route : '';
    const inAdmin = adminRoutes.includes(currentRoute);
    if (inAdmin && !menu.hidden) menu.classList.add('open');
    trigger.classList.toggle('active', inAdmin);
    trigger.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');
  } finally {
    reconciling = false;
    if (observer) observer.observe(nav, {childList:true, subtree:true, attributes:true, attributeFilter:['hidden','class','aria-hidden']});
  }
}

function schedule() {
  if (scheduled) return;
  scheduled = true;
  requestAnimationFrame(() => {
    scheduled = false;
    reconcile();
  });
}

/*
  Own navigation clicks at the container level. Earlier versions relied on
  per-button listeners captured before later UI layers inserted/reconciled
  buttons. Once a duplicate replacement became canonical, that button could
  have no route listener at all. Delegation makes every current/future route
  button work and prevents duplicate handlers from double-rendering.
*/
nav.addEventListener('click', event => {
  const trigger = event.target.closest('.p127-admin-trigger');
  if (trigger && nav.contains(trigger)) {
    event.preventDefault();
    event.stopImmediatePropagation();
    const menu = trigger.closest('.p127-admin-menu');
    if (!menu || menu.hidden) return;
    menu.classList.toggle('open');
    trigger.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');
    return;
  }

  const button = event.target.closest('button[data-route]');
  if (!button || !nav.contains(button) || button.hidden || button.disabled) return;

  const key = button.dataset.route || '';
  if (!key || key === 'asset') return;

  const routeExists = typeof pages !== 'undefined' && Object.prototype.hasOwnProperty.call(pages, key);
  const canRender = typeof render === 'function';
  if (!routeExists || !canRender) return;

  event.preventDefault();
  event.stopImmediatePropagation();

  route = key;

  const menu = nav.querySelector(':scope > .p127-admin-menu');
  if (menu && !menu.hidden) {
    menu.classList.toggle('open', adminRoutes.includes(key));
    menu.querySelector('.p127-admin-trigger')?.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');
  }

  render();
  schedule();
}, true);

nav.addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  const menu = nav.querySelector(':scope > .p127-admin-menu');
  if (!menu?.classList.contains('open')) return;
  menu.classList.remove('open');
  const trigger = menu.querySelector('.p127-admin-trigger');
  trigger?.setAttribute('aria-expanded','false');
  trigger?.focus();
});

document.getElementById('languageButton')?.addEventListener('click', () => setTimeout(schedule, 0));
window.addEventListener('hashchange', schedule);

observer = new MutationObserver(schedule);
observer.observe(nav, {childList:true, subtree:true, attributes:true, attributeFilter:['hidden','class','aria-hidden']});

reconcile();
})();
