// Stundenpläne (Klasse, Lehrkraft, Raum) wie im Original: Elementwahl als Pille mit Info-Symbol (z. B. „Klassenlehrkraft: KM“),
// Datumsbereich als Pille mit eigenem Kalender (Wochenauswahl mit KW-Spalte), weißer „Heute“-Knopf rechts,
// Raster mit Zeitspalte und Jetzt-Linie, Legende als schwebende Pillenleiste mit Menü „⋮“ (Symbole anzeigen, Abwesenheiten,
// Drucken, Öffentlichen Link kopieren), Hilfe unten rechts. Keine A/B-Woche im Kopf.
import { e, icon, holen, heuteIso, plusTage, montag, lade, dialog, toast, druckenMitAusrichtung } from '../ui.js';
import { wochenGitter, tagAuswahl, kartenBinden, karteDialog } from '../plangitter.js';
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

// Kalender (Wochenauswahl im Kopf, Tageswahl in den Tagesansichten): eigene Komponente in kalender.js
export { kalenderwoche, kalenderBinden } from '../kalender.js';
import { kalenderBinden } from '../kalender.js';

/** Einstellungen der Legende (je Gerät). */
export function planEinstellungen() {
  const w = { symbole: true, abwesenheiten: true, zeiten: true, aufsichten: 'normal' };
  try { Object.assign(w, JSON.parse(localStorage.getItem('untplan:planeinstellungen') ?? '{}')); } catch { /* privat */ }
  try { const z = localStorage.getItem('untplan:kartenzeiten'); if (z !== null && !localStorage.getItem('untplan:planeinstellungen')) w.zeiten = z === '1'; } catch { /* privat */ }
  return w;
}
function einstellungenSpeichern(w) { try { localStorage.setItem('untplan:planeinstellungen', JSON.stringify(w)); } catch { /* privat */ } }

