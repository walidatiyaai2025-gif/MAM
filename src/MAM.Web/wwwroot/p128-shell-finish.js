(() => {
'use strict';
const shell=document.querySelector('.app-shell'),main=shell?.querySelector('main'),actions=document.querySelector('.topbar .actions');
if(!shell||!main)return;
let footer=main.querySelector('.p128-app-footer');
if(!footer){footer=document.createElement('footer');footer.className='p128-app-footer';footer.innerHTML='<span>جميع الحقوق محفوظة للديوان الأميري · نظام إدارة الأصول الإعلامية</span><span><b id="p128FooterVersion">MAM</b></span>';main.appendChild(footer);}
fetch('/version',{headers:{Accept:'application/json'},cache:'no-store'}).then(r=>r.ok?r.json():null).then(v=>{const x=document.getElementById('p128FooterVersion');if(x&&v)x.textContent=`${v.version||'MAM'} · ${v.environmentName||'Production'}`;}).catch(()=>{});

let mobile=actions?.querySelector('.p128-mobile-menu');
if(actions&&!mobile){mobile=document.createElement('button');mobile.type='button';mobile.className='p128-mobile-menu';mobile.setAttribute('aria-label','القائمة');mobile.innerHTML='<i class="bi bi-list"></i>';actions.appendChild(mobile);mobile.addEventListener('click',()=>shell.classList.toggle('p128-mobile-open'));}
let scrim=shell.querySelector('.p128-mobile-scrim');if(!scrim){scrim=document.createElement('div');scrim.className='p128-mobile-scrim';shell.appendChild(scrim);scrim.addEventListener('click',()=>shell.classList.remove('p128-mobile-open'));}
document.getElementById('nav')?.addEventListener('click',()=>{if(matchMedia('(max-width:800px)').matches)shell.classList.remove('p128-mobile-open');});
})();
