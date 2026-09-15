(() => {
'use strict';

const nav = document.getElementById('nav');
if (!nav) return;

const sidebar = nav.closest('.sidebar');
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
  P12.13 final navigation owner.

  Production proved that the absolute .nonprod environment card can overlap the
  scrollable navigation when the effective browser height is reduced (for
  example when DevTools is docked). That card then becomes the pointer hit-test
  target even though the route buttons remain visible underneath it.

  The fix is structural rather than another click-handler patch: the sidebar is
  a vertical flex container, the navigation owns the remaining height and
  scrolls inside it, and the environment card participates in normal layout at
  the bottom. Non-interactive decoration is explicitly removed from hit testing.
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

const style = document.createElement('style');
style.dataset.mamNavigationFinal = 'p12.13';
style.textContent = `
.sidebar{
  display:flex!important;
  flex-direction:column!important;
  box-sizing:border-box!important;
}
.sidebar>.brand{
  flex:0 0 auto!important;
}
.sidebar::before,.sidebar::after,.brand::before,.brand::after{
  pointer-events:none!important;
}
#nav{
  position:relative!important;
  z-index:60!important;
  flex:1 1 auto!important;
  min-height:0!important;
  max-height:none!important;
  padding-block-end:8px!important;
  overflow-y:auto!important;
  overflow-x:hidden!important;
  pointer-events:auto!important;
  isolation:isolate!important;
  touch-action:manipulation!important;
}
#nav button,#nav .p127-admin-menu,#nav .p127-admin-trigger,#nav .p127-admin-submenu{
  position:relative;
  z-index:61;
  pointer-events:auto!important;
}
.sidebar>.nonprod{
  position:relative!important;
  inset:auto!important;
  left:auto!important;
  right:auto!important;
  top:auto!important;
  bottom:auto!important;
  width:auto!important;
  flex:0 0 auto!important;
  margin:8px 8px 0!important;
  z-index:1!important;
  pointer-events:none!important;
}
.sidebar>.nonprod *{
  pointer-events:none!important;
}
.p128-mobile-scrim,.p131-sidebar-scrim{
  pointer-events:none!important;
}
.app-shell.p128-mobile-open>.p128-mobile-scrim,
.app-shell.p128-mobile-open>.p131-sidebar-scrim{
  pointer-events:auto!important;
}
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
  version: 'p12.13',
  owner: 'document-capture',
  activateRoute,
  hitTestReport
});
})();
