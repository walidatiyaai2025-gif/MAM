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

  async function request(path, options = {}) {
    const headers = { Accept:'application/json', ...(options.headers || {}) };
    if (options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { ...options, headers, cache:options.cache || 'no-store' });
    if (!response.ok) {
      let body = {}; try { body = await response.json(); } catch { }
      const error = new Error(body.detail || `HTTP ${response.status}`);
      error.status = response.status; error.body = body; throw error;
    }
    if (response.status === 204) return null;
    const type = response.headers.get('content-type') || '';
    return type.includes('json') ? response.json() : response.text();
  }

  function fail(error) {
    const code = error?.status;
    if (code === 401 || code === 403) return state('denied', arabic?'الوصول مرفوض':'Permission denied', arabic?'هذه الصفحة مخصصة لمسؤول النظام.':'This workspace requires system-administrator permission.');
    if (code === 503) return state('degraded', arabic?'الخدمة غير جاهزة':'Degraded', arabic?'مخزن الإدارة المركزي غير جاهز حاليًا.':'The authoritative administration store is unavailable.');
    return state('error', arabic?'خطأ':'Error', String(error?.message || error || 'Unknown error'));
  }

  async function loadAdministration() {
    const lang = arabic;
    content.innerHTML = `${lead(arabic?'إعدادات مسؤول النظام':'System Administrator Settings',arabic?'إدارة المستخدمين والسياسات والتدقيق من مكان واحد، مع البحث المباشر في Active Directory.':'Users, policy and audit administration in one tabbed workspace with direct Active Directory lookup.','P12.7 · ADMINISTRATION')}<div class="card">${state('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل حالة الإدارة…':'Loading administration state…')}</div>`;
    try {
      const [overview, health] = await Promise.all([request('/client-api/admin/overview'), request('/client-api/admin/health')]);
      if (route !== 'admin' || lang !== arabic) return;
      content.innerHTML = `${lead(arabic?'إعدادات مسؤول النظام':'System Administrator Settings',arabic?'إدارة المستخدمين والسياسات والتدقيق من مكان واحد، مع البحث المباشر في Active Directory.':'Users, policy and audit administration in one tabbed workspace with direct Active Directory lookup.','P12.7 · LIVE')}
        <div class="p127-metric-grid">
          ${metric('bi-people',overview.users,arabic?'المستخدمون':'Users')}
          ${metric('bi-sliders',overview.policies,arabic?'السياسات':'Policies')}
          ${metric('bi-journal-text',overview.dictionaryEntries,arabic?'القواميس':'Dictionary entries')}
          ${metric('bi-arrow-repeat',overview.restartRequired,arabic?'تغييرات تتطلب إعادة تشغيل':'Restart-impact')}
        </div>
        <div class="p127-tabs" id="p127AdminTabs">
          ${tab('users','bi-people',arabic?'المستخدمون':'Users')}
          ${tab('policies','bi-sliders',arabic?'السياسات':'Policies')}
          ${tab('audit','bi-clock-history',arabic?'سجل التدقيق':'Audit')}
          ${tab('health','bi-heart-pulse',arabic?'صحة الإدارة':'Health')}
        </div>
        <div id="p127AdminPanel"></div>`;
      document.querySelectorAll('#p127AdminTabs [data-admin-tab]').forEach(button => button.addEventListener('click', () => { activeTab=button.dataset.adminTab;localStorage.setItem('mam.p127.adminTab',activeTab);selectTab();void loadTab(health); }));
      selectTab(); await loadTab(health);
    } catch (error) {
      if (route === 'admin') content.innerHTML = `${lead(arabic?'إعدادات مسؤول النظام':'System Administrator Settings','','P12.7')}${fail(error)}`;
    }
  }

  function metric(icon, value, label) { return `<div class="card metric"><span class="p127-card-icon"><i class="bi ${icon}"></i></span><strong>${esc(value ?? 0)}</strong><span>${esc(label)}</span></div>`; }
  function tab(key, icon, label) { return `<button type="button" class="p127-tab ${activeTab===key?'active':''}" data-admin-tab="${key}"><i class="bi ${icon}"></i> ${esc(label)}</button>`; }
  function selectTab(){document.querySelectorAll('#p127AdminTabs [data-admin-tab]').forEach(x=>x.classList.toggle('active',x.dataset.adminTab===activeTab));}
  async function loadTab(health){if(activeTab==='users')return loadUsers();if(activeTab==='policies')return loadPolicies();if(activeTab==='audit')return loadAudit();return loadHealth(health);}

  async function loadUsers() {
    const panel=document.getElementById('p127AdminPanel'); if(!panel)return;
    panel.innerHTML=`<div class="p127-card-cluster"><div class="card"><h3><i class="bi bi-search"></i> ${arabic?'بحث عن مستخدم في Directory':'Search Directory'}</h3><div class="p127-directory-search"><input id="p127DirectoryQuery" maxlength="200" placeholder="wa.ata أو walid@da.gov.kw"/><button id="p127DirectorySearch" class="action">${arabic?'بحث':'Search'}</button></div><div id="p127DirectoryResults"></div></div><div class="card"><h3><i class="bi bi-info-circle"></i> ${arabic?'سياسة إنشاء المستخدم':'User onboarding policy'}</h3><p>${arabic?'يتم اختيار الحساب من Active Directory أولًا. Username والبريد والاسم وExternal Subject تُملأ آليًا؛ لا يوجد حقل External Subject يدوي.':'Select the account from Active Directory first. Username, mail, display name and External Subject are populated automatically; no manual External Subject field is exposed.'}</p></div></div><div class="card"><div class="toolbar"><h3><i class="bi bi-people"></i> ${arabic?'مستخدمو MAM':'MAM users'}</h3><input id="p127UserQuery" value="${esc(userQuery)}" placeholder="${arabic?'بحث في المستخدمين':'Search users'}"/><button id="p127UserSearch" class="action"><i class="bi bi-search"></i></button></div><div id="p127UsersTable">${state('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل المستخدمين…':'Loading users…')}</div></div>`;
    document.getElementById('p127DirectorySearch').addEventListener('click',searchDirectory);
    document.getElementById('p127DirectoryQuery').addEventListener('keydown',e=>{if(e.key==='Enter')void searchDirectory();});
    document.getElementById('p127UserSearch').addEventListener('click',()=>{userQuery=document.getElementById('p127UserQuery').value.trim();userPage=1;void renderUsersPage();});
    document.getElementById('p127UserQuery').addEventListener('keydown',e=>{if(e.key==='Enter'){userQuery=e.target.value.trim();userPage=1;void renderUsersPage();}});
    await renderUsersPage();
  }

  async function searchDirectory() {
    const query=document.getElementById('p127DirectoryQuery')?.value.trim()||'';const host=document.getElementById('p127DirectoryResults');if(!host)return;
    if(query.length<2){host.innerHTML=state('error',arabic?'تحقق':'Validation',arabic?'اكتب حرفين على الأقل.':'Enter at least two characters.');return;}
    host.innerHTML=state('loading',arabic?'جارٍ البحث':'Searching',arabic?'جاري البحث في Active Directory…':'Searching Active Directory…');
    try{const rows=await request(`/client-api/admin/directory-search?query=${encodeURIComponent(query)}`);if(!rows.length){host.innerHTML=state('empty',arabic?'لا توجد نتائج':'No results',arabic?'لم يتم العثور على حساب مطابق.':'No matching directory account was found.');return;}host.innerHTML=rows.map((u,i)=>`<div class="p127-directory-result"><div><strong>${esc(u.displayName)}</strong><small>${esc(u.userPrincipalName)} · ${esc(u.samAccountName)}</small><small>${u.disabled?(arabic?'معطل':'Disabled'):u.locked?(arabic?'مقفل':'Locked'):u.passwordExpired?(arabic?'كلمة المرور منتهية':'Password expired'):(arabic?'نشط':'Active')}</small></div><button class="action" data-directory-index="${i}" ${u.disabled||u.locked?'disabled':''}>${arabic?'اختيار':'Select'}</button></div>`).join('');host.querySelectorAll('[data-directory-index]').forEach(b=>b.addEventListener('click',()=>openCreateUser(rows[Number(b.dataset.directoryIndex)])));}catch(e){host.innerHTML=fail(e);}
  }

  async function openCreateUser(user) {
    const modal=await window.p127OpenModal({title:arabic?'إضافة مستخدم من Active Directory':'Add user from Active Directory',confirmText:arabic?'إضافة المستخدم':'Add user',body:`<div class="p127-user-form"><div class="p127-field"><label>${arabic?'الاسم':'Name'}</label><input id="p127NewDisplay" value="${esc(user.displayName)}" readonly/></div><div class="p127-field"><label>${arabic?'البريد':'Email'}</label><input value="${esc(user.mail)}" readonly/></div><div class="p127-field"><label>Username</label><input id="p127NewUserName" value="${esc(user.userPrincipalName)}" readonly/></div><div class="p127-field"><label>${arabic?'الدور':'Role'}</label><select id="p127NewRole"><option>Administrator</option><option>CatalogEditor</option><option>Viewer</option></select></div><div class="p127-field full"><label><input id="p127NewEnabled" type="checkbox" checked/> ${arabic?'نشط':'Active'}</label></div></div><p><small>${arabic?'External Subject محفوظ آليًا ومخفي:':'External Subject is stored automatically and hidden:'} ${esc(user.externalSubject)}</small></p>`});
    if(!modal)return;
    try{const body={expectedVersion:0,userName:user.userPrincipalName,displayName:user.displayName,externalSubject:user.externalSubject,isEnabled:modal.querySelector('#p127NewEnabled').checked,roles:[modal.querySelector('#p127NewRole').value]};await request(`/client-api/admin/users/${crypto.randomUUID()}`,{method:'PUT',body:JSON.stringify(body)});userPage=1;await renderUsersPage();}catch(e){alert((e.body&&e.body.detail)||e.message);}
  }

  async function renderUsersPage() {
    const host=document.getElementById('p127UsersTable');if(!host)return;
    host.innerHTML=state('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل المستخدمين…':'Loading users…');
    try{const result=await request(`/client-api/admin/users/page?page=${userPage}&pageSize=${pageSize}${userQuery?`&query=${encodeURIComponent(userQuery)}`:''}`);const rows=result.items||[];host.innerHTML=`<div class="table-wrap"><table class="p127-admin-user-table"><thead><tr><th>${arabic?'الاسم':'Name'}</th><th>Username</th><th>${arabic?'الأدوار':'Roles'}</th><th>${arabic?'الحالة':'Status'}</th><th></th></tr></thead><tbody>${rows.map(u=>`<tr><td>${esc(u.displayName)}</td><td>${esc(u.userName)}</td><td>${esc((u.roles||[]).join(', ')||'—')}</td><td><span class="${u.isEnabled?'p127-status-active':'p127-status-inactive'}">${u.isEnabled?(arabic?'نشط':'Active'):(arabic?'معطل':'Disabled')}</span></td><td><button class="action" data-user-edit="${esc(u.userId)}">${arabic?'تعديل':'Edit'}</button> <button class="action p127-danger" data-user-delete="${esc(u.userId)}">${arabic?'حذف':'Delete'}</button></td></tr>`).join('')||`<tr><td colspan="5">${arabic?'لا توجد سجلات.':'No users.'}</td></tr>`}</tbody></table></div><div class="p127-pager"><button data-prev ${result.page<=1?'disabled':''}><i class="bi bi-chevron-left"></i></button><span class="p127-page-info">${arabic?'صفحة':'Page'} ${result.page} / ${result.totalPages} · ${result.totalCount}</span><button data-next ${result.page>=result.totalPages?'disabled':''}><i class="bi bi-chevron-right"></i></button></div>`;host.querySelector('[data-prev]')?.addEventListener('click',()=>{userPage=Math.max(1,userPage-1);void renderUsersPage();});host.querySelector('[data-next]')?.addEventListener('click',()=>{userPage=Math.min(result.totalPages,userPage+1);void renderUsersPage();});host.querySelectorAll('[data-user-edit]').forEach(b=>b.addEventListener('click',()=>openEditUser(rows.find(x=>x.userId===b.dataset.userEdit))));host.querySelectorAll('[data-user-delete]').forEach(b=>b.addEventListener('click',()=>deleteUser(rows.find(x=>x.userId===b.dataset.userDelete))));}catch(e){host.innerHTML=fail(e);}
  }

  async function openEditUser(user){if(!user)return;const role=user.roles?.[0]||'Viewer';const modal=await window.p127OpenModal({title:arabic?'تعديل المستخدم':'Edit user',confirmText:arabic?'حفظ':'Save',body:`<div class="p127-user-form"><div class="p127-field"><label>${arabic?'الاسم':'Display name'}</label><input id="p127EditDisplay" value="${esc(user.displayName)}"/></div><div class="p127-field"><label>Username</label><input value="${esc(user.userName)}" readonly/></div><div class="p127-field"><label>${arabic?'الدور':'Role'}</label><select id="p127EditRole"><option ${role==='Administrator'?'selected':''}>Administrator</option><option ${role==='CatalogEditor'?'selected':''}>CatalogEditor</option><option ${role==='Viewer'?'selected':''}>Viewer</option></select></div><div class="p127-field"><label><input id="p127EditEnabled" type="checkbox" ${user.isEnabled?'checked':''}/> ${arabic?'نشط':'Active'}</label></div></div><p><small>${arabic?'External Subject مُدار آليًا ولا يتم تحريره يدويًا.':'External Subject is system-managed and is not manually editable.'}</small></p>`});if(!modal)return;try{await request(`/client-api/admin/users/${user.userId}`,{method:'PUT',body:JSON.stringify({expectedVersion:user.version,userName:user.userName,displayName:modal.querySelector('#p127EditDisplay').value.trim(),externalSubject:user.externalSubject,isEnabled:modal.querySelector('#p127EditEnabled').checked,roles:[modal.querySelector('#p127EditRole').value]})});await renderUsersPage();}catch(e){alert((e.body&&e.body.detail)||e.message);}}

  async function deleteUser(user){if(!user)return;const modal=await window.p127OpenModal({title:arabic?'حذف المستخدم نهائيًا':'Delete user permanently',danger:true,confirmText:arabic?'حذف نهائي':'Delete permanently',body:`<p>${arabic?'سيتم حذف المستخدم وأدواره من MAM نهائيًا. لا يتم حذف حساب Active Directory نفسه.':'The MAM user and role assignments will be permanently removed. The Active Directory account itself is not deleted.'}</p><p><strong>${esc(user.displayName)}</strong><br>${esc(user.userName)}</p>`});if(!modal)return;try{await request(`/client-api/admin/users/${user.userId}`,{method:'DELETE'});await renderUsersPage();}catch(e){alert((e.body&&e.body.detail)||e.message);}}

  async function loadPolicies(){const panel=document.getElementById('p127AdminPanel');if(!panel)return;panel.innerHTML=state('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل السياسات…':'Loading policies…');try{const policies=await request('/client-api/admin/policies');panel.innerHTML=`<div class="card"><h3><i class="bi bi-sliders"></i> ${arabic?'السياسات المركزية':'Authoritative policies'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic?'السياسة':'Policy'}</th><th>${arabic?'الفئة':'Category'}</th><th>Version</th><th>${arabic?'الحالة':'Status'}</th><th></th></tr></thead><tbody>${policies.map(p=>`<tr><td>${esc(arabic&&p.displayNameAr?p.displayNameAr:p.displayNameEn)}</td><td>${esc(p.category)}</td><td>v${esc(p.version)}</td><td>${p.isEnabled?(arabic?'مفعلة':'Enabled'):(arabic?'معطلة':'Disabled')}</td><td><button class="action" data-policy="${esc(p.policyKey)}">${arabic?'تعديل':'Edit'}</button></td></tr>`).join('')}</tbody></table></div></div>`;panel.querySelectorAll('[data-policy]').forEach(b=>b.addEventListener('click',()=>editPolicy(policies.find(x=>x.policyKey===b.dataset.policy))));}catch(e){panel.innerHTML=fail(e);}}

  async function editPolicy(policy){if(!policy)return;const modal=await window.p127OpenModal({title:arabic?'تعديل السياسة':'Edit policy',confirmText:arabic?'حفظ':'Save',body:`<div class="p127-field"><label>${arabic?'JSON السياسة':'Policy JSON'}</label><textarea id="p127PolicyPayload" rows="10" style="width:100%">${esc(JSON.stringify(policy.payload,null,2))}</textarea></div><div class="p127-field"><label>SecretRef</label><input id="p127PolicySecret" value="${esc(policy.secretRef||'')}"/></div><p><label><input id="p127PolicyEnabled" type="checkbox" ${policy.isEnabled?'checked':''}/> ${arabic?'مفعلة':'Enabled'}</label> &nbsp; <label><input id="p127PolicyRestart" type="checkbox" ${policy.requiresRestart?'checked':''}/> ${arabic?'يتطلب إعادة تشغيل':'Requires restart'}</label></p>`});if(!modal)return;try{const payload=JSON.parse(modal.querySelector('#p127PolicyPayload').value);const body={expectedVersion:policy.version,category:policy.category,displayNameEn:policy.displayNameEn,displayNameAr:policy.displayNameAr,payload,secretRef:modal.querySelector('#p127PolicySecret').value.trim()||null,requiresRestart:modal.querySelector('#p127PolicyRestart').checked,isEnabled:modal.querySelector('#p127PolicyEnabled').checked};const validation=await request(`/client-api/admin/policies/${encodeURIComponent(policy.policyKey)}/validate`,{method:'POST',body:JSON.stringify(body)});if(!validation.valid)throw new Error((validation.errors||[]).join(' '));await request(`/client-api/admin/policies/${encodeURIComponent(policy.policyKey)}`,{method:'PUT',body:JSON.stringify(body)});await loadPolicies();}catch(e){alert(e.message);}}

  async function loadAudit(){const panel=document.getElementById('p127AdminPanel');if(!panel)return;panel.innerHTML=state('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل التدقيق…':'Loading audit…');try{const audit=await request('/client-api/admin/audit?limit=100');const rows=audit.items||[];panel.innerHTML=`<div class="card"><div class="toolbar"><h3><i class="bi bi-clock-history"></i> ${arabic?'سجل التدقيق':'Audit log'}</h3><button id="p127AuditExport" class="action">${arabic?'تصدير CSV':'Export CSV'}</button></div><div class="table-wrap"><table><thead><tr><th>UTC</th><th>${arabic?'المنفذ':'Actor'}</th><th>${arabic?'الإجراء':'Action'}</th><th>${arabic?'النتيجة':'Outcome'}</th></tr></thead><tbody>${rows.map(a=>`<tr><td>${esc(new Date(a.occurredAtUtc).toLocaleString())}</td><td>${esc(a.actorId)}</td><td>${esc(a.action)}</td><td>${esc(a.outcome)}</td></tr>`).join('')}</tbody></table></div></div>`;document.getElementById('p127AuditExport').addEventListener('click',()=>window.open('/client-api/admin/audit/export?limit=500','_blank','noopener'));if(typeof window.p127ApplyPagination==='function')window.p127ApplyPagination(panel);}catch(e){panel.innerHTML=fail(e);}}

  async function loadHealth(health){const panel=document.getElementById('p127AdminPanel');if(!panel)return;let value=health;try{value=value||await request('/client-api/admin/health');panel.innerHTML=`<div class="p127-card-cluster"><div class="card"><h3><i class="bi bi-heart-pulse"></i> ${arabic?'حالة مخزن الإدارة':'Administration store'}</h3>${value.isReady?state('empty',arabic?'جاهز':'Ready',value.detail||'Ready'):state('degraded',arabic?'متدهور':'Degraded',value.detail||'Unavailable')}</div><div class="card"><h3><i class="bi bi-shield-lock"></i> ${arabic?'حدود الأمان':'Security boundary'}</h3><p>${arabic?'القيم السرية لا تظهر في المتصفح. يتم عرض SecretRef فقط عند الحاجة.':'Resolved secrets are never rendered in the browser. Only opaque SecretRef identifiers are exposed when required.'}</p></div></div>`;}catch(e){panel.innerHTML=fail(e);}}
})();