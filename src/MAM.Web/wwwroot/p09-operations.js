(() => {
  pages.reports = ['Operations & DR', 'التقارير والمراقبة والتعافي'];
  const navHost = document.getElementById('nav');
  const reportsButton = document.createElement('button');
  reportsButton.dataset.route = 'reports';
  reportsButton.textContent = pages.reports[0];
  const adminButton = navHost.querySelector('[data-route="admin"]');
  navHost.insertBefore(reportsButton, adminButton || null);
  reportsButton.addEventListener('click', () => { route = 'reports'; render(); });

  const baseRender = render;
  render = function () {
    baseRender();
    reportsButton.classList.toggle('active', route === 'reports');
    reportsButton.textContent = pages.reports[arabic ? 1 : 0];
    if (route === 'reports') void loadOperations();
  };

  const safe = value => Number(value || 0).toLocaleString();
  const bytes = value => {
    let n = Number(value || 0); const units = ['B','KB','MB','GB','TB']; let i=0;
    while(n>=1024 && i<units.length-1){n/=1024;i++;}
    return `${n.toFixed(i===0?0:1)} ${units[i]}`;
  };
  const status = (kind, title, detail) => `<div class="state ${kind}"><strong>${esc(title)}</strong><br>${esc(detail)}</div>`;

  async function get(path){
    const response = await fetch(path, { headers: { Accept: 'application/json' } });
    if(response.status===401||response.status===403) throw Object.assign(new Error('denied'),{kind:'denied'});
    if(response.status===503) throw Object.assign(new Error('degraded'),{kind:'degraded'});
    if(!response.ok) throw Object.assign(new Error(`HTTP ${response.status}`),{kind:'error'});
    return response.json();
  }

  async function loadOperations(){
    const languageAtRequest = arabic;
    content.innerHTML = `${lead(arabic?'التقارير والمراقبة والتعافي':'Reports, Monitoring, Resilience & DR', arabic?'قراءة تشغيلية مباشرة من الحالة المركزية الموثقة، بدون مؤشرات إنتاجية مختلقة.':'Live operational reporting from authoritative persisted state; no fabricated production targets.', 'P09 · CENTRAL API')}
      <div class="card">${status('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل حالة التشغيل…':'Loading operational state…')}</div>`;
    try{
      const [summary, throughput, queues, integrity, storage, dependencies] = await Promise.all([
        get('/client-api/operations/summary'), get('/client-api/operations/throughput?windowHours=24'),
        get('/client-api/operations/queues'), get('/client-api/operations/integrity'),
        get('/client-api/operations/storage'), get('/client-api/operations/dependencies')
      ]);
      if(route!=='reports'||languageAtRequest!==arabic)return;
      renderOperations(summary, throughput, queues, integrity, storage, dependencies);
    }catch(error){
      if(route!=='reports'||languageAtRequest!==arabic)return;
      const kind=error.kind==='denied'?'denied':error.kind==='degraded'?'degraded':'error';
      const heading=arabic
        ? (error.kind==='denied'?'الوصول مرفوض':error.kind==='degraded'?'حالة متدهورة':'خطأ في واجهة API')
        : (error.kind==='denied'?'Permission denied':error.kind==='degraded'?'Degraded':'API error');
      content.innerHTML=`${lead(arabic?'التقارير والمراقبة والتعافي':'Reports, Monitoring, Resilience & DR','P09 · CENTRAL API')}${status(kind,heading,arabic?'تعذر تحميل الحالة التشغيلية المركزية.':'Authoritative operational state could not be loaded.')}`;
    }
  }

  function renderOperations(summary, throughput, queues, integrity, storage, dependencies){
    const queueRows=(queues.queues||[]).map(q=>`<tr><td><strong>${esc(q.queue)}</strong></td><td>${safe(q.pending)}</td><td>${safe(q.leased)}</td><td>${safe(q.failed)}</td><td>${safe(q.staleLeases)}</td><td>${q.oldestPendingAtUtc?esc(new Date(q.oldestPendingAtUtc).toLocaleString()):'—'}</td></tr>`).join('');
    const depRows=(dependencies.items||[]).map(d=>`<tr><td><strong>${esc(d.dependency)}</strong></td><td>${esc(d.status)}</td><td>${esc(d.targetId||'—')}</td><td>${esc(d.detail)}</td></tr>`).join('');
    const protectionTotal=Number(integrity.protected||0)+Number(integrity.pending||0)+Number(integrity.failed||0)+Number(integrity.mismatch||0);
    const coverage=protectionTotal?`${(Number(integrity.protected||0)*100/protectionTotal).toFixed(1)}%`:'—';
    content.innerHTML=`${lead(arabic?'التقارير والمراقبة والتعافي':'Reports, Monitoring, Resilience & DR',arabic?'المؤشرات محسوبة من SQL والحالة الدائمة للخدمات المركزية.':'Metrics are calculated from SQL and durable central service state.','P09 · LIVE')}
      <div class="grid four">
        <div class="card metric"><strong>${safe(summary.assets)}</strong><span>${arabic?'الأصول':'Assets'}</span><small>${safe(summary.originals)} ${arabic?'نسخة أصلية':'originals'}</small></div>
        <div class="card metric"><strong>${bytes(summary.originalBytes)}</strong><span>${arabic?'حجم الأصول الأصلية':'Original bytes'}</span><small>${esc(storage.primaryTargetId)}</small></div>
        <div class="card metric"><strong>${coverage}</strong><span>${arabic?'تغطية الحماية':'Protection coverage'}</span><small>${safe(integrity.protected)} ${arabic?'تم التحقق منها':'verified'}</small></div>
        <div class="card metric"><strong>${bytes(throughput.completedBytes)}</strong><span>${arabic?'إدخال آخر 24 ساعة':'24h ingest'}</span><small>${safe(throughput.completedSessions)} ${arabic?'جلسة':'sessions'}</small></div>
      </div>
      <div class="card"><h3>${arabic?'القوائم الدائمة والاسترداد':'Durable queues & recovery'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic?'القائمة':'Queue'}</th><th>${arabic?'معلّق':'Pending'}</th><th>${arabic?'قيد التنفيذ':'Leased'}</th><th>${arabic?'فشل':'Failed'}</th><th>${arabic?'متقادم':'Stale'}</th><th>${arabic?'أقدم عنصر معلّق':'Oldest pending'}</th></tr></thead><tbody>${queueRows||`<tr><td colspan="6">${arabic?'لا توجد حالة للقوائم.':'No queue state.'}</td></tr>`}</tbody></table></div><p><small>${arabic?'CaptureUploadHandoff يمثل حالة التسليم المركزية بعد التسجيل؛ ملفات الاسترداد غير المسلّمة تبقى محلية مؤقتًا حسب حدود P07.':'CaptureUploadHandoff represents the central post-capture handoff; unhanded workstation recovery manifests remain temporary/local under the P07 boundary.'}</small></p></div>
      <div class="grid two"><div class="card"><h3>${arabic?'سلامة النسخ':'Integrity & protection'}</h3><p>${arabic?'محمي':'Protected'}: <strong>${safe(integrity.protected)}</strong><br>${arabic?'معلّق':'Pending'}: ${safe(integrity.pending)}<br>${arabic?'فشل':'Failed'}: ${safe(integrity.failed)}<br>${arabic?'عدم تطابق':'Mismatch'}: ${safe(integrity.mismatch)}<br>${arabic?'البايتات التي تم التحقق منها':'Verified bytes'}: ${bytes(integrity.protectedBytes)}</p></div>
      <div class="card"><h3>${arabic?'التخزين الآمن':'Safe storage view'}</h3><p>${arabic?'التخزين الأساسي':'Primary'}: <strong>${esc(storage.primaryTargetId)}</strong> · ${bytes(storage.authoritativeOriginalBytes)}<br>${arabic?'التخزين الاحتياطي':'Backup'}: <strong>${esc(storage.backupTargetId)}</strong> · ${bytes(storage.verifiedProtectedBytes)}</p><p><small>${arabic?'لا يتم إرسال مسارات الملفات أو بيانات الاعتماد إلى المتصفح.':'Filesystem roots and credentials are never sent to the browser.'}</small></p></div></div>
      <div class="card"><h3>${arabic?'صحة الاعتمادات':'Dependency health'}</h3><div class="table-wrap"><table><thead><tr><th>${arabic?'الاعتماد':'Dependency'}</th><th>${arabic?'الحالة':'Status'}</th><th>${arabic?'الهدف':'Target'}</th><th>${arabic?'تفاصيل آمنة':'Safe detail'}</th></tr></thead><tbody>${depRows}</tbody></table></div></div>
      <div class="card"><div class="toolbar"><div><h3>${arabic?'حزمة التشخيص':'Diagnostics bundle'}</h3><p>${arabic?'حزمة دعم خالية من الأسرار مع Correlation ID.':'Secret-safe support bundle with correlation ID.'}</p></div><button id="p09Diagnostics" class="action">${arabic?'عرض التشخيص':'View diagnostics'}</button></div><pre id="p09DiagnosticsOutput" style="white-space:pre-wrap;overflow:auto"></pre></div>`;
    document.getElementById('p09Diagnostics')?.addEventListener('click', async()=>{
      const output=document.getElementById('p09DiagnosticsOutput');
      output.textContent=arabic?'جاري التحميل…':'Loading…';
      try{output.textContent=JSON.stringify(await get('/client-api/operations/diagnostics'),null,2);}catch{output.textContent=arabic?'تعذر تحميل التشخيص.':'Diagnostics unavailable.';}
    });
  }
})();