(() => {
'use strict';

if (window.mamApiResilience?.installed) return;

const nativeFetch = window.fetch.bind(window);
const transientReadStatuses = new Set([500, 502, 503, 504]);
let sessionRedirectScheduled = false;

function correlationId() {
  try {
    return crypto.randomUUID();
  } catch {
    return 'mam-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
  }
}

function clientApiUrl(input) {
  try {
    const raw = input instanceof Request ? input.url : String(input);
    const url = new URL(raw, location.href);
    return url.origin === location.origin && url.pathname.startsWith('/client-api/');
  } catch {
    return false;
  }
}

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET') || 'GET').toUpperCase();
}

function enrichInit(input, init) {
  const result = {...(init || {})};
  const sourceHeaders = input instanceof Request ? input.headers : undefined;
  const headers = new Headers(sourceHeaders || {});
  new Headers(result.headers || {}).forEach((value,key)=>headers.set(key,value));
  if (!headers.has('X-Correlation-ID')) headers.set('X-Correlation-ID', correlationId());
  if (!headers.has('Accept')) headers.set('Accept','application/json');
  result.headers = headers;
  result.credentials = result.credentials || 'same-origin';
  if (requestMethod(input,result) === 'GET' && result.cache === undefined) result.cache = 'no-store';
  return result;
}

async function mamFetch(input, init) {
  if (!clientApiUrl(input)) return nativeFetch(input, init);

  const options = enrichInit(input, init);
  const method = requestMethod(input, options);
  let response = await nativeFetch(input, options);

  if (method === 'GET' && transientReadStatuses.has(response.status)) {
    await new Promise(resolve => setTimeout(resolve, 180));
    response = await nativeFetch(input, options);
  }

  if (response.status === 401) {
    window.dispatchEvent(new CustomEvent('mam:session-expired', {
      detail:{url: response.url || String(input)}
    }));
    if (!sessionRedirectScheduled) {
      sessionRedirectScheduled = true;
      const returnUrl = location.pathname + location.search + location.hash;
      setTimeout(() => {
        location.replace('/auth/login?returnUrl=' + encodeURIComponent(returnUrl));
      }, 80);
    }
  }

  return response;
}

window.fetch = mamFetch;
window.mamApiResilience = Object.freeze({
  installed:true,
  version:'p141-api-resilience-1',
  nativeFetch
});
})();