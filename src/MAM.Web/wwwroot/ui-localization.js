(() => {
  'use strict';

  // Repository-controlled product chrome only. Canonical identifiers (API, SQL, SHA-256,
  // SecretRef, IDs, codecs, profile keys) and user/catalog metadata intentionally remain exact.
  const exact = new Map([
    ['Primary navigation','التنقل الرئيسي'],
    ['Diwan Al Amiri crest','شعار الديوان الأميري'],
    ['DEVELOPMENT DEMO','عرض تطويري'],
    ['NON-PRODUCTION','غير إنتاجي'],
    ['DIWAN AL AMIRI · DEVELOPMENT','الديوان الأميري · بيئة تطوير'],
    ['● Central API connected workflows','● مسارات عمل متصلة بواجهة API المركزية'],
    ['Loading','جارٍ التحميل'],
    ['Empty','لا توجد بيانات'],
    ['API error','خطأ في واجهة API'],
    ['Permission denied','الوصول مرفوض'],
    ['Degraded','حالة متدهورة'],
    ['Retry available','إعادة المحاولة متاحة'],
    ['Validation','التحقق'],
    ['Valid','صالح'],
    ['Rejected','مرفوض'],
    ['Saved','تم الحفظ'],
    ['Conflict','تعارض'],
    ['Invalid JSON','JSON غير صالح'],
    ['Exported','تم التصدير'],
    ['Queued','تمت الإضافة إلى قائمة الانتظار'],
    ['Ready','جاهز'],
    ['Primary verified','تم التحقق من النسخة الأساسية'],
    ['Demo assets','أصول تجريبية'],
    ['Demo protected','محمي تجريبيًا'],
    ['Processing','قيد المعالجة'],
    ['Primary + Backup verified','تم التحقق من التخزين الأساسي والاحتياطي'],
    ['DEMO QUEUE','قائمة معالجة تجريبية'],
    ['System state treatments','حالات واجهة النظام'],
    ['Preview','المعاينة'],
    ['Video preview shell · 00:18:42 / 00:42:18','معاينة فيديو · 00:18:42 / 00:42:18'],
    ['Protection','الحماية'],
    ['Primary verified ✓','تم التحقق من الأساسي ✓'],
    ['Backup verified ✓','تم التحقق من الاحتياطي ✓'],
    ['SHA-256 match ✓','تطابق SHA-256 ✓'],
    ['State: Protected','الحالة: محمي'],
    ['Metadata','البيانات الوصفية'],
    ['Title · event date · category · tags · preservation notes','العنوان · تاريخ الحدث · التصنيف · الوسوم · ملاحظات الحفظ'],
    ['Windows-only capability.','متاح على Windows فقط.'],
    ['Temporary local selection before central upload.','اختيار محلي مؤقت قبل الرفع المركزي.'],
    ['Local selection/cache is temporary; authoritative storage remains server-side.','الاختيار والذاكرة المحلية مؤقتان؛ التخزين الموثوق يبقى على الخادم.'],
    ['File selection area','منطقة اختيار الملفات'],
    ['Drop files here or browse · Demo','أسقط الملفات هنا أو استعرضها · عرض تجريبي'],
    ['Preflight','الفحص الأولي'],
    ['Extension · size · name · path · network readiness','الامتداد · الحجم · الاسم · المسار · جاهزية الشبكة'],
    ['Success, failure, retry and degraded states are explicitly represented.','يتم عرض حالات النجاح والفشل وإعادة المحاولة والتدهور بشكل صريح.'],
    ['Proxy generation','إنشاء نسخة Proxy'],
    ['Technical metadata','البيانات الفنية'],
    ['Backup verification','التحقق من النسخة الاحتياطية'],
    ['Running 68%','قيد التنفيذ 68%'],
    ['P01 shell surface; authoritative identity and permissions are delivered through the Central API boundary.','واجهة عرض؛ الهوية والصلاحيات الموثوقة تُدار عبر واجهة API المركزية.'],
    ['Users','المستخدمون'],
    ['Roles','الأدوار'],
    ['Capture stations','محطات التسجيل'],
    ['This action requires the System Administrator role.','يتطلب هذا الإجراء دور مسؤول النظام.'],
    ['Reviewable environment settings shell; secrets are never displayed.','إعدادات بيئة قابلة للمراجعة؛ لا يتم عرض القيم السرية مطلقًا.'],
    ['Language & appearance','اللغة والمظهر'],
    ['Central services','الخدمات المركزية'],
    ['Central API: configured by deployment','واجهة API المركزية: تُهيأ عند النشر'],
    ['SQL credentials: server-only','بيانات اعتماد SQL: على الخادم فقط'],
    ['Secrets: hidden','الأسرار: مخفية'],
    ['Loading demo catalog data…','جارٍ تحميل بيانات الكتالوج التجريبية…'],
    ['No assets match the current filters.','لا توجد أصول تطابق المرشحات الحالية.'],
    ['Central API is unreachable. Retry is available.','تعذر الوصول إلى واجهة API المركزية. إعادة المحاولة متاحة.'],
    ['You do not have permission for this action.','لا توجد صلاحية لتنفيذ هذا الإجراء.'],
    ['Backup is unavailable; assets are not marked Protected.','النسخة الاحتياطية غير متاحة؛ لن تُعلّم الأصول كمحمية.'],
    ['CENTRAL API','واجهة API المركزية'],
    ['CENTRAL API LIVE','واجهة API المركزية · مباشر'],
    ['Search and filters · Central API','البحث والمرشحات · واجهة API المركزية'],
    ['Create catalog asset','إضافة أصل للكتالوج'],
    ['The write is sent only through the Central API.','يتم الحفظ عبر واجهة API المركزية فقط.'],
    ['Asset title','عنوان الأصل'],
    ['Create','إنشاء'],
    ['Durable Primary Upload','رفع موثوق إلى التخزين الأساسي'],
    ['Resumable upload','رفع قابل للاستكمال'],
    ['Storage boundary','حدود التخزين'],
    ['Choose file','اختيار ملف'],
    ['Start / resume','بدء / استكمال'],
    ['Ready for upload','جاهز للرفع'],
    ['PDF inline preview','معاينة PDF داخلية'],
    ['PDF preview','معاينة PDF'],
    ['Verified preview','معاينة موثقة'],
    ['Technical metadata','البيانات الفنية'],
    ['Verified previews','المعاينات الموثقة'],
    ['Queue inspection','إضافة فحص فني'],
    ['Queue video proxy','إضافة إنشاء Proxy للفيديو'],
    ['Queue image preview','إضافة معاينة للصورة'],
    ['Queue audio preview','إضافة معاينة للصوت'],
    ['Queue PDF inspection','إضافة فحص PDF'],
    ['Search & facets','البحث والمرشحات'],
    ['All lifecycle states','كل الحالات'],
    ['All categories','كل التصنيفات'],
    ['All collections','كل المجموعات'],
    ['Search','بحث'],
    ['List view','عرض قائمة'],
    ['Grid view','عرض شبكي'],
    ['Reset','مسح المرشحات'],
    ['Collections & policy','المجموعات والسياسة'],
    ['Collection name (English)','اسم المجموعة بالإنجليزية'],
    ['Collection name (Arabic)','اسم المجموعة بالعربية'],
    ['Create collection','إنشاء مجموعة'],
    ['No collections yet.','لا توجد مجموعات بعد.'],
    ['Saved filters are enabled.','المرشحات المحفوظة مفعلة.'],
    ['Edit','تعديل'],
    ['Edit metadata','تعديل البيانات'],
    ['Add to first collection','أضف لأول مجموعة'],
    ['Metadata Curation','تهيئة البيانات الوصفية'],
    ['English title','العنوان الإنجليزي'],
    ['Arabic title','العنوان العربي'],
    ['Category','التصنيف'],
    ['Tags','الوسوم'],
    ['Preservation notes','ملاحظات الحفظ'],
    ['Save metadata','حفظ البيانات'],
    ['Archive','أرشفة'],
    ['Restore','استعادة'],
    ['Back to library','رجوع للمكتبة'],
    ['Lifecycle','الحالة'],
    ['Collection','المجموعة'],
    ['Backup Protection','حماية النسخة الاحتياطية'],
    ['Backup Pending','النسخ الاحتياطي معلّق'],
    ['Backup Failed','فشل النسخ الاحتياطي'],
    ['Mismatch','عدم تطابق'],
    ['Protected','محمي'],
    ['Primary + Backup verified','تم التحقق من الأساسي والاحتياطي'],
    ['Awaiting verified copy','بانتظار نسخة موثقة'],
    ['Primary preserved','تم الحفاظ على التخزين الأساسي'],
    ['Never silently accepted','لا يُقبل بصمت مطلقًا'],
    ['Storage health','صحة التخزين'],
    ['Administrator actions','عمليات المسؤول'],
    ['Queue pending copies','إضافة النسخ المعلقة'],
    ['Queue integrity recheck','إضافة إعادة فحص السلامة'],
    ['Protection state','حالة الحماية'],
    ['Enterprise Administration & Policy','إدارة المؤسسة والسياسات'],
    ['Restart','إعادة تشغيل'],
    ['Live','مباشر'],
    ['Enabled','مفعّل'],
    ['Disabled','معطّل'],
    ['Policies','السياسات'],
    ['Dictionary entries','مدخلات القاموس'],
    ['Restart-impact','أثر إعادة التشغيل'],
    ['Administration health','صحة الإدارة'],
    ['Authoritative policies','السياسات المركزية'],
    ['Policy','السياسة'],
    ['Impact','الأثر'],
    ['Display name','الاسم المعروض'],
    ['User','المستخدم'],
    ['Status','الحالة'],
    ['Users & roles','المستخدمون والأدوار'],
    ['Audit explorer','سجل التدقيق'],
    ['Actor','المنفّذ'],
    ['Action','الإجراء'],
    ['Outcome','النتيجة'],
    ['No audit events.','لا توجد أحداث تدقيق.'],
    ['Validate','تحقق'],
    ['Test reference','اختبار المرجع'],
    ['Save','حفظ'],
    ['Preparing audit CSV…','جارٍ تجهيز ملف CSV للتدقيق…'],
    ['Reports, Monitoring, Resilience & DR','التقارير والمراقبة والتعافي'],
    ['Queue','القائمة'],
    ['Pending','معلّق'],
    ['Leased','قيد التنفيذ'],
    ['Failed','فشل'],
    ['Stale','متقادم'],
    ['Oldest pending','أقدم عنصر معلّق'],
    ['No queue state.','لا توجد حالة للقوائم.'],
    ['Integrity & protection','سلامة النسخ والحماية'],
    ['Verified bytes','البايتات التي تم التحقق منها'],
    ['Safe storage view','عرض التخزين الآمن'],
    ['Dependency health','صحة الاعتمادات'],
    ['Dependency','الاعتماد'],
    ['Target','الهدف'],
    ['Safe detail','تفاصيل آمنة'],
    ['Diagnostics bundle','حزمة التشخيص'],
    ['View diagnostics','عرض التشخيص'],
    ['P02 · VERSION UNAVAILABLE','P02 · الإصدار غير متاح']
  ]);

  const patterns = [
    [/^(\d[\d,.]*) originals$/, '$1 نسخة أصلية'],
    [/^(\d[\d,.]*) verified$/, '$1 تم التحقق منها'],
    [/^(\d[\d,.]*) sessions$/, '$1 جلسة'],
    [/^(\d[\d,.]*) enabled$/, '$1 مفعّلة'],
    [/^Queued:\s*(\d+)$/, 'تمت الإضافة إلى قائمة الانتظار: $1'],
    [/^Integrity rechecks queued:\s*(\d+)$/, 'تمت إضافة فحوص السلامة إلى قائمة الانتظار: $1'],
    [/^Version (\d+)( · restart required)?$/, (_, v, restart) => `الإصدار ${v}${restart ? ' · إعادة تشغيل مطلوبة' : ''}`],
    [/^(.+) · attempt (\d+)$/, '$1 · المحاولة $2'],
    [/^v(\d+) · (Active|Archived)(.*)$/, (_, v, lifecycle, rest) => `v${v} · ${lifecycle === 'Archived' ? 'مؤرشف' : 'نشط'}${rest}`]
  ];

  const textState = new Map();
  const attrState = new Map();
  let applying = false;

  function translated(value) {
    if (!value || !value.trim()) return value;
    const leading = value.match(/^\s*/)?.[0] || '';
    const trailing = value.match(/\s*$/)?.[0] || '';
    const core = value.trim();
    const direct = exact.get(core);
    if (direct) return `${leading}${direct}${trailing}`;
    for (const [pattern, replacement] of patterns) {
      if (pattern.test(core)) return `${leading}${core.replace(pattern, replacement)}${trailing}`;
    }
    let result = core
      .replace(/ · attempt /g, ' · المحاولة ')
      .replace(/ · pending /g, ' · معلّق ')
      .replace(/ · leased /g, ' · قيد التنفيذ ')
      .replace(/ · failed /g, ' · فشل ')
      .replace(/ · stale /g, ' · متقادم ')
      .replace(/Restart required/g, 'إعادة تشغيل مطلوبة');
    return result === core ? value : `${leading}${result}${trailing}`;
  }

  function rememberAndSetText(node) {
    if (!node.nodeValue || !node.nodeValue.trim()) return;
    const prior = textState.get(node);
    if (prior && node.nodeValue === prior.translated) return;
    const next = translated(node.nodeValue);
    if (next === node.nodeValue) return;
    textState.set(node, { original: node.nodeValue, translated: next });
    node.nodeValue = next;
  }

  function rememberAndSetAttribute(element, name) {
    if (!element.hasAttribute(name)) return;
    const current = element.getAttribute(name) || '';
    const perElement = attrState.get(element) || new Map();
    const prior = perElement.get(name);
    if (prior && current === prior.translated) return;
    const next = translated(current);
    if (next === current) return;
    perElement.set(name, { original: current, translated: next });
    attrState.set(element, perElement);
    element.setAttribute(name, next);
  }

  function shouldSkipText(node) {
    const parent = node.parentElement;
    if (!parent) return true;
    return Boolean(parent.closest('script,style,code,pre'));
  }

  function localize(root = document.body) {
    if (applying || !root) return;
    applying = true;
    try {
      if (!arabic) {
        restoreEnglish();
        return;
      }
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
      let node;
      while ((node = walker.nextNode())) if (!shouldSkipText(node)) rememberAndSetText(node);
      const elements = root.querySelectorAll ? root.querySelectorAll('[placeholder],[aria-label],[title],[alt]') : [];
      elements.forEach(element => ['placeholder','aria-label','title','alt'].forEach(name => rememberAndSetAttribute(element, name)));
    } finally {
      applying = false;
    }
  }

  function restoreEnglish() {
    for (const [node, state] of textState) {
      if (node.isConnected && node.nodeValue === state.translated) node.nodeValue = state.original;
    }
    textState.clear();
    for (const [element, attributes] of attrState) {
      if (!element.isConnected) continue;
      for (const [name, state] of attributes) if (element.getAttribute(name) === state.translated) element.setAttribute(name, state.original);
    }
    attrState.clear();
  }

  const previousRender = render;
  render = function () {
    previousRender();
    queueMicrotask(() => localize(document.body));
  };

  const observer = new MutationObserver(() => {
    if (arabic) queueMicrotask(() => localize(document.body));
  });
  observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['placeholder','aria-label','title','alt'] });

  window.mamLocalizationAudit = {
    apply: () => localize(document.body),
    exactArabicEntries: exact.size,
    invariantTerms: ['API','SQL','SHA-256','SecretRef','UTC','ID','codec','profile key']
  };

  localize(document.body);
})();
