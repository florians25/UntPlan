// Monitoranzeige (ohne Anmeldung, über geheimen Link): Vertretungsplan mit Uhr, automatischem Bildlauf und Aktualisierung.
import { LOGO, e, datumLang } from './ui.js';

const wurzel = document.getElementById('monitor');
const token = location.pathname.split('/').filter(Boolean).pop();
let versatz = 0; // Abweichung der Geräteuhr von der Serveruhr

function alt(w) { return w ? `<span class="alt">${e(w)}</span>` : ''; }
/** alt → neu (Raum, Fach) bzw. alt | neu (Lehrkraft); ohne neuen Wert nur der alte durchgestrichen. */
function wechsel(a, n, trenn = '→') { return a && n ? `${alt(a)}<span class="v-trenn">${trenn}</span> ${e(n)}` : `${alt(a)}${e(n)}`; }

function tagHtml(t, lehrer, mitNachrichten = true) {
  const infos = [
    t.abwesendeKlassen.length ? `Abwesende Klassen: ${e(t.abwesendeKlassen.join(', '))}` : '',
    lehrer && t.abwesendeLehrer.length ? `Abwesende Lehrkräfte: ${e(t.abwesendeLehrer.join(', '))}` : '',
    t.blockierteRaeume.length ? `Nicht verfügbare Räume: ${e(t.blockierteRaeume.join(', '))}` : '',
  ].filter(Boolean);
  return `<section class="card h-100"><div class="card-header">${e(datumLang(t.datum))}${t.woche ? ` <span class="fw-normal opacity-75">· ${e(t.woche)}-Woche</span>` : ''}</div>
    <div class="card-body pb-0">
      ${mitNachrichten ? t.nachrichten.map(n => `<div class="nachricht">${e(n)}</div>`).join('') : ''}
      ${(t.veranstaltungen ?? []).map(v => `<div class="small mb-1"><span class="opacity-75">Veranstaltung:</span> ${e(v)}</div>`).join('')}
      ${infos.map(i => `<div class="small opacity-75 mb-1">${i}</div>`).join('')}
    </div>
    <div class="monitor-rollbereich"><div>
    ${t.frei ? `<p class="px-3">${e(t.freiGrund || 'Unterrichtsfrei')}</p>` : !t.zeilen.length ? '<p class="px-3 opacity-75">Keine Vertretungen.</p>' : `<table class="table table-card mb-0"><thead><tr><th>Klasse</th><th>Std.</th><th>Fach</th><th>Lehrkraft</th><th>Raum</th><th>Art</th><th>Text</th></tr></thead><tbody>
      ${t.zeilen.map(z => `<tr class="${e(z.status)}"><td class="fw-semibold">${e(z.klassen)}</td><td>${e(z.stunde)}</td><td>${z.status === 'entfall' ? alt(z.fachAlt) + e(z.fach) : wechsel(z.fachAlt, z.fach)}</td>
        <td class="vt-lehrer">${z.status === 'offen' ? alt(z.lehrerAlt) + '???' : z.status === 'entfall' ? alt(z.lehrerAlt) + e(z.lehrer) : wechsel(z.lehrerAlt, z.lehrer, '|')}</td><td>${z.status === 'entfall' ? alt(z.raumAlt) + e(z.raum) : wechsel(z.raumAlt, z.raum)}</td><td>${e(z.art)}</td><td>${e(z.text)}</td></tr>`).join('')}
    </tbody></table>`}</div></div></section>`;
}

function uhr() {
  const d = new Date(Date.now() + versatz);
  const el = document.querySelector('.uhr');
  if (el) el.textContent = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
}

// Langsamer Bildlauf in überlangen Tabellen, mit Pause oben und unten
const bildlauf = new Map();
function rollen() {
  document.querySelectorAll('.monitor-rollbereich').forEach((b, i) => {
    const max = b.scrollHeight - b.clientHeight;
    if (max <= 0) return;
    const s = bildlauf.get(i) ?? { pos: 0, warten: 120, richtung: 1 };
    if (s.warten > 0) s.warten--;
    else {
      s.pos += s.richtung * 0.6;
      if (s.pos >= max) { s.pos = max; s.warten = 160; s.richtung = -1; }
      if (s.pos <= 0 && s.richtung < 0) { s.pos = 0; s.warten = 160; s.richtung = 1; }
      if (s.richtung < 0) s.pos = Math.max(0, s.pos - 6); // schnell zurück
    }
    b.scrollTop = s.pos;
    bildlauf.set(i, s);
  });
  requestAnimationFrame(rollen);
}

async function laden() {
  try {
    const r = await fetch(`/api/monitor/${encodeURIComponent(token)}`, { cache: 'no-store' });
    if (r.status === 404) { wurzel.innerHTML = '<h1 class="h3">Monitor nicht gefunden</h1><p>Der Link ist ungültig oder wurde zurückgesetzt.</p>'; return; }
    const d = await r.json();
    versatz = new Date(d.jetzt).getTime() - Date.now();
    document.title = `${d.name} · UntPlanWeb`;
    // Monitoransichten (Administration): Nachrichten in der Kopfzeile der Tage, Laufschrift, Aktualisierungsintervall
    const anzeige = d.anzeige ?? {};
    const ticker = anzeige.ticker && d.tage[0]?.nachrichten.length ? d.tage[0].nachrichten.join('   +++   ') : '';
    wurzel.innerHTML = `<header class="monitor-kopf">${LOGO}<div><h1>Vertretungsplan</h1><div class="opacity-75">${e(d.schule)}</div></div><div class="uhr"></div></header>
      <div class="row g-3 row-cols-1 ${d.tage.length > 1 ? 'row-cols-xl-2' : ''}">${d.tage.map(t => `<div class="col">${tagHtml(t, d.version === 'lehrer', anzeige.kopfzeile !== false)}</div>`).join('') || '<p>Keine Daten.</p>'}</div>
      ${ticker ? `<div class="monitor-ticker"><span>${e(ticker)}</span></div>` : ''}`;
    const takt = Math.max(15, Number(anzeige.aktualisierung) || 60) * 1000;
    if (takt !== aktualisierung) { aktualisierung = takt; clearInterval(zeitgeber); zeitgeber = setInterval(laden, takt); }
    bildlauf.clear();
    uhr();
  } catch {
    // Netzwerkfehler: alten Stand weiter anzeigen
  }
}

let aktualisierung = 60000;
let zeitgeber = setInterval(laden, aktualisierung);
laden();
setInterval(uhr, 5000);
requestAnimationFrame(rollen);
