/* =====================================================================
   sw.js — App-Shell offline, Daten stale-while-revalidate
   ---------------------------------------------------------------------
   Die Fassung setzt der Server ein (Prüfwert über alle Dateien der
   Oberfläche). Jede Änderung an CSS oder JS ergibt damit einen neuen
   Cachenamen — von Hand hochzählen ist nicht mehr nötig.
   ===================================================================== */
const VERSION = 'untplanmobile-__FASSUNG__';
/* Planwache: Vergleich mit dem zuletzt gesehenen Stand (siehe planwacht.js). */
try { importScripts('./assets/js/planwacht.js?v=__FASSUNG__'); } catch (e){ /* ohne Planwache weiter */ }
const SHELL = [
  './',
  './manifest.webmanifest',
  './assets/css/app.css?v=__FASSUNG__',
  './assets/js/app.js?v=__FASSUNG__',
  './assets/js/data.js?v=__FASSUNG__',
  './assets/js/api-db.js?v=__FASSUNG__',
  './assets/js/planwacht.js?v=__FASSUNG__',
  './assets/icons/icon-192.png',
  './assets/icons/icon-512.png',
  './assets/icons/favicon.svg'
];

self.addEventListener('install', e => {
  e.waitUntil(
    caches.open(VERSION)
      /* Die Hülle braucht eine Anmeldung nicht — sie zeigt dann die
         Anmeldemaske. Schlägt einzelnes fehl, wird trotzdem installiert. */
      .then(c => Promise.all(SHELL.map(u => c.add(u).catch(() => null))))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys()
      // Der Stand der Planwache überdauert jede neue Fassung.
      .then(keys => Promise.all(keys.filter(k => k !== VERSION && k !== 'untplan-planwacht').map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', e => {
  const req = e.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== location.origin) return;
  // Einmalanmeldung (Weiterleitungen zu UntPlanWeb) nie über den Service Worker
  if (url.pathname.includes('/sso/')) return;

  // Navigation: erst Netz, sonst Shell aus dem Cache
  if (req.mode === 'navigate'){
    e.respondWith(
      fetch(req)
        .then(res => {
          // Die Seite bringt die aktuelle Woche mit — als Offline-Hülle aufheben.
          if (res && res.ok){
            const copy = res.clone();
            caches.open(VERSION).then(c => c.put('./', copy));
          }
          return res;
        })
        .catch(() => caches.match('./'))
    );
    return;
  }

  // Stundenplandaten (api/…): Netz zuerst, Kopie im Cache
  if (url.pathname.includes('/api/')){
    e.respondWith(
      fetch(req)
        .then(res => {
          if (res && res.ok){
            const copy = res.clone();
            caches.open(VERSION).then(c => c.put(req, copy));
          }
          return res;
        })
        .catch(() => caches.match(req).then(hit => hit || offlineJson()))
    );
    return;
  }

  /* Stylesheet und Skript: erst das Netz, dann der Cache.
     ---------------------------------------------------------------
     Vorher galt auch hier „Cache zuerst“ — und `caches.match(req)`
     durchsucht *alle* Caches, nicht nur den der aktuellen Fassung. Eine
     neue Version von app.css wurde damit zwar geladen und abgelegt,
     angezeigt wurde aber weiter die alte Kopie; sichtbar frühestens
     beim übernächsten Start, auf dem Handy oft gar nicht, weil die App
     nur fortgesetzt statt neu gestartet wird.

     Beides sind kleine Dateien. Sie vom Netz zu holen kostet nichts und
     erspart die Frage, warum eine Änderung nicht ankommt. Ohne Netz
     greift weiterhin die gespeicherte Kopie. */
  if (/\.(css|js)$/i.test(url.pathname)){
    e.respondWith(
      fetch(req)
        .then(res => {
          if (res && res.status === 200){
            const copy = res.clone();
            caches.open(VERSION).then(c => c.put(req, copy));
          }
          return res;
        })
        .catch(() => caches.match(req).then(hit => hit || offlineJson()))
    );
    return;
  }

  /* Übriges Statisches (Symbole, Schriften): Cache zuerst, aber nur aus
     dem Cache dieser Fassung — sonst gewinnt wieder eine alte Kopie. */
  e.respondWith(
    caches.open(VERSION).then(c => c.match(req)).then(hit => {
      const net = fetch(req)
        .then(res => {
          if (res && res.status === 200){
            const copy = res.clone();
            caches.open(VERSION).then(c => c.put(req, copy));
          }
          return res;
        })
        .catch(() => hit || offlineJson());
      return hit || net;
    })
  );
});

/* Ohne Netz und ohne Kopie im Speicher muss trotzdem eine Antwort kommen.
   Gibt man nichts zurück, bricht der Aufruf in Safari mit „Load failed“ ab —
   und die App meldete das als „Nicht gespeichert“, obwohl gar nichts
   gespeichert werden sollte. */
function offlineJson(){
  return new Response(
    JSON.stringify({ ok:false, error:'Keine Verbindung', offline:true }),
    { status:503, headers:{ 'Content-Type':'application/json; charset=utf-8' } }
  );
}

self.addEventListener('message', e => {
  if (e.data === 'skip-waiting') self.skipWaiting();
});

/* =====================================================================
   Benachrichtigungen bei Änderungen im eigenen Plan
   ---------------------------------------------------------------------
   periodicsync: Wo der Browser es anbietet (Chrome/Edge bei installierter
   App), prüft der Service Worker auch im Hintergrund. Safari kennt das
   nicht — dort wird beim Öffnen und Zurückkehren in die App geprüft.
   push: echte Push-Nachrichten vom Server (siehe unten).
   ===================================================================== */
self.addEventListener('periodicsync', e => {
  if (e.tag === 'planwacht' && self.PlanWacht) e.waitUntil(self.PlanWacht.pruefen().catch(() => null));
});

/* Web-Push vom Server (RFC 8291, verschlüsselt): Die Nachricht bringt
   Titel und Text mit und wird direkt angezeigt. Ohne Inhalt prüft die
   Planwache selbst. Safari verlangt zu jedem Push eine sichtbare Meldung. */
self.addEventListener('push', e => {
  let d = null;
  try { d = e.data ? e.data.json() : null; } catch (x){ d = e.data ? { body: e.data.text() } : null; }
  if (d && (d.title || d.body)){
    e.waitUntil(self.registration.showNotification(d.title || 'Stundenplan', {
      body: d.body || '', tag: d.tag || 'planwacht', renotify: true,
      icon: './assets/icons/icon-192.png', badge: './assets/icons/icon-192.png',
      data: { url: d.url || './' }
    }));
    return;
  }
  if (!self.PlanWacht) return;
  e.waitUntil(self.PlanWacht.pruefen().catch(() => null));
});

self.addEventListener('notificationclick', e => {
  e.notification.close();
  const ziel = new URL((e.notification.data && e.notification.data.url) || './', self.location.href).href;
  e.waitUntil(
    self.clients.matchAll({ type:'window', includeUncontrolled:true }).then(list => {
      const offen = list.find(c => c.url.startsWith(self.registration.scope));
      if (offen) return offen.focus();
      return self.clients.openWindow(ziel);
    })
  );
});
