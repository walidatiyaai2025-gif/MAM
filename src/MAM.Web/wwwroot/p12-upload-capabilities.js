const p12UploadPreviousRender=render;
render=function(){
  p12UploadPreviousRender();
  if(route==='upload') void p12ApplyUploadCapabilities();
};

async function p12ApplyUploadCapabilities(){
  const fileInput=document.getElementById('p03File');
  const uploadButton=document.getElementById('p03Upload');
  const stateHost=document.getElementById('p03UploadState');
  if(!fileInput||!uploadButton||!stateHost)return;
  let host=document.getElementById('p12UploadCapabilities');
  if(!host){
    host=document.createElement('div');
    host.id='p12UploadCapabilities';
    host.className='state loading';
    stateHost.parentElement?.insertBefore(host,stateHost);
  }
  host.innerHTML=`<strong>${arabic?'صلاحيات الرفع':'Upload permissions'}</strong><br>${arabic?'جاري تحميل الأنواع المسموحة لهذا المستخدم…':'Loading media types allowed for this user…'}`;
  try{
    const response=await fetch('/client-api/discovery/my-media-capabilities',{headers:{Accept:'application/json'}});
    if(!response.ok)throw new Error(`HTTP ${response.status}`);
    const rows=await response.json();
    if(route!=='upload')return;
    const allowed=(Array.isArray(rows)?rows:[]).filter(item=>item.canUpload).map(item=>item.mediaKind);
    const extensions={
      Video:['.mxf','.mov','.mp4','.mkv','.avi','.webm','.m4v'],
      Audio:['.wav','.mp3','.m4a','.aac','.flac','.ogg','.wma'],
      Image:['.jpg','.jpeg','.png','.tif','.tiff','.bmp','.webp'],
      Document:['.pdf','.doc','.docx','.rtf','.txt','.odt'],
      Other:[]
    };
    const accepted=allowed.flatMap(kind=>extensions[kind]||[]);
    fileInput.accept=accepted.join(',');
    const labels=allowed.length?allowed.join(' · '):(arabic?'لا توجد أنواع مسموحة للرفع':'No media types are permitted for upload');
    host.className=`state ${allowed.length?'empty':'denied'}`;
    host.innerHTML=`<strong>${arabic?'المسموح لك بالرفع':'Allowed for your role'}</strong><br>${esc(labels)}`;
    if(!allowed.length){fileInput.disabled=true;uploadButton.disabled=true;}
  }catch{
    host.className='state degraded';
    host.innerHTML=`<strong>${arabic?'تعذر تحميل الصلاحيات':'Permission status unavailable'}</strong><br>${arabic?'سيظل الخادم يطبق صلاحيات نوع الوسائط عند بدء الرفع.':'The server will still enforce media-type permissions when upload starts.'}`;
  }
}
