(() => {
'use strict';

let activeTab = localStorage.getItem('mam.p127.adminTab') || 'users';
let userPage = 1;
let userQuery = '';
const pageSize = 10;
const baseRender = render;

render = function () {
  baseRender();
  if (route === 'admin') void loadAdministration();
};

async function req(path, options = {}) {
  const headers = { Accept: 'application/json', ...(options.headers || {}) };
  if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, { ...options, headers, cache: options.cache || 'no-store' });
  if (!response.ok) {
    let body = {};
    try { body = await response.json(); } catch { }
    const error = new Error(body.detail || `HTTP ${response.status}`);
    error.status = response.status;
    error.body = body;
    throw error;
  }
  if (response.status === 204) return null;
  return (response.headers.get('content-type') || '').includes('json') ? response.json() : response.text();
}

function failure(error) {
  if (error?.status === 401 || error?.status === 403) {
    return state('denied', arabic ? 'الوصول مرفوض' : 'Permission denied', arabic ? 'هذه الصفحة مخصصة لمسؤول النظام.' : 'This workspace requires system-administrator permission.');
  }
  if (error?.status === 409) {
    return state('error', arabic ? 'تعارض في التعديل' : 'Conflict', arabic ? 'تم تعديل السجل بواسطة مستخدم آخر. حدّث البيانات ثم أعد المحاولة.' : 'The record changed after it was loaded. Refresh the latest version and try again.');
  }
  if (error?.status === 503) {
    return state('degraded', arabic ? 'الخدمة غير جاهزة' : 'Degraded', arabic ? 'مخزن الإدارة المركزي غير جاهز حاليًا.' : 'The authoritative administration store is unavailable.');
  }
  return state('error', arabic ? 'خطأ في واجهة API' : 'API error', String(error?.message || error || 'Unknown error'));
}

function metric(icon, value, label) {
  return `<div class="card metric"><span class="p127-card-icon"><i class="bi ${icon}"></i></span><strong>${esc(value ?? 0)}</strong><span>${esc(label)}</span></div>`;
}

function tab(key, icon, label) {
  return `<button type="button" class="p127-tab ${activeTab === key ? 'active' : ''}" data-admin-tab="${key}"><i class="bi ${icon}"></i> ${esc(label)}</button>`;
}

function selectTab() {
  document.querySelectorAll('#p127AdminTabs [data-admin-tab]').forEach(x => x.classList.toggle('active', x.dataset.adminTab === activeTab));
}

async function loadAdministration() {
  const lang = arabic;
  const arabicProductText = 'الأدوار عبر واجهة API المركزية · بيانات وصفية ثنائية اللغة · أثر تشغيلي صريح';
  content.innerHTML = `${lead(arabic ? 'إعدادات مسؤول النظام' : 'System Administrator Settings', arabic ? `إدارة المستخدمين والسياسات والتدقيق من مكان واحد. ${arabicProductText}` : 'Users, policies and audit in one tabbed workspace.', 'P12.7 · ADMINISTRATION')}<div class="card">${state('loading', 'Loading', arabic ? 'جاري التحميل…' : 'Loading…')}</div>`;
  try {
    const [overview, health] = await Promise.all([req('/client-api/admin/overview'), req('/client-api/admin/health')]);
    if (route !== 'admin' || lang !== arabic) return;
    content.innerHTML = `${lead(arabic ? 'إعدادات مسؤول النظام' : 'System Administrator Settings', arabic ? `إدارة المستخدمين والسياسات والتدقيق من مكان واحد مع بحث Active Directory. ${arabicProductText}` : 'Users, policies and audit with Active Directory lookup.', 'P12.7 · LIVE')}<div class="p127-metric-grid">${metric('bi-people', overview.users, arabic ? 'المستخدمون' : 'Users')}${metric('bi-sliders', overview.policies, arabic ? 'السياسات' : 'Policies')}${metric('bi-journal-text', overview.dictionaryEntries, arabic ? 'القواميس' : 'Dictionary entries')}${metric('bi-arrow-repeat', overview.restartRequired, arabic ? 'تغييرات تتطلب إعادة تشغيل' : 'Restart-impact')}</div><div class="p127-tabs" id="p127AdminTabs">${tab('users', 'bi-people', arabic ? 'المستخدمون' : 'Users')}${tab('policies', 'bi-sliders', arabic ? 'السياسات' : 'Policies')}${tab('audit', 'bi-clock-history', arabic ? 'سجل التدقيق' : 'Audit')}${tab('health', 'bi-heart-pulse', arabic ? 'صحة الإدارة' : 'Health')}</div><div id="p127AdminPanel"></div>`;
    document.querySelectorAll('#p127AdminTabs [data-admin-tab]').forEach(button => button.addEventListener('click', () => {
      activeTab = button.dataset.adminTab;
      localStorage.setItem('mam.p127.adminTab', activeTab);
      selectTab();
      void loadTab(health);
    }));
    selectTab();
    await loadTab(health);
  } catch (error) {
    if (route === 'admin') content.innerHTML = `${lead(arabic ? 'إعدادات مسؤول النظام' : 'System Administrator Settings', '', 'P12.7')}${failure(error)}`;
  }
}

async function loadTab(health) {
  if (activeTab === 'users') return loadUsers();
  if (activeTab === 'policies') return loadPolicies();
  if (activeTab === 'audit') return loadAudit();
  return loadHealth(health);
}

async function loadUsers() {
  const panel = document.getElementById('p127AdminPanel');
  if (!panel) return;
  panel.innerHTML = `<div class="p127-card-cluster"><div class="card"><h3><i class="bi bi-search"></i> ${arabic ? 'بحث عن مستخدم في Directory' : 'Search Directory'}</h3><div class="p127-directory-search"><input id="p127DirectoryQuery" maxlength="200" placeholder="wa.ata أو walid@da.gov.kw"/><button id="p127DirectorySearch" class="action">${arabic ? 'بحث' : 'Search'}</button></div><div id="p127DirectoryResults"></div></div><div class="card"><h3><i class="bi bi-info-circle"></i> ${arabic ? 'سياسة إنشاء المستخدم' : 'User onboarding policy'}</h3><p>${arabic ? 'يتم اختيار الحساب من Active Directory أولًا. الاسم والبريد وUsername وExternal Subject تُملأ آليًا، وExternal Subject غير قابل للتحرير اليدوي.' : 'Select the account from Active Directory first. Name, email, Username and External Subject are populated automatically; External Subject is not manually editable.'}</p></div></div><div class="card"><div class="toolbar"><h3><i class="bi bi-people"></i> ${arabic ? 'مستخدمو MAM' : 'MAM users'}</h3><input id="p127UserQuery" value="${esc(userQuery)}" placeholder="${arabic ? 'بحث في المستخدمين' : 'Search users'}"/><button id="p127UserSearch" class="action"><i class="bi bi-search"></i></button></div><div id="p127UsersTable">${state('loading', 'Loading', arabic ? 'جاري تحميل المستخدمين…' : 'Loading users…')}</div></div>`;
  document.getElementById('p127DirectorySearch').addEventListener('click', searchDirectory);
  document.getElementById('p127DirectoryQuery').addEventListener('keydown', e => { if (e.key === 'Enter') void searchDirectory(); });
  document.getElementById('p127UserSearch').addEventListener('click', () => { userQuery = document.getElementById('p127UserQuery').value.trim(); userPage = 1; void renderUsers(); });
  document.getElementById('p127UserQuery').addEventListener('keydown', e => { if (e.key === 'Enter') { userQuery = e.target.value.trim(); userPage = 1; void renderUsers(); } });
  await renderUsers();
}

async function searchDirectory() {
  const query = document.getElementById('p127DirectoryQuery')?.value.trim() || '';
  const host = document.getElementById('p127DirectoryResults');
  if (!host) return;
  if (query.length < 2) {
    host.innerHTML = state('error', 'Validation', arabic ? 'اكتب حرفين على الأقل.' : 'Enter at least two characters.');
    return;
  }
  host.innerHTML = state('loading', 'Searching', arabic ? 'جاري البحث في Active Directory…' : 'Searching Active Directory…');
  try {
    const rows = await req(`/client-api/admin/directory-search?query=${encodeURIComponent(query)}`);
    if (!rows.length) {
      host.innerHTML = state('empty', 'No results', arabic ? 'لم يتم العثور على حساب مطابق.' : 'No matching directory account was found.');
      return;
    }
    host.innerHTML = rows.map((u, i) => `<div class="p127-directory-result"><div><strong>${esc(u.displayName)}</strong><small>${esc(u.userPrincipalName)} · ${esc(u.samAccountName)}</small><small>${u.disabled ? (arabic ? 'معطل' : 'Disabled') : u.locked ? (arabic ? 'مقفل' : 'Locked') : u.passwordExpired ? (arabic ? 'كلمة المرور منتهية' : 'Password expired') : (arabic ? 'نشط' : 'Active')}</small></div><button class="action" data-directory-index="${i}" ${u.disabled || u.locked ? 'disabled' : ''}>${arabic ? 'اختيار' : 'Select'}</button></div>`).join('');
    host.querySelectorAll('[data-directory-index]').forEach(button => button.addEventListener('click', () => createUser(rows[Number(button.dataset.directoryIndex)])));
  } catch (error) {
    host.innerHTML = failure(error);
  }
}

async function createUser(user) {
  const modal = await window.p127OpenModal({
    title: arabic ? 'إضافة مستخدم من Active Directory' : 'Add user from Active Directory',
    confirmText: arabic ? 'إضافة المستخدم' : 'Add user',
    body: `<div class="p127-user-form"><div class="p127-field"><label>${arabic ? 'الاسم' : 'Name'}</label><input value="${esc(user.displayName)}" readonly/></div><div class="p127-field"><label>${arabic ? 'البريد' : 'Email'}</label><input value="${esc(user.mail)}" readonly/></div><div class="p127-field"><label>Username</label><input value="${esc(user.userPrincipalName)}" readonly/></div><div class="p127-field"><label>${arabic ? 'الدور' : 'Role'}</label><select id="p127NewRole"><option>Administrator</option><option>CatalogManager</option><option>CatalogEditor</option><option>Viewer</option><option>TapeManager</option><option>TapeOperator</option><option>TapeViewer</option></select></div><div class="p127-field full"><label><input id="p127NewEnabled" type="checkbox" checked/> ${arabic ? 'نشط' : 'Active'}</label></div></div><p><small>${arabic ? 'External Subject محفوظ تلقائيًا ومخفي:' : 'External Subject is stored automatically and hidden:'} ${esc(user.externalSubject)}</small></p>`
  });
  if (!modal) return;
  try {
    await req(`/client-api/admin/users/${crypto.randomUUID()}`, { method: 'PUT', body: JSON.stringify({ expectedVersion: 0, userName: user.userPrincipalName, displayName: user.displayName, externalSubject: user.externalSubject, isEnabled: modal.querySelector('#p127NewEnabled').checked, roles: [modal.querySelector('#p127NewRole').value] }) });
    userPage = 1;
    await renderUsers();
  } catch (error) {
    alert(error.status === 409 ? (arabic ? 'Conflict: المستخدم موجود أو تم تغييره بالفعل.' : 'Conflict: the user already exists or changed concurrently.') : (error.body?.detail || error.message));
  }
}

async function renderUsers() {
  const host = document.getElementById('p127UsersTable');
  if (!host) return;
  host.innerHTML = state('loading', 'Loading', arabic ? 'جاري تحميل المستخدمين…' : 'Loading users…');
  try {
    const result = await req(`/client-api/admin/users/page?page=${userPage}&pageSize=${pageSize}${userQuery ? `&query=${encodeURIComponent(userQuery)}` : ''}`);
    const rows = result.items || [];
    host.innerHTML = `<div class="table-wrap"><table><thead><tr><th>${arabic ? 'الاسم' : 'Name'}</th><th>Username</th><th>${arabic ? 'الأدوار' : 'Roles'}</th><th>${arabic ? 'الحالة' : 'Status'}</th><th></th></tr></thead><tbody>${rows.map(u => `<tr><td>${esc(u.displayName)}</td><td>${esc(u.userName)}</td><td>${esc((u.roles || []).join(', ') || '—')}</td><td>${u.isEnabled ? (arabic ? 'نشط' : 'Active') : (arabic ? 'معطل' : 'Disabled')}</td><td><button class="action" data-edit="${esc(u.userId)}">${arabic ? 'تعديل' : 'Edit'}</button> <button class="action p127-danger" data-delete="${esc(u.userId)}">${arabic ? 'حذف' : 'Delete'}</button></td></tr>`).join('') || `<tr><td colspan="5">${arabic ? 'لا توجد سجلات.' : 'No users.'}</td></tr>`}</tbody></table></div><div class="p127-pager"><button data-prev ${result.page <= 1 ? 'disabled' : ''}><i class="bi bi-chevron-left"></i></button><span class="p127-page-info">${arabic ? 'صفحة' : 'Page'} ${result.page} / ${result.totalPages} · ${result.totalCount}</span><button data-next ${result.page >= result.totalPages ? 'disabled' : ''}><i class="bi bi-chevron-right"></i></button></div>`;
    host.querySelector('[data-prev]')?.addEventListener('click', () => { userPage = Math.max(1, userPage - 1); void renderUsers(); });
    host.querySelector('[data-next]')?.addEventListener('click', () => { userPage = Math.min(result.totalPages, userPage + 1); void renderUsers(); });
    host.querySelectorAll('[data-edit]').forEach(button => button.addEventListener('click', () => editUser(rows.find(x => x.userId === button.dataset.edit))));
    host.querySelectorAll('[data-delete]').forEach(button => button.addEventListener('click', () => deleteUser(rows.find(x => x.userId === button.dataset.delete))));
  } catch (error) {
    host.innerHTML = failure(error);
  }
}

async function editUser(user) {
  if (!user) return;
  const role = user.roles?.[0] || 'Viewer';
  const modal = await window.p127OpenModal({
    title: arabic ? 'تعديل المستخدم' : 'Edit user',
    confirmText: arabic ? 'حفظ' : 'Save',
    body: `<div class="p127-user-form"><div class="p127-field"><label>${arabic ? 'الاسم' : 'Display name'}</label><input id="edn" value="${esc(user.displayName)}"/></div><div class="p127-field"><label>Username</label><input value="${esc(user.userName)}" readonly/></div><div class="p127-field"><label>${arabic ? 'الدور' : 'Role'}</label><select id="er"><option ${role === 'Administrator' ? 'selected' : ''}>Administrator</option><option ${role === 'CatalogManager' ? 'selected' : ''}>CatalogManager</option><option ${role === 'CatalogEditor' ? 'selected' : ''}>CatalogEditor</option><option ${role === 'Viewer' ? 'selected' : ''}>Viewer</option><option ${role === 'TapeManager' ? 'selected' : ''}>TapeManager</option><option ${role === 'TapeOperator' ? 'selected' : ''}>TapeOperator</option><option ${role === 'TapeViewer' ? 'selected' : ''}>TapeViewer</option></select></div><div class="p127-field"><label><input id="ee" type="checkbox" ${user.isEnabled ? 'checked' : ''}/> ${arabic ? 'نشط' : 'Active'}</label></div></div><small>${arabic ? 'External Subject مُدار آليًا.' : 'External Subject is system-managed.'}</small>`
  });
  if (!modal) return;
  try {
    await req(`/client-api/admin/users/${user.userId}`, { method: 'PUT', body: JSON.stringify({ expectedVersion: user.version, userName: user.userName, displayName: modal.querySelector('#edn').value.trim(), externalSubject: user.externalSubject, isEnabled: modal.querySelector('#ee').checked, roles: [modal.querySelector('#er').value] }) });
    await renderUsers();
  } catch (error) {
    alert(error.status === 409 ? (arabic ? 'تعارض: تم تعديل المستخدم بواسطة جلسة أخرى. حدّث القائمة ثم حاول مجددًا.' : 'Conflict: this user was modified by another session. Refresh the list and retry.') : (error.body?.detail || error.message));
  }
}

async function deleteUser(user) {
  if (!user) return;
  const modal = await window.p127OpenModal({ title: arabic ? 'حذف المستخدم نهائيًا' : 'Delete user permanently', danger: true, confirmText: arabic ? 'حذف نهائي' : 'Delete permanently', body: `<p>${arabic ? 'سيتم حذف المستخدم وأدواره من MAM فقط؛ لن يتم حذف حساب Active Directory.' : 'The MAM user and role assignments will be permanently removed; the Active Directory account is not deleted.'}</p><strong>${esc(user.displayName)}</strong><br>${esc(user.userName)}` });
  if (!modal) return;
  try {
    await req(`/client-api/admin/users/${user.userId}`, { method: 'DELETE' });
    await renderUsers();
  } catch (error) {
    alert(error.body?.detail || error.message);
  }
}

async function loadPolicies() {
  const panel = document.getElementById('p127AdminPanel');
  if (!panel) return;
  panel.innerHTML = state('loading', 'Loading', arabic ? 'جاري تحميل السياسات…' : 'Loading policies…');
  try {
    const rows = await req('/client-api/admin/policies');
    panel.innerHTML = `<div id="p127PolicyState"></div><div class="card"><h3><i class="bi bi-sliders"></i> ${arabic ? 'السياسات المركزية' : 'Authoritative policies'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic ? 'السياسة' : 'Policy'}</th><th>${arabic ? 'الفئة' : 'Category'}</th><th>${arabic ? 'الإصدار' : 'Version'}</th><th>${arabic ? 'الحالة' : 'Status'}</th><th></th></tr></thead><tbody>${rows.map(p => `<tr><td>${esc(arabic && p.displayNameAr ? p.displayNameAr : p.displayNameEn)}</td><td>${esc(p.category)}</td><td>v${esc(p.version)}</td><td>${p.isEnabled ? (arabic ? 'مفعلة' : 'Enabled') : (arabic ? 'معطلة' : 'Disabled')}</td><td><button class="action" data-policy-edit="${esc(p.policyKey)}">${arabic ? 'تعديل' : 'Edit'}</button> <button class="action" data-policy-test="${esc(p.policyKey)}">${arabic ? 'اختبار المرجع' : 'Test reference'}</button></td></tr>`).join('')}</tbody></table></div></div>`;
    panel.querySelectorAll('[data-policy-edit]').forEach(button => button.addEventListener('click', () => editPolicy(rows.find(x => x.policyKey === button.dataset.policyEdit))));
    panel.querySelectorAll('[data-policy-test]').forEach(button => button.addEventListener('click', () => testPolicy(button.dataset.policyTest)));
    if (window.p127ApplyPagination) window.p127ApplyPagination(panel);
  } catch (error) {
    panel.innerHTML = failure(error);
  }
}

async function testPolicy(key) {
  const out = document.getElementById('p127PolicyState');
  if (out) out.innerHTML = state('loading', arabic ? 'جارٍ الاختبار' : 'Testing', arabic ? 'جاري اختبار المرجع الآمن…' : 'Testing the policy reference…');
  try {
    const result = await req(`/client-api/admin/policies/${encodeURIComponent(key)}/test`, { method: 'POST' });
    if (out) out.innerHTML = result.success ? state('empty', arabic ? 'نجح الاختبار' : 'Test passed', result.detail || 'Success') : state('error', arabic ? 'فشل الاختبار' : 'Test failed', result.detail || 'Failed');
  } catch (error) {
    if (out) out.innerHTML = failure(error);
  }
}

async function editPolicy(policy) {
  if (!policy) return;
  const modal = await window.p127OpenModal({
    title: arabic ? 'تعديل السياسة' : 'Edit policy',
    confirmText: arabic ? 'حفظ' : 'Save',
    body: `<div class="p127-field"><label>${arabic ? 'JSON السياسة' : 'Policy JSON'}</label><textarea id="pp" rows="10" style="width:100%">${esc(JSON.stringify(policy.payload, null, 2))}</textarea></div><div class="p127-field"><label>SecretRef</label><input id="ps" value="${esc(policy.secretRef || '')}"/></div><p><label><input id="pe" type="checkbox" ${policy.isEnabled ? 'checked' : ''}/> ${arabic ? 'مفعلة' : 'Enabled'}</label> &nbsp; <label><input id="pr" type="checkbox" ${policy.requiresRestart ? 'checked' : ''}/> ${arabic ? 'يتطلب إعادة تشغيل' : 'Requires restart'}</label></p><small>Validate → Test reference → Save · resolved secret values never return to the browser</small>`
  });
  if (!modal) return;
  try {
    const body = { expectedVersion: policy.version, category: policy.category, displayNameEn: policy.displayNameEn, displayNameAr: policy.displayNameAr, payload: JSON.parse(modal.querySelector('#pp').value), secretRef: modal.querySelector('#ps').value.trim() || null, requiresRestart: modal.querySelector('#pr').checked, isEnabled: modal.querySelector('#pe').checked };
    const validation = await req(`/client-api/admin/policies/${encodeURIComponent(policy.policyKey)}/validate`, { method: 'POST', body: JSON.stringify(body) });
    if (!validation.valid) throw new Error((validation.errors || []).join(' '));
    await req(`/client-api/admin/policies/${encodeURIComponent(policy.policyKey)}`, { method: 'PUT', body: JSON.stringify(body) });
    await loadPolicies();
  } catch (error) {
    if (error.status === 409) {
      alert(arabic ? 'تعارض: تم تحديث السياسة بواسطة مستخدم آخر. سيتم إعادة تحميل أحدث نسخة.' : 'Conflict: the policy was updated by another user. The latest version will be reloaded.');
      await loadPolicies();
      return;
    }
    alert(error.message);
  }
}

async function loadAudit() {
  const panel = document.getElementById('p127AdminPanel');
  if (!panel) return;
  panel.innerHTML = state('loading', 'Loading', arabic ? 'جاري تحميل التدقيق…' : 'Loading audit…');
  try {
    const audit = await req('/client-api/admin/audit?limit=100');
    const rows = audit.items || [];
    panel.innerHTML = `<div class="card"><div class="toolbar"><h3><i class="bi bi-clock-history"></i> ${arabic ? 'سجل التدقيق' : 'Audit log'}</h3><button id="p127AuditExport" class="action">Export CSV</button></div><div class="table-wrap"><table><thead><tr><th>UTC</th><th>${arabic ? 'المنفّذ' : 'Actor'}</th><th>${arabic ? 'الإجراء' : 'Action'}</th><th>${arabic ? 'النتيجة' : 'Outcome'}</th></tr></thead><tbody>${rows.map(x => `<tr><td>${esc(new Date(x.occurredAtUtc).toLocaleString())}</td><td>${esc(x.actorId)}</td><td>${esc(x.action)}</td><td>${esc(x.outcome)}</td></tr>`).join('')}</tbody></table></div></div>`;
    document.getElementById('p127AuditExport').addEventListener('click', () => window.open('/client-api/admin/audit/export?limit=500', '_blank', 'noopener'));
    if (window.p127ApplyPagination) window.p127ApplyPagination(panel);
  } catch (error) {
    panel.innerHTML = failure(error);
  }
}

async function loadHealth(health) {
  const panel = document.getElementById('p127AdminPanel');
  if (!panel) return;
  try {
    const current = health || await req('/client-api/admin/health');
    panel.innerHTML = `<div class="p127-card-cluster"><div class="card"><h3><i class="bi bi-heart-pulse"></i> ${arabic ? 'حالة مخزن الإدارة' : 'Administration store'}</h3>${current.isReady ? state('empty', arabic ? 'جاهز' : 'Ready', current.detail || 'Ready') : state('degraded', arabic ? 'متدهور' : 'Degraded', current.detail || 'Unavailable')}</div><div class="card"><h3><i class="bi bi-shield-lock"></i> ${arabic ? 'حدود الأمان' : 'Security boundary'}</h3><p>${arabic ? 'القيم السرية لا تظهر في المتصفح؛ يظهر SecretRef فقط عند الحاجة.' : 'Resolved secrets are never rendered in the browser; only opaque SecretRef identifiers are shown. Secret material is never redisplayed.'}</p></div></div>`;
  } catch (error) {
    panel.innerHTML = failure(error);
  }
}

})();