// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

// Web Push from the gateway (MyApp.Gateway PushSender): { title, body, url, tag }.
self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data?.text() }; }
    event.waitUntil(self.registration.showNotification(data.title || 'MyProduct', {
        body: data.body || '',
        tag: data.tag || undefined,
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        lang: 'fr',
        data: { url: data.url || '' }
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const target = new URL(event.notification.data?.url || '', self.registration.scope).href;
    event.waitUntil((async () => {
        const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        const open = windows.find(w => w.url.startsWith(self.registration.scope));
        if (open) {
            await open.focus();
            return open.navigate(target);
        }
        return clients.openWindow(target);
    })());
});
