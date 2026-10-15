// Stundendetails wie im Original (Screenshots „Klassenbuch“ und „Details“):
// Kopf „Klassen | Unterricht | Fach“, darunter „Wochentag | Datum | Zeit | Raum | Lehrkraft“, Reiter Klassenbuch und Details.
// Klassenbuch: Schülerkacheln (Foto/Platzhalter, Statuspunkt), Alle auswählen/Auswahl löschen, abwesend/Klassenbucheintrag,
// Lehrstoff mit Stift, Abwesende Schüler*innen als alte Liste (grau gestreift) mit Verlängern, Hausaufgaben (Liste mit Fälligkeit),
// Klassenbucheinträge mit „+“, Klassendienste mit Besen, „Ticket erstellen“, unten die Statusleiste
// („Lehrstoffeintragung vorhanden“, „Abwesenheiten wurden kontrolliert“).
// Details: nur „Findet online statt“, Lehrstoff und Vertretungstext (wenn vorhanden), Hausaufgaben, Notizen für Schüler*innen
// und für Lehrkräfte; unten „Vorherige Stunde“, „Nächste Stunde“, „Prüfung erstellen“ und „Raum buchen“.
import { e, heuteIso, holen, senden, toast, WOCHENTAGE } from './ui.js';

const PRUEFUNGSARTEN = ['Klassenarbeit', 'Klausur', 'Test', 'Abschlussprüfung', 'Präsentation', 'Referat', 'Vergleichsarbeit'];
const W = '/img/wu/';

