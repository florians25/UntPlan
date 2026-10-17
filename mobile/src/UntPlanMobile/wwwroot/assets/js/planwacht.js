/* =====================================================================
   planwacht.js — Benachrichtigung bei Änderungen im eigenen Plan
   ---------------------------------------------------------------------
   Läuft in der Seite und im Service Worker (importScripts). Holt die
   laufende und die nächste Woche des eigenen Plans, vergleicht sie mit
   dem zuletzt gesehenen Stand und zeigt bei Unterschieden eine lokale
   Benachrichtigung. Der Stand liegt im Cache Storage — den erreichen
   Seite und Service Worker gleichermaßen.

   Rückfall für Browser ohne Web-Push (sonst meldet sich der Server
   selbst, siehe Pushwache in UntPlanMobile). Die Seite schaltet die Wache
   ein und aus (Schalter im Profil); geprüft wird beim Öffnen, beim
   Zurückkehren in die App, alle 15 Minuten während sie offen ist und —
   wo der Browser es kann — im Hintergrund über „periodic background sync“.
   ===================================================================== */
(function (g){
  const CACHE = 'untplan-planwacht';
  const KEY = new URL('./__planwacht.json', g.location.href).href;
  const API = new URL('./api/mobile', g.location.href).href;
  const WT = ['So.', 'Mo.', 'Di.', 'Mi.', 'Do.', 'Fr.', 'Sa.'];

  const zwei = n => String(n).padStart(2, '0');
  const iso = d => `${d.getFullYear()}-${zwei(d.getMonth() + 1)}-${zwei(d.getDate())}`;
  const parse = s => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
  function montag(d){
    const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    x.setDate(x.getDate() - ((x.getDay() + 6) % 7));
    return x;
  }
  /* Laufende und nächste Woche. Am Wochenende zählt die kommende Woche
     als laufende — die vergangene ist dann ohne Belang. */
  function wochen(heute){
    const h = heute || new Date();
    const m = montag(h);
    if (h.getDay() === 6 || h.getDay() === 0) m.setDate(m.getDate() + 7);
    const n = new Date(m); n.setDate(n.getDate() + 7);
    return [iso(m), iso(n)];
  }

  /* ---------- Stand lesen und schreiben ---------- */
  async function lesen(){
    try {
      const c = await caches.open(CACHE);
      const r = await c.match(KEY);
      if (r) return await r.json();
    } catch (e){ /* kein Speicher */ }
    return { an:false, konto:'', wochen:{} };
  }
  async function schreiben(d){
    try {
      const c = await caches.open(CACHE);
      await c.put(KEY, new Response(JSON.stringify(d), { headers:{ 'Content-Type':'application/json' } }));
    } catch (e){ /* kein Speicher */ }
  }

  /* ---------- Vergleich ---------- */
  /* Nur, was eine Lehrkraft wissen muss: Art, Fach, Klasse, Lehrkraft,
     Raum, Hinweis. Hausaufgaben, Notizen und dergleichen lösen nichts aus. */
  const FELDER = ['lesson', 'span', 'blockType', 'subject', 'class', 'teacher', 'room', 'info', 'openSub', 'teacherAbsent', 'standbyAbsent', '_supSub', '_supAbsent'];
  function zeichen(c){
    return FELDER.map(f => c[f] === undefined || c[f] === null || c[f] === false ? '' : String(c[f])).join('|');
  }
  /* Stand je Tag: sortierte Liste der Kachel-Zeichen (Ferien ohne Belang). */
  function stand(days){
    const out = {};
    Object.keys(days || {}).forEach(d => {
      out[d] = (days[d] || [])
        .filter(c => c && !c.holiday && c.blockType !== 'holiday')
        .map(c => ({ z:zeichen(c), t:text(c) }))
        .sort((a, b) => a.z < b.z ? -1 : a.z > b.z ? 1 : 0);
    });
    return out;
  }
  /* Kurzbeschreibung einer Kachel für die Benachrichtigung. */
  function text(c){
    const was = c.blockType === 'cancelled' ? 'Entfall'
              : c.blockType === 'klausur' ? 'Prüfung'
              : (c._supervision || c.blockType === 'supervision') ? 'Aufsicht'
              : c.blockType === 'standby' ? 'Bereitschaft'
              : (c.changed || []).length === 1 && c.changed[0] === 'room' ? 'Raumänderung'
              : c.blockType === 'change' ? 'Vertretung' : c.blockType === 'normal' ? 'Regulär' : '';
    const std = c._pausePre ? 'vor der 1. Std.' : c._supervision ? `nach der ${c.lesson}. Std.`
              : `${c.lesson}.${(c.span || 1) > 1 ? '–' + (c.lesson + c.span - 1) + '.' : ''} Std.`;
    const teile = [c.subject, c.class, c.room].filter(Boolean).join(' ');
    return { was, std, teile };
  }
  /* Unterschiede ab heute; vergangene Tage interessieren nicht mehr. */
  function vergleich(alt, neu, heute){
    const liste = [];
    const ab = iso(heute || new Date());
    Object.keys(neu).sort().forEach(d => {
      if (d < ab || !alt[d]) return;
      const a = alt[d].map(x => x.z), n = neu[d].map(x => x.z);
      const dazu = neu[d].filter(x => !a.includes(x.z));
      const weg = alt[d].filter(x => !n.includes(x.z));
      if (!dazu.length && !weg.length) return;
      const tag = parse(d);
      const kopf = `${WT[tag.getDay()]} ${tag.getDate()}.${tag.getMonth() + 1}.`;
      dazu.forEach(x => liste.push(`${kopf} ${x.t.std}: ${[x.t.was, x.t.teile].filter(Boolean).join(' ')}`));
      /* Weggefallen ohne Ersatz zur selben Stunde: eigens melden. */
      weg.filter(x => !dazu.some(y => y.t.std === x.t.std))
         .forEach(x => liste.push(`${kopf} ${x.t.std}: ${x.t.teile || 'Stunde'} nicht mehr im Plan`));
    });
    return liste;
  }

  /* ---------- Holen und anzeigen ---------- */
  async function woche(ws){
    const u = new URL(API);
    u.searchParams.set('action', 'week'); u.searchParams.set('date', ws); u.searchParams.set('type', 'mine');
    const r = await fetch(u.href, { credentials:'same-origin', headers:{ 'Accept':'application/json' }, cache:'no-store' });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    const j = await r.json();
    if (!j || j.ok !== true) throw new Error('keine Daten');
    /* Gesperrte Woche (Wochenbegrenzung der Rolle): nichts vergleichen —
       sonst stünden alle Stunden als weggefallen da. */
    if (j.gesperrt === true) throw new Error('gesperrt');
    return j.days || {};
  }

  async function zeigen(liste){
    const titel = liste.length === 1 ? 'Änderung im Stundenplan' : `${liste.length} Änderungen im Stundenplan`;
    const body = liste.slice(0, 4).join('\n') + (liste.length > 4 ? `\n… und ${liste.length - 4} weitere` : '');
    const o = { body, tag:'planwacht', renotify:true, icon:'assets/icons/icon-192.png', badge:'assets/icons/icon-192.png', data:{ url:'./' } };
    try {
      if (g.registration && g.registration.showNotification) return await g.registration.showNotification(titel, o);
      if (g.navigator && g.navigator.serviceWorker){
        const reg = await g.navigator.serviceWorker.getRegistration();
        if (reg) return await reg.showNotification(titel, o);
      }
      if (typeof Notification !== 'undefined') new Notification(titel, o);
    } catch (e){ /* Anzeige nicht möglich */ }
  }

  let laeuft = null;
  /* Prüfen. `stumm`: nur den Stand merken (beim Einschalten). */
  async function pruefen(opt){
    if (laeuft) return laeuft;
    laeuft = (async () => {
      const o = opt || {};
      const d = await lesen();
      if (!d.an) return [];
      if (typeof Notification !== 'undefined' && Notification.permission !== 'granted') return [];
      const heute = o.heute || new Date();
      const alle = [];
      for (const ws of wochen(heute)){
        let days;
        try { days = await woche(ws); }
        catch (e){ if (e && e.message === 'gesperrt') delete d.wochen[ws]; continue; }
        const neu = stand(days);
        if (d.wochen[ws] && !o.stumm) alle.push(...vergleich(d.wochen[ws], neu, heute));
        d.wochen[ws] = neu;
      }
      /* Nur die beiden Wochen aufheben. */
      const behalten = wochen(heute);
      Object.keys(d.wochen).forEach(k => { if (!behalten.includes(k)) delete d.wochen[k]; });
      d.zuletzt = Date.now();
      await schreiben(d);
      if (alle.length) await zeigen(alle);
      return alle;
    })().finally(() => { laeuft = null; });
    return laeuft;
  }

  /* Ein- oder ausschalten. Ein anderes Konto beginnt mit leerem Stand. */
  async function einstellen(an, konto){
    const d = await lesen();
    if (d.konto !== (konto || '')) d.wochen = {};
    d.an = !!an; d.konto = konto || '';
    if (!an) d.wochen = {};
    await schreiben(d);
  }

  g.PlanWacht = { pruefen, einstellen, lesen, CACHE, _intern:{ stand, vergleich, wochen } };
})(self);
