(() => {
'use strict';

const patterns=[
'212222','222122','222221','121223','121322','131222','122213','122312','132212','221213',
'221312','231212','112232','122132','122231','113222','123122','123221','223211','221132',
'221231','213212','223112','312131','311222','321122','321221','312212','322112','322211',
'212123','212321','232121','111323','131123','131321','112313','132113','132311','211313',
'231113','231311','112133','112331','132131','113123','113321','133121','313121','211331',
'231131','213113','213311','213131','311123','311321','331121','312113','312311','332111',
'314111','221411','431111','111224','111422','121124','121421','141122','141221','112214',
'112412','122114','122411','142112','142211','241211','221114','413111','241112','134111',
'111242','121142','121241','114212','124112','124211','411212','421112','421211','212141',
'214121','412121','111143','111341','131141','114113','114311','411113','411311','113141',
'114131','311141','411131','211412','211214','211232','2331112'
];

const QUIET_MODULES=10;
const MIN_PRINT_MODULE_MM=0.19;

function valueForChar(ch){
  const code=ch.charCodeAt(0);
  if(code<32||code>126)throw new Error('Code 128 B supports ASCII characters 32-126 only.');
  return code-32;
}

function encodeValues(text){
  const input=String(text??'');
  if(!input.length)throw new Error('Barcode value is required.');
  const data=[...input].map(valueForChar);
  let checksum=104;
  data.forEach((v,i)=>checksum+=v*(i+1));
  return [104,...data,checksum%103,106];
}

function modules(text){
  const values=encodeValues(text);
  const bars=[];
  let x=QUIET_MODULES;
  for(const value of values){
    const pattern=patterns[value];
    if(!pattern)throw new Error('Unsupported Code 128 symbol.');
    let black=true;
    for(const digit of pattern){
      const width=Number(digit);
      if(black)bars.push({x,width});
      x+=width;
      black=!black;
    }
  }
  return {bars,total:x+QUIET_MODULES};
}

function svg(text,{height=104,module=3,ariaLabel='Barcode'}={}){
  const data=modules(text);
  const width=Math.ceil(data.total*module);
  const rects=data.bars.map(b=>'<rect x="'+(b.x*module)+'" y="0" width="'+(b.width*module)+'" height="'+height+'" fill="#000"/>').join('');
  return '<svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="'+escapeHtml(ariaLabel)+'" viewBox="0 0 '+width+' '+height+'" width="'+width+'" height="'+height+'" shape-rendering="crispEdges">'+rects+'</svg>';
}

function svgMm(text,{widthMm,heightMm=18,ariaLabel='Barcode'}={}){
  const data=modules(text);
  const width=Number(widthMm);
  const height=Number(heightMm);
  if(!(width>0&&height>0))throw new Error('Physical barcode dimensions are required.');
  const rects=data.bars.map(b=>'<rect x="'+b.x+'" y="0" width="'+b.width+'" height="100" fill="#000"/>').join('');
  return '<svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="'+escapeHtml(ariaLabel)+'" viewBox="0 0 '+data.total+' 100" width="'+width+'mm" height="'+height+'mm" preserveAspectRatio="none" shape-rendering="crispEdges">'+rects+'</svg>';
}

function fit(text,labelWidthMm,{paddingMm=5,minModuleMm=MIN_PRINT_MODULE_MM}={}){
  const data=modules(text);
  const width=Number(labelWidthMm);
  const padding=Math.max(0,Number(paddingMm)||0);
  const available=Math.max(0,width-padding);
  const moduleMm=data.total?available/data.total:0;
  const minimumLabelWidthMm=data.total*minModuleMm+padding;
  return {
    ok:moduleMm>=minModuleMm,
    totalModules:data.total,
    moduleMm,
    minimumModuleMm:minModuleMm,
    minimumLabelWidthMm
  };
}

function payload(tapeCode,title){
  const code=String(tapeCode||'').trim().toUpperCase();
  if(!/^TAPE-\d{6}$/.test(code))throw new Error('A durable TAPE-###### code is required.');
  const name=(String(title||'Untitled').trim()||'Untitled');
  const encoded=[...name].every(ch=>{const n=ch.charCodeAt(0);return n>=32&&n<=126;})
    ?name
    :'UTF8='+base64UrlUtf8(name);
  return code+'|'+encoded;
}

function extractTapeCode(value){
  const raw=String(value||'').trim();
  const match=raw.match(/TAPE-\d{6}/i);
  if(match)return match[0].toUpperCase();
  try{
    const decoded=decodeURIComponent(raw);
    const legacy=decoded.match(/TAPE-\d{6}/i);
    return legacy?legacy[0].toUpperCase():null;
  }catch{return null;}
}

function extractTapeName(value){
  const raw=String(value||'').trim();
  const code=extractTapeCode(raw);
  if(!code)return null;
  const index=raw.toUpperCase().indexOf(code);
  if(index>=0){
    const separator=index+code.length;
    if(raw[separator]==='|'){
      const encoded=raw.slice(separator+1);
      if(encoded.startsWith('UTF8=')){
        try{return utf8FromBase64Url(encoded.slice(5));}catch{return null;}
      }
      if(/^TITLE=/i.test(encoded)){
        try{return decodeURIComponent(encoded.slice(6));}catch{return null;}
      }
      return encoded;
    }
  }
  const legacy=raw.match(/\|TITLE=(.*)$/i);
  if(legacy){try{return decodeURIComponent(legacy[1]);}catch{return null;}}
  return null;
}

function base64UrlUtf8(value){
  const bytes=new TextEncoder().encode(value);
  let binary='';
  for(const byte of bytes)binary+=String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');
}

function utf8FromBase64Url(value){
  let base64=String(value||'').replace(/-/g,'+').replace(/_/g,'/');
  while(base64.length%4)base64+='=';
  const binary=atob(base64);
  const bytes=Uint8Array.from(binary,ch=>ch.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

function escapeHtml(value){
  return String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
}

window.mamCode128=Object.freeze({
  svg,
  svgMm,
  fit,
  payload,
  extractTapeCode,
  extractTapeName,
  encodeValues,
  minimumPrintModuleMm:MIN_PRINT_MODULE_MM
});
})();
