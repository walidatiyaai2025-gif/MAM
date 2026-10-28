'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const source = fs.readFileSync(path.join(root, 'src/MAM.Web/wwwroot/p140-media-library-owner.js'), 'utf8');
const release = fs.readFileSync(path.join(root, 'Directory.Build.props'), 'utf8');
const html = fs.readFileSync(path.join(root, 'src/MAM.Web/wwwroot/index.html'), 'utf8');

function segment(start, end) {
  const i = source.indexOf(start), j = source.indexOf(end, i);
  assert(i >= 0 && j > i, `Missing segment ${start} ... ${end}`);
  return source.slice(i, j);
}

// Invoke the production pager and binding functions (not a reimplemented model)
// against a minimal DOM/event model. This catches lost event listeners when
// p140 takes over p128's library markup and bindLibrary is not in scope.
const script = segment('function currentPage() {', 'function mediaKindOptions() {');
const controls = new Map();
const events = [];
const refreshes = [];
function control(id, value = '') {
  const element = {
    value,
    listeners: {},
    addEventListener(type, callback) { this.listeners[type] = callback; },
    fire(type, extra = {}) {
      let prevented = false;
      assert.equal(typeof this.listeners[type], 'function', `${id} missing ${type} listener`);
      this.listeners[type]({
        target: this,
        key: extra.key,
        preventDefault() { prevented = true; }
      });
      events.push({id, type, prevented});
    }
  };
  controls.set(id, element);
  return element;
}
[
  'p128Prev', 'p128Next', 'p128Apply', 'p128Query',
  'p128Kind', 'p128Life', 'p128Cat', 'p128Collection',
  'p128Reset', 'p128Grid', 'p128List', 'p128Sort'
].forEach(id => control(id));

const context = vm.createContext({
  p05Page:1, p05Query:'', p05Lifecycle:'', p05Category:'',
  p05CollectionId:'', p05Tag:'', p05Grid:true,
  selectedMediaKind:'', selectedSort:'newest',
  renderAuthoritativeLibrary() {
    refreshes.push({page:context.p05Page, kind:context.selectedMediaKind, query:context.p05Query});
  },
  Number, Math, URLSearchParams
});
vm.runInContext(script, context, {filename:'p140-media-library-owner.js'});
const host = {querySelector(selector) {return controls.get(selector.replace('#','')) || null;}};
context.bindAuthoritativeControls(host, 4);

assert.equal(controls.get('p128Prev').listeners.click !== undefined, true);
assert.equal(controls.get('p128Next').listeners.click !== undefined, true);
controls.get('p128Next').fire('click');
assert.equal(context.p05Page, 2);
assert.equal(context.buildParams(false).get('page'), '2');
controls.get('p128Next').fire('click');
controls.get('p128Next').fire('click');
assert.equal(context.p05Page, 4);
const count = refreshes.length;
controls.get('p128Next').fire('click');
assert.equal(refreshes.length, count, 'Last page must not request another page');
controls.get('p128Prev').fire('click');
assert.equal(context.p05Page, 3);

controls.get('p128Query').value = 'archive';
controls.get('p128Kind').value = 'Video';
controls.get('p128Life').value = 'Active';
controls.get('p128Cat').value = 'News';
controls.get('p128Collection').value = 'collection-1';
controls.get('p128Apply').fire('click');
assert.equal(context.p05Page, 1, 'Applying filters resets to page 1');
assert.equal(context.p05Query, 'archive');
assert.equal(context.selectedMediaKind, 'Video');
assert.equal(context.p05Category, 'News');
controls.get('p128Next').fire('click');
assert.equal(refreshes.at(-1).kind, 'Video', 'Filters survive Next');

controls.get('p128Sort').value = 'title';
controls.get('p128Sort').fire('change');
assert.equal(context.selectedSort, 'title');
controls.get('p128List').fire('click');
assert.equal(context.p05Grid, false);
controls.get('p128Grid').fire('click');
assert.equal(context.p05Grid, true);
controls.get('p128Query').value = 'arabic';
controls.get('p128Query').fire('keydown',{key:'Enter'});
assert.equal(context.p05Query, 'arabic');
controls.get('p128Reset').fire('click');
assert.equal(context.p05Page, 1);
assert.equal(context.p05Query, '');
assert.equal(context.selectedMediaKind, '');
assert.equal(context.p05Category, '');

assert.match(source, /bindAuthoritativeControls\(host, totalPages\)/, 'P140 renderer must bind its own controls');
assert.doesNotMatch(source, /typeof bindLibrary === 'function'/, 'Must not depend on private p128 bindLibrary');
assert.match(source, /if \(renderInFlight\) \{ pendingRender = true; return; \}/, 'Refresh during in-flight render is queued');
assert.match(source, /if \(pendingRender\) \{[\s\S]*?queueMicrotask\(\(\) => void renderAuthoritativeLibrary\(\)\)/, 'Queued refresh eventually runs');
assert.match(release, /<Version>0\.12\.19-p12\.52<\/Version>/, 'Installer release version must be incremented');
assert.match(html, /p140-media-library-owner\.js\?v=0\.12\.19-p12\.52/, 'Browser must cache-bust the fixed owner');

console.log('PASS: Next/Previous pages, boundary guards, filter persistence, sort, reset and queued render');
