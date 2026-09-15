(() => {
  'use strict';

  const ROUTE_KEY = 'mam.p127.route';
  const ASSET_KEY = 'mam.p127.asset';
  const META_KEY = 'mam.p127.metadataAsset';
  const SIDEBAR_KEY = 'mam.p127.sidebarCollapsed';
  const PAGE_SIZE = 10;

  const iconByRoute = {
    dashboard:'bi-speedometer2', library:'bi-collection-play', asset:'bi-file-earmark-richtext', ingest:'bi-box-arrow-in-down',
    upload:'bi-cloud-arrow-up', queue:'bi-hourglass-split', reports:'bi-graph-up-arrow', admin:'bi-shield-lock', settings:'bi-gear',
    search:'bi-search', categories:'bi-diagram-3', references:'bi-person-badge', mediaPermissions:'bi-shield-check', myPermissions:'bi-person-lock'
  };
  const cardIcons = ['bi-collection-play','bi-shield-check','bi-activity','bi-database-check','bi-people','bi-hdd-stack','bi-clock-history','bi-file-earmark-text'];

  let currentSession = null;
  let currentCapabilities = [];
  let queuePage = 1;
  let queuePoll = null;
  let queueTimer = null;
  let passiveQueued = false;

  pages.myPermissions = ['My Permissions', 'صلاحياتي'];

  const priorShellPage = shellPage;
  shellPage = function () {
    if (route === 'myPermissions') {
      return `${lead(arabic ? 'صلاحياتي' : 'My Permissions', arabic ? 'الأدوار والصلاحيات الفعلية للحساب الحالي كما يراها MAM.' : 'Effective roles and permissions for the current MAM session.', 'IDENTITY & ACCESS')}<div id="p127PermissionsHost">${state('loading', arabic ? 'جارٍ التحميل' : 'Loading', arabic ? 'جاري تحميل الصلاحيات…' : 'Loading effective permissions…')}</div>`;
    }
    return priorShellPage();
  };

  function safe(value) { return typeof esc === 'function' ? esc(value) : String(value ?? ''); }
  function has(permission) { return (currentSession?.permissions || []).includes(permission); }

  window.p127OpenModal = function ({ title, body, confirmText, cancelText, danger = false, hideCancel = false }) {
    return new Promise(resolve => {
      document.querySelector('.p127-modal-backdrop')?.remove();
      const backdrop = document.createElement('div');
      backdrop.className = 'p127-modal-backdrop';
      backdrop.innerHTML = `<section class="p127-modal" role="dialog" aria-modal="true" aria-label="${safe(title)}"><div class="p127-modal-head"><h3>${safe(title)}</h3><button type="button" class="p127-modal-close" aria-label="Close"><i class="bi bi-x-lg"></i></button></div><div class="p127-modal-body">${body}</div><div class="p127-modal-actions">${hideCancel ? '' : `<button type="button" class="action p127-modal-cancel">${safe(cancelText || (arabic ? 'إلغاء' : 'Cancel'))}</button>`}<button type="button" class="action p127-modal-confirm ${danger ? 'p127-danger' : ''}">${safe(confirmText || (arabic ? 'تأكيد' : 'Confirm'))}</button></div></section>`;
      document.body.appendChild(backdrop);
      const close = value => { backdrop.remove(); resolve(value); };
      backdrop.querySelector('.p127-modal-close')?.addEventListener('click', () => close(null));
      backdrop.querySelector('.p127-modal-cancel')?.addEventListener('click', () => close(null));
      backdrop.querySelector('.p127-modal-confirm')?.addEventListener('click', () => close(backdrop));
      backdrop.addEventListener('click', event => { if (event.target === backdrop) close(null); });
      backdrop.querySelector('input,select,textarea,button')?.focus();
    });
  };

  async function promptText(title, label, initial = '') {
    const modal = await window.p127OpenModal({
      title,
      body:`<div class="p127-field"><label>${safe(label)}</label><input id="p127PromptValue" value="${safe(initial)}" maxlength="200" /></div>`,
      confirmText:arabic ? 'إضافة' : 'Add'
    });
    if (!modal) return null;
    return modal.querySelector('#p127PromptValue')?.value.trim() || null;
  }

  async function loadIdentity() {
    try {
      const [sessionResponse, capabilityResponse, authResponse] = await Promise.all([
        fetch('/client-api/session', { headers:{Accept:'application/json'}, cache:'no-store' }),
        fetch('/client-api/discovery/my-media-capabilities', { headers:{Accept:'application/json'}, cache:'no-store' }),
        fetch('/auth/status', { headers:{Accept:'application/json'}, cache:'no-store' })
      ]);
      if (sessionResponse.ok) currentSession = await sessionResponse.json();
      if (capabilityResponse.ok) currentCapabilities = await capabilityResponse.json();
      const auth = authResponse.ok ? await authResponse.json() : null;
      ensureProfile(auth);
      refreshAdminMenuVisibility();
      if (route === 'myPermissions') void renderMyPermissions();
    } catch { }
  }

  function restoreLocation() {
    const params = new URLSearchParams(location.hash.replace(/^#/, ''));
    const desired = params.get('route') || localStorage.getItem(ROUTE_KEY) || 'dashboard';
    const allowed = pages[desired] ? desired : 'dashboard';
    const asset = params.get('asset') || localStorage.getItem(ASSET_KEY) || '';
    route = allowed;
    if (typeof p12SelectedAssetId !== 'undefined' && asset) p12SelectedAssetId = asset;
  }

  function persistLocation() {
    localStorage.setItem(ROUTE_KEY, route);
    let asset = '';
    if (route === 'asset' && typeof p12SelectedAssetId !== 'undefined' && p12SelectedAssetId) {
      asset = p12SelectedAssetId;
      localStorage.setItem(ASSET_KEY, asset);
    }
    const params = new URLSearchParams({ route });
    if (asset) params.set('asset', asset);
    history.replaceState(null, '', `${location.pathname}${location.search}#${params}`);
  }

  function ensureSidebarToggle() {
    const shell = document.querySelector('.app-shell');
    const brand = document.querySelector('.sidebar .brand');
    if (!shell || !brand) return;
    shell.classList.toggle('p127-sidebar-collapsed', localStorage.getItem(SIDEBAR_KEY) === '1');
    if (brand.querySelector('.p127-sidebar-toggle')) return;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'p127-sidebar-toggle';
    button.title = arabic ? 'طي القائمة' : 'Collapse navigation';
    button.innerHTML = '<i class="bi bi-list"></i>';
    button.addEventListener('click', () => {
      shell.classList.toggle('p127-sidebar-collapsed');
      localStorage.setItem(SIDEBAR_KEY, shell.classList.contains('p127-sidebar-collapsed') ? '1' : '0');
    });
    brand.appendChild(button);
  }

  function ensureAdminMenu() {
    const host = document.getElementById('nav');
    if (!host) return;
    let menu = host.querySelector('.p127-admin-menu');
    if (!menu) {
      menu = document.createElement('div');
      menu.className = 'p127-admin-menu';
      menu.innerHTML = `<button type="button" class="p127-admin-trigger"><span class="p127-nav-icon"><i class="bi bi-sliders"></i></span><span class="p127-nav-label">${arabic ? 'إعدادات مسؤول النظام' : 'System Administrator Settings'}</span><i class="bi bi-chevron-down"></i></button><div class="p127-admin-submenu"></div>`;
      const firstAdmin = host.querySelector('[data-route="admin"]');
      host.insertBefore(menu, firstAdmin || null);
      menu.querySelector('.p127-admin-trigger').addEventListener('click', () => menu.classList.toggle('open'));
    }
    const submenu = menu.querySelector('.p127-admin-submenu');
    ['admin','settings','categories','references','mediaPermissions'].forEach(key => {
      const button = host.querySelector(`[data-route="${key}"]`);
      if (button && button.parentElement !== submenu) submenu.appendChild(button);
    });
    const label = menu.querySelector('.p127-admin-trigger .p127-nav-label');
    if (label) label.textContent = arabic ? 'إعدادات مسؤول النظام' : 'System Administrator Settings';
    refreshAdminMenuVisibility();
  }

  function refreshAdminMenuVisibility() {
    const menu = document.querySelector('.p127-admin-menu');
    if (!menu) return;
    const visible = [...menu.querySelectorAll('.p127-admin-submenu [data-route]')].some(button => !button.hidden && getComputedStyle(button).display !== 'none');
    menu.hidden = !visible;
  }

  function decorateNavigation() {
    ensureSidebarToggle();
    ensureAdminMenu();
    document.querySelectorAll('#nav [data-route]').forEach(button => {
      const key = button.dataset.route;
      const page = pages[key];
      const label = page ? page[arabic ? 1 : 0] : button.textContent.trim();
      const icon = iconByRoute[key] || 'bi-circle';
      if (!button.querySelector('.p127-nav-icon') || !button.querySelector('.p127-nav-label')) {
        button.innerHTML = `<span class="p127-nav-icon"><i class="bi ${icon}"></i></span><span class="p127-nav-label">${safe(label)}</span>`;
      } else {
        button.querySelector('.p127-nav-label').textContent = label;
        button.querySelector('.p127-nav-icon i').className = `bi ${icon}`;
      }
    });
    refreshAdminMenuVisibility();
  }

  function ensureProfile(auth) {
    const actions = document.querySelector('.topbar .actions');
    if (!actions) return;
    let root = actions.querySelector('.p127-profile');
    if (!root) {
      root = document.createElement('div');
      root.className = 'p127-profile';
      root.innerHTML = `<button type="button" class="p127-profile-button" title="${arabic ? 'الحساب الحالي' : 'Current user'}"><i class="bi bi-person-circle"></i></button><div class="p127-profile-menu"><div class="p127-profile-summary"><strong id="p127ProfileName"></strong><small id="p127ProfileRoles"></small></div><button type="button" data-p127-permissions><i class="bi bi-person-lock"></i><span>${arabic ? 'صلاحيات المستخدم الحالي' : 'Current user permissions'}</span></button><a href="/"><i class="bi bi-house-door"></i><span>${arabic ? 'فتح الصفحة الرئيسية' : 'Open landing page'}</span></a><button type="button" data-p127-logout class="p127-danger"><i class="bi bi-box-arrow-right"></i><span>${arabic ? 'تسجيل الخروج' : 'Sign out'}</span></button></div>`;
      actions.appendChild(root);
      root.querySelector('.p127-profile-button').addEventListener('click', () => root.classList.toggle('open'));
      root.querySelector('[data-p127-permissions]').addEventListener('click', () => { root.classList.remove('open'); route='myPermissions'; render(); });
      root.querySelector('[data-p127-logout]').addEventListener('click', async () => {
        root.classList.remove('open');
        try { await fetch('/auth/logout', { method:'POST', credentials:'same-origin' }); } catch { }
        location.href = '/';
      });
      document.addEventListener('click', event => { if (!root.contains(event.target)) root.classList.remove('open'); });
    }
    const name = currentSession?.displayName || auth?.userName || (arabic ? 'المستخدم الحالي' : 'Current user');
    const roles = (currentSession?.roles || []).join(' · ') || '—';
    root.querySelector('#p127ProfileName').textContent = name;
    root.querySelector('#p127ProfileRoles').textContent = roles;
    root.querySelector('[data-p127-permissions] span').textContent = arabic ? 'صلاحيات المستخدم الحالي' : 'Current user permissions';
    root.querySelector('[data-p127-logout] span').textContent = arabic ? 'تسجيل الخروج' : 'Sign out';
  }

  function iconizeCards(root = content) {
    let index = 0;
    root.querySelectorAll('.card > h3').forEach(heading => {
      if (heading.classList.contains('p127-card-heading')) return;
      const text = heading.innerHTML;
      heading.classList.add('p127-card-heading');
      heading.innerHTML = `<span class="p127-card-icon"><i class="bi ${cardIcons[index++ % cardIcons.length]}"></i></span><span>${text}</span>`;
    });
    root.querySelectorAll('.card.metric').forEach((card, i) => {
      if (card.querySelector(':scope > .p127-card-icon')) return;
      card.insertAdjacentHTML('afterbegin', `<span class="p127-card-icon"><i class="bi ${cardIcons[i % cardIcons.length]}"></i></span>`);
    });
  }

  function clusterCards(parent) {
    if (!parent || ['admin','reports'].includes(route)) return;
    const children = [...parent.children];
    let run = [];
    const flush = () => {
      if (run.length < 2) { run = []; return; }
      const cluster = document.createElement('div');
      cluster.className = 'p127-card-cluster';
      run[0].before(cluster);
      run.forEach(node => cluster.appendChild(node));
      run = [];
    };
    children.forEach(child => {
      if (child.matches('.card:not(.p127-full-width)')) run.push(child);
      else flush();
    });
    flush();
  }

  function paginateTable(table) {
    if (!table || table.dataset.p127Pager === '1') return;
    const body = table.tBodies?.[0];
    if (!body) return;
    const rows = [...body.rows];
    if (rows.length <= PAGE_SIZE) return;
    table.dataset.p127Pager = '1';
    let page = 1;
    const totalPages = Math.ceil(rows.length / PAGE_SIZE);
    const pager = document.createElement('div');
    pager.className = 'p127-pager';
    const renderPage = () => {
      rows.forEach((row, index) => row.hidden = index < (page - 1) * PAGE_SIZE || index >= page * PAGE_SIZE);
      pager.innerHTML = `<button type="button" data-prev ${page===1?'disabled':''}><i class="bi bi-chevron-left"></i></button><span class="p127-page-info">${arabic?'صفحة':'Page'} ${page} / ${totalPages}</span><button type="button" data-next ${page===totalPages?'disabled':''}><i class="bi bi-chevron-right"></i></button>`;
      pager.querySelector('[data-prev]')?.addEventListener('click', () => { if(page>1){page--;renderPage();} });
      pager.querySelector('[data-next]')?.addEventListener('click', () => { if(page<totalPages){page++;renderPage();} });
    };
    (table.closest('.table-wrap') || table).after(pager);
    renderPage();
  }

  function paginateList(list) {
    if (!list || list.dataset.p127Pager === '1') return;
    const rows = [...list.children].filter(x => x.classList.contains('row'));
    if (rows.length <= PAGE_SIZE) return;
    list.dataset.p127Pager = '1';
    let page = 1;
    const totalPages = Math.ceil(rows.length / PAGE_SIZE);
    const pager = document.createElement('div');
    pager.className = 'p127-pager';
    const renderPage = () => {
      rows.forEach((row, index) => row.hidden = index < (page - 1) * PAGE_SIZE || index >= page * PAGE_SIZE);
      pager.innerHTML = `<button type="button" data-prev ${page===1?'disabled':''}><i class="bi bi-chevron-left"></i></button><span class="p127-page-info">${arabic?'صفحة':'Page'} ${page} / ${totalPages}</span><button type="button" data-next ${page===totalPages?'disabled':''}><i class="bi bi-chevron-right"></i></button>`;
      pager.querySelector('[data-prev]')?.addEventListener('click', () => { if(page>1){page--;renderPage();} });
      pager.querySelector('[data-next]')?.addEventListener('click', () => { if(page<totalPages){page++;renderPage();} });
    };
    list.after(pager);
    renderPage();
  }

  function applyPagination(root = content) {
    root.querySelectorAll('table').forEach(paginateTable);
    root.querySelectorAll('.list').forEach(paginateList);
  }

  function enhanceReports() {
    if (route !== 'reports' || content.dataset.p127Reports === '1' || !document.getElementById('p09Diagnostics')) return;
    const leadNode = content.querySelector(':scope > .lead');
    const children = [...content.children].filter(x => x !== leadNode);
    if (children.length < 4) return;
    content.dataset.p127Reports = '1';
    const tabs = document.createElement('div');
    tabs.className = 'p127-tabs';
    const labels = arabic
      ? ['نظرة عامة','القوائم والتعافي','الحماية والتخزين','الاعتمادات','التشخيص']
      : ['Overview','Queues & recovery','Protection & storage','Dependencies','Diagnostics'];
    const panels = labels.map((label, index) => {
      const button = document.createElement('button');
      button.type = 'button'; button.className = `p127-tab ${index===0?'active':''}`; button.textContent = label; button.dataset.tab = String(index);
      tabs.appendChild(button);
      const panel = document.createElement('div');
      panel.className = 'p127-tab-panel'; panel.hidden = index !== 0; panel.dataset.panel = String(index);
      return panel;
    });
    leadNode?.after(tabs);
    panels.slice().reverse().forEach(panel => tabs.after(panel));
    if (children[0]) panels[0].appendChild(children[0]);
    if (children[1]) panels[1].appendChild(children[1]);
    if (children[2]) panels[2].appendChild(children[2]);
    if (children[3]) panels[3].appendChild(children[3]);
    children.slice(4).forEach(node => panels[4].appendChild(node));
    tabs.querySelectorAll('.p127-tab').forEach(button => button.addEventListener('click', () => {
      tabs.querySelectorAll('.p127-tab').forEach(x => x.classList.toggle('active', x === button));
      panels.forEach((panel, index) => panel.hidden = index !== Number(button.dataset.tab));
    }));
    iconizeCards(content);
  }

  async function renderMyPermissions() {
    const host = document.getElementById('p127PermissionsHost');
    if (!host) return;
    if (!currentSession) await loadIdentity();
    if (!currentSession) {
      host.innerHTML = state('error', arabic ? 'تعذر التحميل' : 'Unavailable', arabic ? 'تعذر قراءة جلسة المستخدم.' : 'The current session could not be read.');
      return;
    }
    const roles = currentSession.roles || [];
    const permissions = currentSession.permissions || [];
    const capRows = (currentCapabilities || []).map(c => `<tr><td>${safe(c.mediaKind)}</td><td>${c.canView?'✓':'—'}</td><td>${c.canUpload?'✓':'—'}</td><td>${c.canEdit?'✓':'—'}</td><td>${c.canProcess?'✓':'—'}</td><td>${c.canDownload?'✓':'—'}</td></tr>`).join('');
    host.innerHTML = `<div class="p127-card-cluster"><div class="card"><h3>${arabic?'الحساب':'Account'}</h3><p><strong>${safe(currentSession.displayName || '—')}</strong></p></div><div class="card"><h3>${arabic?'الأدوار':'Roles'}</h3><p>${safe(roles.join(' · ') || '—')}</p></div><div class="card"><h3>${arabic?'عدد الصلاحيات':'Permissions'}</h3><p><strong>${permissions.length}</strong></p></div></div><div class="card"><h3>${arabic?'صلاحيات النظام':'System permissions'}</h3><div class="list">${permissions.map(p=>`<div class="row"><b><i class="bi bi-check-circle"></i></b><span style="grid-column:span 3">${safe(p)}</span></div>`).join('') || `<p>${arabic?'لا توجد صلاحيات.':'No permissions.'}</p>`}</div></div><div class="card"><h3>${arabic?'صلاحيات أنواع الميديا':'Media type permissions'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic?'النوع':'Type'}</th><th>View</th><th>Upload</th><th>Edit</th><th>Process</th><th>Download</th></tr></thead><tbody>${capRows}</tbody></table></div></div>`;
    iconizeCards(host); applyPagination(host);
  }

  function formatDuration(ms) {
    const total = Math.max(0, Math.floor(Number(ms || 0) / 1000));
    const h = Math.floor(total / 3600), m = Math.floor((total % 3600) / 60), s = total % 60;
    return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
  }

  function stopQueueTimers() {
    if (queuePoll) clearTimeout(queuePoll);
    if (queueTimer) clearInterval(queueTimer);
    queuePoll = null; queueTimer = null;
  }

  p04LoadQueue = async function () {
    stopQueueTimers();
    const host = document.getElementById('p04QueueState');
    if (!host) return;
    const languageAtRequest = arabic;
    try {
      const response = await fetch(`/client-api/processing/jobs/page?page=${queuePage}&pageSize=${PAGE_SIZE}`, { headers:{Accept:'application/json'}, cache:'no-store' });
      if (!response.ok) return p04ShowFailure(host, response.status, 'queue');
      const result = await response.json();
      if (route !== 'queue' || languageAtRequest !== arabic) return;
      const jobs = Array.isArray(result.items) ? result.items : [];
      if (!jobs.length) { host.innerHTML = state('empty', arabic?'لا توجد وظائف':'Empty', arabic?'لا توجد وظائف معالجة.':'No processing jobs are queued.'); return; }
      const stateName = value => ({0:arabic?'في الانتظار':'Queued',1:arabic?'قيد التنفيذ':'Running',2:arabic?'مكتمل':'Succeeded',3:arabic?'فشل':'Failed'})[Number(value)] || String(value);
      const rows = jobs.map(job => {
        const progress = Math.max(0, Math.min(100, Number(job.progressPercent ?? (job.state===2 ? 100 : 0))));
        const running = Number(job.state) === 1;
        const isTranscript = job.profileId === 'transcript-text-v1';
        const label = isTranscript && running ? (arabic?'جاري التفريغ':'Transcribing') : (job.extractionState || stateName(job.state));
        const elapsed = Number(job.elapsedMs || 0);
        return `<div class="p127-processing-row"><div class="p127-processing-title"><strong>${safe(job.title)}</strong><small>${safe(job.profileId)} · ${safe(job.mediaType)} · ${safe(String(job.jobId).slice(0,13))}</small></div><div><strong>${safe(label)}</strong><br><small>${arabic?'المحاولة':'Attempt'} ${safe(job.attemptCount)}</small></div><div class="p127-progress-wrap"><div class="p127-progress-line"><div class="p127-progress-fill" style="width:${progress}%"></div></div><div class="p127-progress-meta"><span>${progress}%</span><span>${safe(job.extractionDetail || '')}</span></div></div><div><span class="p127-timer" data-p127-elapsed="${elapsed}" ${running&&job.startedAtUtc?`data-p127-start="${safe(job.startedAtUtc)}"`:''}>${formatDuration(elapsed)}</span>${Number(job.state)===3?`<br><button class="action" data-p127-retry="${safe(job.jobId)}">${arabic?'إعادة المحاولة':'Retry'}</button>`:''}</div></div>`;
      }).join('');
      host.innerHTML = `<div id="p04QueueActionState" aria-live="polite"></div><div class="card"><h3>${arabic?'حالة المعالجة':'Processing status'}</h3><div>${rows}</div><div class="p127-pager"><button type="button" data-prev ${result.page<=1?'disabled':''}><i class="bi bi-chevron-left"></i></button><span class="p127-page-info">${arabic?'صفحة':'Page'} ${result.page} / ${result.totalPages} · ${result.totalCount}</span><button type="button" data-next ${result.page>=result.totalPages?'disabled':''}><i class="bi bi-chevron-right"></i></button></div></div>`;
      host.querySelector('[data-prev]')?.addEventListener('click', () => { queuePage=Math.max(1,queuePage-1); void p04LoadQueue(); });
      host.querySelector('[data-next]')?.addEventListener('click', () => { queuePage=Math.min(result.totalPages,queuePage+1); void p04LoadQueue(); });
      host.querySelectorAll('[data-p127-retry]').forEach(button => button.addEventListener('click', async () => {
        const output=document.getElementById('p04QueueActionState'); button.disabled=true;
        try { const r=await fetch(`/client-api/processing/jobs/${button.dataset.p127Retry}/retry`,{method:'POST',headers:{Accept:'application/json'}}); if(!r.ok) throw new Error(`HTTP ${r.status}`); if(output) output.innerHTML=state('empty',arabic?'تمت إعادة المحاولة':'Retry queued',arabic?'تمت إعادة الوظيفة إلى قائمة المعالجة.':'The job was returned to the processing queue.'); setTimeout(()=>void p04LoadQueue(),400); }
        catch { if(output) output.innerHTML=state('error',arabic?'فشل':'Retry failed',arabic?'تعذر إعادة الوظيفة.':'The job could not be retried.'); }
        finally { button.disabled=false; }
      }));
      queueTimer = setInterval(() => {
        if (route !== 'queue') return stopQueueTimers();
        host.querySelectorAll('[data-p127-start]').forEach(node => {
          const start = Date.parse(node.dataset.p127Start); if (Number.isFinite(start)) node.textContent = formatDuration(Date.now() - start);
        });
      }, 1000);
      if (jobs.some(j => Number(j.state)===0 || Number(j.state)===1)) queuePoll=setTimeout(()=>{ if(route==='queue')void p04LoadQueue(); },4000);
      iconizeCards(host);
    } catch { if(route==='queue') host.innerHTML=state('error','API error',arabic?'تعذر تحميل قائمة المعالجة.':'Processing queue could not be loaded.'); }
  };

  function acceptFor(kind) {
    if (kind === 'Video') return [...p03VideoExtensions].join(',');
    if (kind === 'Audio') return [...p03AudioExtensions].join(',');
    if (kind === 'Image') return [...p03ImageExtensions].join(',');
    if (kind === 'Document') return [...p03DocumentExtensions].join(',');
    return p03AllowedExtensions.join(',');
  }

  bindP03UploadWorkspace = function () {
    const fileInput=document.getElementById('p03File');
    const titleInput=document.getElementById('p03Title');
    const button=document.getElementById('p03Upload');
    const statusBox=document.getElementById('p03UploadState');
    if(!fileInput||!titleInput||!button||!statusBox)return;

    if (!document.getElementById('p127UploadKind')) {
      const filter=document.createElement('div'); filter.className='p127-upload-filter';
      filter.innerHTML=`<label for="p127UploadKind">${arabic?'نوع الميديا':'Media type'}</label><select id="p127UploadKind"><option value="">${arabic?'كل الأنواع':'All types'}</option><option value="Video">${arabic?'فيديو':'Video'}</option><option value="Audio">${arabic?'صوت':'Audio'}</option><option value="Image">${arabic?'صور':'Images'}</option><option value="Document">${arabic?'مستندات':'Documents'}</option></select><small>${arabic?'يتم تطبيق الفلتر داخل نافذة اختيار الملف.':'The file picker will filter to the selected media type.'}</small>`;
      fileInput.closest('.toolbar')?.before(filter);
      filter.querySelector('select').addEventListener('change', event => { fileInput.accept=acceptFor(event.target.value); fileInput.value=''; p03SessionId=null; p03SelectedFingerprint=null; });
    }

    fileInput.addEventListener('change',()=>{
      const file=fileInput.files?.[0]; const fingerprint=file?`${file.name}|${file.size}|${file.lastModified}`:null;
      if(fingerprint!==p03SelectedFingerprint){p03SessionId=null;p03SelectedFingerprint=fingerprint;}
      if(file && !titleInput.value.trim()) titleInput.value=file.name.replace(/\.[^.]+$/,'');
      const ext=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
      if(file && !p03AllowedExtensions.includes(ext)){statusBox.innerHTML=state('error',arabic?'نوع غير مسموح':'Unsupported type',arabic?'امتداد الملف غير موجود في سياسة الرفع.':'The selected extension is not allowed.');button.disabled=true;} else button.disabled=false;
    });

    const duplicateVersion = async payload => {
      const modal=await window.p127OpenModal({title:arabic?'الملف مرفوع مسبقًا':'File already uploaded',body:`<div class="p127-duplicate-box"><strong>${arabic?'تم العثور على نسخة مطابقة تمامًا بواسطة SHA-256.':'An identical authoritative original already exists (SHA-256 match).'}</strong><p>${arabic?'يمكنك إلغاء العملية أو إنشاء نسخة جديدة في MAM بدون إعادة نقل نفس البايتات.' : 'Cancel, or create a new MAM version without retransmitting identical bytes.'}</p></div>`,confirmText:arabic?'إنشاء نسخة جديدة':'Create new version'});
      if(!modal)return false;
      statusBox.innerHTML=state('loading',arabic?'جارٍ إنشاء النسخة':'Creating version',arabic?'جاري إنشاء نسخة جديدة موثقة…':'Creating a new authoritative version…');
      const r=await fetch(`/client-api/uploads/duplicates/${payload.existingAssetId}/version`,{method:'POST',headers:{Accept:'application/json'}});
      if(!r.ok){let d={};try{d=await r.json();}catch{}throw new Error(d.detail||`HTTP ${r.status}`);}
      const created=await r.json();
      statusBox.innerHTML=state('empty',arabic?'تم إنشاء النسخة':'Version created',`${safe(created.title)} · Version ${safe(created.version)} · ${safe(created.assetId)}`);
      fileInput.value=''; titleInput.value=''; p03SessionId=null; p03SelectedFingerprint=null;
      return true;
    };

    button.addEventListener('click',async()=>{
      const file=fileInput.files?.[0]; const assetTitle=titleInput.value.trim(); const extension=file?`.${file.name.split('.').pop()?.toLowerCase()||''}`:'';
      if(!file||!assetTitle){statusBox.innerHTML=state('error',arabic?'تحقق من البيانات':'Validation',arabic?'اختر ملفًا واكتب العنوان.':'Choose a file and enter a title.');return;}
      if(!p03AllowedExtensions.includes(extension)){statusBox.innerHTML=state('error',arabic?'نوع غير مسموح':'Unsupported type',arabic?'هذا النوع غير مسموح به.':'This media type is not allowed.');return;}
      button.disabled=true;
      try{
        statusBox.innerHTML=state('loading',arabic?'جارٍ التحضير':'Preparing',arabic?'جاري حساب SHA-256…':'Calculating SHA-256…');
        const fullSha=await p03HashBlob(file); let session;
        if(p03SessionId){const rr=await fetch(`/client-api/uploads/sessions/${p03SessionId}`,{headers:{Accept:'application/json'}});if(rr.ok)session=await rr.json();else p03SessionId=null;}
        if(!session){
          const create=await fetch('/client-api/uploads/sessions',{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({title:assetTitle,originalFileName:file.name,expectedLength:file.size,expectedSha256:fullSha})});
          if(create.status===409){const payload=await create.json().catch(()=>({}));if(payload.error==='duplicate_detected'&&payload.existingAssetId){await duplicateVersion(payload);return;}}
          if(!create.ok)await p03ThrowResponse(create); session=await create.json(); p03SessionId=session.session.sessionId;
        }
        const chunkSize=session.session.chunkSizeBytes; let offset=session.receivedLength;
        while(offset<file.size){const chunk=file.slice(offset,Math.min(offset+chunkSize,file.size));const chunkSha=await p03HashBlob(chunk);const percent=Math.floor(offset*100/file.size);statusBox.innerHTML=state('loading',arabic?'جاري الرفع':'Uploading',`${percent}% · ${offset}/${file.size}`);const r=await fetch(`/client-api/uploads/sessions/${p03SessionId}/chunks?offset=${offset}`,{method:'PUT',headers:{'X-Chunk-SHA256':chunkSha,'Content-Type':'application/octet-stream',Accept:'application/json'},body:chunk});if(!r.ok)await p03ThrowResponse(r);offset=(await r.json()).receivedLength;}
        const finalize=await fetch(`/client-api/uploads/sessions/${p03SessionId}/finalize`,{method:'POST',headers:{Accept:'application/json'}});
        if(finalize.status===409){const payload=await finalize.json().catch(()=>({}));if(payload.error==='duplicate_detected'&&payload.existingAssetId){await duplicateVersion(payload);return;}}
        if(!finalize.ok)await p03ThrowResponse(finalize); const result=await finalize.json();
        statusBox.innerHTML=state('loading',arabic?'تم الاعتماد':'Primary verified',arabic?'تم اعتماد الأصل؛ جاري إدراج المعالجة…':'Primary verified; queueing processing…');
        const queued=await p03QueueAutomaticProcessing(result.assetId,extension);
        statusBox.innerHTML=state('empty',arabic?'اكتمل الرفع':'Upload completed',`${safe(result.assetId)} · ${safe(queued.join(', ') || '—')}`); p03SessionId=null;
      }catch(error){statusBox.innerHTML=state('error',arabic?'تعذر إكمال العملية':'Upload failed',safe(String(error?.message||error).slice(0,240)));}
      finally{button.disabled=false;}
    });
  };

  p05OpenEditor = async function (assetId) {
    localStorage.setItem(META_KEY, assetId);
    try {
      const [metadataResponse,categoriesResponse,facetsResponse]=await Promise.all([
        fetch(`/client-api/curation/assets/${assetId}/metadata`,{headers:{Accept:'application/json'}}),
        fetch('/client-api/discovery/categories',{headers:{Accept:'application/json'}}),
        fetch('/client-api/curation/search?page=1&pageSize=1',{headers:{Accept:'application/json'}})
      ]);
      if(!metadataResponse.ok)throw new Error(`HTTP ${metadataResponse.status}`);
      const metadata=await metadataResponse.json(); const categories=categoriesResponse.ok?await categoriesResponse.json():[]; const facets=facetsResponse.ok?await facetsResponse.json():{};
      const categoryOptions=(categories||[]).map(c=>`<option value="${safe(c.nameEn)}" ${String(c.nameEn).toLowerCase()===String(metadata.category||'').toLowerCase()?'selected':''}>${safe(arabic&&c.nameAr?c.nameAr:c.nameEn)}</option>`).join('');
      const allTags=new Set([...(metadata.tags||[]),...((facets.facets?.tags||[]).map(x=>x.value))]);
      const tagOptions=[...allTags].sort().map(tag=>`<option value="${safe(tag)}" ${(metadata.tags||[]).includes(tag)?'selected':''}>${safe(tag)}</option>`).join('');
      content.innerHTML=`${lead(arabic?'تهيئة البيانات الوصفية':'Metadata Curation',`${safe(assetId)} · v${safe(metadata.version)} · ${safe(metadata.lifecycle)}`,'CURATION')}<div class="p127-card-cluster"><div class="card"><h3>${arabic?'العنوان الإنجليزي':'English title'}</h3><input id="p127MetaTitle" maxlength="300" value="${safe(metadata.titleEn||'')}" /></div><div class="card"><h3>${arabic?'العنوان العربي':'Arabic title'}</h3><input id="p127MetaTitleAr" maxlength="300" value="${safe(metadata.titleAr||'')}" dir="rtl" /></div></div><div class="p127-card-cluster"><div class="card"><h3>${arabic?'التصنيف':'Category'}</h3><select id="p127MetaCategory"><option value="">${arabic?'بدون تصنيف':'No category'}</option>${categoryOptions}</select><button type="button" class="p127-inline-link" id="p127AddCategory">${arabic?'+ إضافة تصنيف جديد':'+ Add new category'}</button></div><div class="card"><h3>${arabic?'الوسوم':'Tags'}</h3><select id="p127MetaTags" class="p127-multiselect" multiple>${tagOptions}</select><button type="button" class="p127-inline-link" id="p127AddTag">${arabic?'+ إضافة وسم جديد':'+ Add new tag'}</button></div></div><div class="card p127-full-width"><h3>${arabic?'ملاحظات الحفظ':'Preservation notes'}</h3><textarea id="p127MetaNotes" maxlength="2000" style="width:100%;min-height:110px">${safe(metadata.preservationNotes||'')}</textarea></div><div class="card p127-full-width"><div class="toolbar"><button id="p127MetaSave" class="action">${arabic?'حفظ':'Save'}</button><button id="p127MetaLifecycle" class="action">${metadata.lifecycle==='Archived'?(arabic?'استعادة':'Restore'):(arabic?'أرشفة':'Archive')}</button><button id="p127MetaBack" class="action">${arabic?'رجوع للمكتبة':'Back to library'}</button></div><div id="p127MetaState"></div></div>`;
      iconizeCards(content);
      document.getElementById('p127MetaBack').addEventListener('click',()=>{localStorage.removeItem(META_KEY);void p05LoadLibrary();});
      document.getElementById('p127AddTag').addEventListener('click',async()=>{const value=await promptText(arabic?'إضافة وسم':'Add tag',arabic?'اسم الوسم':'Tag name');if(!value)return;const select=document.getElementById('p127MetaTags');if(![...select.options].some(o=>o.value.toLowerCase()===value.toLowerCase()))select.add(new Option(value,value,true,true));else [...select.options].find(o=>o.value.toLowerCase()===value.toLowerCase()).selected=true;});
      document.getElementById('p127AddCategory').addEventListener('click',async()=>{
        const modal=await window.p127OpenModal({title:arabic?'إضافة تصنيف':'Add category',body:`<div class="p127-user-form"><div class="p127-field"><label>English</label><input id="p127NewCategoryEn" maxlength="200" /></div><div class="p127-field"><label>العربية</label><input id="p127NewCategoryAr" maxlength="200" dir="rtl" /></div></div>`,confirmText:arabic?'إضافة':'Add'}); if(!modal)return;
        const nameEn=modal.querySelector('#p127NewCategoryEn')?.value.trim();const nameAr=modal.querySelector('#p127NewCategoryAr')?.value.trim();if(!nameEn)return;
        const r=await fetch('/client-api/discovery/categories',{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({parentCategoryId:null,nameEn,nameAr:nameAr||null,sortOrder:0})});if(!r.ok)return;const created=await r.json();const select=document.getElementById('p127MetaCategory');select.add(new Option(arabic&&created.nameAr?created.nameAr:created.nameEn,created.nameEn,true,true));
      });
      document.getElementById('p127MetaSave').addEventListener('click',async()=>{
        const out=document.getElementById('p127MetaState'); const tags=[...document.getElementById('p127MetaTags').selectedOptions].map(x=>x.value);
        const body={expectedVersion:metadata.version,schemaKey:'core-media-v1',titleEn:document.getElementById('p127MetaTitle').value.trim(),titleAr:document.getElementById('p127MetaTitleAr').value.trim(),eventDate:metadata.eventDate,category:document.getElementById('p127MetaCategory').value||'',tags,preservationNotes:document.getElementById('p127MetaNotes').value.trim()};
        out.innerHTML=state('loading',arabic?'جارٍ الحفظ':'Saving',arabic?'جاري حفظ البيانات…':'Saving metadata…'); const r=await fetch(`/client-api/curation/assets/${assetId}/metadata`,{method:'PUT',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify(body)});if(!r.ok){out.innerHTML=state('error',arabic?'فشل الحفظ':'Save failed',`HTTP ${r.status}`);return;}await p05OpenEditor(assetId);
      });
      document.getElementById('p127MetaLifecycle').addEventListener('click',async()=>{const action=metadata.lifecycle==='Archived'?'restore':'archive';const r=await fetch(`/client-api/curation/assets/${assetId}/${action}`,{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json'},body:JSON.stringify({expectedVersion:metadata.version})});if(r.ok){localStorage.removeItem(META_KEY);void p05LoadLibrary();}});
    } catch { content.innerHTML=`${lead(arabic?'تهيئة البيانات الوصفية':'Metadata Curation','','CURATION')}${state('error','API error',arabic?'تعذر تحميل البيانات الوصفية.':'Metadata could not be loaded.')}`; }
  };

  function enhancePassive() {
    decorateNavigation();
    ensureProfile(null);
    iconizeCards(content);
    applyPagination(content);
    clusterCards(content);
    clusterCards(document.getElementById('p04AssetState'));
    enhanceReports();
  }

  const baseRender = render;
  render = function () {
    stopQueueTimers();
    delete content.dataset.p127Reports;
    baseRender();
    persistLocation();
    requestAnimationFrame(() => {
      enhancePassive();
      if (route === 'myPermissions') void renderMyPermissions();
      if (route === 'library') {
        const metadataAsset = localStorage.getItem(META_KEY);
        if (metadataAsset) setTimeout(() => { if(route==='library' && localStorage.getItem(META_KEY)===metadataAsset) void p05OpenEditor(metadataAsset); }, 80);
      }
    });
  };

  const observer = new MutationObserver(() => {
    if (passiveQueued) return;
    passiveQueued = true;
    requestAnimationFrame(() => { passiveQueued=false; enhancePassive(); });
  });
  observer.observe(document.body, { childList:true, subtree:true });

  restoreLocation();
  decorateNavigation();
  void loadIdentity();
  render();
})();