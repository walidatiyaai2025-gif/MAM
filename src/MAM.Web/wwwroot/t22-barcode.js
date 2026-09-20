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
  const quiet=10;
  const bars=[];
  let x=quiet;
  for(const value of values){
    const pattern=patterns[value];
    let black=true;
    for(const digit of pattern){
      const width=Number(digit);
      if(black)bars.push({x,width});
      x+=width;
      black=!black;
    }
  }
  return {bars,total:x+quiet};
}

function svg(text,{height=76,module=2,ariaLabel='Barcode'}={}){
  const data=modules(text);
  const width=data.total*module;
  const rects=data.bars.map(b=>`<rect x="${b.x*module}" y="0" width="${b.width*module}" height="${height}" fill="#000"/>`).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="${escapeHtml(ariaLabel)}" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" width="100%" height="${height}">${rects}</svg>`;
}

function payload(tapeCode,title){
  const code=String(tapeCode||'').trim().toUpperCase();
  const name=String(title||'Untitled').trim()||'Untitled';
  return `MAM|${code}|TITLE=${encodeURIComponent(name)}`;
}

function extractTapeCode(value){
  const raw=decodeURIComponent(String(value||'').trim());
  const match=raw.match(/TAPE-\d{6}/i);
  return match?match[0].toUpperCase():null;
}

function escapeHtml(value){
  return String(value??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
}

window.mamCode128=Object.freeze({svg,payload,extractTapeCode,encodeValues});
})();