(() => {
  pages['curation-actions'] = ['Curation Actions', 'إجراءات التهيئة'];
  pages['admin-actions'] = ['Management Actions', 'إجراءات الإدارة'];
  const navHost = document.getElementById('nav');
  const makeNav = (routeName, beforeRoute) => {
    const button = document.createElement('button');
    button.dataset.route = routeName;
    button.textContent = pages[routeName][0];
    const before = navHost.querySelector(`[data-route="${beforeRoute}"]`);
    navHost.insertBefore(button, before || null);
    button.addEventListener('click', () => { route = routeName; render(); });
    return button;
  };
  const curationButton = makeNav('curation-actions', 'asset');
  const adminActionsButton = makeNav('admin-actions', 'settings');

  const baseShellPage = shellPage;
  shellPage = function () {
    if (route === 'curation-actions') return `${lead(arabic ? 'إجراءات التهيئة' : 'Curation Actions', arabic ? 'التعديل الجماعي وعضوية المجموعات بتنفيذ مركزي واضح.' : 'Explicit Central API controls for bulk metadata and collection membership.', 'P12 · FUNCTION AUDIT')}<div id="p12CurationHost">${state('loading', 'Loading', arabic ? 'جاري تحميل إجراءات التهيئة…' : 'Loading curation actions…')}</div>`;
    if (route === 'admin-actions') return `${lead(arabic ? 'إجراءات الإدارة' : 'Management Actions', arabic ? 'إدارة المستخدمين والأدوار والقواميس مع نتائج ظاهرة.' : 'Explicit controls for user/role policy and bilingual dictionaries.', 'P12 · FUNCTION AUDIT')}<div id="p12AdminHost">${state('loading', 'Loading', arabic ? 'جاري تحميل إجراءات الإدارة…' : 'Loading management actions…')}</div>`;
    return baseShellPage();
  };

  const baseRender = render;
  render = function () {
    baseRender();
    curationButton.classList.toggle('active', route === 'curation-actions');
    adminActionsButton.classList.toggle('active', route === 'admin-actions');
    curationButton.textContent = pages['curation-actions'][arabic ? 1 : 0];
    adminActionsButton.textContent = pages['admin-actions'][arabic ? 1 : 0];
    if (route === 'curation-actions') void loadCurationActions();
    if (route === 'admin-actions') void loadAdminActions();
  };

  async function api(path, options = {}) {
    const headers = { Accept: 'application/json', ...(options.headers || {}) };
    if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { ...options, headers });
    if (response.status === 401 || response.status === 403) throw Object.assign(new Error('denied'), { kind: 'denied', status: response.status });
    if (response.status === 409) throw Object.assign(new Error('conflict'), { kind: 'conflict', status: response.status });
    if (response.status === 503) throw Object.assign(new Error('degraded'), { kind: 'degraded', status: response.status });
    if (!response.ok) throw Object.assign(new Error(`HTTP ${response.status}`), { kind: 'error', status: response.status });
    if (response.status === 204) return null;
    const type = response.headers.get('content-type') || '';
    return type.includes('application/json') ? response.json() : response.text();
  }

  const actionState = (kind, heading, detail) => `<div class="state ${kind}"><strong>${esc(heading)}</strong><br>${esc(detail)}</div>`;
  const failure = error => error?.kind === 'denied'
    ? actionState('denied', arabic ? 'الوصول مرفوض' : 'Permission denied', arabic ? 'لا توجد صلاحية لتنفيذ هذه الوظيفة.' : 'The current identity cannot execute this function.')
    : error?.kind === 'conflict'
      ? actionState('degraded', arabic ? 'تعارض إصدار' : 'Version conflict', arabic ? 'أعد تحميل الصفحة ثم كرر العملية.' : 'Reload the action page and retry.')
      : actionState(error?.kind === 'degraded' ? 'degraded' : 'error', error?.kind === 'degraded' ? (arabic ? 'حالة متدهورة' : 'Degraded') : (arabic ? 'خطأ في واجهة API' : 'API error'), arabic ? 'لم تكتمل العملية.' : 'The operation did not complete.');

  async function loadCurationActions() {
    const host = document.getElementById('p12CurationHost'); if (!host) return;
    try {
      const [collections, policy] = await Promise.all([api('/client-api/curation/collections'), api('/client-api/curation/policy')]);
      if (route !== 'curation-actions') return;
      const options = (collections || []).map(c => `<option value="${esc(c.collectionId)}" data-version="${Number(c.version)}">${esc(arabic && c.nameAr ? c.nameAr : c.nameEn)} · v${Number(c.version)}</option>`).join('');
      host.innerHTML = `<div class="card"><h3>${arabic ? 'تعديل البيانات الوصفية جماعيًا' : 'Bulk metadata'}</h3>
        <p>${arabic ? `حد العملية ${policy.maxBulkItems} أصل. اترك أي حقل فارغًا للاحتفاظ بالقيمة الحالية.` : `Maximum ${policy.maxBulkItems} assets. Leave a field blank to preserve its current value.`}</p>
        <textarea id="p12BulkIds" rows="5" placeholder="${arabic ? 'معرّف أصل واحد في كل سطر' : 'One asset GUID per line'}" aria-label="Asset IDs"></textarea>
        <div class="toolbar"><input id="p12BulkCategory" maxlength="120" placeholder="${arabic ? 'التصنيف' : 'Category'}"/><input id="p12BulkTags" maxlength="1000" placeholder="${arabic ? 'وسوم مفصولة بفواصل' : 'Comma-separated tags'}"/></div>
        <textarea id="p12BulkNotes" rows="3" maxlength="2000" placeholder="${arabic ? 'ملاحظات الحفظ' : 'Preservation notes'}"></textarea>
        <div class="toolbar"><button id="p12BulkApply" class="action">${arabic ? 'تطبيق تعديل جماعي' : 'Apply bulk metadata'}</button></div><div id="p12BulkState" aria-live="polite"></div></div>
        <div class="card"><h3>${arabic ? 'عضوية المجموعات' : 'Collection membership'}</h3><div class="toolbar"><select id="p12Collection">${options}</select><input id="p12CollectionAsset" placeholder="${arabic ? 'معرّف الأصل GUID' : 'Asset GUID'}"/><button id="p12CollectionAdd" class="action">${arabic ? 'إضافة للمجموعة' : 'Add to collection'}</button><button id="p12CollectionRemove" class="action">${arabic ? 'إزالة من المجموعة' : 'Remove from collection'}</button></div><div id="p12CollectionState" aria-live="polite"></div></div>`;
      bindCuration(policy);
    } catch (error) { host.innerHTML = failure(error); }
  }

  function bindCuration(policy) {
    document.getElementById('p12BulkApply')?.addEventListener('click', async () => {
      const output = document.getElementById('p12BulkState');
      const ids = (document.getElementById('p12BulkIds')?.value || '').split(/[\r\n,;]+/).map(x => x.trim()).filter(x => /^[0-9a-f-]{36}$/i.test(x)).slice(0, Number(policy.maxBulkItems || 50));
      if (!ids.length) { output.innerHTML = actionState('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'أدخل معرّف أصل صحيحًا واحدًا على الأقل.' : 'Enter at least one valid asset GUID.'); return; }
      output.innerHTML = actionState('loading', 'Loading', arabic ? 'جاري قراءة الإصدارات الحالية…' : 'Loading current metadata versions…');
      try {
        const category = document.getElementById('p12BulkCategory')?.value.trim() || '';
        const tagsText = document.getElementById('p12BulkTags')?.value.trim() || '';
        const notes = document.getElementById('p12BulkNotes')?.value.trim() || '';
        const current = await Promise.all(ids.map(id => api(`/client-api/curation/assets/${id}/metadata`).catch(() => null)));
        const items = current.filter(Boolean).map(m => ({ assetId: m.assetId, expectedVersion: m.version, schemaKey: m.schemaKey, titleEn: m.titleEn, titleAr: m.titleAr, eventDate: m.eventDate, category: category || m.category, tags: tagsText ? tagsText.split(',').map(x => x.trim()).filter(Boolean) : m.tags, preservationNotes: notes || m.preservationNotes }));
        if (!items.length) throw new Error('no_assets');
        const result = await api('/client-api/curation/assets/bulk-metadata', { method: 'POST', body: JSON.stringify({ items }) });
        output.innerHTML = actionState(result.failed ? 'degraded' : 'empty', arabic ? 'اكتمل' : 'Completed', arabic ? `مطلوب ${result.requested} · نجح ${result.succeeded} · فشل ${result.failed}` : `Requested ${result.requested} · succeeded ${result.succeeded} · failed ${result.failed}`);
      } catch (error) { output.innerHTML = failure(error); }
    });

    const mutate = async adding => {
      const output = document.getElementById('p12CollectionState');
      const select = document.getElementById('p12Collection');
      const option = select?.selectedOptions?.[0];
      const collectionId = select?.value || '';
      const version = Number(option?.dataset?.version || 0);
      const assetId = document.getElementById('p12CollectionAsset')?.value.trim() || '';
      if (!collectionId || !/^[0-9a-f-]{36}$/i.test(assetId)) { output.innerHTML = actionState('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'اختر مجموعة وأدخل معرّف أصل صحيحًا.' : 'Choose a collection and enter a valid asset GUID.'); return; }
      output.innerHTML = actionState('loading', 'Loading', arabic ? 'جاري تنفيذ العملية…' : 'Applying membership change…');
      try {
        const path = `/client-api/curation/collections/${collectionId}/assets/${assetId}${adding ? '' : `?expectedVersion=${version}`}`;
        const options = adding ? { method: 'POST', body: JSON.stringify({ expectedVersion: version }) } : { method: 'DELETE' };
        const updated = await api(path, options);
        output.innerHTML = actionState('empty', arabic ? 'تم التنفيذ' : 'Completed', arabic ? `إصدار المجموعة ${updated.version}` : `Collection version ${updated.version}`);
        await loadCurationActions();
      } catch (error) { output.innerHTML = failure(error); }
    };
    document.getElementById('p12CollectionAdd')?.addEventListener('click', () => void mutate(true));
    document.getElementById('p12CollectionRemove')?.addEventListener('click', () => void mutate(false));
  }

  async function loadAdminActions() {
    const host = document.getElementById('p12AdminHost'); if (!host) return;
    try {
      const users = await api('/client-api/admin/users');
      if (route !== 'admin-actions') return;
      const userOptions = (users || []).map(u => `<option value="${esc(u.userId)}">${esc(u.displayName)} · ${esc(u.userName)} · v${Number(u.version)}</option>`).join('');
      host.innerHTML = `<div class="card"><h3>${arabic ? 'المستخدمون والأدوار' : 'Users & roles'}</h3><p>${arabic ? 'توفير الهوية الخارجية يبقى P12؛ هذه الشاشة تدير سياسة صلاحيات MAM فقط.' : 'External identity provisioning remains P12; this surface manages MAM authorization policy only.'}</p>
        <div class="toolbar"><select id="p12UserSelect"><option value="">${arabic ? 'سجل جديد' : 'New record'}</option>${userOptions}</select><button id="p12UserLoad" class="action">${arabic ? 'تحميل السجل' : 'Load record'}</button><button id="p12UserNew" class="action">${arabic ? 'سجل جديد' : 'New record'}</button></div>
        <div class="toolbar"><input id="p12UserId" placeholder="User ID GUID"/><input id="p12UserName" placeholder="${arabic ? 'اسم المستخدم' : 'User name'}"/><input id="p12DisplayName" placeholder="${arabic ? 'الاسم المعروض' : 'Display name'}"/></div>
        <div class="toolbar"><input id="p12ExternalSubject" placeholder="External subject"/><input id="p12Roles" placeholder="${arabic ? 'الأدوار مفصولة بفواصل' : 'Roles, comma separated'}"/><label><input id="p12UserEnabled" type="checkbox" checked/> ${arabic ? 'مفعّل' : 'Enabled'}</label><button id="p12UserSave" class="action">${arabic ? 'حفظ المستخدم والأدوار' : 'Save user & roles'}</button></div><div id="p12UserState" aria-live="polite"></div></div>
        <div class="card"><h3>${arabic ? 'القواميس الثنائية اللغة' : 'Bilingual dictionaries'}</h3><div class="toolbar"><input id="p12DictionaryKey" value="metadata.categories" placeholder="Dictionary key"/><button id="p12DictionaryLoad" class="action">${arabic ? 'تحميل القاموس' : 'Load dictionary'}</button><select id="p12DictionaryEntries"></select><button id="p12DictionaryNew" class="action">${arabic ? 'عنصر جديد' : 'New entry'}</button></div>
        <div class="toolbar"><input id="p12EntryKey" placeholder="Entry key"/><input id="p12LabelEn" placeholder="English label"/><input id="p12LabelAr" dir="rtl" placeholder="التسمية العربية"/><label><input id="p12EntryEnabled" type="checkbox" checked/> ${arabic ? 'مفعّل' : 'Enabled'}</label><button id="p12DictionarySave" class="action">${arabic ? 'حفظ عنصر القاموس' : 'Save dictionary entry'}</button></div><div id="p12DictionaryState" aria-live="polite"></div></div>
        <div class="card"><h3>${arabic ? 'السياسات والتدقيق' : 'Policies & audit'}</h3><p>${arabic ? 'تعديل/تحقق/اختبار السياسات وتصدير CSV متاح من شاشة Administration الرئيسية.' : 'Policy edit/validate/test controls and audit CSV export remain available on the main Administration screen.'}</p><button id="p12OpenAdmin" class="action">${arabic ? 'فتح الإدارة' : 'Open Administration'}</button></div>`;
      bindAdmin(users || []);
    } catch (error) { host.innerHTML = failure(error); }
  }

  function bindAdmin(users) {
    let loadedUser = null;
    const fillUser = user => {
      loadedUser = user || null;
      document.getElementById('p12UserId').value = user?.userId || crypto.randomUUID();
      document.getElementById('p12UserName').value = user?.userName || '';
      document.getElementById('p12DisplayName').value = user?.displayName || '';
      document.getElementById('p12ExternalSubject').value = user?.externalSubject || '';
      document.getElementById('p12Roles').value = (user?.roles || []).join(', ');
      document.getElementById('p12UserEnabled').checked = user?.isEnabled ?? true;
    };
    fillUser(null);
    document.getElementById('p12UserLoad')?.addEventListener('click', () => fillUser(users.find(u => u.userId === document.getElementById('p12UserSelect')?.value) || null));
    document.getElementById('p12UserNew')?.addEventListener('click', () => fillUser(null));
    document.getElementById('p12UserSave')?.addEventListener('click', async () => {
      const output = document.getElementById('p12UserState');
      const id = document.getElementById('p12UserId')?.value.trim() || '';
      const userName = document.getElementById('p12UserName')?.value.trim() || '';
      const displayName = document.getElementById('p12DisplayName')?.value.trim() || '';
      if (!/^[0-9a-f-]{36}$/i.test(id) || !userName || !displayName) { output.innerHTML = actionState('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'المعرّف واسم المستخدم والاسم المعروض مطلوبة.' : 'User ID, user name and display name are required.'); return; }
      try {
        const saved = await api(`/client-api/admin/users/${id}`, { method: 'PUT', body: JSON.stringify({ expectedVersion: loadedUser?.version || 0, userName, displayName, externalSubject: document.getElementById('p12ExternalSubject')?.value.trim() || null, isEnabled: document.getElementById('p12UserEnabled')?.checked === true, roles: (document.getElementById('p12Roles')?.value || '').split(',').map(x => x.trim()).filter(Boolean) }) });
        output.innerHTML = actionState('empty', arabic ? 'تم الحفظ' : 'Saved', arabic ? `الإصدار ${saved.version}` : `Version ${saved.version}`);
      } catch (error) { output.innerHTML = failure(error); }
    });

    let dictionaryEntries = [];
    let loadedEntry = null;
    const entrySelect = document.getElementById('p12DictionaryEntries');
    const fillEntry = entry => { loadedEntry = entry || null; document.getElementById('p12EntryKey').value = entry?.entryKey || ''; document.getElementById('p12LabelEn').value = entry?.labelEn || ''; document.getElementById('p12LabelAr').value = entry?.labelAr || ''; document.getElementById('p12EntryEnabled').checked = entry?.isEnabled ?? true; };
    document.getElementById('p12DictionaryLoad')?.addEventListener('click', async () => {
      const output = document.getElementById('p12DictionaryState');
      const key = document.getElementById('p12DictionaryKey')?.value.trim() || '';
      try {
        dictionaryEntries = await api(`/client-api/admin/dictionaries/${encodeURIComponent(key)}`);
        entrySelect.innerHTML = (dictionaryEntries || []).map(e => `<option value="${esc(e.entryKey)}">${esc(e.entryKey)} · ${esc(arabic ? e.labelAr : e.labelEn)} · v${Number(e.version)}</option>`).join('');
        fillEntry(dictionaryEntries[0] || null);
        output.innerHTML = actionState('empty', arabic ? 'تم التحميل' : 'Loaded', arabic ? `${dictionaryEntries.length} عنصر` : `${dictionaryEntries.length} entries`);
      } catch (error) { output.innerHTML = failure(error); }
    });
    entrySelect?.addEventListener('change', () => fillEntry(dictionaryEntries.find(e => e.entryKey === entrySelect.value) || null));
    document.getElementById('p12DictionaryNew')?.addEventListener('click', () => fillEntry(null));
    document.getElementById('p12DictionarySave')?.addEventListener('click', async () => {
      const output = document.getElementById('p12DictionaryState');
      const dictionaryKey = document.getElementById('p12DictionaryKey')?.value.trim() || '';
      const entryKey = document.getElementById('p12EntryKey')?.value.trim() || '';
      const labelEn = document.getElementById('p12LabelEn')?.value.trim() || '';
      const labelAr = document.getElementById('p12LabelAr')?.value.trim() || '';
      if (!dictionaryKey || !entryKey || !labelEn || !labelAr) { output.innerHTML = actionState('error', arabic ? 'تحقق من البيانات' : 'Validation', arabic ? 'المفاتيح والتسميتان مطلوبة.' : 'Keys and both labels are required.'); return; }
      try {
        const saved = await api(`/client-api/admin/dictionaries/${encodeURIComponent(dictionaryKey)}/${encodeURIComponent(entryKey)}`, { method: 'PUT', body: JSON.stringify({ expectedVersion: loadedEntry?.version || 0, labelEn, labelAr, isEnabled: document.getElementById('p12EntryEnabled')?.checked === true }) });
        output.innerHTML = actionState('empty', arabic ? 'تم الحفظ' : 'Saved', arabic ? `الإصدار ${saved.version}` : `Version ${saved.version}`);
      } catch (error) { output.innerHTML = failure(error); }
    });
    document.getElementById('p12OpenAdmin')?.addEventListener('click', () => { route = 'admin'; render(); });
  }
})();
