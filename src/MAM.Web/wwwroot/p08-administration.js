(() => {
  const baseRender = render;
  render = function () {
    baseRender();
    if (route === 'admin') void loadP08Administration();
  };

  async function adminRequest(path, options = {}) {
    const headers = { Accept: 'application/json', ...(options.headers || {}) };
    if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { ...options, headers });
    if (response.status === 401 || response.status === 403) throw Object.assign(new Error('denied'), { kind: 'denied', status: response.status });
    if (response.status === 409) {
      const body = await response.json().catch(() => ({}));
      throw Object.assign(new Error('conflict'), { kind: 'conflict', status: response.status, body });
    }
    if (response.status === 503) throw Object.assign(new Error('degraded'), { kind: 'degraded', status: response.status });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw Object.assign(new Error(body.detail || `HTTP ${response.status}`), { kind: 'error', status: response.status, body });
    }
    const type = response.headers.get('content-type') || '';
    return type.includes('application/json') ? response.json() : response.text();
  }

  function state(kind, heading, detail) {
    return `<div class="state ${kind}"><strong>${esc(heading)}</strong><br>${esc(detail)}</div>`;
  }

  async function loadP08Administration() {
    const languageAtRequest = arabic;
    content.innerHTML = `${lead(arabic ? 'إدارة المؤسسة والسياسات' : 'Enterprise Administration & Policy', arabic ? 'إدارة مركزية موثقة؛ لا يتم عرض كلمات المرور أو مفاتيح الخدمات.' : 'Authoritative central administration; secret material is never redisplayed.', 'P08 · CENTRAL API')}
      <div class="card">${state('loading', 'Loading', arabic ? 'جاري تحميل السياسات والمستخدمين والتدقيق…' : 'Loading policy, user and audit state…')}</div>`;
    try {
      const [overview, policies, users, audit, health] = await Promise.all([
        adminRequest('/client-api/admin/overview'),
        adminRequest('/client-api/admin/policies'),
        adminRequest('/client-api/admin/users'),
        adminRequest('/client-api/admin/audit?limit=12'),
        adminRequest('/client-api/admin/health')
      ]);
      if (route !== 'admin' || languageAtRequest !== arabic) return;
      renderDashboard(overview, policies, users, audit, health);
    } catch (error) {
      if (route !== 'admin' || languageAtRequest !== arabic) return;
      renderFailure(error);
    }
  }

  function renderDashboard(overview, policies, users, audit, health) {
    const policyRows = (policies || []).map(p => `<tr>
      <td><strong>${esc(p.displayNameEn)}</strong><br><small>${esc(p.displayNameAr)}</small></td>
      <td>${esc(p.category)}</td><td>v${Number(p.version || 0)}</td>
      <td>${p.requiresRestart ? '<span class="pill warning">Restart</span>' : '<span class="pill">Live</span>'}</td>
      <td>${p.secretRef ? `<code>${esc(p.secretRef)}</code>` : '—'}</td>
      <td><button class="action p08-edit" data-key="${esc(p.policyKey)}">${arabic ? 'تعديل' : 'Edit'}</button></td>
    </tr>`).join('');
    const userRows = (users || []).slice(0, 8).map(u => `<tr><td>${esc(u.displayName)}</td><td>${esc(u.userName)}</td><td>${esc((u.roles || []).join(', ') || '—')}</td><td>${u.isEnabled ? 'Enabled' : 'Disabled'}</td><td>v${Number(u.version || 0)}</td></tr>`).join('');
    const auditRows = ((audit && audit.items) || []).map(a => `<tr><td>${esc(new Date(a.occurredAtUtc).toLocaleString())}</td><td>${esc(a.actorId)}</td><td>${esc(a.action)}</td><td>${esc(a.outcome)}</td></tr>`).join('');
    const healthCard = health.isReady
      ? state('empty', arabic ? 'جاهز' : 'Ready', arabic ? 'مخزن سياسات P08 المركزي جاهز.' : 'Authoritative P08 policy store is ready.')
      : state('degraded', 'Degraded', health.detail || 'Administration store is unavailable.');

    content.innerHTML = `${lead(arabic ? 'إدارة المؤسسة والسياسات' : 'Enterprise Administration & Policy', arabic ? 'كل تعديل محمي بالصلاحيات، متزامن بإصدار، ومسجل في سجل التدقيق.' : 'Every mutation is permission-protected, versioned and audited.', 'P08 · LIVE')}
      <div class="grid four">
        <div class="card metric"><strong>${overview.policies ?? 0}</strong><span>${arabic ? 'السياسات' : 'Policies'}</span><small>${overview.enabledPolicies ?? 0} enabled</small></div>
        <div class="card metric"><strong>${overview.users ?? 0}</strong><span>${arabic ? 'المستخدمون' : 'Users'}</span><small>roles via Central API</small></div>
        <div class="card metric"><strong>${overview.dictionaryEntries ?? 0}</strong><span>${arabic ? 'القواميس' : 'Dictionary entries'}</span><small>bilingual metadata</small></div>
        <div class="card metric"><strong>${overview.restartRequired ?? 0}</strong><span>${arabic ? 'يتطلب إعادة تشغيل' : 'Restart-impact'}</span><small>explicit operational impact</small></div>
      </div>
      <div class="card"><h3>${arabic ? 'صحة الإدارة' : 'Administration health'}</h3>${healthCard}<p>${arabic ? 'القيم السرية تبقى على الخادم كمرجع SecretRef فقط، ولا يتم إرجاع قيمتها للمتصفح.' : 'Secret-backed settings expose only opaque SecretRef identifiers; resolved secret values never return to the browser.'}</p></div>
      <div class="card"><h3>${arabic ? 'السياسات المركزية' : 'Authoritative policies'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic ? 'السياسة' : 'Policy'}</th><th>${arabic ? 'الفئة' : 'Category'}</th><th>Version</th><th>Impact</th><th>SecretRef</th><th></th></tr></thead><tbody>${policyRows || `<tr><td colspan="6">${arabic ? 'لا توجد سياسات.' : 'No policies.'}</td></tr>`}</tbody></table></div><div id="p08Editor"></div></div>
      <div class="card"><h3>${arabic ? 'المستخدمون والأدوار' : 'Users & roles'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic ? 'الاسم' : 'Display name'}</th><th>User</th><th>Roles</th><th>Status</th><th>Version</th></tr></thead><tbody>${userRows || `<tr><td colspan="5">${arabic ? 'لا توجد سجلات مستخدمين بعد.' : 'No user policy records yet.'}</td></tr>`}</tbody></table></div><p><small>${arabic ? 'توفير هوية الإنتاج نفسه يظل من اختصاص موفر الهوية في P12؛ P08 يدير سياسة الصلاحيات فقط.' : 'Production identity provisioning remains external/P12; P08 governs MAM authorization policy only.'}</small></p></div>
      <div class="card"><div class="toolbar"><h3>${arabic ? 'سجل التدقيق' : 'Audit explorer'}</h3><button id="p08Export" class="action">${arabic ? 'تصدير CSV' : 'Export CSV'}</button></div><div class="table-wrap"><table><thead><tr><th>UTC</th><th>Actor</th><th>Action</th><th>Outcome</th></tr></thead><tbody>${auditRows || '<tr><td colspan="4">No audit events.</td></tr>'}</tbody></table></div><div id="p08AuditState" aria-live="polite"></div></div>`;

    document.querySelectorAll('.p08-edit').forEach(button => button.addEventListener('click', () => openPolicyEditor(button.dataset.key, policies)));
    document.getElementById('p08Export')?.addEventListener('click', exportAudit);
  }

  function openPolicyEditor(key, policies) {
    const policy = (policies || []).find(p => p.policyKey === key);
    const host = document.getElementById('p08Editor');
    if (!policy || !host) return;
    host.innerHTML = `<div class="card" style="margin-top:16px"><h3>${esc(policy.displayNameEn)}</h3>
      <p><strong>${arabic ? 'المعرف' : 'Key'}:</strong> <code>${esc(policy.policyKey)}</code> · <strong>v${Number(policy.version)}</strong></p>
      <label>${arabic ? 'JSON سياسة (بدون أسرار)' : 'Policy JSON (no secrets)'}<textarea id="p08Payload" rows="8" style="width:100%;margin-top:6px">${esc(JSON.stringify(policy.payload, null, 2))}</textarea></label>
      <label>SecretRef <input id="p08SecretRef" value="${esc(policy.secretRef || '')}" placeholder="env:SERVER_SIDE_REFERENCE" style="width:100%" /></label>
      <label><input id="p08Enabled" type="checkbox" ${policy.isEnabled ? 'checked' : ''}/> ${arabic ? 'مفعلة' : 'Enabled'}</label>
      <label><input id="p08Restart" type="checkbox" ${policy.requiresRestart ? 'checked' : ''}/> ${arabic ? 'يتطلب إعادة تشغيل' : 'Requires restart'}</label>
      <div class="toolbar"><button id="p08Validate" class="action">${arabic ? 'تحقق' : 'Validate'}</button><button id="p08Test" class="action">${arabic ? 'اختبار المرجع' : 'Test reference'}</button><button id="p08Save" class="action">${arabic ? 'حفظ' : 'Save'}</button></div>
      <div id="p08EditState" aria-live="polite"></div></div>`;

    const body = () => {
      let payload;
      try { payload = JSON.parse(document.getElementById('p08Payload').value); }
      catch { throw Object.assign(new Error('invalid_json'), { kind: 'invalid_json' }); }
      return {
        expectedVersion: policy.version,
        category: policy.category,
        displayNameEn: policy.displayNameEn,
        displayNameAr: policy.displayNameAr,
        payload,
        secretRef: document.getElementById('p08SecretRef').value.trim() || null,
        requiresRestart: document.getElementById('p08Restart').checked,
        isEnabled: document.getElementById('p08Enabled').checked
      };
    };
    const status = document.getElementById('p08EditState');
    document.getElementById('p08Validate').addEventListener('click', async () => {
      status.innerHTML = state('loading', 'Loading', arabic ? 'جاري التحقق…' : 'Validating policy…');
      try {
        const result = await adminRequest(`/client-api/admin/policies/${encodeURIComponent(key)}/validate`, { method: 'POST', body: JSON.stringify(body()) });
        status.innerHTML = result.valid ? state('empty', 'Valid', arabic ? 'السياسة صالحة للحفظ.' : 'Policy is valid for persistence.') : state('error', 'Rejected', (result.errors || []).join(' '));
      } catch (e) { showEditError(status, e); }
    });
    document.getElementById('p08Test').addEventListener('click', async () => {
      status.innerHTML = state('loading', 'Loading', arabic ? 'اختبار مرجع الخادم بدون إظهار السر…' : 'Testing server-side reference without exposing secret material…');
      try {
        const result = await adminRequest(`/client-api/admin/policies/${encodeURIComponent(key)}/test`, { method: 'POST' });
        status.innerHTML = result.success ? state('empty', result.code, result.detail) : state('degraded', result.code, result.detail);
      } catch (e) { showEditError(status, e); }
    });
    document.getElementById('p08Save').addEventListener('click', async () => {
      status.innerHTML = state('loading', 'Loading', arabic ? 'جاري الحفظ بإصدار متفائل…' : 'Saving with optimistic concurrency…');
      try {
        const saved = await adminRequest(`/client-api/admin/policies/${encodeURIComponent(key)}`, { method: 'PUT', body: JSON.stringify(body()) });
        status.innerHTML = state('empty', 'Saved', `Version ${saved.version}${saved.requiresRestart ? ' · restart required' : ''}`);
        await loadP08Administration();
      } catch (e) { showEditError(status, e); }
    });
  }

  function showEditError(host, error) {
    if (error.kind === 'conflict') host.innerHTML = state('degraded', 'Conflict', arabic ? 'تم تعديل السياسة في جلسة أخرى. أعد التحميل قبل الحفظ.' : 'The policy changed in another session. Refresh before saving.');
    else if (error.kind === 'invalid_json') host.innerHTML = state('error', 'Invalid JSON', arabic ? 'صيغة JSON غير صحيحة.' : 'Policy JSON is invalid.');
    else if (error.kind === 'denied') host.innerHTML = state('denied', 'Permission denied', arabic ? 'لا توجد صلاحية إدارة.' : 'Administration permission is required.');
    else host.innerHTML = state(error.kind === 'degraded' ? 'degraded' : 'error', error.kind === 'degraded' ? 'Degraded' : 'API error', error.message || 'Operation failed.');
  }

  async function exportAudit() {
    const host = document.getElementById('p08AuditState');
    host.innerHTML = state('loading', 'Loading', arabic ? 'جاري تجهيز CSV…' : 'Preparing audit CSV…');
    try {
      const response = await fetch('/client-api/admin/audit/export?limit=500', { headers: { Accept: 'text/csv' } });
      if (response.status === 401 || response.status === 403) throw Object.assign(new Error('denied'), { kind: 'denied' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const csv = await response.text();
      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = 'mam-audit.csv';
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 0);
      host.innerHTML = state('empty', 'Exported', arabic ? 'تم تجهيز سجل التدقيق.' : 'Audit CSV prepared.');
    } catch (e) { host.innerHTML = state(e.kind === 'denied' ? 'denied' : 'error', e.kind === 'denied' ? 'Permission denied' : 'API error', arabic ? 'تعذر تصدير سجل التدقيق.' : 'Audit export failed.'); }
  }

  function renderFailure(error) {
    const kind = error.kind === 'denied' ? 'denied' : error.kind === 'degraded' ? 'degraded' : 'error';
    const heading = error.kind === 'denied' ? 'Permission denied' : error.kind === 'degraded' ? 'Degraded' : 'API error';
    const detail = error.kind === 'denied'
      ? (arabic ? 'صلاحية Administrator مطلوبة لإدارة المؤسسة.' : 'Administrator permission is required for enterprise administration.')
      : error.kind === 'degraded'
        ? (arabic ? 'مخزن سياسات الإدارة غير جاهز؛ التغييرات تفشل بشكل آمن.' : 'Administration policy store is unavailable; mutations fail closed.')
        : (arabic ? 'تعذر الوصول إلى خدمة الإدارة المركزية.' : 'The Central API administration service is unreachable.');
    content.innerHTML = `${lead(arabic ? 'إدارة المؤسسة والسياسات' : 'Enterprise Administration & Policy', detail, 'P08 · CENTRAL API')}<div class="card">${state(kind, heading, detail)}</div>`;
  }
})();
