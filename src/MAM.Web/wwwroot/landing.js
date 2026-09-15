(() => {
  'use strict';
  const versionHost = document.getElementById('landingVersion');
  const identity = document.getElementById('identityHint');
  const firefox = document.getElementById('firefoxHint');
  const language = document.getElementById('landingLanguage');
  const userChip = document.getElementById('landingUserChip');
  const userName = document.getElementById('landingUserName');
  const searchForm = document.getElementById('landingSearch');
  const searchInput = document.getElementById('landingSearchInput');
  let authenticated = false;
  let english = false;

  if (/firefox/i.test(navigator.userAgent || '')) firefox.hidden = false;

  Promise.allSettled([
    fetch('/version', { headers: { Accept: 'application/json' }, cache:'no-store' }).then(r => r.ok ? r.json() : null),
    fetch('/auth/status', { headers: { Accept: 'application/json' }, cache:'no-store' }).then(r => r.ok ? r.json() : null)
  ]).then(results => {
    const version = results[0].status === 'fulfilled' ? results[0].value : null;
    const auth = results[1].status === 'fulfilled' ? results[1].value : null;
    if (versionHost && version) versionHost.textContent = `${version.version || 'MAM'} · ${version.environmentName || 'Production'}`;
    authenticated = auth?.authenticated === true;
    if (!authenticated) return;

    const account = auth.userName || '';
    if (identity) {
      identity.hidden = false;
      identity.innerHTML = `<strong>تم التعرف على حسابك</strong> · ${escapeHtml(account)}`;
    }
    if (userChip) userChip.hidden = false;
    if (userName) userName.textContent = account || 'Current user';
    document.querySelectorAll('a[href="/auth/login"]').forEach(link => {
      link.href = '/app';
      if (link.id === 'landingLoginHero') link.innerHTML = `متابعة إلى النظام <i class="bi bi-arrow-left"></i>`;
      if (link.id === 'landingLoginTop') link.textContent = 'متابعة';
    });
  }).catch(() => {});

  searchForm?.addEventListener('submit', event => {
    event.preventDefault();
    const query = searchInput?.value.trim() || '';
    if (query.length < 2) {
      searchInput?.focus();
      return;
    }
    sessionStorage.setItem('mam.p128.pendingSearch', query);
    location.href = authenticated ? '/app#route=search' : `/auth/login?returnUrl=${encodeURIComponent('/app#route=search')}`;
  });

  const translations = {
    ar: {
      lang: 'English', top: 'دخول النظام', heroButton: 'ابدأ مع النظام', featuresButton: 'استكشف المزايا',
      eyebrow: 'منصة مؤسسية موثوقة لإدارة الأصول الإعلامية',
      h1: 'إدارة، فهرسة، بحث<br/>وحماية المحتوى الإعلامي<br/><span>من مكان واحد</span>',
      intro: 'من رفع الملفات والتسجيل المرئي، OCR والتعرف الضوئي على النصوص، وفهرسة الوسائط، إلى البحث المتقدم والنسخ الاحتياطي المتكامل. النظام متكامل مع Active Directory ويوفر أدوات الحماية والتدقيق الشاملة لأصولكم الإعلامية.',
      search:'بحث سريع داخل المحتوى'
    },
    en: {
      lang: 'العربية', top: 'Sign in', heroButton: 'Start with MAM', featuresButton: 'Explore features',
      eyebrow: 'Trusted institutional media asset management platform',
      h1: 'Manage, index, discover<br/>and protect media content<br/><span>from one place</span>',
      intro: 'From upload and media capture to OCR, indexing, advanced discovery and verified backup, with Active Directory integration, governed permissions and complete auditing.',
      search:'Quick content search'
    }
  };

  language?.addEventListener('click', () => {
    english = !english;
    const t = translations[english ? 'en' : 'ar'];
    document.documentElement.lang = english ? 'en' : 'ar';
    document.documentElement.dir = english ? 'ltr' : 'rtl';
    language.textContent = t.lang;
    const top = document.getElementById('landingLoginTop');
    const hero = document.getElementById('landingLoginHero');
    if (top) top.textContent = authenticated ? (english ? 'Continue' : 'متابعة') : t.top;
    if (hero) hero.innerHTML = `${authenticated ? (english ? 'Continue to application' : 'متابعة إلى النظام') : t.heroButton} <i class="bi ${english ? 'bi-arrow-right' : 'bi-arrow-left'}"></i>`;
    const eyebrow = document.querySelector('.hero-copy .eyebrow');
    const h1 = document.querySelector('.hero-copy h1');
    const intro = document.querySelector('.hero-copy > p');
    const featureLink = document.querySelector('.hero-actions .ghost');
    if (eyebrow) eyebrow.textContent = t.eyebrow;
    if (h1) h1.innerHTML = t.h1;
    if (intro) intro.textContent = t.intro;
    if (featureLink) featureLink.innerHTML = `${t.featuresButton} <i class="bi bi-play"></i>`;
    if (searchInput) searchInput.placeholder = t.search;
  });

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
})();