const wochentag = iso => WOCHENTAGE[(new Date(iso + 'T12:00').getDay() + 6) % 7];
const datumDe = iso => { if (!iso) return ''; const [j, m, t] = iso.split('-'); return `${t}.${m}.${j}`; };
const kuerzel = l => (l || []).map(w => w.k || w.alt).filter(Boolean);
const plusTage = (iso, n) => { const d = new Date(iso + 'T12:00'); d.setDate(d.getDate() + n); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
const bildIcon = (datei, klasse = '') => `<img class="kb-icon ${klasse}" src="${W}${datei}" alt="" aria-hidden="true">`;
const maskeIcon = datei => `<span class="u-sym wu kb-icon" style="--wu:url(${W}${datei})" aria-hidden="true"></span>`;

/** Wert wie im Original: geänderter Wert „alt → neu“, offener Wert durchgestrichen. */
function wertKopf(liste, max = 6) {
  const l = liste || [];
  const teil = l.length > max + 1 ? l.slice(0, max - 2) : l;
  return teil.map(w => w.alt && !w.k ? `<span class="lbl"><s>${e(w.alt)}</s></span>`
    : w.alt ? `<s class="text-body-tertiary">${e(w.alt)}</s> → <span class="lbl">${e(w.k)}</span>` : e(w.k)).join(', ')
    + (teil.length < l.length ? ` +${l.length - teil.length}` : '');
}

/** Öffnet die Stundendetails. kontext.liste = alle Karten des Plans (für Vorherige/Nächste Stunde), kontext.typ = Planart. */
export function karteDialog(karte, kontext = {}) {
  const ich = kontext.ich ?? {};
  const kollegium = !!ich.kollegium;
  const liste = (kontext.liste ?? [karte]).filter(x => !x.ereignis).slice()
    .sort((a, b) => a.datum.localeCompare(b.datum) || a.von - b.von);

  const el = document.createElement('div');
  el.className = 'modal fade stundendetails';
  el.tabIndex = -1;
  el.innerHTML = `<div class="modal-dialog modal-xl modal-fullscreen-lg-down modal-dialog-scrollable"><div class="modal-content"></div></div>`;
  document.body.append(el);
  // wie im Original: Fläche rechts der Navigation, Navigation bleibt sichtbar
  const modal = new bootstrap.Modal(el, { backdrop: false });
  document.body.classList.toggle('leiste-zu', !!document.querySelector('.u-app.zu'));
  const box = el.querySelector('.modal-content');
  el.addEventListener('hidden.bs.modal', () => setTimeout(() => { modal.dispose(); el.remove(); }));
  let geaendert = false;
  el.addEventListener('hide.bs.modal', () => { if (geaendert) setTimeout(() => kontext.nachSpeichern ? kontext.nachSpeichern() : kontext.navigieren?.(location.hash)); });

  let k = karte;
  let reiter = null;
  let daten = null;
  let zeiten = [];
  const auswahl = new Set();
  const abwAuswahl = new Set();
  let offen = null;          // geöffneter Editor: 'thema', 'hausaufgaben', 'vermerk', 'ticket', 'abw:<id>'
  let hwBearbeiten = null;   // Id der bearbeiteten Hausaufgabe
  let sortierung = 'name';

  const istEntfall = () => k.status === 'entfall' || (k.status === 'abwesend' && kontext.typ !== 'lehrer');
  const mitKlassenbuch = () => kollegium && k.unterricht && !['entfall', 'abwesend', 'vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
  const darfPruefung = () => kollegium && k.unterricht && k.klassen.length && k.datum >= heuteIso() && !['entfall', 'abwesend', 'veranstaltung', 'vorgemerkt'].includes(k.status);
  const darfRaum = () => darfPruefung() && (daten?.darf || ich.admin);
  const klassenListe = () => kuerzel(k.klassen);
  // gekoppelte Karten mit unterschiedlichen Fächern teilen sich den Unterricht: Klassenbuch der Lehrkraft dieser Karte
  const einzigeLehrkraft = () => { const l = kuerzel(k.lehrer); return l.length === 1 && !(k.lehrer[0].alt && !k.lehrer[0].k) ? `&lehrer=${encodeURIComponent(l[0])}` : ''; };

  async function laden() {
    daten = { eintrag: null, darf: false, anwesenheit: null, vermerke: [], arten: [], befreit: new Set(), hausaufgaben: { liste: [], faellig: [], darf: false }, dienste: [], gruende: [] };
    if (!zeiten.length) try { zeiten = (await kontext.elemente?.())?.stunden ?? []; } catch { /* ohne Zeiten */ }
    if (!k.unterricht || ['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status)) return;
    const kl = klassenListe();
    const [st, hw] = await Promise.all([
      holen(`/api/app/klassenbuch/stunde?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}${einzigeLehrkraft()}`).catch(() => null),
      holen(`/api/app/klassenbuch/hausaufgabenliste?datum=${k.datum}&stunde=${k.von}&bis=${k.bis}&unterricht=${k.unterricht}&fach=${encodeURIComponent(k.fach || '')}&klassen=${encodeURIComponent(kl.join(','))}`).catch(() => null),
    ]);
    if (st) { daten.eintrag = st.eintrag; daten.darf = st.darf; }
    if (hw) daten.hausaufgaben = hw;
    if (mitKlassenbuch()) {
      const [anw, verm, dienste, gruende, befreit] = await Promise.all([
        holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`).catch(() => null),
        Promise.all(kl.map(x => holen(`/api/app/klassenbuch/vermerke?klasse=${encodeURIComponent(x)}&von=${plusTage(k.datum, -365)}&bis=${k.datum}`).catch(() => null))),
        Promise.all(kl.map(x => holen(`/api/app/klassendienste?klasse=${encodeURIComponent(x)}&von=${k.datum}&bis=${k.datum}`).catch(() => null))),
        holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => []),
        holen(`/api/app/klassenbuch/befreit?datum=${k.datum}&klassen=${encodeURIComponent(kl.join(','))}&fach=${encodeURIComponent(k.fach || '')}&beginn=${encodeURIComponent(k.beginn || '')}`).catch(() => []),
      ]);
      daten.anwesenheit = anw;
      daten.vermerke = verm.filter(Boolean).flatMap(v => v.vermerke ?? []).sort((a, b) => b.datum.localeCompare(a.datum) || b.id - a.id);
      daten.arten = verm.find(Boolean)?.arten ?? [];
      daten.dienste = dienste.filter(Boolean).flatMap(d => d.zuordnungen ?? []);
      daten.gruende = (gruende ?? []).filter(g => g.aktiv !== false);
      daten.befreit = new Set(befreit ?? []);
    }
  }

  // ---------------------------------------------------------------- Kopf, Fuß, Aufbau

  function kopf() {
    const titel = [k.klassen.length && k.klassen.length < 8 ? k.klassen.map(x => x.k || x.alt).join(', ') : '', k.fachName && k.fachName !== k.fach ? k.fachName : '', k.fach || k.text || k.art || 'Stunde']
      .filter(Boolean);
    const zeile = [wochentag(k.datum), datumDe(k.datum), k.beginn ? `${k.beginn}-${k.ende}` : `${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'} Std.`,
      wertKopf(k.raeume), wertKopf(k.lehrer)].filter(Boolean);
    const badge = k.status === 'klausur' ? '<span class="sd-badge">PRÜFUNG</span>' : '';
    const leiste = istEntfall() ? 'ENTFÄLLT' : k.status === 'abwesend' ? 'VERTRETEN' : k.status === 'offen' ? 'VERTRETUNG OFFEN' : '';
    const reiterListe = reiter === 'pruefung' ? [['pruefung', 'Prüfung erstellen']] : reiter === 'raum' ? [['raum', 'Raum buchen']]
      : [...(mitKlassenbuch() ? [['klassenbuch', 'Klassenbuch']] : []), ['details', 'Details']];
    return `<div class="sd-kopf ${leiste ? 'grau' : ''}">
        <div class="sd-titel"><h2>${titel.map(e).join('<span class="trenn">|</span>')}${badge}</h2>
          <div class="sd-zeile">${zeile.join('<span class="trenn">|</span>')}</div></div>
        <div class="sd-knoepfe"><button type="button" data-bs-dismiss="modal" title="Zur Ansicht zurück" aria-label="Zur Ansicht zurück"><i class="bi bi-chevron-down"></i></button>
          <button type="button" data-bs-dismiss="modal" title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg"></i></button></div>
      </div>
      ${leiste ? `<div class="sd-leiste ${k.status === 'abwesend' && !istEntfall() ? 'grau' : ''}">${leiste}</div>` : ''}
      <ul class="nav sd-reiter">${reiterListe.map(([id, t]) => `<li><button type="button" data-reiter="${id}" class="${reiter === id ? 'aktiv' : ''}">${t}</button></li>`).join('')}</ul>`;
  }

  function fuss() {
    if (reiter === 'pruefung') return `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-speichern-pruefung>Speichern</button></div>`;
    if (reiter === 'raum') return '<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button></div>';
    if (reiter === 'klassenbuch') {
      const a = daten.anwesenheit;
      const stoff = !!daten.eintrag?.thema;
      const kontrolliert = !!a?.geprueft;
      const kannKontrolle = a?.darf && k.datum <= heuteIso();
      return `<div class="sd-fuss sd-status">
        ${stoff ? '<span class="ok"><i class="bi bi-check-circle-fill"></i> Lehrstoffeintragung vorhanden</span>'
          : daten.darf ? '<button type="button" class="sd-statusknopf" data-bearbeiten="thema">Lehrstoff eingeben</button>' : '<span><i class="bi bi-circle"></i> Keine Lehrstoffeintragung</span>'}
        ${kontrolliert ? '<span class="ok"><i class="bi bi-check-circle-fill"></i> Abwesenheiten wurden kontrolliert</span>'
          : kannKontrolle ? '<button type="button" class="sd-statusknopf" data-kontrolle>Abwesenheiten kontrollieren</button>' : '<span><i class="bi bi-circle"></i> Abwesenheiten nicht kontrolliert</span>'}
      </div>`;
    }
    const i = liste.indexOf(k);
    return `<div class="sd-fuss">
      <button type="button" class="sd-pille gross" data-blaettern="-1" ${i <= 0 ? 'disabled' : ''}><i class="bi bi-chevron-left"></i> Vorherige Stunde</button>
      <button type="button" class="sd-pille gross" data-blaettern="1" ${i < 0 || i >= liste.length - 1 ? 'disabled' : ''}>Nächste Stunde <i class="bi bi-chevron-right"></i></button>
      ${darfPruefung() ? '<button type="button" class="sd-pille gross" data-pruefung>Prüfung erstellen</button>' : ''}
      ${darfRaum() ? '<button type="button" class="sd-pille gross" data-raumbuchen>Raum buchen</button>' : ''}</div>`;
  }

  function zeichnen() {
    const scroll = box.querySelector('.sd-rumpf')?.scrollTop ?? 0;
    box.innerHTML = `<div class="modal-header p-0 d-block border-0">${kopf()}</div>
      <div class="modal-body sd-rumpf">${reiter === 'klassenbuch' ? klassenbuchHtml() : reiter === 'pruefung' ? pruefungHtml() : reiter === 'raum' ? '<div data-raumwahl></div>' : detailsHtml()}</div>
      <div class="modal-footer p-0 d-block">${fuss()}</div>`;
    box.querySelector('.sd-rumpf').scrollTop = scroll;
    binden();
    if (reiter === 'raum') raumWahl(box.querySelector('[data-raumwahl]'));
    // Fokus im Dialog halten, damit Esc weiter schließt
    if (!el.contains(document.activeElement)) el.focus();
  }

  // ---------------------------------------------------------------- Klassenbuch

  const fehlzeitVon = id => (daten.anwesenheit?.fehlzeiten ?? []).find(f => f.schuelerId === id);
  const schueler = () => {
    const l = (daten.anwesenheit?.schueler ?? []).slice();
    return sortierung === 'vorname' ? l.sort((a, b) => a.vorname.localeCompare(b.vorname, 'de') || a.name.localeCompare(b.name, 'de')) : l;
  };
  const schuelerName = id => { const s = daten.anwesenheit?.schueler.find(x => x.id === id); return s ? `${s.name} ${s.vorname}` : ''; };
  const zeitVon = nr => zeiten.find(z => z.nr === nr) ?? {};

  function abschnitt(icon, titel, zahl, rechts, inhalt, klasse = '') {
    return `<section class="sd-abschnitt ${klasse}">
      <div class="sd-abschnitt-kopf">${icon}<span>${titel}</span>${zahl !== null ? `<span class="sd-zahl">${zahl}</span>` : ''}${rechts ? `<span class="ms-auto d-flex gap-3 align-items-center">${rechts}</span>` : ''}</div>
      <div class="sd-abschnitt-rumpf">${inhalt}</div></section>`;
  }

  function klassenbuchHtml() {
    const a = daten.anwesenheit;
    const darf = !!a?.darf;
    const sl = schueler();
    const kacheln = sl.map(s => {
      const f = fehlzeitVon(s.id);
      const st = !f ? 'da' : f.art === 'verspaetet' ? 'spaet' : 'weg';
      const befreit = daten.befreit?.has(s.id);
      const status = st === 'da' ? bildIcon('exact/status_present.svg', 'sd-statusbild') : st === 'weg' ? bildIcon('exact/status_absent.svg', 'sd-statusbild') : '<span class="sd-statusbild spaet"><i class="bi bi-clock-fill"></i></span>';
      return `<div class="sd-kachel ${st} ${befreit && st === 'da' ? 'befreit' : ''} ${auswahl.has(s.id) ? 'gewaehlt' : ''}" data-sid="${s.id}" role="button" tabindex="0" aria-pressed="${auswahl.has(s.id)}" title="${befreit ? 'vom Unterricht befreit' : st === 'weg' ? 'abwesend' : st === 'spaet' ? `verspätet (${f.minuten} min)` : 'anwesend'}">
        ${status}
        <span class="sd-avatar"><img src="${W}avatar-default-white.svg" alt=""></span>
        <span class="sd-name"><span>${e(s.name)}</span><span>${e(s.vorname)}</span></span>
        ${darf ? `<button type="button" class="sd-umschalten" data-umschalten="${s.id}" title="${st === 'da' ? 'Als abwesend markieren' : 'Als anwesend markieren'}" aria-label="${st === 'da' ? 'Als abwesend markieren' : 'Als anwesend markieren'}">${st === 'da' ? '<i class="bi bi-x-circle"></i>' : '<i class="bi bi-check-circle"></i>'}</button>` : ''}
      </div>`;
    }).join('');
    const werkzeuge = `<button type="button" class="sd-werkzeug" data-werkzeug="mail" title="Mitteilung schreiben" aria-label="Mitteilung schreiben"><i class="bi bi-envelope"></i></button>
      <button type="button" class="sd-werkzeug" data-werkzeug="druck" title="Drucken" aria-label="Drucken"><i class="bi bi-printer"></i></button>
      <button type="button" class="sd-werkzeug" data-werkzeug="einst" title="Sortierung: ${sortierung === 'name' ? 'Nachname' : 'Vorname'}" aria-label="Sortierung wechseln"><i class="bi bi-gear"></i></button>`;
    const schuelerTeil = !a ? '<p class="small text-body-secondary">Anwesenheit kann nicht geladen werden.</p>'
      : !sl.length ? '<p class="small text-body-secondary mb-0">Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen (UntPlanWeb → Einstellungen → „Schülerliste übertragen“).</p>'
      : `${darf ? '<div class="d-flex flex-wrap gap-2 mb-3"><button type="button" class="sd-pille klein" data-alle>Alle auswählen</button><button type="button" class="sd-pille klein" data-keine>Auswahl löschen</button></div>' : ''}
        <div class="sd-kacheln">${kacheln}</div>
        ${darf ? `<div class="d-flex flex-wrap gap-2 mt-3">
          <button type="button" class="sd-pille klein grau" data-setzen="fehlt" ${auswahl.size ? '' : 'disabled'}>abwesend</button>
          <button type="button" class="sd-pille klein grau" data-setzen="verspaetet" ${auswahl.size ? '' : 'disabled'}>verspätet</button>
          <button type="button" class="sd-pille klein grau" data-vermerk-auswahl ${auswahl.size ? '' : 'disabled'}>Klassenbucheintrag</button></div>
          ${offen === 'vermerk-auswahl' ? vermerkFormular(true) : ''}` : ''}`;
    return `
      ${abschnitt(bildIcon('klassenbuch/multiple-man-woman-2.svg'), 'Schüler*innen im Unterricht', sl.length, a ? werkzeuge : '', schuelerTeil, 'kb-schueler')}
      ${lehrstoffHtml()}
      ${abwesendeHtml()}
      ${hausaufgabenHtml()}
      ${vermerkeHtml()}
      ${diensteHtml()}
      <div class="sd-abschnitt">${offen === 'ticket' ? ticketFormular() : '<button type="button" class="sd-pille klein" data-ticket>Ticket erstellen</button>'}</div>`;
  }

  function lehrstoffHtml() {
    const wert = daten.eintrag?.thema;
    const stift = daten.darf && offen !== 'thema' ? '<button type="button" class="sd-stift" data-bearbeiten="thema" title="Lehrstoff bearbeiten" aria-label="Lehrstoff bearbeiten"><i class="bi bi-pencil"></i></button>' : '';
    const inhalt = offen === 'thema'
      ? `<textarea class="form-control mb-2" rows="3" data-feldwert>${e(wert)}</textarea>
         <div class="d-flex gap-2"><button type="button" class="sd-pille klein primaer" data-feld-speichern="thema">Speichern</button><button type="button" class="sd-pille klein" data-feld-abbrechen>Abbrechen</button></div>`
      : wert ? `<p class="sd-text">${e(wert)}</p>` : '<p class="sd-text text-body-tertiary">Kein Lehrstoff eingetragen</p>';
    return abschnitt(bildIcon('klassenbuch/common-file-text.svg'), 'Lehrstoff', null, stift, inhalt);
  }

  function abwesendeHtml() {
    const a = daten.anwesenheit;
    if (!a) return '';
    const sl = a.schueler ?? [];
    const name = id => { const s = sl.find(x => x.id === id); return s ? `${s.name} ${s.vorname}` : ''; };
    const grundText = g => { const x = daten.gruende.find(y => y.kurz === g); return x ? x.kurz : g; };
    const vonStunde = zeitVon(k.von), bisStunde = zeitVon(k.bis);
    const fruehere = zeiten.some(z => z.nr < k.von);
    const zeilen = [
      ...(a.fehlzeiten ?? []).map(f => ({ f, vor: false })),
      ...(a.vorstunden ?? []).map(f => ({ f, vor: true })),
    ];
    const reihe = ({ f, vor }) => {
      const sv = vor ? zeitVon(f.stunde) : vonStunde;
      const beginn = f.art === 'verspaetet' && !vor ? (k.beginn || sv.beginn || '') : (sv.beginn || k.beginn || '');
      const ende = vor ? (zeitVon(f.stunde).ende || '') : (bisStunde.ende || k.ende || '');
      const anfang = !vor && f.art === 'fehlt' && fruehere && a.darf
        ? `<button type="button" class="kb-zeitknopf" data-tagesbeginn="${f.schuelerId}" title="Abwesenheit auf Tagesbeginn verlängern"><img src="${W}legacy/absence-start.png" alt=""></button>` : '';
      const schluss = vor && a.darf
        ? `<button type="button" class="kb-zeitknopf" data-bisende="${f.schuelerId}" title="Abwesenheit bis Stundenende verlängern"><img src="${W}legacy/absence-end.png" alt=""></button>` : '';
      const status = vor ? 'Vorstunde – verlängern' : f.art === 'verspaetet' ? `verspätet ${f.minuten} min` : ({ offen: 'offen', entschuldigt: 'entschuldigt', unentschuldigt: 'unentschuldigt' }[f.status] ?? '');
      return `<tr class="${vor ? 'vorstunde' : ''}">
        <td class="mitte"><input type="checkbox" data-abw="${f.schuelerId}" ${abwAuswahl.has(f.schuelerId) ? 'checked' : ''} aria-label="${e(name(f.schuelerId))} auswählen"></td>
        <td class="mitte">${a.darf && !vor ? `<button type="button" class="kb-stift" data-abw-bearbeiten="${f.schuelerId}" title="Abwesenheit bearbeiten"><img src="${W}legacy/absence-edit.png" alt="Bearbeiten"></button>` : ''}</td>
        <td>${e(name(f.schuelerId))}</td><td>${datumDe(f.datum || k.datum)}</td><td class="zeit">${anfang}${e(beginn)}</td><td>${datumDe(f.datum || k.datum)}</td><td class="zeit">${e(ende)}${schluss}</td>
        <td>${e(grundText(f.grund))}</td><td>${e(status)}</td><td class="text">${e(f.bemerkung)}</td></tr>
        ${offen === 'abw:' + f.schuelerId ? `<tr class="editor"><td colspan="10">${abwEditor(f)}</td></tr>` : ''}`;
    };
    const tabelle = zeilen.length ? `<div class="table-responsive"><table class="alt-liste kb-abwesend">
        <thead><tr><th class="mitte">Auswahl<br><input type="checkbox" data-abw-alle aria-label="Alle auswählen" ${abwAuswahl.size && abwAuswahl.size === zeilen.length ? 'checked' : ''}></th><th></th><th>Schüler*innen</th><th>Von</th><th>Beginnzeit</th><th>Bis</th><th>Endzeit</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th></tr></thead>
        <tbody>${zeilen.map(reihe).join('')}</tbody></table></div>
        ${a.darf ? `<button type="button" class="sd-pille klein grau mt-3" data-verlaengern ${abwAuswahl.size ? '' : 'disabled'}>Verlängern</button>` : ''}`
      : '<p class="sd-text small">Keine Abwesenheiten vorhanden</p>';
    return abschnitt(bildIcon('klassenbuch/chair-1.svg'), 'Abwesende Schüler*innen', (a.fehlzeiten ?? []).length, '', tabelle);
  }

  function abwEditor(f) {
    return `<div class="kb-editor"><div class="kb-editor-titel">Abwesenheit – ${e(schuelerName(f.schuelerId))}</div>
      <div class="kb-formular">
        <label>Abwesenheitsgrund</label><select class="form-select form-select-sm" data-abw-grund><option value="">fehlt</option>${daten.gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === f.grund ? 'selected' : ''}>${e(g.kurz)} – ${e(g.name)}</option>`).join('')}</select>
        <label>Text</label><input class="form-control form-control-sm" data-abw-text value="${e(f.bemerkung)}">
      </div>
      <div class="d-flex gap-2 mt-2"><button type="button" class="sd-pille klein primaer" data-abw-speichern="${f.schuelerId}">Speichern</button>
        <button type="button" class="sd-pille klein" data-abw-loeschen="${f.schuelerId}">Löschen</button><button type="button" class="sd-pille klein" data-zu>Abbrechen</button></div></div>`;
  }

  function hausaufgabeZeile(h, bearbeitbar) {
    return `<div class="kb-hw ${h.erledigt ? 'erledigt' : ''}">${h.faellig ? `<span class="faellig">bis ${datumDe(h.faellig)}</span>` : ''}<span class="text">${e(h.text)}</span>${h.bemerkung ? `<small>${e(h.bemerkung)}</small>` : ''}
      ${bearbeitbar ? `<span class="kb-hw-knoepfe"><button type="button" data-hw-bearbeiten="${h.id}" title="Bearbeiten"><i class="bi bi-pencil"></i></button>
        <button type="button" data-hw-erledigt="${h.id}" title="${h.erledigt ? 'Als offen markieren' : 'Erledigt'}"><i class="bi ${h.erledigt ? 'bi-check-circle-fill' : 'bi-check-circle'}"></i></button>
        <button type="button" data-hw-loeschen="${h.id}" title="Löschen"><i class="bi bi-trash"></i></button></span>` : ''}</div>`;
  }

  function hausaufgabeFormular(h) {
    const neu = !h;
    return `<div class="kb-editor" data-hw-formular="${neu ? 'neu' : h.id}"><div class="kb-editor-titel">${neu ? '<i class="bi bi-plus-circle"></i> Neue Hausaufgabe' : 'Hausaufgabe bearbeiten'}</div>
      <div class="kb-formular">
        <label>Fälligkeitsdatum</label><input type="date" class="form-control form-control-sm" style="max-width:180px" data-hw-faellig value="${e(h?.faellig ?? plusTage(k.datum, 7))}" min="${k.datum}">
        <label>Hausaufgaben</label><textarea class="form-control form-control-sm" rows="3" data-hw-text>${e(h?.text ?? '')}</textarea>
        <label>Bemerkung</label><input class="form-control form-control-sm" data-hw-bemerkung value="${e(h?.bemerkung ?? '')}">
        <label>Anhang</label><span class="small text-body-secondary pt-1">Nichts anzuzeigen</span>
      </div>
      <div class="d-flex gap-2 mt-2"><button type="button" class="sd-pille klein primaer" data-hw-speichern>${neu ? 'Erstellen' : 'Speichern'}</button><button type="button" class="sd-pille klein" data-hw-abbrechen>Abbrechen</button></div></div>`;
  }

  function hausaufgabenHtml() {
    const hw = daten.hausaufgaben;
    const darf = hw.darf;
    const stift = darf && offen !== 'hausaufgaben' ? '<button type="button" class="sd-stift" data-bearbeiten="hausaufgaben" title="Hausaufgaben bearbeiten" aria-label="Hausaufgaben bearbeiten"><i class="bi bi-pencil"></i></button>' : '';
    const liste = hw.liste.length ? hw.liste.map(h => hwBearbeiten === h.id ? hausaufgabeFormular(h) : hausaufgabeZeile(h, offen === 'hausaufgaben')).join('') : '<p class="sd-text small">Keine Hausaufgaben vorhanden</p>';
    const faellig = hw.faellig.length ? `<div class="kb-hw-faellig"><div class="small fw-semibold mb-1">Fällige Hausaufgaben</div>${hw.faellig.map(h => hausaufgabeZeile(h, false)).join('')}</div>` : '';
    const neu = offen === 'hausaufgaben' ? (hwBearbeiten === 'neu' ? hausaufgabeFormular(null) : '<div class="d-flex gap-2 mt-2"><button type="button" class="sd-pille klein" data-hw-neu><i class="bi bi-plus-lg"></i> Neue Hausaufgabe</button><button type="button" class="sd-pille klein" data-zu>Schließen</button></div>') : '';
    return abschnitt(bildIcon('klassenbuch/common-file-text-home-kopie.svg'), 'Hausaufgaben', null, stift, liste + neu + faellig);
  }

  function vermerkFormular(fuerAuswahl) {
    const arten = daten.arten?.length ? daten.arten : ['Vermerk'];
    return `<div class="kb-editor mt-2"><div class="kb-editor-titel">Klassenbucheintrag ${fuerAuswahl ? `für ${auswahl.size} ${auswahl.size === 1 ? 'Schüler*in' : 'Schüler*innen'}` : 'für die Klasse'}</div>
      <div class="d-flex flex-wrap gap-2"><select class="form-select form-select-sm" style="width:auto" data-vart>${arten.map(a => `<option>${e(a)}</option>`).join('')}</select>
        <input class="form-control form-control-sm flex-grow-1" style="min-width:220px" data-vtext placeholder="Text">
        <button type="button" class="sd-pille klein primaer" data-vok="${fuerAuswahl ? 'auswahl' : 'klasse'}">Speichern</button><button type="button" class="sd-pille klein" data-zu>Abbrechen</button></div></div>`;
  }

  function vermerkeHtml() {
    const v = daten.vermerke;
    const plus = kollegium && offen !== 'vermerk' ? '<button type="button" class="sd-plus" data-vermerk-neu title="Klassenbucheintrag erfassen" aria-label="Klassenbucheintrag erfassen"><i class="bi bi-plus-lg"></i></button>' : '';
    const kurz = (t, n) => t.length > n ? t.slice(0, n - 1) + '…' : t;
    const tabelle = v.length ? `<div class="table-responsive"><table class="alt-liste kb-eintraege"><tbody>${v.map(x => `<tr title="${e(x.text)}">
        <td>${e(x.schuelerId ? schuelerName(x.schuelerId) || '–' : 'Klasse ' + x.klasse)}</td><td>${datumDe(x.datum)}</td><td>${e((x.erstellt || '').slice(11, 16))}</td>
        <td>${e((x.art || '').slice(0, 3))}</td><td class="text">${e(kurz(x.text, 30))}</td><td>${e(x.lehrer)}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="sd-text small">Keine Klassenbucheinträge vorhanden</p>';
    return abschnitt(maskeIcon('class_register_entries.svg'), 'Klassenbucheinträge', v.length, plus, (offen === 'vermerk' ? vermerkFormular(false) : '') + tabelle);
  }

  function diensteHtml() {
    const d = daten.dienste;
    const inhalt = d.length ? `<table class="alt-liste kb-dienste"><tbody>${d.map(x => `<tr title="${e(x.dienst)}${x.text ? ' · ' + e(x.text) : ''}">
        <td class="mitte"><img src="${W}legacy/duty-broom.png" alt=""></td><td>${e(x.dienstKurz || x.dienst)}</td><td>${e(x.schueler)}</td></tr>`).join('')}</tbody></table>`
      : '<p class="sd-text small">Nichts anzuzeigen</p>';
    return abschnitt(maskeIcon('class_services.svg'), 'Klassendienste', d.length, '', inhalt);
  }

  function ticketFormular() {
    return `<div class="kb-editor"><div class="kb-editor-titel">Ticket erstellen</div>
      <p class="small text-body-secondary mb-2">Das Ticket geht als Mitteilung an Schulleitung und Administration.</p>
      <textarea class="form-control form-control-sm mb-2" rows="3" data-ticket-text placeholder="Was soll erledigt werden?"></textarea>
      <div class="d-flex gap-2"><button type="button" class="sd-pille klein primaer" data-ticket-senden>Senden</button><button type="button" class="sd-pille klein" data-zu>Abbrechen</button></div></div>`;
  }

  // ---------------------------------------------------------------- Details (nur das Nötige wie im Original)

  function detailsHtml() {
    const ein = daten.eintrag;
    const reihe = (ic, titel, inhalt) => `<div class="sd-detail">${ic}<div class="flex-grow-1"><div class="sd-detail-titel">${titel}</div>${inhalt}</div></div>`;
    const datei = bildIcon('klassenbuch/common-file-text.svg');
    const teile = [];
    const unterricht = k.unterricht && !['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
    if (unterricht && kollegium && !istEntfall())
      teile.push(`<div class="sd-detail">${bildIcon('klassenbuch/online_lesson.svg')}<div class="form-check form-switch mb-0 d-flex align-items-center gap-2 ps-0">
          <label class="sd-detail-titel mb-0" for="sd-online">Findet online statt</label><input class="form-check-input ms-1 mt-0" type="checkbox" role="switch" id="sd-online" data-online ${ein?.online ? 'checked' : ''} ${daten.darf ? '' : 'disabled'}></div></div>`);
    const vertretung = k.text && (k.fach || k.status !== 'veranstaltung') ? k.text : '';
    if (vertretung && k.status !== 'klausur') teile.push(reihe(datei, k.fach ? 'Vertretungstext' : 'Bezeichnung', `<p class="sd-text">${e(vertretung)}</p>`));
    if (k.status === 'klausur') teile.push(reihe(datei, 'Prüfung', `<p class="sd-text">${e([k.art, k.text].filter(Boolean).join(' – '))}</p>`));
    if (k.status !== 'regulaer' && k.art && !vertretung && k.status !== 'klausur') teile.push(reihe(datei, 'Vertretungsart', `<p class="sd-text">${e(k.art)}</p>`));
    if (ein?.thema) teile.push(reihe(datei, 'Lehrstoff', `<p class="sd-text">${e(ein.thema)}</p>`));
    const hw = daten.hausaufgaben;
    if (hw.faellig.length) teile.push(reihe(bildIcon('exact/homework.svg'), 'Fällige Hausaufgaben', hw.faellig.map(h => hausaufgabeZeile(h, false)).join('')));
    if (hw.liste.length) teile.push(reihe(bildIcon('exact/homework.svg'), 'Hausaufgaben', hw.liste.map(h => hausaufgabeZeile(h, false)).join('')));
    if (unterricht) {
      teile.push(notizFeld('notizSchueler', 'Notizen für Schüler*innen', ein?.notizSchueler ?? k.notizSchueler));
      if (kollegium) teile.push(notizFeld('notiz', 'Notizen für Lehrkräfte', ein?.notiz ?? k.notizLehrer));
    }
    const html = teile.filter(Boolean).join('');
    return html || '<div class="sd-leer"><b>Keine weiteren Details</b><span>Zu dieser Stunde gibt es keine zusätzlichen Informationen.</span></div>';
  }

  function notizFeld(feld, titel, wert) {
    const ic = bildIcon('klassenbuch/common-file-text.svg');
    if (!daten.darf) return wert ? `<div class="sd-detail">${ic}<div><div class="sd-detail-titel">${titel}</div><p class="sd-text">${e(wert)}</p></div></div>` : '';
    return `<div class="sd-detail">${ic}<div class="flex-grow-1"><div class="sd-detail-titel">${titel}</div>
      <textarea class="form-control sd-notiz" rows="2" data-notiz="${feld}">${e(wert)}</textarea>
      <div class="sd-notiz-knoepfe d-none" data-notiz-knoepfe="${feld}"><button type="button" class="sd-pille klein" data-notiz-abbrechen="${feld}">Abbrechen</button><button type="button" class="sd-pille klein primaer" data-notiz-speichern="${feld}">Speichern</button></div></div></div>`;
  }

  // ---------------------------------------------------------------- Prüfung erstellen

  let pruefungSchueler = null;
  function pruefungHtml() {
    const sl = daten.anwesenheit?.schueler ?? [];
    if (!pruefungSchueler) pruefungSchueler = new Set(sl.map(s => s.id));
    const max = Math.max(k.bis + 3, 10);
    const std = (n, w) => `<select class="form-select" name="${n}">${Array.from({ length: max }, (_, i) => i + 1).map(x => `<option value="${x}" ${x === w ? 'selected' : ''}>${x}.</option>`).join('')}</select>`;
    return `<div class="sd-formular">
      <label>Name</label><input class="form-control" name="name" value="${e(k.fach || '')}">
      <label>Text</label><input class="form-control" name="text">
      <label>Prüfungsart</label><select class="form-select" name="art"><option value=""></option>${PRUEFUNGSARTEN.map(a => `<option>${a}</option>`).join('')}</select>
      <label>Datum</label><div class="d-flex flex-wrap gap-2 align-items-center"><input type="date" class="form-control" style="width:auto" name="datum" value="${k.datum}">
        <span class="small">Stunde</span><div style="width:90px">${std('von', k.von)}</div><span>–</span><div style="width:90px">${std('bis', k.bis)}</div></div>
      <label>Raum</label><input class="form-control" name="raeume" value="${e(kuerzel(k.raeume).join(', '))}">
      <label>Zugewiesene Lehrkraft</label><input class="form-control" name="aufsicht" value="${e(kuerzel(k.lehrer).join(', '))}">
      <label>Schüler*innen</label><div class="sd-chips">${sl.length ? sl.map(s => `<button type="button" class="sd-chip ${pruefungSchueler.has(s.id) ? 'an' : ''}" data-chip="${s.id}">${e(s.name)} ${e(s.vorname)}</button>`).join('')
        : `<span class="small text-body-secondary">Klasse(n) ${e(kuerzel(k.klassen).join(', '))}</span>`}</div>
    </div>`;
  }

  // ---------------------------------------------------------------- Speichern

  async function anwesenheitSpeichern(neu) {
    const eintraege = [...neu].map(([schuelerId, f]) => ({ schuelerId, art: f.art, minuten: f.minuten || 5, bemerkung: f.bemerkung ?? '', grund: f.grund ?? '' }));
    try {
      await senden('/api/app/klassenbuch/anwesenheit', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, eintraege }, 'PUT');
      await anwesenheitNeu();
      geaendert = true;
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  async function anwesenheitNeu() {
    daten.anwesenheit = await holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`);
  }

  const fzKarte = () => new Map((daten.anwesenheit?.fehlzeiten ?? []).map(f => [f.schuelerId, { art: f.art, minuten: f.minuten, bemerkung: f.bemerkung, grund: f.grund }]));

  async function eintragSpeichern(aenderung) {
    const alt = daten.eintrag ?? {};
    const neu = { thema: alt.thema ?? '', hausaufgabe: alt.hausaufgabe ?? '', faellig: alt.faellig ?? null, notiz: alt.notiz ?? '', notizSchueler: alt.notizSchueler ?? '', ...aenderung };
    try {
      daten.eintrag = await senden('/api/app/klassenbuch/stunde', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, ...neu }, 'PUT');
      geaendert = true;
      toast('Gespeichert.');
      return true;
    } catch (err) { toast(err.message, true); return false; }
  }

  async function hausaufgabenNeu() {
    daten.hausaufgaben = await holen(`/api/app/klassenbuch/hausaufgabenliste?datum=${k.datum}&stunde=${k.von}&bis=${k.bis}&unterricht=${k.unterricht}&fach=${encodeURIComponent(k.fach || '')}&klassen=${encodeURIComponent(klassenListe().join(','))}`);
    geaendert = true;
  }

  async function verlaengern(ids, bis) {
    try {
      await senden('/api/app/klassenbuch/abwesenheit/verlaengern', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, schuelerIds: ids, bis });
      await anwesenheitNeu();
      abwAuswahl.clear();
      geaendert = true;
      toast(bis === 'tagesbeginn' ? 'Abwesenheit auf Tagesbeginn verlängert.' : 'Abwesenheit bis Stundenende verlängert.');
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  // ---------------------------------------------------------------- Ereignisse

  function binden() {
    const q = s => box.querySelector(s);
    const alle = (s, f) => box.querySelectorAll(s).forEach(f);
    alle('[data-reiter]', b => b.onclick = () => { if (reiter === 'pruefung' || reiter === 'raum') return; reiter = b.dataset.reiter; offen = null; zeichnen(); });
    alle('[data-blaettern]', b => b.onclick = async () => {
      const x = liste[liste.indexOf(k) + Number(b.dataset.blaettern)];
      if (!x) return;
      k = x; auswahl.clear(); abwAuswahl.clear(); offen = null; hwBearbeiten = null; pruefungSchueler = null;
      box.querySelector('.sd-rumpf').innerHTML = '<div class="py-5 text-center"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
      await laden();
      if (reiter === 'klassenbuch' && !mitKlassenbuch()) reiter = 'details';
      zeichnen();
    });
    q('[data-pruefung]')?.addEventListener('click', async () => {
      if (!daten.anwesenheit && k.unterricht) daten.anwesenheit = await holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`).catch(() => null);
      reiter = 'pruefung'; zeichnen();
    });
    q('[data-raumbuchen]')?.addEventListener('click', () => { reiter = 'raum'; zeichnen(); });
    q('[data-abbrechen]')?.addEventListener('click', () => { reiter = 'details'; zeichnen(); });
    alle('[data-chip]', c => c.onclick = () => {
      const id = Number(c.dataset.chip);
      pruefungSchueler.has(id) ? pruefungSchueler.delete(id) : pruefungSchueler.add(id);
      c.classList.toggle('an');
    });
    q('[data-speichern-pruefung]')?.addEventListener('click', async ev => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      const listeVon = n => f(n).split(/[,;\s]+/).map(x => x.trim()).filter(Boolean);
      const sl = daten.anwesenheit?.schueler ?? [];
      const teil = sl.length && pruefungSchueler.size < sl.length
        ? `Teilnehmende: ${sl.filter(s => pruefungSchueler.has(s.id)).map(s => `${s.vorname} ${s.name}`).join(', ')}` : '';
      const text = [f('art') ? `${f('art')}${f('text') ? ': ' + f('text') : ''}` : f('text'), teil].filter(Boolean).join(' – ');
      const knopf = ev.currentTarget;
      knopf.disabled = true;
      try {
        await senden('/api/app/meldungen/klausur', { datum: f('datum'), stundeVon: Number(f('von')), stundeBis: Number(f('bis')), klassen: kuerzel(k.klassen), raeume: listeVon('raeume'), aufsicht: listeVon('aufsicht'), titel: f('name') || 'Prüfung', text });
        toast('Prüfung erstellt – sie steht jetzt im Stundenplan.');
        geaendert = true;
        modal.hide();
      } catch (err) { toast(err.message, true); knopf.disabled = false; }
    });

    // Schülerkacheln
    alle('[data-sid]', t => t.onclick = ev => {
      if (ev.target.closest('[data-umschalten]') || !daten.anwesenheit?.darf) return;
      const id = Number(t.dataset.sid);
      auswahl.has(id) ? auswahl.delete(id) : auswahl.add(id);
      zeichnen();
    });
    alle('[data-sid]', t => t.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); t.click(); } });
    alle('[data-umschalten]', b => b.onclick = () => {
      const id = Number(b.dataset.umschalten);
      const m = fzKarte();
      m.has(id) ? m.delete(id) : m.set(id, { art: 'fehlt', minuten: 0 });
      anwesenheitSpeichern(m);
    });
    q('[data-alle]')?.addEventListener('click', () => { (daten.anwesenheit?.schueler ?? []).forEach(s => auswahl.add(s.id)); zeichnen(); });
    q('[data-keine]')?.addEventListener('click', () => { auswahl.clear(); zeichnen(); });
    alle('[data-setzen]', b => b.onclick = () => {
      const m = fzKarte();
      let minuten = 5;
      if (b.dataset.setzen === 'verspaetet') {
        const w = prompt('Verspätung in Minuten', '5');
        if (w === null) return;
        minuten = Math.max(1, Math.min(90, Number(w) || 5));
      }
      for (const id of auswahl) m.set(id, { ...(m.get(id) ?? {}), art: b.dataset.setzen, minuten: b.dataset.setzen === 'verspaetet' ? minuten : 0 });
      auswahl.clear();
      anwesenheitSpeichern(m);
    });
    q('[data-kontrolle]')?.addEventListener('click', () => anwesenheitSpeichern(fzKarte()));
    alle('[data-werkzeug]', b => b.onclick = () => {
      if (b.dataset.werkzeug === 'druck') window.print();
      else if (b.dataset.werkzeug === 'mail') { modal.hide(); kontext.navigieren?.('#/mitteilungen'); }
      else { sortierung = sortierung === 'name' ? 'vorname' : 'name'; toast(`Sortiert nach ${sortierung === 'name' ? 'Nachname' : 'Vorname'}.`); zeichnen(); }
    });

    // Editoren schließen
    alle('[data-zu]', b => b.onclick = () => { offen = null; hwBearbeiten = null; zeichnen(); });

    // Lehrstoff, Hausaufgaben-Editor öffnen
    alle('[data-bearbeiten]', b => b.onclick = () => {
      offen = b.dataset.bearbeiten; hwBearbeiten = null;
      if (offen === 'thema' && reiter !== 'klassenbuch' && mitKlassenbuch()) reiter = 'klassenbuch';
      zeichnen();
      box.querySelector('[data-feldwert]')?.focus();
      box.querySelector(offen === 'thema' ? '[data-feldwert]' : '[data-hw-neu]')?.scrollIntoView({ block: 'center' });
    });
    q('[data-feld-abbrechen]')?.addEventListener('click', () => { offen = null; zeichnen(); });
    q('[data-feld-speichern]')?.addEventListener('click', async () => {
      if (await eintragSpeichern({ thema: box.querySelector('[data-feldwert]').value })) { offen = null; zeichnen(); }
    });

    // Hausaufgaben
    q('[data-hw-neu]')?.addEventListener('click', () => { hwBearbeiten = 'neu'; zeichnen(); box.querySelector('[data-hw-text]')?.focus(); });
    alle('[data-hw-bearbeiten]', b => b.onclick = () => { hwBearbeiten = Number(b.dataset.hwBearbeiten); zeichnen(); });
    q('[data-hw-abbrechen]')?.addEventListener('click', () => { hwBearbeiten = null; zeichnen(); });
    q('[data-hw-speichern]')?.addEventListener('click', async () => {
      const form = box.querySelector('[data-hw-formular]');
      const daten2 = { text: form.querySelector('[data-hw-text]').value.trim(), faellig: form.querySelector('[data-hw-faellig]').value || null, bemerkung: form.querySelector('[data-hw-bemerkung]').value };
      if (!daten2.text) return toast('Bitte die Hausaufgabe eingeben.', true);
      try {
        if (form.dataset.hwFormular === 'neu') await senden('/api/app/klassenbuch/hausaufgabenliste', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, ...daten2 });
        else await senden(`/api/app/klassenbuch/hausaufgabenliste/${form.dataset.hwFormular}`, daten2, 'PUT');
        await hausaufgabenNeu();
        toast(form.dataset.hwFormular === 'neu' ? 'Hausaufgabe erstellt.' : 'Hausaufgabe gespeichert.');
        hwBearbeiten = null; zeichnen();
      } catch (err) { toast(err.message, true); }
    });
    alle('[data-hw-erledigt]', b => b.onclick = async () => {
      const h = daten.hausaufgaben.liste.find(x => x.id === Number(b.dataset.hwErledigt));
      try { await senden(`/api/app/klassenbuch/hausaufgabenliste/${h.id}`, { erledigt: !h.erledigt }, 'PUT'); await hausaufgabenNeu(); zeichnen(); } catch (err) { toast(err.message, true); }
    });
    alle('[data-hw-loeschen]', b => b.onclick = async () => {
      if (!confirm('Hausaufgabe löschen?')) return;
      try { await senden(`/api/app/klassenbuch/hausaufgabenliste/${b.dataset.hwLoeschen}`, undefined, 'DELETE'); await hausaufgabenNeu(); toast('Hausaufgabe gelöscht.'); zeichnen(); } catch (err) { toast(err.message, true); }
    });

    // Abwesende Schüler*innen
    alle('[data-abw]', c => c.onchange = () => { const id = Number(c.dataset.abw); c.checked ? abwAuswahl.add(id) : abwAuswahl.delete(id); zeichnen(); });
    q('[data-abw-alle]')?.addEventListener('change', ev => {
      abwAuswahl.clear();
      if (ev.target.checked) [...(daten.anwesenheit?.fehlzeiten ?? []), ...(daten.anwesenheit?.vorstunden ?? [])].forEach(f => abwAuswahl.add(f.schuelerId));
      zeichnen();
    });
    q('[data-verlaengern]')?.addEventListener('click', () => {
      const vor = new Set((daten.anwesenheit?.vorstunden ?? []).map(f => f.schuelerId));
      const ids = [...abwAuswahl];
      const ausVor = ids.filter(id => vor.has(id));
      if (ausVor.length) verlaengern(ausVor, 'stunde');
      else if (zeiten.some(z => z.nr < k.von)) verlaengern(ids, 'tagesbeginn');
      else toast('Die Abwesenheit reicht bereits bis zum Stundenende.');
    });
    alle('[data-tagesbeginn]', b => b.onclick = () => verlaengern([Number(b.dataset.tagesbeginn)], 'tagesbeginn'));
    alle('[data-bisende]', b => b.onclick = () => verlaengern([Number(b.dataset.bisende)], 'stunde'));
    alle('[data-abw-bearbeiten]', b => b.onclick = () => { offen = 'abw:' + b.dataset.abwBearbeiten; zeichnen(); });
    q('[data-abw-speichern]')?.addEventListener('click', ev => {
      const id = Number(ev.currentTarget.dataset.abwSpeichern);
      const m = fzKarte();
      m.set(id, { ...(m.get(id) ?? { art: 'fehlt', minuten: 0 }), grund: box.querySelector('[data-abw-grund]').value, bemerkung: box.querySelector('[data-abw-text]').value });
      offen = null;
      anwesenheitSpeichern(m);
    });
    q('[data-abw-loeschen]')?.addEventListener('click', ev => {
      const id = Number(ev.currentTarget.dataset.abwLoeschen);
      const m = fzKarte(); m.delete(id);
      offen = null;
      anwesenheitSpeichern(m);
    });

    // Klassenbucheinträge
    q('[data-vermerk-neu]')?.addEventListener('click', () => { offen = 'vermerk'; zeichnen(); box.querySelector('[data-vtext]')?.focus(); });
    q('[data-vermerk-auswahl]')?.addEventListener('click', () => { offen = 'vermerk-auswahl'; zeichnen(); box.querySelector('[data-vtext]')?.focus(); });
    q('[data-vok]')?.addEventListener('click', async ev => {
      const text = box.querySelector('[data-vtext]').value.trim();
      if (!text) return toast('Bitte einen Text eingeben.', true);
      const art = box.querySelector('[data-vart]').value;
      const kl = klassenListe()[0];
      try {
        if (ev.currentTarget.dataset.vok === 'auswahl') {
          for (const id of auswahl) {
            const s = daten.anwesenheit.schueler.find(x => x.id === id);
            daten.vermerke.unshift(await senden('/api/app/klassenbuch/vermerke', { datum: k.datum, klasse: s?.klasse || kl, schuelerId: id, art, text }));
          }
          auswahl.clear();
        } else daten.vermerke.unshift(await senden('/api/app/klassenbuch/vermerke', { datum: k.datum, klasse: kl, schuelerId: null, art, text }));
        offen = null;
        toast('Klassenbucheintrag gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); }
    });

    // Ticket
    q('[data-ticket]')?.addEventListener('click', () => { offen = 'ticket'; zeichnen(); box.querySelector('[data-ticket-text]')?.focus(); });
    q('[data-ticket-senden]')?.addEventListener('click', async () => {
      const text = box.querySelector('[data-ticket-text]').value.trim();
      if (!text) return toast('Bitte beschreiben Sie das Anliegen.', true);
      try {
        await senden('/api/app/mitteilungen', { empfaenger: ['gruppe:leitung'], betreff: `Ticket: ${klassenListe().join(', ')} ${k.fach || ''} ${datumDe(k.datum)} ${k.von}. Std.`.trim(), text, wichtig: false });
        offen = null; toast('Ticket gesendet.'); zeichnen();
      } catch (err) { toast(err.message, true); }
    });

    // Details: online, Notizen (Speichern/Abbrechen erscheinen beim Bearbeiten)
    q('[data-online]')?.addEventListener('change', async ev => {
      try { await senden('/api/app/klassenbuch/online', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, online: ev.target.checked }, 'PUT'); if (daten.eintrag) daten.eintrag.online = ev.target.checked; geaendert = true; toast(ev.target.checked ? 'Die Stunde findet online statt.' : 'Die Stunde findet im Raum statt.'); }
      catch (err) { ev.target.checked = !ev.target.checked; toast(err.message, true); }
    });
    alle('[data-notiz]', t => t.oninput = () => box.querySelector(`[data-notiz-knoepfe="${t.dataset.notiz}"]`)?.classList.remove('d-none'));
    alle('[data-notiz-abbrechen]', b => b.onclick = () => zeichnen());
    alle('[data-notiz-speichern]', b => b.onclick = async () => {
      const feld = b.dataset.notizSpeichern;
      if (await eintragSpeichern({ [feld]: box.querySelector(`[data-notiz="${feld}"]`).value })) {
        k = { ...k, [feld === 'notiz' ? 'notizLehrer' : 'notizSchueler']: box.querySelector(`[data-notiz="${feld}"]`).value };
        zeichnen();
      }
    });
  }

  async function raumWahl(ziel) {
    ziel.innerHTML = '<div class="py-2"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
    let raeume;
    try { raeume = await holen(`/api/app/meldungen/freieraeume?datum=${k.datum}&von=${k.von}&bis=${k.bis}`); }
    catch (err) { ziel.innerHTML = `<p class="small text-danger mt-2">${e(err.message)}</p>`; return; }
    const kopfzeile = `<div class="sd-detail-titel mb-1">Freie buchbare Räume für ${wochentag(k.datum)}, ${datumDe(k.datum)}, ${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'} Stunde</div>
      <p class="small text-body-secondary">Die Stunde wird in den gewählten Raum verlegt und erscheint sofort als Änderung im Stundenplan.</p>`;
    if (!raeume.length) { ziel.innerHTML = kopfzeile + '<p class="small text-body-secondary mt-2 mb-0">In dieser Zeit ist kein buchbarer Raum frei.</p>'; return; }
    ziel.innerHTML = `${kopfzeile}<div class="raum-wahl">${raeume.map((r, i) => `<button type="button" class="btn btn-sm btn-outline-secondary text-start" data-r="${i}"><b>${e(r.kurz)}</b>
      <small class="d-block text-body-secondary text-truncate">${e([r.name !== r.kurz ? r.name : '', r.raumart, r.kapazitaet ? r.kapazitaet + ' Plätze' : ''].filter(Boolean).join(' · ') || ' ')}</small></button>`).join('')}</div>`;
    ziel.querySelectorAll('[data-r]').forEach(b => b.onclick = async () => {
      const r = raeume[Number(b.dataset.r)];
      try {
        await senden('/api/app/meldungen/raumbuchung', { datum: k.datum, stundeVon: k.von, stundeBis: k.bis, raum: r.kurz, klassen: kuerzel(k.klassen), titel: `${k.fach || 'Unterricht'} – Raumänderung` });
        toast(`Raum ${r.kurz} gebucht – die Raumänderung steht im Stundenplan.`);
        geaendert = true;
        modal.hide();
      } catch (err) { toast(err.message, true); }
    });
  }

  // Start
  box.innerHTML = '<div class="py-5 text-center"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
  modal.show();
  laden().then(() => { reiter = mitKlassenbuch() ? 'klassenbuch' : 'details'; zeichnen(); });
  return box;
}

