(() => {
'use strict';

const nav = document.getElementById('nav');
if (!nav) return;

const adminRoutes = new Set(['admin','settings','categories','references','mediaPermissions']);

function closeMobileNavigation() {
  document.querySelector('.app-shell')?.classList.remove('p128-mobile-open');
}

function adminMenu() {
  return nav.querySelector(':scope > .p127-admin-menu');
}

function setAdminOpen(open, activeRoute = '') {
  const menu = adminMenu();
  if (!menu || menu.hidden) return;
  const trigger = menu.querySelector(':scope > .p127-admin-trigger');
  menu.classList.toggle('open', !!open);
  trigger?.setAttribute('aria-expanded', open ? 'true' : 'false');
  const inAdmin = adminRoutes.has(activeRoute);
  trigger?.classList.toggle('active', inAdmin);
  menu.querySelectorAll('.p127-admin-submenu button[data-route]').forEach(button => {
    const active = button.dataset.route === activeRoute;
    button.classList.toggle('active', active);
    if (active) button.setAttribute('aria-current','page');
    else button.removeAttribute('aria-current');
  });
}

function syncAdminMenu(key) {
  setAdminOpen(adminRoutes.has(key), key);
}

function activateRoute(key) {
  if (!key || key === 'asset' || typeof render !== 'function' || typeof route === 'undefined') return false;
  route = key;
  syncAdminMenu(key);
  render();
  if (!adminRoutes.has(key)) setAdminOpen(false, key);
  closeMobileNavigation();
  return true;
}

/*
  P12.13 final navigation owner.
  The final handler owns both primary routes and nested Administration routes.
  Parent clicks toggle only the submenu; child clicks always navigate.
*/
document.addEventListener('click', event => {
  const target = event.target instanceof Element ? event.target : event.target?.parentElement;
  if (!target) return;

  const routeButton = target.closest('#nav button[data-route]');
  if (routeButton && nav.contains(routeButton) && !routeButton.hidden && !routeButton.disabled && routeButton.getAttribute('aria-hidden') !== 'true') {
    const key = routeButton.dataset.route || '';
    if (!key || key === 'asset') return;
    event.preventDefault();
    event.stopImmediatePropagation();
    activateRoute(key);
    return;
  }

  const adminTrigger = target.closest('#nav .p127-admin-trigger');
  if (adminTrigger && nav.contains(adminTrigger)) {
    event.preventDefault();
    event.stopImmediatePropagation();
    const menu = adminTrigger.closest('.p127-admin-menu');
    if (!menu || menu.hidden) return;
    const open = !menu.classList.contains('open');
    setAdminOpen(open, typeof route !== 'undefined' ? route : '');
  }
}, true);

const style = document.createElement('style');
style.dataset.mamNavigationFinal = 'p12.13';
style.textContent = `
.sidebar{display:flex!important;flex-direction:column!important;box-sizing:border-box!important}
.sidebar>.brand{flex:0 0 auto!important}
.sidebar::before,.sidebar::after,.brand::before,.brand::after{pointer-events:none!important}
#nav{position:relative!important;z-index:60!important;flex:1 1 auto!important;min-height:0!important;max-height:none!important;padding-block-end:8px!important;overflow-y:auto!important;overflow-x:hidden!important;pointer-events:auto!important;isolation:isolate!important;touch-action:manipulation!important}
#nav>button,#nav .p127-admin-menu,#nav .p127-admin-trigger,#nav .p127-admin-submenu,#nav .p127-admin-submenu button{position:relative!important;z-index:61!important;pointer-events:auto!important}
.p127-admin-menu{width:100%!important;margin:0!important}
.p127-admin-trigger{width:100%!important;min-height:48px!important;cursor:pointer!important}
.p127-admin-trigger .bi-chevron-down{margin-inline-start:auto!important;transition:transform .18s ease!important}
.p127-admin-menu.open>.p127-admin-trigger .bi-chevron-down{transform:rotate(180deg)!important}
.p127-admin-submenu{display:none!important;position:relative!important;inset:auto!important;width:auto!important;margin:4px 8px 8px!important;padding:5px!important;border:1px solid rgba(255,255,255,.12)!important;border-inline-start:3px solid #d7ad49!important;border-radius:10px!important;background:rgba(0,0,0,.14)!important;box-shadow:none!important;overflow:visible!important}
.p127-admin-menu.open>.p127-admin-submenu{display:grid!important;gap:3px!important}
.p127-admin-submenu button[data-route]{width:100%!important;min-height:40px!important;padding:7px 10px!important;border-radius:8px!important;font-size:12px!important;background:transparent!important;text-align:start!important}
.p127-admin-submenu button[data-route]:hover{background:rgba(255,255,255,.08)!important}
.p127-admin-submenu button[data-route].active{background:rgba(201,152,47,.16)!important;color:#f6d77b!important;box-shadow:inset -3px 0 0 #d7ad49!important}
.app-shell.p127-sidebar-collapsed .sidebar{overflow:visible!important}
.app-shell.p127-sidebar-collapsed #nav{overflow:visible!important}
.app-shell.p127-sidebar-collapsed .p127-admin-menu.open>.p127-admin-submenu{position:absolute!important;inset-inline-start:66px!important;top:0!important;width:240px!important;margin:0!important;z-index:5000!important;background:#062b4c!important;box-shadow:0 14px 40px rgba(0,0,0,.28)!important}
.sidebar>.nonprod{position:relative!important;inset:auto!important;left:auto!important;right:auto!important;top:auto!important;bottom:auto!important;width:auto!important;flex:0 0 auto!important;margin:8px 8px 0!important;z-index:1!important;pointer-events:none!important}
.sidebar>.nonprod *{pointer-events:none!important}
.p128-mobile-scrim,.p131-sidebar-scrim{pointer-events:none!important}
.app-shell.p128-mobile-open>.p128-mobile-scrim,.app-shell.p128-mobile-open>.p131-sidebar-scrim{pointer-events:auto!important}
`;
document.head.appendChild(style);

function hitTestReport() {
  return [...nav.querySelectorAll('button[data-route]')]
    .filter(button => !button.hidden && !button.disabled && button.getAttribute('aria-hidden') !== 'true')
    .map(button => {
      const rect = button.getBoundingClientRect();
      const x = Math.min(window.innerWidth - 1, Math.max(0, rect.left + rect.width / 2));
      const y = Math.min(window.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
      const top = document.elementFromPoint(x, y);
      return {
        route: button.dataset.route || '',
        visible: rect.bottom > 0 && rect.top < window.innerHeight && rect.right > 0 && rect.left < window.innerWidth,
        topTag: top?.tagName || null,
        topClass: top?.className || null,
        clickable: !!top && (top === button || button.contains(top))
      };
    });
}

window.mamNavigationRuntime = Object.freeze({
  version: 'p12.13-submenu',
  owner: 'document-capture',
  activateRoute,
  hitTestReport
});
})();
