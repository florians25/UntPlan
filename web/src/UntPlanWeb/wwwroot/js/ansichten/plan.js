// Stundenpläne (Klasse, Lehrkraft, Raum) wie im Original: Element mit Blättern links, Woche „12. 10. - 18. 10. 2026“ mittig,
// „Heute“ rechts, Raster mit Zeitspalte und Jetzt-Linie, Legende als schwebende Pillenleiste unten, Hilfe unten rechts.
import { e, icon, holen, heuteIso, plusTage, montag, lade, dialog } from '../ui.js';
import { wochenGitter, tagAuswahl, kartenBinden } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';

const TYPEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', raum: 'Raum' };
const LISTE = { klasse: 'klassen', lehrer: 'lehrer', raum: 'raeume' };

/** Langname ohne vorangestelltes Kürzel („7a – Klasse 7a“ → „Klasse 7a“). */
function langname(plan) {
  let n = (plan.name || '').trim();
  if (n.toLowerCase().startsWith(plan.element.toLowerCase())) n = n.slice(plan.element.length).replace(/^\s*[–-]\s*/, '');
  return n && n !== plan.element ? n : '';
}

/** Woche wie im Original: „12. 10. - 18. 10. 2026“ (Montag bis Sonntag). */
export function wochenText(mo) {
  const so = plusTage(mo, 6);
  const t = iso => { const [, m, d] = iso.split('-'); return `${d}. ${m}.`; };
  return `${t(mo)} - ${t(so)} ${so.slice(0, 4)}`;
}

/** Legende als schwebende Pillenleiste; die Pillen blenden Statusfarben aus und ein (wie im Original). */
export function legendeHtml(aus, kartenZeiten, mitDruck = true) {
  const pille = (id, marker, text) => `<button type="button" class="u-legende-pille ${aus.has(id) ? 'aus' : ''}" data-legende="${id}" aria-pressed="${!aus.has(id)}" title="${text} hervorheben ein/aus">${marker}${text}</button>`;
  return `<div class="u-legende-leiste nicht-drucken" data-legendenleiste>
    ${pille('entfall', '<svg class="lg-mk" viewBox="0 0 22 20" aria-hidden="true"><path d="M11 2 20.5 18.2H1.5z" fill="#f03e3e" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/></svg>', 'Entfall')}${pille('pruefung', '<svg class="lg-mk" viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="8.4" fill="#f7c600" stroke="#fff" stroke-width="1.6"/></svg>', 'Prüfung')}${pille('aenderung', '<svg class="lg-mk" viewBox="0 0 20 20" aria-hidden="true"><rect x="1.6" y="1.6" width="16.8" height="16.8" rx="2" fill="#1f9d55" stroke="#fff" stroke-width="1.6"/></svg>', 'Änderung')}
    <div class="position-relative"><button type="button" class="u-legende-mehr" data-legende-mehr title="Weitere Einstellungen" aria-label="Weitere Einstellungen" aria-expanded="false"><i class="bi bi-three-dots-vertical"></i></button>
      <div class="u-legende-menue d-none" data-legende-menue>
        <div class="eintrag"><i class="feld" style="background:var(--u-gruen-hell)"></i>Änderung / Vertretung</div>
        <div class="eintrag"><i class="mk-plus"></i>zusätzliche Stunde, Veranstaltung</div>
        <div class="eintrag"><i class="feld" style="background:var(--u-rot-hell)"></i>Entfall</div>
        <div class="eintrag"><i class="feld" style="background:var(--u-gelb-hell)"></i>Prüfung</div>
        <div class="eintrag"><i class="feld" style="background:var(--u-ferien)"></i>Ferien, Feiertag</div>
        <div class="eintrag"><i class="feld" style="background:#f3f5f7;outline:1px dashed #8d99a3;outline-offset:-1px"></i>Bereitschaft, Vormerkung</div>
        <div class="eintrag"><span class="std-lbl">AB</span>geänderter Wert</div>
        <div class="eintrag"><span class="std-lbl weg">AB</span>entfällt (ohne Ersatz)</div>
        <hr>
        <button type="button" class="eintrag" data-zeiten>${kartenZeiten ? '<i class="bi bi-check2"></i>' : '<i class="bi bi-clock"></i>'} Uhrzeiten auf den Karten</button>
        ${mitDruck ? '<button type="button" class="eintrag" data-drucken><i class="bi bi-printer"></i> Drucken</button>' : ''}
      </div></div>
  </div>
  <button type="button" class="u-hilfe-rund nicht-drucken" data-hilfe title="Hilfe" aria-label="Hilfe">${icon('hilfe')}</button>`;
}

