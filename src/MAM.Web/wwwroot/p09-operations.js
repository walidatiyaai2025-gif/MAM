(() => {
  pages.reports = ['Reports','التقارير'];
  const navHost = document.getElementById('nav');
  const existingReports = navHost.querySelector('[data-route="reports"]');
  const reportsButton = existingReports || document.createElement('button');
  if(!existingReports){
    reportsButton.dataset.route='reports';
    const adminButton=navHost.querySelector('[data-route="admin"]');
    navHost.insertBefore(reportsButton,adminButton||null);
  }
  reportsButton.textContent=pages.reports[0];
  if(!reportsButton.dataset.p09Bound){
    reportsButton.dataset.p09Bound='1';
    reportsButton.addEventListener('click',()=>{route='reports';render();});
  }

  const baseRender=render;
  render=function(){
    baseRender();
    reportsButton.classList.toggle('active',route==='reports');
    reportsButton.textContent=pages.reports[arabic?1:0];
    if(route==='reports')void loadOperations();
  };

  const safe=value=>Number(value||0).toLocaleString();
  const bytes=value=>{
    let n=Number(value||0);const units=['B','KB','MB','GB','TB'];let i=0;
    while(n>=1024&&i<units.length-1){n/=1024;i++;}
    return `${n.toFixed(i===0?0:1)} ${units[i]}`;
  };
  const status=(kind,title,detail)=>`<div class="state ${kind}"><strong>${esc(title)}</strong><br>${esc(detail)}</div>`;
  const pct=(value,total)=>total?Math.max(0,Math.min(100,Number(value||0)*100/Number(total))):0;
  const healthy=value=>/ok|healthy|ready|available|connected|online|success/i.test(String(value||''));

  async function get(path){
    const response=await fetch(path,{headers:{Accept:'application/json'},cache:'no-store'});
    if(response.status===401||response.status===403)throw Object.assign(new Error('denied'),{kind:'denied'});
    if(response.status===503)throw Object.assign(new Error('degraded'),{kind:'degraded'});
    if(!response.ok)throw Object.assign(new Error(`HTTP ${response.status}`),{kind:'error'});
    return response.json();
  }

  function reportHead(subtitle){
    return `<div class="p132-page-head"><div class="p132-page-head-main"><span class="p132-page-icon"><i class="bi bi-bar-chart-fill"></i></span><div><h2>${arabic?'التقارير والمراقبة والتعافي':'Reports, Monitoring & Resilience'}</h2><p>${esc(subtitle||'')}</p></div></div></div>`;
  }

  function reportTabs(){
    const labels=arabic
      ? [['overview','نظرة عامة'],['queues','المعالجة'],['protection','الحماية والتعافي'],['dependencies','الاعتمادات'],['diagnostics','التقارير المتخصصة']]
      : [['overview','Overview'],['queues','Processing'],['protection','Protection & recovery'],['dependencies','Dependencies'],['diagnostics','Specialized reports']];
    return `<div class="p132-report-tabs p127-tabs" id="p132ReportTabs">${labels.map(([key,label],index)=>`<button type="button" class="p132-report-tab p127-tab ${index===0?'active':''}" data-tab="${key}">${esc(label)}</button>`).join('')}</div>`;
  }

  function degradedReport(kind,heading,detail){
    content.innerHTML=`<div class="p132-report">${reportHead(arabic?'قراءة مباشرة من الحالة المركزية الموثقة.':'Live reporting from authoritative central state.')}<div class="p132-report-toolbar">${reportTabs()}<div class="p132-date-chip"><i class="bi bi-clock-history"></i>${arabic?'الحالة الحالية':'Current state'}</div></div><section class="p132-report-kpis">${['bi-database','bi-shield-check','bi-hdd-stack','bi-file-earmark-bar-graph'].map((icon,index)=>`<article class="p132-kpi ${index===1?'green':index===2?'gold':''}"><span class="p132-kpi-icon"><i class="bi ${icon}"></i></span><strong>—</strong><b>${[arabic?'إجمالي التخزين':'Total storage',arabic?'تغطية الحماية':'Protection coverage',arabic?'المعالجة':'Processing',arabic?'التقارير':'Reports'][index]}</b><small>${arabic?'غير متاح حتى عودة الخدمة':'Unavailable until the service recovers'}</small></article>`).join('')}</section><section class="p132-report-card">${status(kind,heading,detail)}</section></div>`;
    bindReportTabs();
  }

  async function loadOperations(){
    const languageAtRequest=arabic;
    content.innerHTML=`<div class="p132-report">${reportHead(arabic?'قراءة تشغيلية مباشرة من الحالة المركزية الموثقة، بدون مؤشرات مختلقة.':'Live operational reporting from authoritative persisted state; no fabricated targets.')}<section class="p132-report-card">${status('loading',arabic?'جارٍ التحميل':'Loading',arabic?'جاري تحميل حالة التشغيل…':'Loading operational state…')}</section></div>`;
    try{
      const [summary,throughput,queues,integrity,storage,dependencies]=await Promise.all([
        get('/client-api/operations/summary'),
        get('/client-api/operations/throughput?windowHours=24'),
        get('/client-api/operations/queues'),
        get('/client-api/operations/integrity'),
        get('/client-api/operations/storage'),
        get('/client-api/operations/dependencies')
      ]);
      if(route!=='reports'||languageAtRequest!==arabic)return;
      renderOperations(summary,throughput,queues,integrity,storage,dependencies);
    }catch(error){
      if(route!=='reports'||languageAtRequest!==arabic)return;
      const kind=error.kind==='denied'?'denied':error.kind==='degraded'?'degraded':'error';
      const heading=arabic
        ?(error.kind==='denied'?'الوصول مرفوض':error.kind==='degraded'?'الخدمة التشغيلية غير جاهزة':'تعذر تحميل التقارير')
        :(error.kind==='denied'?'Permission denied':error.kind==='degraded'?'Operational service unavailable':'Reports unavailable');
      degradedReport(kind,heading,arabic?'تعذر تحميل الحالة التشغيلية المركزية. لم يتم عرض أي أرقام بديلة أو تجريبية.':'Authoritative operational state could not be loaded. No fallback or demo metrics are shown.');
    }
  }

  function renderOperations(summary,throughput,queues,integrity,storage,dependencies){
    const queueRows=queues.queues||[];
    const depRows=dependencies.items||[];
    const protectedTotal=Number(integrity.protected||0)+Number(integrity.pending||0)+Number(integrity.failed||0)+Number(integrity.mismatch||0);
    const coverageNumber=protectedTotal?pct(integrity.protected,protectedTotal):0;
    const coverage=protectedTotal?`${coverageNumber.toFixed(1)}%`:'—';
    const totalQueue=queueRows.reduce((sum,q)=>sum+Number(q.pending||0)+Number(q.leased||0)+Number(q.failed||0)+Number(q.staleLeases||0),0);
    const pending=queueRows.reduce((sum,q)=>sum+Number(q.pending||0),0);
    const leased=queueRows.reduce((sum,q)=>sum+Number(q.leased||0),0);
    const failed=queueRows.reduce((sum,q)=>sum+Number(q.failed||0),0);
    const stale=queueRows.reduce((sum,q)=>sum+Number(q.staleLeases||0),0);
    const a=pct(pending,totalQueue),b=a+pct(leased,totalQueue),c=b+pct(stale,totalQueue);
    const storageTotal=Math.max(Number(storage.authoritativeOriginalBytes||0),Number(storage.verifiedProtectedBytes||0),1);
    const generated=summary.generatedAtUtc?new Date(summary.generatedAtUtc):new Date();
    const generatedLabel=generated.toLocaleString(arabic?'ar-KW':'en-GB',{dateStyle:'short',timeStyle:'short'});
    const dependencyGood=depRows.filter(d=>healthy(d.status)).length;
    const dependencyBad=Math.max(0,depRows.length-dependencyGood);
    const maxQueue=Math.max(1,...queueRows.map(q=>Number(q.pending||0)+Number(q.leased||0)+Number(q.failed||0)+Number(q.staleLeases||0)));

    content.innerHTML=`<div class="p132-report">
      ${reportHead(arabic?'رؤية شاملة لحالة النظام والمعالجة والتخزين والحماية والتعافي.':'A consolidated view of system health, processing, storage, protection and recovery.')}
      <div class="p132-report-toolbar">${reportTabs()}<div class="p132-date-chip"><i class="bi bi-calendar3"></i>${esc(generatedLabel)}</div></div>

      <section class="p132-report-kpis">
        <article class="p132-kpi"><span class="p132-kpi-icon"><i class="bi bi-database"></i></span><strong>${bytes(storage.authoritativeOriginalBytes)}</strong><b>${arabic?'إجمالي التخزين المستخدم':'Authoritative storage used'}</b><small>${safe(storage.originalCount)} ${arabic?'أصل أصلي':'originals'}</small><div class="p132-meter" style="--meter:${Math.min(100,pct(storage.authoritativeOriginalBytes,storageTotal))}%"><span></span></div></article>
        <article class="p132-kpi green"><span class="p132-kpi-icon"><i class="bi bi-shield-check"></i></span><strong>${coverage}</strong><b>${arabic?'تغطية الحماية':'Protection coverage'}</b><small>${safe(integrity.protected)} ${arabic?'نسخة محققة':'verified copies'}</small></article>
        <article class="p132-kpi gold"><span class="p132-kpi-icon"><i class="bi bi-clock-history"></i></span><strong>${bytes(throughput.completedBytes)}</strong><b>${arabic?'بيانات معالجة خلال 24 ساعة':'24h completed ingest'}</b><small>${safe(throughput.completedSessions)} ${arabic?'جلسة مكتملة':'completed sessions'}</small></article>
        <article class="p132-kpi"><span class="p132-kpi-icon"><i class="bi bi-file-earmark-bar-graph"></i></span><strong>${safe(summary.assets)}</strong><b>${arabic?'الأصول في التقارير':'Assets represented'}</b><small>${safe(summary.originals)} ${arabic?'نسخة أصلية':'originals'}</small></article>
      </section>

      <section class="p132-report-panel" data-report-panel="overview">
        <div class="p132-report-grid">
          <article class="p132-report-card"><h3>${arabic?'حالة النظام':'System status'}</h3><div class="p132-health-list">
            <div class="p132-health-row"><i class="bi bi-cpu"></i><strong>${arabic?'المعالجة':'Processing'}</strong><span class="p132-health-pill ${Number(summary.processingFailed||0)>0?'bad':''}">${Number(summary.processingFailed||0)>0?(arabic?'يحتاج متابعة':'Attention'):(arabic?'طبيعي':'Healthy')}</span></div>
            <div class="p132-health-row"><i class="bi bi-database"></i><strong>${arabic?'التخزين الأساسي':'Primary storage'}</strong><span class="p132-health-pill">${esc(storage.primaryTargetId||'—')}</span></div>
            <div class="p132-health-row"><i class="bi bi-cloud-check"></i><strong>${arabic?'النسخ الاحتياطي':'Backup protection'}</strong><span class="p132-health-pill ${Number(integrity.failed||0)+Number(integrity.mismatch||0)>0?'bad':''}">${Number(integrity.failed||0)+Number(integrity.mismatch||0)>0?(arabic?'يحتاج متابعة':'Attention'):(arabic?'متحقق':'Verified')}</span></div>
            <div class="p132-health-row"><i class="bi bi-link-45deg"></i><strong>${arabic?'الخدمات التابعة':'Dependencies'}</strong><span class="p132-health-pill ${dependencyBad?'bad':''}">${dependencyGood}/${depRows.length||0}</span></div>
          </div></article>

          <article class="p132-report-card"><h3>${arabic?'توزيع حالة القوائم':'Queue state distribution'}</h3><div class="p132-donut-wrap"><div class="p132-donut ${totalQueue?'':'empty'}" style="--a:${a}%;--b:${b}%;--c:${c}%"><span class="p132-donut-center">${safe(totalQueue)}<br>${arabic?'عنصر':'items'}</span></div><div class="p132-legend"><span><i></i>${arabic?'معلّق':'Pending'} ${safe(pending)}</span><span><i></i>${arabic?'قيد التنفيذ':'Leased'} ${safe(leased)}</span><span><i></i>${arabic?'متقادم':'Stale'} ${safe(stale)}</span><span><i></i>${arabic?'فشل':'Failed'} ${safe(failed)}</span></div></div></article>

          <article class="p132-report-card"><h3>${arabic?'القوائم خلال الحالة الحالية':'Current queue load'}</h3><div class="p132-queue-bars">${queueRows.length?queueRows.map(q=>{const total=Number(q.pending||0)+Number(q.leased||0)+Number(q.failed||0)+Number(q.staleLeases||0);return `<div class="p132-qrow"><span title="${esc(q.queue)}">${esc(q.queue)}</span><div class="p132-qtrack"><div class="p132-qfill" style="width:${pct(total,maxQueue)}%"></div></div><b>${safe(total)}</b></div>`;}).join(''):`<div class="state empty">${arabic?'لا توجد عناصر في القوائم.':'No queued items.'}</div>`}</div></article>
        </div>
        <div class="p132-report-bottom">
          <article class="p132-report-card"><h3>${arabic?'أحدث مؤشرات التشغيل':'Latest operational snapshot'}</h3><div class="table-wrap"><table class="p132-compact-table"><thead><tr><th>${arabic?'المؤشر':'Metric'}</th><th>${arabic?'القيمة':'Value'}</th><th>${arabic?'الحالة':'Status'}</th></tr></thead><tbody>
            <tr><td>${arabic?'استلام الرفع':'Upload receiving'}</td><td>${safe(summary.uploadReceiving)}</td><td>${safe(summary.uploadFailed)} ${arabic?'فشل':'failed'}</td></tr>
            <tr><td>${arabic?'المعالجة':'Processing'}</td><td>${safe(Number(summary.processingQueued||0)+Number(summary.processingLeased||0))}</td><td>${safe(summary.processingFailed)} ${arabic?'فشل':'failed'}</td></tr>
            <tr><td>${arabic?'الحماية':'Protection'}</td><td>${safe(summary.protected)}</td><td>${safe(summary.protectionFailed)} ${arabic?'فشل':'failed'}</td></tr>
          </tbody></table></div></article>
          <article class="p132-report-card"><h3>${arabic?'روابط سريعة':'Quick links'}</h3><div class="p132-quick-links">
            <button type="button" class="p132-quick" data-p09-go="queue"><span><i class="bi bi-hourglass-split"></i>${arabic?'العمليات الجارية':'Active processing'}</span></button>
            <button type="button" class="p132-quick" data-p09-go="protection"><span><i class="bi bi-shield-check"></i>${arabic?'الحماية والتعافي':'Protection'}</span></button>
            <button type="button" class="p132-quick" data-p09-go="upload"><span><i class="bi bi-cloud-arrow-up"></i>${arabic?'إضافة ميديا':'Add media'}</span></button>
            <button type="button" class="p132-quick" data-p09-tab="diagnostics"><span><i class="bi bi-file-earmark-bar-graph"></i>${arabic?'تقرير تشخيصي':'Diagnostics report'}</span></button>
          </div></article>
        </div>
      </section>

      <section class="p132-report-panel" data-report-panel="queues" hidden>
        <article class="p132-report-card"><h3>${arabic?'القوائم الدائمة والاسترداد':'Durable queues & recovery'}</h3><div class="table-wrap"><table class="p132-compact-table"><thead><tr><th>${arabic?'القائمة':'Queue'}</th><th>${arabic?'معلّق':'Pending'}</th><th>${arabic?'قيد التنفيذ':'Leased'}</th><th>${arabic?'فشل':'Failed'}</th><th>${arabic?'متقادم':'Stale'}</th><th>${arabic?'أقدم عنصر':'Oldest pending'}</th></tr></thead><tbody>${queueRows.map(q=>`<tr><td><strong>${esc(q.queue)}</strong></td><td>${safe(q.pending)}</td><td>${safe(q.leased)}</td><td>${safe(q.failed)}</td><td>${safe(q.staleLeases)}</td><td>${q.oldestPendingAtUtc?esc(new Date(q.oldestPendingAtUtc).toLocaleString(arabic?'ar-KW':'en-GB')):'—'}</td></tr>`).join('')||`<tr><td colspan="6">${arabic?'لا توجد حالة للقوائم.':'No queue state.'}</td></tr>`}</tbody></table></div></article>
      </section>

      <section class="p132-report-panel" data-report-panel="protection" hidden>
        <div class="p132-report-grid">
          <article class="p132-report-card"><h3>${arabic?'سلامة النسخ والحماية':'Integrity & protection'}</h3><div class="p132-health-list"><div class="p132-health-row"><i class="bi bi-check-circle"></i><strong>${arabic?'محمي':'Protected'}</strong><b>${safe(integrity.protected)}</b></div><div class="p132-health-row"><i class="bi bi-clock"></i><strong>${arabic?'معلّق':'Pending'}</strong><b>${safe(integrity.pending)}</b></div><div class="p132-health-row"><i class="bi bi-x-circle"></i><strong>${arabic?'فشل':'Failed'}</strong><b>${safe(integrity.failed)}</b></div><div class="p132-health-row"><i class="bi bi-exclamation-diamond"></i><strong>${arabic?'عدم تطابق':'Mismatch'}</strong><b>${safe(integrity.mismatch)}</b></div></div></article>
          <article class="p132-report-card"><h3>${arabic?'التخزين الآمن':'Safe storage view'}</h3><p>${arabic?'التخزين الأساسي':'Primary'}: <strong>${esc(storage.primaryTargetId||'—')}</strong><br>${bytes(storage.authoritativeOriginalBytes)}</p><div class="p132-meter" style="--meter:${pct(storage.authoritativeOriginalBytes,storageTotal)}%"><span></span></div><p style="margin-top:14px">${arabic?'التخزين الاحتياطي':'Backup'}: <strong>${esc(storage.backupTargetId||'—')}</strong><br>${bytes(storage.verifiedProtectedBytes)}</p><div class="p132-meter" style="--meter:${pct(storage.verifiedProtectedBytes,storageTotal)}%"><span></span></div></article>
          <article class="p132-report-card"><h3>${arabic?'بايتات تم التحقق منها':'Verified bytes'}</h3><p><strong>${bytes(integrity.protectedBytes)}</strong></p><p><small>${arabic?'لا يتم إرسال مسارات الملفات أو بيانات الاعتماد إلى المتصفح.':'Filesystem roots and credentials are never sent to the browser.'}</small></p></article>
        </div>
      </section>

      <section class="p132-report-panel" data-report-panel="dependencies" hidden>
        <article class="p132-report-card"><h3>${arabic?'صحة الاعتمادات':'Dependency health'}</h3><div class="table-wrap"><table class="p132-compact-table"><thead><tr><th>${arabic?'الاعتماد':'Dependency'}</th><th>${arabic?'الحالة':'Status'}</th><th>${arabic?'الهدف':'Target'}</th><th>${arabic?'تفاصيل آمنة':'Safe detail'}</th></tr></thead><tbody>${depRows.map(d=>`<tr><td><strong>${esc(d.dependency)}</strong></td><td>${esc(d.status)}</td><td>${esc(d.targetId||'—')}</td><td>${esc(d.detail)}</td></tr>`).join('')||`<tr><td colspan="4">${arabic?'لا توجد تبعيات مسجلة.':'No dependencies reported.'}</td></tr>`}</tbody></table></div></article>
      </section>

      <section class="p132-report-panel" data-report-panel="diagnostics" hidden>
        <article class="p132-report-card"><div class="p132-report-toolbar"><div><h3>${arabic?'حزمة التشخيص':'Diagnostics bundle'}</h3><p>${arabic?'حزمة دعم خالية من الأسرار مع Correlation ID.':'Secret-safe support bundle with Correlation ID.'}</p></div><button id="p09Diagnostics" class="action">${arabic?'عرض التشخيص':'View diagnostics'}</button></div><pre id="p09DiagnosticsOutput" style="white-space:pre-wrap;overflow:auto"></pre></article>
      </section>
    </div>`;

    bindReportTabs();
    content.querySelectorAll('[data-p09-go]').forEach(button=>button.addEventListener('click',()=>{route=button.dataset.p09Go;render();}));
    content.querySelectorAll('[data-p09-tab]').forEach(button=>button.addEventListener('click',()=>content.querySelector(`[data-tab="${CSS.escape(button.dataset.p09Tab)}"]`)?.click()));
    document.getElementById('p09Diagnostics')?.addEventListener('click',async()=>{
      const output=document.getElementById('p09DiagnosticsOutput');
      output.textContent=arabic?'جاري التحميل…':'Loading…';
      try{output.textContent=JSON.stringify(await get('/client-api/operations/diagnostics'),null,2);}catch{output.textContent=arabic?'تعذر تحميل التشخيص.':'Diagnostics unavailable.';}
    });
  }

  function bindReportTabs(){
    const tabs=document.getElementById('p132ReportTabs');
    if(!tabs)return;
    tabs.querySelectorAll('[data-tab]').forEach(button=>button.addEventListener('click',()=>{
      tabs.querySelectorAll('[data-tab]').forEach(item=>item.classList.toggle('active',item===button));
      document.querySelectorAll('[data-report-panel]').forEach(panel=>panel.hidden=panel.dataset.reportPanel!==button.dataset.tab);
    }));
  }
})();
