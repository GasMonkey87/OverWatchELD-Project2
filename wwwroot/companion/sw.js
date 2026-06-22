const CACHE = 'overwatch-eld-companion-v2';
const SHELL = ['/', '/index.html', '/connect.html', '/map.html', '/manifest.webmanifest'];
self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(SHELL).catch(() => {})));
  self.skipWaiting();
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))));
  self.clients.claim();
});
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith('/api/') || url.pathname === '/health') return;
  event.respondWith(fetch(event.request).then(resp => {
    const clone = resp.clone();
    caches.open(CACHE).then(cache => cache.put(event.request, clone)).catch(() => {});
    return resp;
  }).catch(() => caches.match(event.request).then(resp => resp || caches.match('/index.html'))));
});
