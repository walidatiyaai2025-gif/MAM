(() => {
'use strict';

if (window.mamRuntimeInspector?.installed) return;

const delegatedFetch = window.fetch.bind(window);
const sent = new Map();
let clientBuild = null;

function clip(value, max) {
  const text = String(value ?? '').trim();
  if (!text) return null;
  return text.length <= max ? text : text.slice(0, max) + '…';
}

function route() {
  return clip(location.pathname + location.hash, 1000);
}

function fingerprint(event) {
  return [event.kind, event.message, event.route, event.status].join('|').slice(0, 1000);
}

function shouldSend(event) {
  const key = fingerprint(event);
  const now = Date.now();
  const previous = sent.get(key) || 0;
  sent.set(key, now);
  if (sent.size > 100) {
    for (const [item, at] of sent) if (now - at > 60000) sent.delete(item);
  }
  return now - previous > 10000;
}

function report(event) {
  const metadata = {...(event.metadata || {})};
  if (clientBuild?.version) metadata.clientVersion = String(clientBuild.version);
  if (clientBuild?.commitSha) metadata.clientCommitSha = String(clientBuild.commitSha);

  const payload = {
    source: 'WebPortal',
    level: event.level || 'Error',
    kind: event.kind || 'web-runtime',
    message: clip(event.message, 8000),
    exceptionType: clip(event.exceptionType, 300),
    stack: clip(event.stack, 24000),
    correlationId: clip(event.correlationId, 120),
    route: clip(event.route || route(), 1000),
    method: clip(event.method, 20),
    status: Number.isFinite(event.status) ? event.status : null,
    metadata: Object.keys(metadata).length ? metadata : null
  };

  if (!shouldSend(payload)) return;

  try {
    delegatedFetch('/client-api/runtime-inspector/client-event', {
      method: 'POST',
      credentials: 'same-origin',
      keepalive: true,
      headers: {'Content-Type': 'application/json', 'Accept': 'application/json'},
      body: JSON.stringify(payload)
    }).catch(() => {});
  } catch { }
}

window.addEventListener('error', event => {
  if (event.target && event.target !== window) {
    const target = event.target;
    report({
      kind: 'resource-load-error',
      message: 'A page resource failed to load.',
      route: route(),
      metadata: {
        tag: target.tagName || '',
        resource: clip(target.src || target.href || '', 1000)
      }
    });
    return;
  }

  report({
    kind: 'javascript-error',
    message: event.message || event.error?.message || 'Unhandled JavaScript error',
    exceptionType: event.error?.name || 'Error',
    stack: event.error?.stack || null,
    route: route(),
    metadata: {
      file: clip(event.filename, 1000),
      line: String(event.lineno || ''),
      column: String(event.colno || '')
    }
  });
}, true);

window.addEventListener('unhandledrejection', event => {
  const reason = event.reason;
  report({
    kind: 'unhandled-promise-rejection',
    message: reason?.message || String(reason || 'Unhandled promise rejection'),
    exceptionType: reason?.name || 'PromiseRejection',
    stack: reason?.stack || null,
    route: route()
  });
});

async function inspectedFetch(input, init) {
  const raw = input instanceof Request ? input.url : String(input);
  let url;
  try { url = new URL(raw, location.href); } catch { return delegatedFetch(input, init); }

  if (url.origin !== location.origin || url.pathname === '/client-api/runtime-inspector/client-event')
    return delegatedFetch(input, init);

  const method = String(init?.method || (input instanceof Request ? input.method : 'GET') || 'GET').toUpperCase();
  try {
    const response = await delegatedFetch(input, init);
    if (url.pathname.startsWith('/client-api/') && response.status >= 400) {
      report({
        level: response.status >= 500 ? 'Error' : 'Warning',
        kind: 'client-api-http-error',
        message: `HTTP ${response.status} from ${url.pathname}`,
        correlationId: response.headers.get('X-Correlation-ID'),
        route: route(),
        method,
        status: response.status,
        metadata: { endpoint: url.pathname }
      });
    }
    return response;
  } catch (error) {
    if (url.pathname.startsWith('/client-api/')) {
      report({
        kind: 'client-api-network-error',
        message: error?.message || 'Network request failed',
        exceptionType: error?.name || 'NetworkError',
        stack: error?.stack || null,
        route: route(),
        method,
        metadata: { endpoint: url.pathname }
      });
    }
    throw error;
  }
}

window.fetch = inspectedFetch;

delegatedFetch('/version', {cache:'no-store', headers:{'Accept':'application/json'}})
  .then(response => response.ok ? response.json() : null)
  .then(value => { if (value) clientBuild = value; })
  .catch(() => {});

window.mamRuntimeInspector = Object.freeze({
  installed: true,
  version: 'runtime-inspector-1',
  report
});
})();