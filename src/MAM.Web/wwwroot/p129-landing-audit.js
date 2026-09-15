(() => {
'use strict';
if(!document.querySelector('link[href="/p130-typography.css"]')){const l=document.createElement('link');l.rel='stylesheet';l.href='/p130-typography.css';document.head.appendChild(l);}
const language=document.getElementById('landingLanguage');
if(!language)return;
const isEn=()=>document.documentElement.lang==='en';
const pair=(en,ar)=>isEn()?en:ar;
const set=(selector,en,ar,html=false)=>{const el=document.querySelector(selector);if(!el)return;if(html)el.innerHTML=pair(en,ar);else el.textContent=pair(en,ar);};
const sets=(selector,values)=>{document.querySelectorAll(selector).forEach((el,i)=>{if(values[i])el.textContent=pair(values[i][0],values[i][1]);});};
const setWithLeadingIcon=(selector,values)=>{document.querySelectorAll(selector).forEach((el,i)=>{if(!values[i])return;const icon=el.querySelector('i')?.outerHTML||'';el.innerHTML=`${icon}${pair(values[i][0],values[i][1])}`;});};
const apply=()=>{
 const en=isEn();
 document.title=en?'Diwan Al Amiri · Media Asset Management':'الديوان الأميري · إدارة الأصول الإعلامية';
 const brand=document.querySelector('.landing-brand');if(brand)brand.setAttribute('aria-label',pair('Diwan Al Amiri','الديوان الأميري'));
 const logo=document.querySelector('.landing-brand img');if(logo)logo.alt=pair('Diwan Al Amiri crest','شعار الديوان الأميري');
 set('.landing-brand strong','Diwan Al Amiri · Media Asset Management','الديوان الأميري · نظام إدارة الأصول الإعلامية');
 set('.landing-brand small','Media Library','مكتبة الوسائط');
 const search=document.getElementById('landingSearchInput');if(search)search.setAttribute('aria-label',pair('Quick content search','بحث سريع داخل المحتوى'));
 const home=document.querySelector('.landing-icon[href="/"]');if(home)home.setAttribute('aria-label',pair('Home','الرئيسية'));
 const connected=document.querySelector('#landingUserChip small');if(connected)connected.innerHTML=`${pair('Connected','متصل')} <i></i>`;
 setWithLeadingIcon('.landing-health span',[[ 'System operating efficiently','النظام يعمل بكفاءة عالية'],['Integrated with Diwan Al Amiri environment','متكامل مع بيئة الديوان الأميري']]);
 const visual=document.querySelector('.hero-visual');if(visual)visual.setAttribute('aria-label',pair('MAM platform preview','معاينة منصة MAM'));
 set('.visual-badge','Your content · secure · always','محتواكم .. بأمان .. دائمًا');
 sets('.mock-heading small,.mock-heading strong',[[ 'Media Library','مكتبة الوسائط'],['Media Library','مكتبة الوسائط']]);
 set('.mock-heading em','● Connected','● متصل');
 sets('.mock-metrics small',[[ 'Media assets','أصل إعلامي'],['Protected','محمي'],['Processing','قيد المعالجة']]);
 sets('.mock-cards article>b',[[ 'Archive video','فيديو أرشيفي'],['Official document','وثيقة رسمية'],['Media image','صورة إعلامية']]);
 sets('.mock-cards article>small',[[ 'Transcript · Indexed','Transcript · Indexed'],['OCR · Searchable','OCR · Searchable'],['Metadata · Protected','Metadata · Protected']]);
 set('.visual-count span','Managed media assets','أصل إعلامي مُدار');
 const trust=document.querySelector('.trust-strip');if(trust)trust.setAttribute('aria-label',pair('Core capabilities','خصائص أساسية'));
 setWithLeadingIcon('.trust-strip span',[[ 'Active Directory SSO','Active Directory SSO'],['Arabic + English OCR','Arabic + English OCR'],['Role-based permissions','صلاحيات مبنية على الأدوار'],['SHA-256 protection','حماية SHA-256'],['Complete audit log','سجل تدقيق كامل']]);
 set('.features .section-title>span','System capabilities','ميزات النظام');
 set('.features .section-title>h2','Designed for the complete content lifecycle','مصمم لدورة حياة المحتوى بالكامل');
 set('.features .section-title>p','An integrated institutional experience for media assets, documents, images and video.','تجربة مؤسسية متكاملة تلبي جميع احتياجات إدارة الأصول الإعلامية والوثائق والصور والفيديو.');
 const featureText=[
  ['Central media library','مكتبة وسائط مركزية','Manage images, video, audio and documents in one secure, organized environment.','إدارة جميع الملفات الإعلامية من صور وفيديو وصوت ومستندات في بيئة واحدة آمنة ومنظمة.'],
  ['OCR and document indexing','OCR وفهرسة المستندات','Automatically extract and index text from documents and images for fast, accurate discovery.','استخراج النصوص تلقائيًا من الوثائق والصور وفهرستها لتمكين البحث السريع والدقيق.'],
  ['Advanced index and search','فهرس وبحث متقدم','Discover all content types through advanced indexing, extracted text and searchable metadata.','ابحث في جميع أنواع المحتوى باستخدام فهرسة متقدمة والتعرف على النصوص والبحث الدلالي.'],
  ['Integrated workflow','سير عمل متكامل','Govern review and operational workflows with paths designed for institutional use.','أتمتة سير العمل والمراجعة والاعتماد مع مسارات مخصصة تلائم احتياجات مؤسستكم.'],
  ['Protection and backup','حماية ونسخ احتياطي','Protect data with verified backup and recovery capabilities for business continuity.','حماية البيانات مع نسخ احتياطي تلقائي وآليات استعادة متقدمة لضمان استمرارية الأعمال.'],
  ['Enterprise permissions and audit','صلاحيات وتدقيق مؤسسي','Manage permissions through Active Directory with comprehensive activity auditing.','إدارة الصلاحيات عبر Active Directory مع سجل تدقيق شامل لجميع الأنشطة والتغييرات.']
 ];
 document.querySelectorAll('.feature-grid article').forEach((card,i)=>{const item=featureText[i];if(!item)return;const h=card.querySelector('h3'),p=card.querySelector('p');if(h)h.textContent=en?item[0]:item[1];if(p)p.textContent=en?item[2]:item[3];});
 set('.workflow-copy .eyebrow','From content to value','من المحتوى إلى القيمة');
 set('.workflow-copy h2','Content transforms from a file<br/>into a manageable, searchable asset.','المحتوى يتحول من ملف<br/>إلى أصل قابل للإدارة والبحث.',true);
 set('.workflow-copy p','MAM follows a complete media-content lifecycle from ingest through discovery, with processing, protection and governed enterprise access.','يتبع نظام MAM في الديوان الأميري دورة حياة متكاملة للمحتوى الإعلامي من لحظة الرفع وحتى الاكتشاف، مع معالجة ذكية وحماية شاملة وصلاحيات مؤسسية.');
 const workflowLink=document.querySelector('.workflow-copy a');if(workflowLink)workflowLink.innerHTML=`${pair('Explore the system','تعرف على النظام')} <i class="bi ${en?'bi-arrow-right':'bi-arrow-left'}"></i>`;
 sets('.flow-list small',[[ 'Upload and capture content','رفع وتسجيل المحتوى'],['Process and transform media','معالجة وتحويل الصيغ'],['Index and enrich metadata','فهرسة وإضافة البيانات الوصفية'],['Protect and back up','حماية ونسخ احتياطي'],['Search and discover content','بحث واكتشاف المحتوى']]);
 set('footer div span','Media Asset Management · MAM','نظام إدارة الأصول الإعلامية - MAM');
 const footer=document.querySelector('footer>small');if(footer){const first=[...footer.childNodes].find(n=>n.nodeType===Node.TEXT_NODE);if(first)first.nodeValue=`${pair('All rights reserved © 2026 Diwan Al Amiri, State of Kuwait','جميع الحقوق محفوظة © 2026 الديوان الأميري لدولة الكويت')} · `;}
};
language.addEventListener('click',()=>requestAnimationFrame(apply));
apply();
})();
