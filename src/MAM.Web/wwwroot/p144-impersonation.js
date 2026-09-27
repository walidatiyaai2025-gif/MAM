(() => {
'use strict';

const tr = (en, ar) => (document.documentElement.lang === 'ar' || window.arabic) ? ar : en;
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function request(url, options = {}) {
  const response = await fetch(url, {
    cache: 'no-store',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...(options.headers || {}) },
    ...options
  });
  let body = null;
  try { body = await response.json(); } catch { }
  if (!response.ok) {
    const error = new Error(body?.detail || body?.error || `HTTP ${response.status}`);
    error.status = response.status;
    error.body = body;
    throw error;
  }
  return body;
}

function removeBanner() {
  document.getElementById('mamImpersonationBanner')?.remove();
  document.body.classList.remove('mam-impersonating');
}

function renderBanner(status) {
  removeBanner();
  if (!status?.impersonating) return;

  const banner = document.createElement('aside');
  banner.id = 'mamImpersonationBanner';
  banner.className = 'mam-impersonation-banner';
  banner.setAttribute('role', 'status');
  banner.setAttribute('aria-live', 'polite');
  banner.innerHTML = `
    <div class="mam-impersonation-copy">
      <i class="bi bi-person-bounding-box" aria-hidden="true"></i>
      <div>
        <strong>${esc(tr('User view mode', 'وضع الدخول كمستخدم'))}</strong>
        <span>${esc(tr('You are now using the system as', 'أنت الآن داخل النظام كمستخدم'))} <bdi>${esc(status.userName || '—')}</bdi></span>
        <small>${esc(tr('Administrator account', 'حساب المدير'))}: <bdi>${esc(status.originalUserName || '—')}</bdi></small>
      </div>
    </div>
    <button type="button" class="mam-impersonation-stop">
      <i class="bi bi-box-arrow-in-left" aria-hidden="true"></i>
      ${esc(tr('Return to administrator', 'العودة إلى حساب المدير'))}
    </button>`;

  document.body.prepend(banner);
  document.body.classList.add('mam-impersonating');

  banner.querySelector('.mam-impersonation-stop')?.addEventListener('click', async event => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await request('/auth/impersonate/stop', { method: 'POST' });
      location.assign('/app#route=admin');
    } catch (error) {
      button.disabled = false;
      if (window.MamPopup?.notify) {
        window.MamPopup.notify(
          error.body?.detail || error.message,
          'error',
          tr('Could not return to administrator', 'تعذر الرجوع إلى حساب المدير'));
      } else {
        alert(error.body?.detail || error.message);
      }
    }
  });
}

async function refresh() {
  try {
    const status = await request('/auth/status');
    renderBanner(status);
  } catch {
    removeBanner();
  }
}

window.mamImpersonation = Object.freeze({ refresh });
window.addEventListener('pageshow', refresh);
window.addEventListener('focus', refresh);
void refresh();
})();