/** Legende als schwebende Pillenleiste; die Pillen blenden Statusfarben aus und ein, „⋮“ öffnet das Menü wie im Original. */
export function legendeHtml(aus, einst, mitDruck = true) {
  const pille = (id, marker, text) => `<button type="button" class="u-legende-pille ${aus.has(id) ? 'aus' : ''}" data-legende="${id}" aria-pressed="${!aus.has(id)}" title="${text} hervorheben ein/aus">${marker}${text}</button>`;
  const haken = an => an ? '<i class="bi bi-check-circle haken"></i>' : '<i class="bi bi-circle haken leer"></i>';
  return `<div class="u-legende-leiste nicht-drucken" data-legendenleiste>
    ${pille('entfall', '<svg class="lg-mk" viewBox="0 0 20 20" aria-hidden="true"><path d="M10 3.6 17.3 16.2H2.7z" fill="#fff" stroke="#fff" stroke-width="5" stroke-linejoin="round"/><path d="M10 3.6 17.3 16.2H2.7z" fill="#f03e3e" stroke="#f03e3e" stroke-width="2.6" stroke-linejoin="round"/></svg>', 'Entfall')}${pille('pruefung', '<svg class="lg-mk" viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="8.4" fill="#f7c600" stroke="#fff" stroke-width="1.6"/></svg>', 'Prüfung')}${pille('aenderung', '<svg class="lg-mk" viewBox="0 0 20 20" aria-hidden="true"><rect x="1.6" y="1.6" width="16.8" height="16.8" rx="2" fill="#1f9d55" stroke="#fff" stroke-width="1.6"/></svg>', 'Änderung')}
    <div class="position-relative"><button type="button" class="u-legende-mehr" data-legende-mehr title="Weitere Einstellungen" aria-label="Weitere Einstellungen" aria-expanded="false"><i class="bi bi-three-dots-vertical"></i></button>
      <div class="u-legende-menue d-none" data-legende-menue role="menu">
        <div class="kopf">Einstellungen</div>
        <button type="button" class="eintrag" data-einst="symbole" role="menuitemcheckbox" aria-checked="${einst.symbole}">${haken(einst.symbole)}Symbole anzeigen</button>
        <button type="button" class="eintrag" data-einst="zeiten" role="menuitemcheckbox" aria-checked="${einst.zeiten}">${haken(einst.zeiten)}Uhrzeiten anzeigen</button>
        <div class="kopf">Anzeigen</div>
        <button type="button" class="eintrag" data-einst="abwesenheiten" role="menuitemcheckbox" aria-checked="${einst.abwesenheiten}">${haken(einst.abwesenheiten)}Abwesenheiten anzeigen</button>
        <div class="kopf">Pausenaufsichten</div>
        ${[['normal', 'Normal'], ['halbiert', 'Halbiert (Blöcke durchgängig)'], ['aus', 'Ausgeblendet']].map(([w, t]) => `<button type="button" class="eintrag" data-aufsichten="${w}" role="menuitemradio" aria-checked="${(einst.aufsichten ?? 'normal') === w}">${haken((einst.aufsichten ?? 'normal') === w)}${t}</button>`).join('')}
        <div class="kopf">Andere</div>
        ${mitDruck ? '<button type="button" class="eintrag ohne" data-drucken role="menuitem">Drucken</button>' : ''}
        <button type="button" class="eintrag ohne" data-link role="menuitem">Öffentlichen Link kopieren <i class="bi bi-copy ms-auto text-body-tertiary"></i></button>
      </div></div>
  </div>
  <button type="button" class="u-hilfe-rund nicht-drucken" data-hilfe title="Hilfe" aria-label="Hilfe"><i class="bi bi-question-circle"></i></button>`;
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
        <div class="d-flex gap-2 align-items-center mb-1"><span class="hilfe-gestrichelt"></span> Lehrkraft abwesend – die Stunde wird von anderen gehalten (gestrichelt, eigenes Kürzel durchgestrichen).</div>
        <div class="d-flex gap-2 align-items-center mb-1"><img src="img/wu/exact/info.svg" alt="" width="16"> Notizen oder Vertretungstext zur Stunde.</div>
        <div class="d-flex gap-2 align-items-center mb-1"><img src="img/wu/exact/tt-homework-icon.svg" alt="" width="16"> Hausaufgabe fällig.</div>
        <div class="d-flex gap-2 align-items-center"><span class="std-lbl weg">AB</span> Vertretung ohne Lehrkraft – die bisherige Lehrkraft ist durchgestrichen.</div></div>
      <div class="col-md-6"><b class="d-block mb-1">Bedienung</b>
        <ul class="ps-3 mb-0"><li>Ein Klick auf den Datumsbereich öffnet den Kalender, „Heute“ springt zur aktuellen Woche.</li>
          <li>Klasse, Lehrkraft oder Raum wählen Sie oben links (Alt+← / Alt+→ blättert).</li>
          <li>Ein Klick auf eine Stunde öffnet die Stundendetails mit Klassenbuch.</li>
          <li>Die Pillen unten blenden die Hervorhebung von Entfall, Prüfung und Änderung aus und ein, „⋮“ öffnet weitere Einstellungen.</li></ul></div></div>`,
    knoepfe: [{ text: 'Schließen', art: 'haupt' }],
  });
}

/** Legende und Hilfe binden. neuZeichnen() wird nach dem Umschalten einer Einstellung aufgerufen. */
export function legendeBinden(wurzel, aus, einst, neuZeichnen) {
  const tt = () => wurzel.querySelector('.tt');
  const anwenden = () => {
    const t = tt(); if (!t) return;
    ['entfall', 'pruefung', 'aenderung'].forEach(x => t.classList.toggle('ohne-' + x, aus.has(x)));
    t.classList.toggle('ohne-symbole', !einst.symbole);
    t.classList.toggle('ohne-abwesenheiten', !einst.abwesenheiten);
  };
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
  wurzel.querySelectorAll('[data-einst]').forEach(b => b.onclick = ev => {
    ev.stopPropagation();
    einst[b.dataset.einst] = !einst[b.dataset.einst];
    einstellungenSpeichern(einst);
    neuZeichnen(true);
  });
  wurzel.querySelectorAll('[data-aufsichten]').forEach(b => b.onclick = ev => {
    ev.stopPropagation();
    einst.aufsichten = b.dataset.aufsichten;
    einstellungenSpeichern(einst);
    neuZeichnen(true);
  });
  wurzel.querySelector('[data-drucken]')?.addEventListener('click', () => { menue.classList.add('d-none'); druckenMitAusrichtung('quer', 'stundenplan'); });
  wurzel.querySelector('[data-link]').onclick = async () => {
    menue.classList.add('d-none');
    try { await navigator.clipboard.writeText(location.href); toast('Link in die Zwischenablage kopiert.'); }
    catch { prompt('Link zum Stundenplan:', location.href); }
  };
  wurzel.querySelector('[data-hilfe]').onclick = planHilfe;
}

export function legendeAus() {
  try { return new Set(JSON.parse(localStorage.getItem('untplan:legende') ?? '[]')); } catch { return new Set(); }
}

/** Info-Text zur Elementwahl wie im Original („Klassenlehrkraft: KM“, Name der Lehrkraft, Raumname). */
function elementInfo(typ, x) {
  if (!x) return '';
  if (typ === 'klasse') return x.leitung?.length ? `Klassenlehrkraft: ${x.leitung.join(', ')}` : (x.name && x.name !== x.kurz ? x.name : '');
  return x.name && x.name !== x.kurz ? x.name : '';
}

export async function zeigen(seite, kontext, [typParam, elementParam, datumParam, stundeParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const erlaubt = ich.kollegium ? ['klasse', 'lehrer', 'raum'] : ['klasse', 'raum'];
  const mein = !typParam || typParam === 'mein';
  // Erziehungsberechtigte mit mehreren Kindern: „Mein Stundenplan“ zeigt die Klasse des gewählten Kindes
  let meinElement = ich.element, meinKind = 0;
  if (kontext.familie && (ich.klassen ?? []).length > 1) {
    try {
      const kind = Number(sessionStorage.getItem('untplan:kind'));
      const k = kind ? (await holen('/api/app/familie/kinder').catch(() => [])).find(x => x.id === kind) : null;
      if (k) { meinElement = k.klasse; meinKind = k.id; }
    } catch { /* privat */ }
  }
  if (mein && meinElement) { datumParam = /^\d{4}-/.test(elementParam ?? '') ? elementParam : datumParam; typParam = ich.typ; elementParam = meinElement; }
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

  // eigener Plan ohne Elementauswahl nur unter „Mein Stundenplan“; unter „Lehrkraft“ immer mit Auswahl (auch eigene Lehrkraft)
  const istMein = mein && typ === ich.typ && !!meinElement && element.toLowerCase() === meinElement.toLowerCase();
  kontext.titel('');
  document.title = `${istMein ? 'Mein Stundenplan' : `${TYPEN[typ]} ${element}`} – UntPlanWeb`;

  const einst = planEinstellungen();
  const aus = legendeAus();
  const mo = montag(datum);
  const liste = el[LISTE[typ]];
  const pos = liste.findIndex(x => x.kurz.toLowerCase() === String(element).toLowerCase());
  const zuDatum = d => kontext.navigieren(istMein ? `#/plan/mein/${d}` : ziel(typ, element, d));
  seite.innerHTML = `
    <div class="planleiste nicht-drucken ${istMein ? 'mein' : ''}">
      <div class="links">${istMein ? '' : `
        <button type="button" class="rundknopf" data-element="-1" title="Vorheriges Element (Alt+←)" aria-label="Vorheriges Element" ${pos <= 0 ? 'disabled' : ''}>${icon('links')}</button>
        <span class="element-pill"><span data-wahl></span></span>
        <button type="button" class="rundknopf" data-element="1" title="Nächstes Element (Alt+→)" aria-label="Nächstes Element" ${pos < 0 || pos >= liste.length - 1 ? 'disabled' : ''}>${icon('rechts')}</button>`}
      </div>
      <div class="mitte">
        <button type="button" class="rundknopf" data-woche="-7" title="Vorherige Woche" aria-label="Vorherige Woche">${icon('links')}</button>
        <span class="position-relative"><button type="button" class="zeitraum" data-kalender title="Datum wählen" aria-haspopup="dialog">${wochenText(mo)}</button></span>
        <button type="button" class="rundknopf" data-woche="7" title="Nächste Woche" aria-label="Nächste Woche">${icon('rechts')}</button>
      </div>
      <div class="rechts"><button type="button" class="heute-pill" data-woche="0">Heute</button></div>
    </div>
    <div data-inhalt>${lade()}</div>`;

  seite.querySelector('[data-wahl]')?.replaceWith(elementWahl({
    eintraege: liste, wert: element, platzhalter: `${TYPEN[typ]} wählen`, info: x => elementInfo(typ, x),
    beiWahl: x => kontext.navigieren(ziel(typ, x, datum)),
  }));
  const blaettern = n => { const x = liste[pos + n]; if (x) kontext.navigieren(ziel(typ, x.kurz, datum)); };
  seite.querySelectorAll('[data-element]').forEach(b => b.onclick = () => blaettern(Number(b.dataset.element)));
  seite.querySelectorAll('[data-woche]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.woche);
    zuDatum(n === 0 ? heuteIso() : plusTage(mo, n));
  });
  kalenderBinden(seite.querySelector('[data-kalender]'), mo, zuDatum);
  // Kopf (Elementwahl, Zeitraum, Heute) und Tageskopf bleiben beim Scrollen oben stehen: Höhe der Leiste für den Tageskopf
  const leiste = seite.querySelector('.planleiste');
  const kopfHoehe = () => seite.style.setProperty('--plankopf', `${leiste.offsetHeight}px`);
  kopfHoehe();
  if (window.ResizeObserver) new ResizeObserver(kopfHoehe).observe(leiste);
  const tasten = ev => {
    if (!document.body.contains(seite) || !location.hash.startsWith('#/plan')) { document.removeEventListener('keydown', tasten); return; }
    if (!ev.altKey || ev.target.closest('input, textarea, select') || istMein) return;
    if (ev.key === 'ArrowLeft' || ev.key === 'ArrowRight') { ev.preventDefault(); document.removeEventListener('keydown', tasten); blaettern(ev.key === 'ArrowLeft' ? -1 : 1); }
  };
  document.addEventListener('keydown', tasten);

  const istAktuell = kontext.aktuell();
  // eigene Abwesenheiten (graue Fläche hinter den Stunden) liefert der Server nur im eigenen Plan; bei mehreren Kindern das gewählte
  const plan = await holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${datum}${istMein && meinKind ? `&schueler=${meinKind}` : ''}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  let gewaehlt = Math.max(0, plan.tage.findIndex(t => t.datum === datum));

  const zeichnen = (menueOffen = false) => {
    inhalt.innerHTML = `
      <div class="u-plantitel"><h2>${e(TYPEN[typ])} ${e(plan.element)}${langname(plan) ? ` – ${e(langname(plan))}` : ''}</h2>
        <span class="text-body-secondary small ms-2">${wochenText(mo)}</span></div>
      ${tagAuswahl(plan, gewaehlt)}
      ${wochenGitter(plan, { gewaehlterTag: gewaehlt, kartenZeiten: einst.zeiten, aufsichten: einst.aufsichten })}
      ${legendeHtml(aus, einst)}`;
    inhalt.querySelectorAll('[data-tagwahl]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.tagwahl); zeichnen(); });
    kartenBinden(inhalt, plan.karten, kontext, typ);
    legendeBinden(inhalt, aus, einst, () => zeichnen(true));
    if (menueOffen) inhalt.querySelector('[data-legende-menue]')?.classList.remove('d-none');
  };
  zeichnen();
  // Link direkt zur Stunde (#/plan/<typ>/<element>/<datum>/<stunde>, z. B. aus der Erinnerung an offene Stunden): Stundendetails öffnen
  const nr = Number(stundeParam);
  const stundeKarte = nr > 0 ? plan.karten.find(x => x.datum === datum && x.von <= nr && x.bis >= nr && x.unterricht && !x.ereignis) : null;
  if (stundeKarte) karteDialog(stundeKarte, { ...kontext, liste: plan.karten, typ });
  // Jetzt-Linie minütlich nachführen
  const takt = setInterval(() => { if (!document.body.contains(inhalt)) { clearInterval(takt); return; } if (!document.querySelector('.modal.show') && inhalt.querySelector('[data-legende-menue].d-none')) zeichnen(); }, 60000);
}
