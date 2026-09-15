(() => {
'use strict';

const nav = document.getElementById('nav');
if (!nav) return;

const adminRoutes = new Set(['admin','settings','categories','references','mediaPermissions']);

function closeMobileNavigation() {
  document.querySelector('.app-shell')?.classList.remove('p128-mobile-open');
}

function syncAdminMenu(key) {
  const menu = nav.querySelector(':scope > .p127-admin-menu');
  if (!menu || menu.hidden) return;
  const inAdmin = adminRoutes.has(key);
  menu.classList.toggle('open', inAdmin);
  const trigger = menu.querySelector(':scope > .p127-admin-trigger');
  trigger?.classList.toggle('active', inAdmin);
  trigger?.setAttribute('aria-expanded', inAdmin ? 'true' : 'false');
}

function activateRoute(key) {
  if (!key || key === 'asset' || typeof render !== 'function' || typeof route === 'undefined') return false;

  route = key;
  syncAdminMenu(key);
  render();
  closeMobileNavigation();
  return true;
}

/*
  P12.12 final navigation owner.

  The application has multiple progressive UI layers which can replace or move
  navigation buttons after the original per-button handlers were attached. A
  document-capture listener survives every DOM reconciliation and executes
  before legacy handlers, so the visible button always owns exactly one route
  transition regardless of which UI layer produced it.
*/
document.addEventListener('click', event => {
  const target = event.target instanceof Element ? event.target : event.target?.parentElement;
  if (!target) return;

  const adminTrigger = target.closest('#nav .p127-admin-trigger');
  if (adminTrigger && nav.contains(adminTrigger)) {
    event.preventDefault();
    event.stopImmediatePropagation();
    const menu = adminTrigger.closest('.p127-admin-menu');
    if (!menu || menu.hidden) return;
    menu.classList.toggle('open');
    adminTrigger.setAttribute('aria-expanded', menu.classList.contains('open') ? 'true' : 'false');
    return;
  }

  const button = target.closest('#nav button[data-route]');
  if (!button || !nav.contains(button) || button.hidden || button.disabled || button.getAttribute('aria-hidden') === 'true') return;

  const key = button.dataset.route || '';
  if (!key || key === 'asset') return;

  event.preventDefault();
  event.stopImmediatePropagation();
  activateRoute(key);
}, true);

/* Decorative layers must never become the click target above the navigation. */
const style = document.createElement('style');
style.dataset.mamNavigationFinal = 'p12.12';
style.textContent = `
.sidebar::before,.sidebar::after,.brand::before,.brand::after{pointer-events:none!important}
#nav{position:relative!important;z-index:50!important;pointer-events:auto!important;isolation:isolate!important}
#nav button,#nav .p127-admin-menu,#nav .p127-admin-trigger,#nav .p127-admin-submenu{position:relative;z-index:51;pointer-events:auto!important}
`;
document.head.appendChild(style);

window.mamNavigationRuntime = Object.freeze({
  version: 'p12.12',
  owner: 'document-capture',
  activateRoute
});
})();
