(() => {
'use strict';

const query = new URLSearchParams(location.search);
let stored = '';
try { stored = localStorage.getItem('mam.language') || ''; } catch { }
let language = ['ar','en'].includes(query.get('lang')) ? query.get('lang') : (['ar','en'].includes(stored) ? stored : 'ar');

const pair = (en, ar) => language === 'en' ? en : ar;
const setText = (selector, en, ar) => {
  const element = document.querySelector(selector);
  if (element) element.textContent = pair(en, ar);
};
const setTexts = (selector, values) => document.querySelectorAll(selector).forEach((element, index) => {
  if (values[index]) element.textContent = pair(values[index][0], values[index][1]);
});
const localPathWithLanguage = (path, value = language) => {
  const target = new URL(path, location.origin);
  target.searchParams.set('lang', value);
  return target.pathname + target.search + target.hash;
};

function syncLocation() {
  const url = new URL(location.href);
  url.searchParams.set('lang', language);
  history.replaceState(history.state, '', url);
  try { localStorage.setItem('mam.language', language); } catch { }
}

function updateLandingLinks() {
  document.querySelectorAll('a[href^="/auth/login"],a[href^="/app"]').forEach(link => {
    link.href = localPathWithLanguage(link.getAttribute('href') || '/auth/login');
  });
}

function applyLandingLanguage() {
  const button = document.getElementById('landingLanguage');
  if (!button) return;
  const current = document.documentElement.lang === 'en' ? 'en' : 'ar';
  if (current !== language) button.click();
  syncLocation();
  requestAnimationFrame(updateLandingLinks);
  setTimeout(updateLandingLinks, 700);
  button.addEventListener('click', () => {
    requestAnimationFrame(() => {
      language = document.documentElement.lang === 'en' ? 'en' : 'ar';
      syncLocation();
      updateLandingLinks();
    });
  });
}

function setLabelText(label, en, ar) {
  const node = [...label.childNodes].find(item => item.nodeType === Node.TEXT_NODE);
  if (node) node.nodeValue = `${pair(en, ar)} `;
}

const loginErrors = {
  invalid_credentials: ['The username or password is incorrect. Verify the account with your Active Directory administrator.', 'اسم المستخدم أو كلمة المرور غير صحيحة. تحقق من الحساب مع مسؤول Active Directory.'],
  password_expired: ['The Active Directory password has expired. Change it before signing in again.', 'انتهت صلاحية كلمة مرور Active Directory. غيّرها قبل تسجيل الدخول مجددًا.'],
  password_change_required: ['Active Directory requires a password change before the next sign-in.', 'يتطلب Active Directory تغيير كلمة المرور قبل تسجيل الدخول التالي.'],
  account_locked: ['The Active Directory account is locked. Contact the system administrator.', 'حساب Active Directory مقفل. تواصل مع مسؤول النظام.'],
  account_disabled: ['The Active Directory account is disabled.', 'حساب Active Directory معطّل.'],
  account_expired: ['The Active Directory account has expired.', 'انتهت صلاحية حساب Active Directory.'],
  account_logon_restricted: ['Active Directory policy blocks sign-in from this device or at this time.', 'تمنع سياسة Active Directory تسجيل الدخول من هذا الجهاز أو في هذا الوقت.'],
  mam_access_denied: ['Active Directory verified the identity, but this account is not authorized in MAM.', 'تم التحقق من الهوية، لكن الحساب غير مخوّل داخل MAM.'],
  central_api_unavailable: ['MAM permissions could not be verified because the Central API is unavailable.', 'تعذر التحقق من صلاحيات MAM لأن الواجهة المركزية غير متاحة.'],
  ad_validation_unavailable: ['Active Directory validation is unavailable on the MAM server.', 'خدمة التحقق من Active Directory غير متاحة على خادم MAM.'],
  invalid_login_request: ['The sign-in request was rejected for security reasons.', 'رُفض طلب تسجيل الدخول لأسباب أمنية.'],
  windows_identity_missing: ['Windows SSO did not return a valid Windows identity.', 'لم يُرجع Windows SSO هوية Windows صالحة.']
};

function translateLoginStatus() {
  const host = document.getElementById('loginStatus');
  if (!host) return;
  const value = host.textContent || '';
  if (/Checking current|No active MAM|Authentication status could not/i.test(value)) {
    const translated = pair(
      'No active MAM session. Choose Windows SSO or Active Directory sign-in.',
      'لا توجد جلسة MAM نشطة. اختر Windows SSO أو تسجيل الدخول بحساب Active Directory.'
    );
    if (host.textContent !== translated) host.textContent = translated;
  }
}

function applyLoginLanguage() {
  const en = language === 'en';
  document.documentElement.lang = language;
  document.documentElement.dir = en ? 'ltr' : 'rtl';
  document.title = pair('Sign in · Diwan Al Amiri MAM', 'تسجيل الدخول · الديوان الأميري MAM');
  const crest = document.querySelector('.login-head img');
  if (crest) crest.alt = pair('Diwan Al Amiri crest', 'شعار الديوان الأميري');
  setText('.login-head h1', 'Diwan Al Amiri', 'الديوان الأميري');
  setText('.login-head p', 'Media Asset Management · Secure Enterprise Sign-In', 'إدارة الأصول الإعلامية · تسجيل دخول مؤسسي آمن');
  setText('.login-body > h2', 'Choose a sign-in method', 'اختر طريقة تسجيل الدخول');
  setText('.login-body > p', 'Domain devices use Windows SSO. Other devices can use an Active Directory account through the secure form below.', 'أجهزة الدومين تستخدم Windows SSO. ويمكن للأجهزة الأخرى استخدام حساب Active Directory عبر النموذج الآمن أدناه.');
  setTexts('.mode-badge', [['DOMAIN DEVICE · SSO','جهاز الدومين · SSO'],['NON-DOMAIN DEVICE · AD','جهاز خارج الدومين · AD']]);
  setTexts('.mode h3', [['Windows SSO','Windows SSO'],['Active Directory account','حساب Active Directory']]);
  setTexts('.mode > p', [
    ['For Diwan domain devices. Chrome or Edge uses the current Windows identity through Kerberos/Negotiate; MAM never receives the password.', 'لأجهزة نطاق الديوان. يستخدم Chrome أو Edge هوية Windows الحالية عبر Kerberos/Negotiate دون استلام MAM لكلمة المرور.'],
    ['Use this option on a non-domain device. Active Directory validates the password directly; MAM never stores it in its database or cookie.', 'استخدم هذا الخيار على جهاز غير منضم للدومين. يتحقق Active Directory من كلمة المرور مباشرة ولا يحفظها MAM في قاعدة البيانات أو الارتباط.']
  ]);
  setText('#windowsLogin', 'Sign in automatically with Windows', 'الدخول تلقائيًا بحساب Windows');
  const labels = document.querySelectorAll('.ad-form label');
  if (labels[0]) setLabelText(labels[0], 'Username', 'اسم المستخدم');
  if (labels[1]) setLabelText(labels[1], 'Password', 'كلمة المرور');
  const userInput = document.querySelector('input[name="username"]');
  if (userInput) userInput.placeholder = pair('DA\\username or user@da.gov.kw', 'DA\\username أو user@da.gov.kw');
  setText('.ad-form button', 'Sign in with AD', 'تسجيل الدخول بحساب AD');
  setText('.security-line', 'TLS only · HttpOnly/Secure session · Password is never stored', 'TLS فقط · جلسة HttpOnly/Secure · لا يتم تخزين كلمة المرور');
  const note = document.querySelector('.login-note');
  if (note) note.innerHTML = pair(
    '<strong>MAM authorization</strong><br>Active Directory validation alone does not grant access. The same enabled account must exist in MAM and have at least one role. A clear reason is shown when identity succeeds but authorization fails.',
    '<strong>الصلاحيات داخل MAM</strong><br>نجاح التحقق من Active Directory لا يمنح الوصول وحده. يجب أن يكون الحساب نفسه موجودًا ومفعّلًا في MAM ومُسندًا له دور واحد على الأقل، ويظهر سبب واضح عند نجاح الهوية وفشل التخويل.'
  );
  setText('.footer-actions > a', 'Back to landing page', 'العودة للصفحة الرئيسية');
  setText('.footer-actions > span', 'Production · Active Directory', 'الإنتاج · Active Directory');
  const languageButton = document.getElementById('loginLanguage');
  if (languageButton) languageButton.textContent = pair('العربية', 'English');

  const errorCode = query.get('error');
  const errorHost = document.getElementById('loginError');
  if (errorCode && errorHost) {
    errorHost.hidden = false;
    const message = loginErrors[errorCode] || ['Sign-in could not be completed. Try again.', 'تعذر إكمال تسجيل الدخول. حاول مرة أخرى.'];
    errorHost.textContent = en ? message[0] : message[1];
    errorHost.style.direction = en ? 'ltr' : 'rtl';
    errorHost.style.textAlign = 'start';
  }

  const returnInput = document.getElementById('returnUrl');
  const windowsLink = document.getElementById('windowsLogin');
  if (returnInput && windowsLink) {
    const localizedReturn = localPathWithLanguage(returnInput.value || '/app');
    returnInput.value = localizedReturn;
    windowsLink.href = '/auth/windows?returnUrl=' + encodeURIComponent(localizedReturn);
  }
  const landingLink = document.querySelector('.footer-actions > a');
  if (landingLink) landingLink.href = localPathWithLanguage('/landing');
  translateLoginStatus();
  syncLocation();
}

if (document.body.classList.contains('p128-landing')) {
  applyLandingLanguage();
} else if (document.querySelector('.login-shell')) {
  const button = document.getElementById('loginLanguage');
  button?.addEventListener('click', () => {
    language = language === 'en' ? 'ar' : 'en';
    applyLoginLanguage();
  });
  const status = document.getElementById('loginStatus');
  if (status) new MutationObserver(translateLoginStatus).observe(status, { childList:true, characterData:true, subtree:true });
  applyLoginLanguage();
}
})();
