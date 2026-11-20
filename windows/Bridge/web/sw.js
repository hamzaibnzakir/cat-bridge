// Cat Bridge service worker: shows notifications the laptop sends, opens the app when you tap one.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

self.addEventListener('push', e => {
  let d = {};
  try { d = e.data ? e.data.json() : {}; } catch { d = { title: 'Cat Bridge', body: e.data ? e.data.text() : '' }; }
  const title = d.title || 'Cat Bridge';
  e.waitUntil(self.registration.showNotification(title, {
    body: d.body || '',
    tag: d.tag || undefined,
    icon: '/icon-180.png',
    badge: '/icon-180.png',
    data: { url: d.url || '/' },
  }));
});

self.addEventListener('notificationclick', e => {
  e.notification.close();
  const url = (e.notification.data && e.notification.data.url) || '/';
  e.waitUntil((async () => {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of all) {
      try { await c.navigate(url); } catch {}
      if ('focus' in c) return c.focus();
    }
    return self.clients.openWindow(url);
  })());
});