/** Hilfe zum Stundenplan (Symbole und Bedienung). */
export function planHilfe() {
  dialog({
    titel: 'Hilfe zum Stundenplan', breit: true,
    inhalt: `<div class="row g-3 small">
      <div class="col-md-6"><b class="d-block mb-1">Karten</b>
        <div class="d-flex gap-2 align-items-center mb-1"><i class="mk-dreieck"></i> Entfall – die Stunde fällt aus, alle Angaben sind durchgestrichen.</div>
        <div class="d-flex gap-2 align-items-center mb-1"><i class="mk-quadrat"></i> Änderung – z. B. Vertretung oder Raumänderung, geänderte Werte als grüne Kürzel.</div>
        <div class="d-flex gap-2 align-items-center mb-1"><i class="mk-plus"></i> zusätzliche Stunde oder Veranstaltung.</div>
        <div class="d-flex gap-2 align-items-center mb-1"><i class="mk-kreis"></i> Prüfung (Klausur, Klassenarbeit).</div>
        <div class="d-flex gap-2 align-items-center"><span class="std-lbl weg">AB</span> Vertretung ohne Lehrkraft – die bisherige Lehrkraft ist durchgestrichen.</div></div>
      <div class="col-md-6"><b class="d-block mb-1">Bedienung</b>
        <ul class="ps-3 mb-0"><li>Mit ‹ › oben blättern Sie wochenweise, „Heute“ springt zur aktuellen Woche; ein Klick auf das Datum öffnet den Kalender.</li>
          <li>Klasse, Lehrkraft oder Raum wählen Sie oben links (Alt+← / Alt+→ blättert).</li>
          <li>Ein Klick auf eine Stunde öffnet die Stundendetails mit Klassenbuch.</li>
          <li>Die Pillen unten blenden die Hervorhebung von Entfall, Prüfung und Änderung aus und ein.</li>
          <li>Die dunkle Marke links zeigt die aktuelle Uhrzeit.</li></ul></div></div>`,
    knoepfe: [{ text: 'Schließen', art: 'haupt' }],
  });
}

/** Legende und Hilfe binden. neuZeichnen(kartenZeiten) wird beim Umschalten der Uhrzeiten aufgerufen. */
export function legendeBinden(wurzel, aus, kartenZeitenUmschalten) {
  const tt = () => wurzel.querySelector('.tt');
  const anwenden = () => { const t = tt(); if (t) ['entfall', 'pruefung', 'aenderung'].forEach(x => t.classList.toggle('ohne-' + x, aus.has(x))); };
  anwenden();
  wurzel.querySelectorAll('[data-legende]').forEach(b => b.onclick = () => {
    const id = b.dataset.legende;
    if (aus.has(id)) aus.delete(id); else aus.add(id);
    try { localStorage.setItem('untplan:legende', JSON.stringify([...aus])); } catch { /* privat */ }
    b.classList.toggle('aus', aus.has(id));
    b.setAttribute('aria-pressed', !aus.has(id));
    anwenden();
  });
  const menue = wurzel.querySelector('[data-legende-menue]');
  const mehr = wurzel.querySelector('[data-legende-mehr]');
  mehr.onclick = ev => { ev.stopPropagation(); menue.classList.toggle('d-none'); mehr.setAttribute('aria-expanded', !menue.classList.contains('d-none')); };
  if (!window.untplanLegendeKlick) {
    window.untplanLegendeKlick = true;
    document.addEventListener('click', ev => { if (!ev.target.closest('[data-legende-menue]')) document.querySelectorAll('[data-legende-menue]').forEach(m => m.classList.add('d-none')); });
  }
  wurzel.querySelector('[data-zeiten]').onclick = () => kartenZeitenUmschalten();
  wurzel.querySelector('[data-drucken]')?.addEventListener('click', () => { menue.classList.add('d-none'); window.print(); });
  wurzel.querySelector('[data-hilfe]').onclick = planHilfe;
}

export function legendeAus() {
  try { return new Set(JSON.parse(localStorage.getItem('untplan:legende') ?? '[]')); } catch { return new Set(); }
}

