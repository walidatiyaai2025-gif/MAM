(() => {
  'use strict';
  const versionHost = document.getElementById('landingVersion');
  const identity = document.getElementById('identityHint');
  const firefox = document.getElementById('firefoxHint');
  const language = document.getElementById('landingLanguage');

  if (/firefox/i.test(navigator.userAgent || '')) firefox.hidden = false;

  Promise.allSettled([
    fetch('/version', { headers: { Accept: 'application/json' } }).then(r => r.ok ? r.json() : null),
    fetch('/auth/status', { headers: { Accept: 'application/json' } }).then(r => r.ok ? r.json() : null)
  ]).then(results => {
    const version = results[0].status === 'fulfilled' ? results[0].value : null;
    const auth = results[1].status === 'fulfilled' ? results[1].value : null;
    if (versionHost && version) versionHost.textContent = `${version.version || 'MAM'} · ${version.environmentName || 'Production'}`;
    if (identity && auth?.authenticated) {
      identity.hidden = false;
      identity.innerHTML = `<strong>تم التعرف على حسابك</strong> · ${escapeHtml(auth.userName || '')}`;
      document.querySelectorAll('a[href="/auth/login"]').forEach(link => {
        link.href = '/app';
        if (link.id === 'landingLoginHero') link.textContent = 'متابعة إلى التطبيق';
        if (link.id === 'landingLoginTop') link.textContent = 'متابعة';
      });
    }
  }).catch(() => {});

  let english = false;
  const translations = {
    ar: {
      lang: 'English', top: 'دخول النظام', heroButton: 'فتح التطبيق', featuresButton: 'استعراض المميزات',
      eyebrow: 'منصة مؤسسية موحدة للأرشيف الإعلامي',
      h1: 'إدارة، فهرسة، بحث وحماية المحتوى الإعلامي من مكان واحد.',
      intro: 'من رفع الملفات والتسجيل وحتى OCR والتفريغ الصوتي الزمني والبحث الشامل والنسخ الاحتياطي الموثق — مع صلاحيات Active Directory وتدقيق كامل للعمليات.'
    },
    en: {
      lang: 'العربية', top: 'Sign in', heroButton: 'Open application', featuresButton: 'Explore features',
      eyebrow: 'One institutional platform for the media archive',
      h1: 'Manage, index, discover and protect media content from one place.',
      intro: 'From upload and capture to OCR, timestamped transcription, full-text discovery and verified backup — with Active Directory access control and complete auditability.'
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
    if (top && !top.href.endsWith('/app')) top.textContent = t.top;
    if (hero && !hero.href.endsWith('/app')) hero.textContent = t.heroButton;
    const eyebrow = document.querySelector('.hero-copy .eyebrow');
    const h1 = document.querySelector('.hero-copy h1');
    const intro = document.querySelector('.hero-copy > p');
    const featureLink = document.querySelector('.hero-actions .ghost');
    if (eyebrow) eyebrow.textContent = t.eyebrow;
    if (h1) h1.textContent = t.h1;
    if (intro) intro.textContent = t.intro;
    if (featureLink) featureLink.textContent = t.featuresButton;
  });

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
})();
