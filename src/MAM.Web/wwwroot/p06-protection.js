(() => {
  pages.protection = ['Backup Protection', 'حماية النسخة الاحتياطية'];
  const navHost = document.getElementById('nav');
  const protectionButton = document.createElement('button');
  protectionButton.dataset.route = 'protection';
  protectionButton.textContent = pages.protection[0];
  const adminButton = navHost.querySelector('[data-route="admin"]');
  navHost.insertBefore(protectionButton, adminButton || null);
  protectionButton.addEventListener('click', () => { route = 'protection'; render(); });

  const baseRender = render;
  render = function () {
    baseRender();
    protectionButton.classList.toggle('active', route === 'protection');
    protectionButton.textContent = pages.protection[arabic ? 1 : 0];
    if (route === 'protection') void loadProtectionAdmin();
  };

  async function request(path, options) {
    const response = await fetch(path, { headers: { Accept: 'application/json' }, ...options });
    if (response.status === 401 || response.status === 403) throw Object.assign(new Error('denied'), { kind: 'denied' });
    if (response.status === 503) throw Object.assign(new Error('degraded'), { kind: 'degraded' });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return response.status === 204 ? null : response.json();
  }

  function protectionState(kind, heading, detail) {
    return `<div class="state ${kind}"><strong>${esc(heading)}</strong><br>${esc(detail)}</div>`;
  }

  async function loadProtectionAdmin() {
    const languageAtRequest = arabic;
    content.innerHTML = `${lead(arabic ? 'حماية النسخة الاحتياطية' : 'Backup Protection', arabic ? 'حالة الحماية الفعلية من الخدمة المركزية.' : 'Authoritative protection state from the Central API.', 'P06 · CENTRAL API')}<div class="card">${protectionState('loading', 'Loading', arabic ? 'جاري تحميل حالة النسخة الاحتياطية…' : 'Loading Backup Storage health and protection counts…')}</div>`;
    try {
      const [summary, health] = await Promise.all([
        request('/client-api/protection/summary'),
        request('/client-api/protection/health')
      ]);
      if (route !== 'protection' || languageAtRequest !== arabic) return;
      const healthState = health.isReady
        ? protectionState('empty', arabic ? 'جاهز' : 'Ready', arabic ? 'Primary و Backup جاهزان للتحقق.' : 'Primary and Backup targets are ready for verified protection.')
        : protectionState('degraded', 'Degraded', health.detail || 'Backup Storage is unavailable; no asset is promoted to Protected.');
      content.innerHTML = `${lead(arabic ? 'حماية النسخة الاحتياطية' : 'Backup Protection', arabic ? 'Protected لا تظهر إلا بعد تطابق SHA-256 والحجم.' : 'Protected is shown only after independent SHA-256 and length verification.', 'P06 · LIVE')}
        <div class="grid four"><div class="card metric"><strong>${summary.protected ?? 0}</strong><span>Protected</span><small>Primary + Backup verified</small></div><div class="card metric"><strong>${summary.pending ?? 0}</strong><span>Backup Pending</span><small>Awaiting verified copy</small></div><div class="card metric"><strong>${summary.failed ?? 0}</strong><span>Backup Failed</span><small>Primary preserved</small></div><div class="card metric"><strong>${summary.mismatch ?? 0}</strong><span>Mismatch</span><small>Never silently accepted</small></div></div>
        <div class="card"><h3>${arabic ? 'صحة التخزين' : 'Storage health'}</h3>${healthState}<p><strong>Primary:</strong> ${esc(health.primaryTargetId)}<br><strong>Backup:</strong> ${esc(health.backupTargetId)}</p></div>
        <div class="card"><h3>${arabic ? 'عمليات الحماية' : 'Protection actions'}</h3><div class="toolbar"><button id="p06Queue" class="action">${arabic ? 'إضافة النسخ المعلقة' : 'Queue pending copies'}</button><button id="p06Recheck" class="action">${arabic ? 'إعادة فحص السلامة' : 'Queue integrity recheck'}</button></div><div id="p06ActionState" aria-live="polite"></div></div>
        <div class="card"><h3>${arabic ? 'حماية أصل محدد' : 'Asset protection lookup'}</h3><div class="toolbar"><input id="p06AssetId" placeholder="${arabic ? 'معرّف الأصل GUID' : 'Asset GUID'}" aria-label="${arabic ? 'معرّف الأصل' : 'Asset ID'}"/><button id="p06Lookup" class="action">${arabic ? 'عرض حماية الأصل' : 'View asset protection'}</button></div><div id="p06AssetState" aria-live="polite"></div></div>`;
      bindAdminActions();
    } catch (error) {
      if (route !== 'protection' || languageAtRequest !== arabic) return;
      const kind = error.kind === 'denied' ? 'denied' : error.kind === 'degraded' ? 'degraded' : 'error';
      const heading = error.kind === 'denied' ? 'Permission denied' : error.kind === 'degraded' ? 'Degraded' : 'API error';
      const detail = error.kind === 'denied'
        ? (arabic ? 'لا توجد صلاحية لعرض أو تشغيل إدارة الحماية.' : 'The current identity cannot administer backup protection.')
        : error.kind === 'degraded'
          ? (arabic ? 'خدمة الحماية غير جاهزة، ولن يتم إعلان أي أصل Protected.' : 'Protection is degraded; assets remain non-Protected.')
          : (arabic ? 'تعذر الوصول إلى خدمة الحماية المركزية.' : 'The Central API protection service is unreachable.');
      content.innerHTML = `${lead(arabic ? 'حماية النسخة الاحتياطية' : 'Backup Protection', detail, 'P06 · CENTRAL API')}<div class="card">${protectionState(kind, heading, detail)}</div>`;
    }
  }

  function bindAdminActions() {
    const stateBox = document.getElementById('p06ActionState');
    document.getElementById('p06Queue')?.addEventListener('click', async () => {
      stateBox.innerHTML = protectionState('loading', 'Loading', arabic ? 'جاري إضافة النسخ المعلقة…' : 'Queueing eligible verified Primary originals…');
      try {
        const result = await request('/client-api/protection/queue', { method: 'POST' });
        stateBox.innerHTML = protectionState('empty', arabic ? 'تمت الإضافة' : 'Queued', arabic ? `تمت إضافة ${result.queued ?? 0} مهمة حماية.` : `${result.queued ?? 0} protection item(s) queued.`);
      } catch (error) {
        stateBox.innerHTML = protectionState(error.kind === 'denied' ? 'denied' : 'error', error.kind === 'denied' ? 'Permission denied' : 'API error', arabic ? 'لم تقبل الخدمة المركزية العملية.' : 'The operation was not accepted by the Central API.');
      }
    });
    document.getElementById('p06Recheck')?.addEventListener('click', async () => {
      stateBox.innerHTML = protectionState('loading', 'Loading', arabic ? 'جاري إضافة فحوصات السلامة…' : 'Queueing integrity rechecks for protected assets…');
      try {
        const result = await request('/client-api/protection/integrity/recheck?olderThanHours=24', { method: 'POST' });
        stateBox.innerHTML = protectionState('empty', arabic ? 'تمت الإضافة' : 'Queued', arabic ? `تمت إضافة ${result.queued ?? 0} عملية إعادة فحص.` : `${result.queued ?? 0} integrity recheck(s) queued.`);
      } catch (error) {
        stateBox.innerHTML = protectionState(error.kind === 'denied' ? 'denied' : 'error', error.kind === 'denied' ? 'Permission denied' : 'API error', arabic ? 'لم تقبل الخدمة المركزية العملية.' : 'The operation was not accepted by the Central API.');
      }
    });
    document.getElementById('p06Lookup')?.addEventListener('click', async () => {
      const host = document.getElementById('p06AssetState');
      const raw = document.getElementById('p06AssetId')?.value.trim() || '';
      if (!/^[0-9a-f-]{36}$/i.test(raw)) {
        host.innerHTML = protectionState('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'أدخل معرّف أصل صحيحًا.' : 'Enter a valid asset GUID.');
        return;
      }
      host.innerHTML = protectionState('loading', 'Loading', arabic ? 'جاري تحميل حالة الحماية…' : 'Loading asset protection…');
      try {
        const record = await request(`/client-api/protection/assets/${encodeURIComponent(raw)}`);
        host.innerHTML = protectionState('empty', String(record.state), `${arabic ? 'التخزين الأساسي' : 'Primary'}: ${record.primaryTargetId} · ${arabic ? 'التخزين الاحتياطي' : 'Backup'}: ${record.backupTargetId} · SHA-256 ${record.expectedSha256}`);
      } catch (error) {
        host.innerHTML = protectionState(error.kind === 'denied' ? 'denied' : 'error', error.kind === 'denied' ? 'Permission denied' : 'API error', arabic ? 'تعذر تحميل حالة الحماية.' : 'Asset protection could not be loaded.');
      }
    });
  }
})();
