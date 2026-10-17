// Unterricht → Prüfungskalender (UW-08) wie im Original: links Prüfungsart, Zeitbereich, Planung („Alle Ihre Prüfungen anzeigen“)
// und „Mein Unterricht“ als Auswahl; rechts das Jahresraster (Monate August–Juli × Tage 1–31, Wochentag in jeder Zelle,
// Wochenenden und Ferien grau) mit farbigen Prüfungstagen und Legende darunter.
import { e, holen, heuteIso, dialog } from '../ui.js';
import { pruefungLoeschen } from './pruefungen.js';

const MONATE = ['August', 'September', 'Oktober', 'November', 'Dezember', 'Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli'];
const WT = ['So.', 'Mo.', 'Di.', 'Mi.', 'Do.', 'Fr.', 'Sa.'];
let ARTEN = ['Klausur', 'Klassenarbeit', 'Test', 'Abschlussprüfung', 'Präsentation', 'Referat', 'Vergleichsarbeit'];
const z2 = n => String(n).padStart(2, '0');
const datumDe = iso => iso.split('-').reverse().join('.');

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  const sj = kontext.schuljahr;
  const startJahr = Number((sj?.von || heuteIso()).slice(0, 4)) - ((sj?.von ? 0 : (new Date().getMonth() < 7 ? 1 : 0)));
  const von = `${startJahr}-08-01`, bis = `${startJahr + 1}-07-31`;
  kontext.titel('Prüfungskalender');
  const [pruefungen, el, unterricht] = await Promise.all([
    holen(`/api/app/pruefungen?von=${von}&bis=${bis}`),
    kontext.elemente(),
    ich.kollegium && ich.element ? holen(`/api/app/unterricht?lehrer=${encodeURIComponent(ich.element)}`).catch(() => []) : Promise.resolve([]),
    // Prüfungsarten aus den Stammdaten (Kollegium; sonst der Grundbestand)
    ich.kollegium ? holen('/api/app/pruefungsarten').then(r => { if (r.arten?.length) ARTEN = r.arten.map(a => a.name || a.kurz); }).catch(() => null) : null,
  ]);
  let art = 'alle', alleEigenen = true, gewaehlt = 0;
  const wahlen = unterricht.filter(u => u.klassen.length).map(u => ({ text: `${u.fach} - ${u.klassen.join(', ')}${u.kurs ? ' - ' + u.kurs : ''}`, klassen: u.klassen, fach: u.fach }));
  const ferien = (el.ferien ?? []);
  const istFerien = iso => ferien.some(f => iso >= f.von && iso <= f.bis);

  const kategorie = p => {
    const eigen = ich.element && p.lehrer.some(l => l.toLowerCase() === ich.element.toLowerCase());
    const w = wahlen[gewaehlt];
    const klasse = w && p.klassen.some(k => w.klassen.includes(k));
    const artPasst = art === 'alle' || p.art === art;
    if (eigen && klasse && artPasst) return 'eigen-art';
    if (eigen && klasse) return 'eigen-andere';
    if (eigen && alleEigenen) return 'eigen-klassen';
    if (klasse && artPasst) return 'fremd-art';
    if (klasse) return 'fremd';
    return null;
  };

  const zeichnen = () => {
    const proTag = new Map();
    for (const p of pruefungen) { const k = kategorie(p); if (k) proTag.set(p.datum, [...(proTag.get(p.datum) ?? []), { p, k }]); }
    const rang = ['eigen-art', 'eigen-andere', 'fremd-art', 'eigen-klassen', 'fremd'];
    let raster = `<table class="pk-raster"><thead><tr><th></th>${MONATE.map(m => `<th>${m}</th>`).join('')}</tr></thead><tbody>`;
    for (let t = 1; t <= 31; t++) {
      raster += `<tr><th>${t}</th>`;
      for (let m = 0; m < 12; m++) {
        const jahr = m < 5 ? startJahr : startJahr + 1, monat = (m + 7) % 12;
        const d = new Date(jahr, monat, t, 12);
        if (d.getMonth() !== monat) { raster += '<td class="leer"></td>'; continue; }
        const iso = `${jahr}-${z2(monat + 1)}-${z2(t)}`;
        const we = d.getDay() === 0 || d.getDay() === 6;
        const liste = proTag.get(iso) ?? [];
        const k = rang.find(r => liste.some(x => x.k === r));
        const titel = liste.map(x => `${x.p.titel} (${x.p.klassen.join(', ')}${x.p.lehrer.length ? ', ' + x.p.lehrer.join(', ') : ''})`).join('\n');
        raster += `<td class="${we ? 'we' : ''} ${istFerien(iso) && !we ? 'ferien' : ''} ${k ? 'p-' + k : ''} ${iso === heuteIso() ? 'heute' : ''}" ${titel ? `title="${e(datumDe(iso) + '\n' + titel)}" data-tag="${iso}" tabindex="0" role="button"` : ''}>${WT[d.getDay()]}${liste.length > 1 ? `<sup>${liste.length}</sup>` : ''}</td>`;
      }
      raster += '</tr>';
    }
    raster += '</tbody></table>';
    seite.innerHTML = `<div class="pk">
      <aside class="pk-filter">
        <label><span>Prüfungsart</span><select class="form-select form-select-sm" data-art><option value="alle">Alle Prüfungsarten</option>${ARTEN.map(a => `<option ${a === art ? 'selected' : ''}>${a}</option>`).join('')}</select></label>
        <label><span>Zeitbereich</span><select class="form-select form-select-sm" disabled><option>${sj?.aktuell === false ? 'Schuljahr ' + e(sj.text) : 'Aktuelles Schuljahr'}</option></select></label>
        <div class="pk-gruppe"><span>Planung</span><label class="form-check"><input type="checkbox" class="form-check-input" data-alle ${alleEigenen ? 'checked' : ''}> <span class="form-check-label">Alle Ihre Prüfungen anzeigen</span></label></div>
        ${wahlen.length ? `<div class="pk-gruppe"><h3>Mein Unterricht</h3>${wahlen.map((w, i) => `<label class="form-check"><input type="radio" class="form-check-input" name="pk-u" value="${i}" ${i === gewaehlt ? 'checked' : ''}> <span class="form-check-label">${e(w.text)}</span></label>`).join('')}</div>` : ''}
      </aside>
      <div class="pk-haupt"><div class="u-tabelle-karte p-2">${raster}</div>
        <div class="pk-legende">
          <span><i class="p-eigen-art"></i>Ihre Prüfungen für die ausgewählte Art</span><span><i class="p-fremd-art"></i>Prüfungen der ausgewählten Klasse und Art</span><span><i class="p-fremd"></i>Fremde Prüfungen für die ausgewählte Klasse</span>
          <span><i class="p-eigen-andere"></i>Ihre Prüfungen für alle anderen Arten</span><span><i class="p-eigen-klassen"></i>Ihre Prüfungen für andere Klassen</span><span><i class="ferien"></i>Ferien, Feiertage</span>
        </div></div></div>`;
    seite.querySelector('[data-art]').onchange = ev => { art = ev.target.value; zeichnen(); };
    seite.querySelector('[data-alle]').onchange = ev => { alleEigenen = ev.target.checked; zeichnen(); };
    seite.querySelectorAll('[name=pk-u]').forEach(r => r.onchange = () => { gewaehlt = Number(r.value); zeichnen(); });
    // Tag mit Prüfungen: Liste mit „Löschen“ (Ersteller, Lehrkraft der Prüfung, Administration)
    const tagOeffnen = iso => {
      const liste = (proTag.get(iso) ?? []).map(x => x.p);
      const box = dialog({ titel: `Prüfungen am ${datumDe(iso)}`, inhalt: `<ul class="u-liste">${liste.map((p, i) => `<li><div class="u-zeilentext"><div class="titel">${e(p.titel)}${p.status === 'vorgemerkt' ? ' <span class="u-chip">vorgemerkt</span>' : ''}</div>
          <div class="unter">${p.stundeVon === p.stundeBis ? `${p.stundeVon}.` : `${p.stundeVon}.–${p.stundeBis}.`} Stunde · ${e(p.klassen.join(', '))}${p.lehrer.length ? ' · ' + e(p.lehrer.join(', ')) : ''}${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</div></div>
          ${p.darfLoeschen ? `<button type="button" class="nl-knopf rot" data-pk-loeschen="${i}"><i class="bi bi-trash" aria-hidden="true"></i>Löschen</button>` : ''}</li>`).join('')}</ul>`,
        knoepfe: [{ text: 'Schließen' }] });
      box.querySelectorAll('[data-pk-loeschen]').forEach(b => b.onclick = async () => {
        const p = liste[Number(b.dataset.pkLoeschen)];
        if (!await pruefungLoeschen(p)) return;
        pruefungen.splice(pruefungen.indexOf(p), 1);
        box.schliessen();
        zeichnen();
      });
    };
    seite.querySelectorAll('[data-tag]').forEach(td => {
      td.onclick = () => tagOeffnen(td.dataset.tag);
      td.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); tagOeffnen(td.dataset.tag); } };
    });
  };
  zeichnen();
}