export async function zeigen(seite, kontext, [typParam, elementParam, datumParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const erlaubt = ich.kollegium ? ['klasse', 'lehrer', 'raum'] : ['klasse', 'raum'];
  const mein = !typParam || typParam === 'mein';
  if (mein && ich.element) { datumParam = /^\d{4}-/.test(elementParam ?? '') ? elementParam : datumParam; typParam = ich.typ; elementParam = ich.element; }
  const typ = erlaubt.includes(typParam) ? typParam : (ich.typ && erlaubt.includes(ich.typ) ? ich.typ : 'klasse');
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  let element = elementParam;
  if (!element) element = typ === ich.typ && ich.element ? ich.element : el[LISTE[typ]][0]?.kurz;
  const ziel = (t, x, d) => `#/plan/${t}/${encodeURIComponent(x ?? '')}/${d}`;
  if (!element) {
    kontext.titel('Stundenplan');
    seite.innerHTML = '<div class="alert alert-secondary">Noch keine Stammdaten vorhanden.</div>';
    return;
  }

  const istMein = typ === ich.typ && ich.element && element.toLowerCase() === ich.element.toLowerCase();
  kontext.titel('');
  document.title = `${istMein ? 'Mein Stundenplan' : `${TYPEN[typ]} ${element}`} – UntPlanWeb`;

  let kartenZeiten = !!kontext.ansicht.kartenZeiten;
  try { const w = localStorage.getItem('untplan:kartenzeiten'); if (w !== null) kartenZeiten = w === '1'; } catch { /* privat */ }
  const aus = legendeAus();
  const mo = montag(datum);
  const liste = el[LISTE[typ]];
  const pos = liste.findIndex(x => x.kurz.toLowerCase() === String(element).toLowerCase());
  seite.innerHTML = `
    <div class="planleiste nicht-drucken">
      <div class="links">${istMein ? '' : `
        <button type="button" class="rundknopf" data-element="-1" title="Vorheriges Element (Alt+←)" aria-label="Vorheriges Element" ${pos <= 0 ? 'disabled' : ''}>${icon('links')}</button>
        <span class="element-pill"><span data-wahl></span></span>
        <button type="button" class="rundknopf" data-element="1" title="Nächstes Element (Alt+→)" aria-label="Nächstes Element" ${pos < 0 || pos >= liste.length - 1 ? 'disabled' : ''}>${icon('rechts')}</button>`}
      </div>
      <div class="mitte">
        <button type="button" class="rundknopf" data-woche="-7" title="Vorherige Woche" aria-label="Vorherige Woche">${icon('links')}</button>
        <label class="zeitraum" title="Datum wählen">${wochenText(mo)}<small data-wochenart></small>
          <input type="date" value="${datum}" data-datum aria-label="Datum"></label>
        <button type="button" class="rundknopf" data-woche="7" title="Nächste Woche" aria-label="Nächste Woche">${icon('rechts')}</button>
      </div>
      <div class="rechts"><button type="button" class="heute-pill" data-woche="0">Heute</button></div>
    </div>
    <div data-inhalt>${lade()}</div>`;

  seite.querySelector('[data-wahl]')?.replaceWith(elementWahl({
    eintraege: liste, wert: element, platzhalter: `${TYPEN[typ]} wählen`,
    beiWahl: x => kontext.navigieren(ziel(typ, x, datum)),
  }));
  const blaettern = n => { const x = liste[pos + n]; if (x) kontext.navigieren(ziel(typ, x.kurz, datum)); };
  seite.querySelectorAll('[data-element]').forEach(b => b.onclick = () => blaettern(Number(b.dataset.element)));
  seite.querySelectorAll('[data-woche]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.woche);
    const d = n === 0 ? heuteIso() : plusTage(mo, n);
    kontext.navigieren(istMein ? `#/plan/mein/${d}` : ziel(typ, element, d));
  });
  const datumFeld = seite.querySelector('[data-datum]');
  datumFeld.onchange = ev => { if (ev.target.value) kontext.navigieren(istMein ? `#/plan/mein/${ev.target.value}` : ziel(typ, element, ev.target.value)); };
  datumFeld.onclick = () => { try { datumFeld.showPicker(); } catch { /* ältere Browser */ } };
  const tasten = ev => {
    if (!document.body.contains(seite) || !location.hash.startsWith('#/plan')) { document.removeEventListener('keydown', tasten); return; }
    if (!ev.altKey || ev.target.closest('input, textarea, select') || istMein) return;
    if (ev.key === 'ArrowLeft' || ev.key === 'ArrowRight') { ev.preventDefault(); document.removeEventListener('keydown', tasten); blaettern(ev.key === 'ArrowLeft' ? -1 : 1); }
  };
  document.addEventListener('keydown', tasten);

  const istAktuell = kontext.aktuell();
  const plan = await holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  let gewaehlt = Math.max(0, plan.tage.findIndex(t => t.datum === datum));
  if (plan.woche) seite.querySelector('[data-wochenart]').textContent = `${plan.woche}-Woche`;

  const zeichnen = () => {
    inhalt.innerHTML = `
      <div class="u-plantitel"><h2>${e(TYPEN[typ])} ${e(plan.element)}${langname(plan) ? ` – ${e(langname(plan))}` : ''}</h2>
        <span class="text-body-secondary small ms-2">${wochenText(mo)}</span></div>
      ${tagAuswahl(plan, gewaehlt)}
      ${wochenGitter(plan, { gewaehlterTag: gewaehlt, kartenZeiten })}
      ${legendeHtml(aus, kartenZeiten)}`;
    inhalt.querySelectorAll('[data-tagwahl]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.tagwahl); zeichnen(); });
    kartenBinden(inhalt, plan.karten, kontext);
    legendeBinden(inhalt, aus, () => {
      kartenZeiten = !kartenZeiten;
      try { localStorage.setItem('untplan:kartenzeiten', kartenZeiten ? '1' : '0'); } catch { /* privat */ }
      zeichnen();
    });
  };
  zeichnen();
  // Jetzt-Linie minütlich nachführen
  const takt = setInterval(() => { if (!document.body.contains(inhalt)) { clearInterval(takt); return; } if (!document.querySelector('.modal.show')) zeichnen(); }, 60000);
}
