// Monitoranzeige (ohne Anmeldung, über geheimen Link): Vertretungsplan mit Uhr, automatischem Bildlauf und Aktualisierung.
import { LOGO, e, datumLang } from './ui.js';

const wurzel = document.getElementById('monitor');
const token = location.pathname.split('/').filter(Boolean).pop();
let versatz = 0; // Abweichung der Geräteuhr von der Serveruhr

function alt(w) { return w ? `<span class="alt">${e(w)}</span>` : ''; }

function tagHtml(t, lehrer) {
  const infos = [
    t.abwesendeKlassen.length ? `Abwesende Klassen: ${e(t.abwesendeKlassen.join(', '))}` : '',
    lehrer && t.abwesendeLehrer.length ? `Abwesende Lehrkräfte: ${e(t.abwesendeLehrer.join(', '))}` : '',
    t.blockierteRaeume.length ? `Nicht verfügbare Räume: ${e(t.blockierteRaeume.join(', '))}` : '',
  ].filter(Boolean);
  return `<section class="monitor-tag"><h2>${e(datumLang(t.datum))}${t.woche ? ` <small style="font-weight:400;opacity:.7">${e(t.woche)}-Woche</small>` : ''}</h2>
    ${t.nachrichten.map(n => `<div class="nachricht">${e(n)}</div>`).join('')}
    ${infos.map(i => `<div class="infos">${i}</div>`).join('')}
    <div class="monitor-rollbereich"><div class="rolle">
    ${t.frei ? `<p>${e(t.freiGrund || 'Unterrichtsfrei')}</p>` : !t.zeilen.length ? '<p style="opacity:.7">Keine Vertretungen.</p>' : `<table><thead><tr><th>Klasse</th><th>Std.</th><th>Fach</th><th>Lehrkraft</th><th>Raum</th><th>Art</th><th>Hinweis</th></tr></thead><tbody>
      ${t.zeilen.map(z => `<tr class="${e(z.status)}"><td><b>${e(z.klassen)}</b></td><td>${e(z.stunde)}</td><td>${alt(z.fachAlt)}${e(z.fach)}</td>
        <td>${alt(z.lehrerAlt)}${z.status === 'offen' ? '???' : e(z.lehrer)}</td><td>${alt(z.raumAlt)}${e(z.raum)}</td><td>${e(z.art)}</td><td>${e(z.text)}</td></tr>`).join('')}
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
    if (r.status === 404) { wurzel.innerHTML = '<h1>Monitor nicht gefunden</h1><p>Der Link ist ungültig oder wurde zurückgesetzt.</p>'; return; }
    const d = await r.json();
    versatz = new Date(d.jetzt).getTime() - Date.now();
    document.title = `${d.name} · UntPlanWeb`;
    wurzel.innerHTML = `<header class="monitor-kopf"><span style="width:52px;height:52px;display:block">${LOGO}</span>
        <div><h1>Vertretungsplan</h1><div style="opacity:.75">${e(d.schule)}</div></div><div class="uhr"></div></header>
      <div class="monitor-tage" style="--spalten:${Math.min(d.tage.length, 2) || 1}">${d.tage.map(t => tagHtml(t, d.version === 'lehrer')).join('') || '<p>Keine Daten.</p>'}</div>`;
    wurzel.querySelectorAll('.monitor-kopf svg').forEach(s => { s.style.width = '52px'; s.style.height = '52px'; });
    bildlauf.clear();
    uhr();
  } catch {
    // Netzwerkfehler: alten Stand weiter anzeigen
  }
}

laden();
setInterval(laden, 60000);
setInterval(uhr, 5000);
requestAnimationFrame(rollen);
