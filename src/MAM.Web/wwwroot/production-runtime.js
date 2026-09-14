(() => {
  'use strict';

  const languageKey = 'mam.language';
  const validRoles = ['Administrator', 'CatalogEditor', 'Viewer'];
  const roleLabels = {
    Administrator: ['Administrator', 'مسؤول النظام'],
    CatalogEditor: ['Catalog Editor', 'محرر الكتالوج'],
    Viewer: ['Viewer', 'مشاهد']
  };
  let runtimeStatus = null;
  let dashboardSequence = 0;
  let accessRenderBusy = false;

  const queryLanguage = new URLSearchParams(window.location.search).get('lang');
  const storedLanguage = localStorage.getItem(languageKey);
  const preferredLanguage = queryLanguage === 'ar' || queryLanguage === 'en'
    ? queryLanguage
    : (storedLanguage === 'ar' || storedLanguage === 'en' ? storedLanguage : null);

  if (preferredLanguage) {
    arabic = preferredLanguage === 'ar';
    localStorage.setItem(languageKey, preferredLanguage);
  }

  languageButton.addEventListener('click', () => {
    queueMicrotask(() => localStorage.setItem(languageKey, arabic ? 'ar' : 'en'));
  });

  const previousShellPage = shellPage;
  shellPage = function () {
    if (route !== 'dashboard') return previousShellPage();
    return `${lead(arabic ? 'نظرة تشغيلية مباشرة' : 'Live operational dashboard', arabic ? 'بيانات فعلية من الخدمات المركزية الموثوقة.' : 'Authoritative live data from the central MAM services.', 'LIVE')}
      <div id="productionDashboardHost"><div class="card">${state('loading', arabic ? 'جارٍ التحميل' : 'Loading', arabic ? 'جاري تحميل حالة النظام…' : 'Loading live system state…')}</div></div>`;
  };

  if (typeof p12AugmentDashboard === 'function') {
    p12AugmentDashboard = async function () { };
  }

  const previousRender = render;
  render = function () {
    previousRender();
    applyProductionChrome();
    if (route === 'dashboard') void loadProductionDashboard();
  };

  async function json(path, options = {}) {
    const headers = { Accept: 'application/json', ...(options.headers || {}) };
    if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { ...options, headers });
    let payload = null;
    const type = response.headers.get('content-type') || '';
    if (response.status !== 204) payload = type.includes('application/json') ? await response.json().catch(() => null) : await response.text().catch(() => '');
    if (!response.ok) {
      const error = new Error((payload && payload.detail) || `HTTP ${response.status}`);
      error.status = response.status;
      error.payload = payload;
      throw error;
    }
    return payload;
  }

  async function loadRuntimeStatus() {
    try {
      runtimeStatus = await json('/client-api/status');
      applyProductionChrome();
    } catch { }
  }

  function applyProductionChrome() {
    const badge = document.querySelector('.nonprod');
    if (badge) {
      badge.innerHTML = `<strong>${arabic ? 'نظام الإنتاج' : 'PRODUCTION'}</strong><span id="buildIdentity">${esc(runtimeStatus?.environment || 'MAM')}</span>`;
    }
    const topSmall = document.querySelector('.topbar small');
    if (topSmall) topSmall.textContent = arabic ? 'الديوان الأميري · نظام إدارة الأصول الإعلامية' : 'DIWAN AL AMIRI · MEDIA ASSET MANAGEMENT';
    const connected = document.querySelector('.topbar .status');
    if (connected) {
      const user = runtimeStatus?.userName || '';
      connected.textContent = user
        ? `● ${arabic ? 'متصل' : 'Connected'} · ${user}`
        : `● ${arabic ? 'الخدمات المركزية' : 'Central services'}`;
    }
  }

  async function loadProductionDashboard() {
    const host = document.getElementById('productionDashboardHost');
    if (!host) return;
    const sequence = ++dashboardSequence;
    const languageAtRequest = arabic;
    try {
      const [assets, discovery, jobs, protection] = await Promise.all([
        json('/client-api/catalog/assets'),
        json('/client-api/discovery/dashboard'),
        json('/client-api/processing/jobs?limit=100'),
        json('/client-api/protection/summary').catch(() => null)
      ]);
      if (sequence !== dashboardSequence || route !== 'dashboard' || languageAtRequest !== arabic) return;

      const assetRows = Array.isArray(assets) ? assets : [];
      const jobRows = Array.isArray(jobs) ? jobs : [];
      const activeJobs = jobRows.filter(j => j.state === 0 || j.state === 1 || String(j.state).toLowerCase() === 'queued' || String(j.state).toLowerCase() === 'leased').length;
      const failedJobs = jobRows.filter(j => j.state === 3 || String(j.state).toLowerCase() === 'failed').length;
      const protectedCount = Number(protection?.protected ?? protection?.protectedCount ?? protection?.verified ?? 0);

      host.innerHTML = `<div class="grid three">
        ${metric(assetRows.length, arabic ? 'إجمالي الأصول' : 'Total assets', arabic ? 'الكتالوج المركزي' : 'authoritative catalog')}
        ${metric(discovery?.indexedAssetCount ?? 0, arabic ? 'أصول مفهرسة' : 'Indexed assets', `${discovery?.transcriptCount ?? 0} ${arabic ? 'تفريغ' : 'transcripts'} · ${discovery?.ocrCount ?? 0} OCR`)}
        ${metric(activeJobs, arabic ? 'معالجة نشطة' : 'Active processing', `${failedJobs} ${arabic ? 'فشل' : 'failed'}`)}
        ${metric(discovery?.categoryCount ?? 0, arabic ? 'التصنيفات' : 'Categories', `${discovery?.uncategorizedAssetCount ?? 0} ${arabic ? 'غير مصنف' : 'uncategorized'}`)}
        ${metric(discovery?.referenceSubjectCount ?? 0, arabic ? 'مراجع' : 'Reference subjects', arabic ? 'مكتبة المراجع' : 'reference library')}
        ${metric(protectedCount, arabic ? 'محمي' : 'Protected', arabic ? 'نسخ تم التحقق منها' : 'verified copies')}
      </div>
      <div class="card"><h3>${arabic ? 'حالة الإنتاج' : 'Production state'}</h3>
        <div class="state empty"><strong>${arabic ? 'متصل' : 'Connected'}</strong><br>${arabic ? 'تم تحميل البيانات المباشرة بنجاح من الكتالوج والفهرسة والمعالجة.' : 'Live catalog, discovery and processing data loaded successfully.'}</div>
      </div>`;
    } catch (error) {
      if (sequence !== dashboardSequence || route !== 'dashboard') return;
      const denied = error.status === 401 || error.status === 403;
      host.innerHTML = `<div class="card">${state(denied ? 'denied' : 'error', denied ? (arabic ? 'الوصول مرفوض' : 'Permission denied') : (arabic ? 'خطأ في الخدمة' : 'Service error'), denied ? (arabic ? 'حساب Windows الحالي غير مضاف أو غير مخول في MAM.' : 'The current Windows account is not provisioned or authorized in MAM.') : (arabic ? 'تعذر تحميل بيانات لوحة التحكم المباشرة.' : 'Live dashboard data could not be loaded.'))}</div>`;
    }
  }

  function metric(value, label, detail) {
    return `<div class="card metric"><strong>${esc(value ?? 0)}</strong><span>${esc(label)}</span><small>${esc(detail)}</small></div>`;
  }

  const accessObserver = new MutationObserver(() => {
    if (route !== 'admin' || accessRenderBusy || document.getElementById('mamAccessManagement')) return;
    const adminContent = document.getElementById('content');
    if (!adminContent || !adminContent.querySelector('.table-wrap')) return;
    void renderAccessManagement();
  });
  accessObserver.observe(content, { childList: true, subtree: true });

  async function renderAccessManagement() {
    if (accessRenderBusy || route !== 'admin') return;
    accessRenderBusy = true;
    try {
      const [users, permissions] = await Promise.all([
        json('/client-api/admin/users'),
        json('/client-api/discovery/media-permissions')
      ]);
      if (route !== 'admin' || document.getElementById('mamAccessManagement')) return;

      const host = document.createElement('div');
      host.id = 'mamAccessManagement';
      host.className = 'card';
      const userRows = (users || []).map(user => `<tr>
        <td><strong>${esc(user.displayName)}</strong></td>
        <td><code>${esc(user.userName)}</code></td>
        <td>${esc((user.roles || []).join(', ') || '—')}</td>
        <td>${user.isEnabled ? (arabic ? 'مفعّل' : 'Enabled') : (arabic ? 'معطّل' : 'Disabled')}</td>
        <td><button class="action" data-mam-edit-user="${esc(user.userId)}">${arabic ? 'تعديل' : 'Edit'}</button></td>
      </tr>`).join('');

      host.innerHTML = `<div class="toolbar"><h3>${arabic ? 'إدارة المستخدمين والصلاحيات' : 'User & access management'}</h3><button id="mamAddUser" class="action">${arabic ? 'إضافة مستخدم' : 'Add user'}</button></div>
        <p>${arabic ? 'أضف حساب Windows/Active Directory، فعّل أو عطّل الحساب، وحدد أدواره وصلاحيات أنواع الوسائط من هنا.' : 'Add Windows/Active Directory accounts, enable or disable access, assign roles and manage media permissions here.'}</p>
        <div class="table-wrap"><table><thead><tr><th>${arabic ? 'الاسم' : 'Display name'}</th><th>${arabic ? 'حساب Windows' : 'Windows account'}</th><th>${arabic ? 'الأدوار' : 'Roles'}</th><th>${arabic ? 'الحالة' : 'Status'}</th><th></th></tr></thead><tbody>${userRows || `<tr><td colspan="5">${arabic ? 'لا توجد سجلات مستخدمين.' : 'No user records.'}</td></tr>`}</tbody></table></div>
        <div id="mamUserEditor"></div>
        <hr style="margin:24px 0"/>
        <h3>${arabic ? 'مصفوفة صلاحيات أنواع الوسائط' : 'Media permission matrix'}</h3>
        <p>${arabic ? 'الصلاحيات التالية تطبق على أعضاء كل دور حسب نوع الوسائط.' : 'These permissions apply to members of each role by media type.'}</p>
        <div id="mamPermissionMatrix">${renderPermissionMatrix(permissions || [])}</div>
        <div id="mamAccessState" aria-live="polite"></div>`;

      content.appendChild(host);
      document.getElementById('mamAddUser')?.addEventListener('click', () => openUserEditor(null, users || []));
      host.querySelectorAll('[data-mam-edit-user]').forEach(button => button.addEventListener('click', () => openUserEditor((users || []).find(u => u.userId === button.dataset.mamEditUser) || null, users || [])));
      host.querySelectorAll('[data-mam-perm-save]').forEach(button => button.addEventListener('click', () => void savePermission(permissions || [], Number(button.dataset.mamPermSave))));
    } catch (error) {
      if (route === 'admin' && !document.getElementById('mamAccessManagement')) {
        const host = document.createElement('div');
        host.id = 'mamAccessManagement';
        host.className = 'card';
        host.innerHTML = state(error.status === 401 || error.status === 403 ? 'denied' : 'error', arabic ? 'إدارة المستخدمين' : 'User management', arabic ? 'تعذر تحميل إدارة المستخدمين والصلاحيات.' : 'User and permission management could not be loaded.');
        content.appendChild(host);
      }
    } finally {
      accessRenderBusy = false;
    }
  }

  function openUserEditor(user) {
    const host = document.getElementById('mamUserEditor');
    if (!host) return;
    const editing = !!user;
    const roles = new Set(user?.roles || []);
    host.innerHTML = `<div class="card" style="margin-top:16px">
      <h3>${editing ? (arabic ? 'تعديل المستخدم' : 'Edit user') : (arabic ? 'إضافة مستخدم' : 'Add user')}</h3>
      <div class="grid two">
        <label>${arabic ? 'حساب Windows / AD' : 'Windows / AD account'}<input id="mamUserName" value="${esc(user?.userName || '')}" placeholder="DA\\username" style="width:100%"/></label>
        <label>${arabic ? 'الاسم المعروض' : 'Display name'}<input id="mamDisplayName" value="${esc(user?.displayName || '')}" style="width:100%"/></label>
        <label>${arabic ? 'المعرف الخارجي (اختياري)' : 'External subject (optional)'}<input id="mamExternalSubject" value="${esc(user?.externalSubject || '')}" style="width:100%"/></label>
        <label><input id="mamUserEnabled" type="checkbox" ${user?.isEnabled !== false ? 'checked' : ''}/> ${arabic ? 'الحساب مفعّل' : 'Account enabled'}</label>
      </div>
      <div class="toolbar" style="margin-top:12px">${validRoles.map(role => `<label><input type="checkbox" data-mam-user-role="${role}" ${roles.has(role) ? 'checked' : ''}/> ${arabic ? roleLabels[role][1] : roleLabels[role][0]}</label>`).join('')}</div>
      <div class="toolbar"><button id="mamSaveUser" class="action">${arabic ? 'حفظ المستخدم' : 'Save user'}</button><button id="mamCancelUser" class="action">${arabic ? 'إلغاء' : 'Cancel'}</button></div>
      <div id="mamUserState"></div>
    </div>`;
    document.getElementById('mamCancelUser')?.addEventListener('click', () => { host.innerHTML = ''; });
    document.getElementById('mamSaveUser')?.addEventListener('click', () => void saveUser(user));
  }

  async function saveUser(existing) {
    const stateHost = document.getElementById('mamUserState');
    const userName = document.getElementById('mamUserName')?.value.trim() || '';
    const displayName = document.getElementById('mamDisplayName')?.value.trim() || '';
    const externalSubject = document.getElementById('mamExternalSubject')?.value.trim() || '';
    const roles = [...document.querySelectorAll('[data-mam-user-role]:checked')].map(x => x.dataset.mamUserRole);
    if (!userName || !displayName || roles.length === 0) {
      if (stateHost) stateHost.innerHTML = state('error', arabic ? 'تحقق' : 'Validation', arabic ? 'حساب Windows والاسم ودور واحد على الأقل مطلوبة.' : 'Windows account, display name and at least one role are required.');
      return;
    }
    const userId = existing?.userId || crypto.randomUUID();
    const body = {
      expectedVersion: existing?.version || 0,
      userName,
      displayName,
      externalSubject: externalSubject || null,
      isEnabled: document.getElementById('mamUserEnabled')?.checked === true,
      roles
    };
    try {
      if (stateHost) stateHost.innerHTML = state('loading', arabic ? 'جارٍ الحفظ' : 'Saving', arabic ? 'جاري تحديث سياسة المستخدم…' : 'Updating user access policy…');
      await json(`/client-api/admin/users/${encodeURIComponent(userId)}`, { method: 'PUT', body: JSON.stringify(body) });
      document.getElementById('mamAccessManagement')?.remove();
      await renderAccessManagement();
    } catch (error) {
      if (stateHost) stateHost.innerHTML = state('error', arabic ? 'تعذر الحفظ' : 'Save failed', error.status === 409 ? (arabic ? 'تم تعديل المستخدم من جلسة أخرى. حدّث الصفحة ثم أعد المحاولة.' : 'The user changed in another session. Refresh and retry.') : (arabic ? 'تعذر حفظ المستخدم.' : 'The user could not be saved.'));
    }
  }

  function renderPermissionMatrix(rows) {
    return `<div class="list">${rows.map((permission, index) => `<div class="row">
      <b>${esc(permission.roleName)}</b><span>${esc(permission.mediaKind)}</span>
      <span>${['View','Upload','Edit','Process','Download'].map(key => {
        const prop = 'can' + key;
        const ar = { View:'عرض', Upload:'رفع', Edit:'تعديل', Process:'معالجة', Download:'تنزيل' }[key];
        return `<label style="margin-inline-end:8px"><input type="checkbox" data-mam-perm="${index}|${prop}" ${permission[prop] ? 'checked' : ''}/> ${arabic ? ar : key}</label>`;
      }).join('')}</span>
      <span><button class="action" data-mam-perm-save="${index}">${arabic ? 'حفظ' : 'Save'}</button></span>
    </div>`).join('')}</div>`;
  }

  async function savePermission(rows, index) {
    const permission = rows[index];
    if (!permission) return;
    const checked = prop => document.querySelector(`[data-mam-perm="${index}|${prop}"]`)?.checked === true;
    const body = {
      roleName: permission.roleName,
      mediaKind: permission.mediaKind,
      canView: checked('canView'),
      canUpload: checked('canUpload'),
      canEdit: checked('canEdit'),
      canProcess: checked('canProcess'),
      canDownload: checked('canDownload')
    };
    const stateHost = document.getElementById('mamAccessState');
    try {
      await json('/client-api/discovery/media-permissions', { method: 'PUT', body: JSON.stringify(body) });
      if (stateHost) stateHost.innerHTML = state('empty', arabic ? 'تم الحفظ' : 'Saved', arabic ? 'تم تحديث صلاحيات الدور.' : 'Role media permissions were updated.');
    } catch {
      if (stateHost) stateHost.innerHTML = state('error', arabic ? 'تعذر الحفظ' : 'Save failed', arabic ? 'تعذر تحديث الصلاحيات.' : 'Permissions could not be updated.');
    }
  }

  void loadRuntimeStatus().finally(() => render());
})();
