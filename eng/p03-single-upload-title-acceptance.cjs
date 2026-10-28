'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const web = read('src/MAM.Web/wwwroot/p03-upload.js');
const enterprise = read('src/MAM.Web/wwwroot/p127-enterprise.js');
const activeEnterprise = read('src/MAM.Web/wwwroot/p127-enterprise-v2.js');
const index = read('src/MAM.Web/wwwroot/index.html');
const props = read('Directory.Build.props');
const desktop = read('src/MAM.Desktop/MainWindow.P03.cs');

const match = web.match(/function p03ApplySelectedFileTitle\(titleInput, file\)\{[\s\S]*?\n\}/);
assert.ok(match, 'Shared file-to-title helper must exist');
const applyTitle = vm.runInNewContext('(' + match[0] + ')');

const titleField = {value: ''};
for (let i = 1; i <= 20; i++) {
  const name = i === 20 ? 'زيارة رسمية.أرشيف-٢٠.mp4' : `archive.event-${i}.mp4`;
  applyTitle(titleField, {name});
  assert.equal(titleField.value, name.slice(0, -4), `File ${i} must have its own title`);
  titleField.value = 'Operator edited title'; // Preserve manual edits until next selection.
}
applyTitle(titleField, null);
assert.equal(titleField.value, 'Operator edited title', 'Clearing selection does not overwrite title');
applyTitle(titleField, {name: 'last-clip.mxf'});
assert.equal(titleField.value, 'last-clip', 'Next file replaces prior manual title');

assert.match(web, /p03ApplySelectedFileTitle\(titleInput,file\)/, 'Web file picker must apply selected file title');
assert.equal((enterprise.match(/p03ApplySelectedFileTitle\(titleInput,(?:file|dropped)\)/g) || []).length, 2,
  'Enterprise picker and drag/drop must both apply the selected file title');
assert.match(activeEnterprise, /p03ApplySelectedFileTitle\(title,f\)/, 'Active enterprise v2 picker and drag/drop must refresh title');
assert.doesNotMatch(activeEnterprise, /if\(f&&!title\.value\.trim\(\)\)/, 'Active enterprise v2 must not retain a stale title');
assert.match(activeEnterprise, /p03SelectedFile=null;\s*file\.value=''; title\.value='';/, 'Active enterprise v2 must reset after success');
assert.match(index, /p127-enterprise-v2\.js\?v=0\.12\.19-p12\.51/, 'Actual enterprise upload script must have current cache version');
assert.match(props, /<Version>0\.12\.19-p12\.51<\/Version>/, 'Installer version must match cache release');

for (const [name, source] of [['Web', web], ['Enterprise', enterprise]]) {
  assert.doesNotMatch(source, /if\s*\(\s*file\s*&&\s*!titleInput\.value\.trim\(\)\s*\)/,
    `${name} must not keep a stale nonempty title`);
  assert.match(source, /p03SelectedFile=null;[\s\S]{0,120}fileInput\.value='';\s*titleInput\.value='';/,
    `${name} must clear stale upload fields after success`);
}

assert.match(desktop, /titleInput\.Text = Path\.GetFileNameWithoutExtension\(dialog\.FileName\);/,
  'Desktop title must follow every newly selected file');
assert.doesNotMatch(desktop, /if \(string\.IsNullOrWhiteSpace\(titleInput\.Text\)\) titleInput\.Text/,
  'Desktop must not retain the prior file title');
assert.match(desktop, /_p03SelectedFile = null;\s*titleInput\.Clear\(\);/,
  'Desktop must clear completed file/title state');

console.log('PASS: 20 sequential single-file titles, drag-drop, manual edit and success resets');
