// Stundendetails wie im Original (Screenshots „Klassenbuch“ und „Details“):
// Kopf „Klassen | Unterricht | Fach“, darunter „Wochentag | Datum | Zeit | Raum | Lehrkraft“, Reiter Klassenbuch und Details.
// Klassenbuch: Schülerkacheln (Foto/Platzhalter, Statuspunkt), Alle auswählen/Auswahl löschen, abwesend/Klassenbucheintrag,
// Lehrstoff mit Stift, Abwesende Schüler*innen als alte Liste (grau gestreift) mit Verlängern, Hausaufgaben (Liste mit Fälligkeit),
// Klassenbucheinträge mit „+“, Klassendienste mit Besen, „Ticket erstellen“, unten die Statusleiste
// („Lehrstoffeintragung vorhanden“, „Abwesenheiten wurden kontrolliert“).
// Details: nur „Findet online statt“, Lehrstoff und Vertretungstext (wenn vorhanden), Hausaufgaben, Notizen für Schüler*innen
// und für Lehrkräfte; unten „Vorherige Stunde“, „Nächste Stunde“, „Prüfung erstellen“ und „Raum buchen“.
import { e, heuteIso, holen, senden, toast, bestaetigen, WOCHENTAGE, lehrkraftLabel } from './ui.js';

const PRUEFUNGSARTEN = ['Klassenarbeit', 'Klausur', 'Test', 'Abschlussprüfung', 'Präsentation', 'Referat', 'Vergleichsarbeit'];
const W = '/img/wu/';

const wochentag = iso => WOCHENTAGE[(new Date(iso + 'T12:00').getDay() + 6) % 7];
const datumDe = iso => { if (!iso) return ''; const [j, m, t] = iso.split('-'); return `${t}.${m}.${j}`; };
const kuerzel = l => (l || []).map(w => w.k || w.alt).filter(Boolean);
const plusTage = (iso, n) => { const d = new Date(iso + 'T12:00'); d.setDate(d.getDate() + n); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
const bildIcon = (datei, klasse = '') => `<img class="kb-icon ${klasse}" src="${W}${datei}" alt="" aria-hidden="true">`;
/** Uhrzeit „HH:MM“ in Minuten seit Mitternacht bzw. um n Minuten verschoben. */
const minutenVon = hhmm => { const [h, m] = String(hhmm || '0:0').split(':').map(Number); return (h || 0) * 60 + (m || 0); };
const uhrPlus = (hhmm, n) => { if (!hhmm) return ''; const x = Math.max(0, Math.min(1439, minutenVon(hhmm) + Number(n || 0))); return `${String(Math.floor(x / 60)).padStart(2, '0')}:${String(x % 60).padStart(2, '0')}`; };
const maskeIcon = datei => `<span class="u-sym wu kb-icon" style="--wu:url(${W}${datei})" aria-hidden="true"></span>`;

/**
 * Wert im Kopf wie im Original, ohne zusätzliches Label: geänderter Wert „B070 → C197“ mit altem Wert grau durchgestrichen,
 * entfallener Wert durchgestrichen. Mit typ werden Klassen, Lehrkräfte und Räume zu Links auf den Stundenplan.
 */
const KEIN_ELEMENT = /^(-{3}|\?{3}|\+{1,3})$/;
function planLink(typ, kurz) {
  if (!typ || !kurz || KEIN_ELEMENT.test(kurz)) return e(kurz);
  return `<a href="#" class="sd-link" data-plan="${typ}" data-element="${e(kurz)}" data-hover="Im Stundenplan anzeigen" data-hover-art="dunkel" data-hover-pos="unten">${e(kurz)}</a>`;
}
function wertKopf(liste, max = 6, typ = null, label = false) {
  const l = liste || [];
  const teil = l.length > max + 1 ? l.slice(0, max - 2) : l;
  // Lehrkräfte wie auf den Plankarten: offene Vertretung → abwesende Lehrkraft durchgestrichen im grünen Label, Vertretung → neue Lehrkraft grün
  if (label) return teil.map(w => lehrkraftLabel(w, x => planLink(typ, x))).filter(Boolean).join(', ')
    + (teil.length < l.length ? ` +${l.length - teil.length}` : '');
  // auch der ersetzte (durchgestrichene) Wert – z. B. die abwesende Lehrkraft – führt zu seinem Plan
  const alt = w => typ ? `<s class="sd-alt">${planLink(typ, w.alt)}</s>` : `<s class="sd-alt">${e(w.alt)}</s>`;
  return teil.map(w => w.alt && !w.k ? alt(w)
    : w.alt ? `${alt(w)} <span class="sd-pfeil">→</span> ${planLink(typ, w.k)}` : planLink(typ, w.k)).join(', ')
    + (teil.length < l.length ? ` +${l.length - teil.length}` : '');
}

const LEHRFORMEN = ['Einzelarbeit', 'Partnerarbeit', 'Gruppenarbeit', 'Frontalunterricht', 'Unterrichtsgespräch', 'Projektarbeit', 'Stationenlernen', 'Präsentation', 'Diskussion', 'Distanzunterricht'];
const zeitstempel = s => { const m = String(s || '').match(/^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?/); return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6] ?? '00'}` : ''; };
const WT_KURZ = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

/** Öffnet die Stundendetails. kontext.liste = alle Karten des Plans (für Vorherige/Nächste Stunde), kontext.typ = Planart. */
export function karteDialog(karte, kontext = {}) {
  const ich = kontext.ich ?? {};
  const kollegium = !!ich.kollegium;
  const liste = (kontext.liste ?? [karte]).filter(x => !x.ereignis).slice()
    .sort((a, b) => a.datum.localeCompare(b.datum) || a.von - b.von);

  const el = document.createElement('div');
  el.className = 'modal stundendetails';
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
  let raumModus = null;      // Raumwahl: { art: 'ersetzen' | 'hinzufuegen', raum, gewaehlt, suche, abteilung, arten:Set }

  const istEntfall = () => k.status === 'entfall' || (k.status === 'abwesend' && kontext.typ !== 'lehrer' && !k.lehrerAbwesend);
  const mitKlassenbuch = () => kollegium && k.unterricht && !['entfall', 'abwesend', 'vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
  // Prüfung auch in Vertretungs- und Zusatzstunden (ohne eigenen Unterricht), nur nicht in entfallenen Stunden oder Veranstaltungen
  const darfPruefung = () => kollegium && kuerzel(k.klassen).length && k.datum >= heuteIso() && !['entfall', 'abwesend', 'veranstaltung', 'vorgemerkt'].includes(k.status);
  const darfRaum = () => darfPruefung() && k.unterricht && (daten?.darf || ich.admin);
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
    // „Klassen | Gruppe | Fach“ – Klassen und Gruppe verlinkt („Im Stundenplan anzeigen“)
    // Mitklassen (gekoppelte Parallelklassen) mit anzeigen; fehlende Mitklasse durchgestrichen im grünen Label
    const alleKlassen = [...k.klassen, ...(k.mitklassen || [])];
    const klassenTeil = alleKlassen.length && alleKlassen.length < 8 ? wertKopf(alleKlassen, 8, kollegium || ich.element ? 'klasse' : null, !istEntfall() && (k.mitklassen || []).some(w => !w.k)) : '';
    const ersteKlasse = kuerzel(k.klassen)[0];
    const gruppe = k.gruppe ? (ersteKlasse ? `<a href="#" class="sd-link" data-plan="klasse" data-element="${e(ersteKlasse)}" data-hover="Im Stundenplan anzeigen" data-hover-art="dunkel" data-hover-pos="unten">${e(k.gruppe)}</a>` : e(k.gruppe))
      : ''; // kein Fach-Langname im Kopf – nur Kürzel
    const fachTeil = k.fachAlt && !istEntfall() ? `<s class="sd-alt">${e(k.fachAlt)}</s> <span class="sd-pfeil">→</span> ${e(k.fach)}` : e(k.fach || k.text || k.art || 'Stunde');
    const titel = [klassenTeil, gruppe, fachTeil].filter(Boolean);
    const zeile = [wochentag(k.datum), datumDe(k.datum), k.beginn ? `${k.beginn}-${k.ende}` : `${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'} Std.`,
      wertKopf(k.raeume, 6, 'raum'), wertKopf(k.lehrer, 6, kollegium ? 'lehrer' : null, !istEntfall())].filter(Boolean);
    // Badge statt Zusatzlabel: zusätzlicher Unterricht, Veranstaltung, Prüfung
    const badge = k.status === 'klausur' ? '<span class="sd-badge">PRÜFUNG</span>'
      : k.status === 'veranstaltung' ? '<span class="sd-badge voll">VERANSTALTUNG</span>'
      : k.zusaetzlich || (k.status === 'geaendert' && /zusatz|sondereinsatz|zusätzlich/i.test(k.art || '')) ? '<span class="sd-badge voll">ZUSÄTZLICHER UNTERRICHT</span>' : '';
    const leiste = istEntfall() ? 'ENTFÄLLT' : '';
    const reiterListe = reiter === 'pruefung' ? [['pruefung', 'Prüfung erstellen']] : reiter === 'raum' ? [['raum', raumModus?.art === 'ersetzen' ? `${raumModus.raum} ersetzen` : 'Räume hinzufügen']]
      : [...(mitKlassenbuch() ? [['klassenbuch', 'Klassenbuch']] : []), ['details', 'Details']];
    return `<div class="sd-kopf ${leiste ? 'grau' : ''}">
        <div class="sd-titel"><h2>${titel.join('<span class="trenn">|</span>')}${badge ? `<span class="trenn">|</span>${badge}` : ''}</h2>
          <div class="sd-zeile">${zeile.join('<span class="trenn">|</span>')}</div></div>
        <div class="sd-knoepfe"><button type="button" data-bs-dismiss="modal" title="Zur Ansicht zurück" aria-label="Zur Ansicht zurück"><i class="bi bi-chevron-down"></i></button>
          <button type="button" data-bs-dismiss="modal" title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg"></i></button></div>
      </div>
      ${leiste ? `<div class="sd-leiste">${leiste}</div>` : ''}
      <ul class="nav sd-reiter">${reiterListe.map(([id, t]) => `<li><button type="button" data-reiter="${id}" class="${reiter === id ? 'aktiv' : ''}">${t}</button></li>`).join('')}</ul>`;
  }

  function fuss() {
    if (reiter === 'pruefung') return `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-speichern-pruefung>Speichern</button></div>`;
    if (reiter === 'raum') return `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-raum-speichern ${raumModus?.gewaehlt ? '' : 'disabled'}>Speichern</button></div>`;
    if (reiter === 'klassenbuch') {
      const a = daten.anwesenheit;
      const stoff = !!daten.eintrag?.thema;
      const kontrolliert = !!a?.geprueft;
      // Kontrolle erst ab Beginn der Stunde (davor steht – wie im Vorbild – „Abwesenheitskontrolle ab: …“)
      const jetzt = new Date(), jetztMin = jetzt.getHours() * 60 + jetzt.getMinutes();
      const kannKontrolle = a?.darf && (k.datum < heuteIso() || (k.datum === heuteIso() && (!k.beginn || minutenVon(k.beginn) <= jetztMin)));
      // wie im Vorbild: rechts orange Pille(n), daneben „Abwesenheitskontrolle ab: Datum - Uhrzeit“ (Beginn der Stunde)
      const ab = `${datumDe(k.datum)} - ${e(k.beginn || '')}`;
      return `<div class="sd-fuss sd-status">
        ${stoff ? '<span class="ok"><i class="bi bi-check-circle-fill"></i> Lehrstoffeintragung vorhanden</span>'
          : daten.darf ? '<button type="button" class="sd-orange" data-bearbeiten="thema">Lehrstoff eingeben</button>' : '<span><i class="bi bi-circle"></i> Keine Lehrstoffeintragung</span>'}
        ${kontrolliert ? '<span class="ok"><i class="bi bi-check-circle-fill"></i> Abwesenheiten wurden kontrolliert</span>'
          : kannKontrolle ? '<button type="button" class="sd-orange" data-kontrolle>Abwesenheiten kontrollieren</button>'
          : `<span class="sd-kontrolle"><span>Abwesenheitskontrolle ab:</span><span>${ab}</span></span>`}
      </div>`;
    }
    const i = liste.indexOf(k);
    return `<div class="sd-fuss">
      <button type="button" class="sd-pille gross" data-blaettern="-1" ${i <= 0 ? 'disabled' : ''}><i class="bi bi-chevron-left"></i> Vorherige Stunde</button>
      <button type="button" class="sd-pille gross" data-blaettern="1" ${i < 0 || i >= liste.length - 1 ? 'disabled' : ''}>Nächste Stunde <i class="bi bi-chevron-right"></i></button>
      ${k.status === 'klausur' && kollegium ? `<button type="button" class="sd-pille gross" data-pruefung-verlegen>Prüfung verlegen</button>
        <button type="button" class="sd-pille gross rot" data-pruefung-loeschen>Prüfung löschen</button>`
        : darfPruefung() ? '<button type="button" class="sd-pille gross" data-pruefung>Prüfung erstellen</button>' : ''}
      </div>`;
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
      const st = !f ? 'da' : f.art === 'verspaetet' ? 'spaet' : f.art === 'vorzeitig' ? 'frueh' : 'weg';
      const befreit = daten.befreit?.has(s.id);
      const status = st === 'da' ? bildIcon('exact/status_present.svg', 'sd-statusbild') : st === 'weg' ? bildIcon('exact/status_absent.svg', 'sd-statusbild')
        : st === 'frueh' ? '<span class="sd-statusbild frueh"><i class="bi bi-box-arrow-right"></i></span>' : '<span class="sd-statusbild spaet"><i class="bi bi-clock-fill"></i></span>';
      const zusatz = f && st !== 'weg' ? `<span class="sd-minuten ${f.fehlstunden ? 'gewertet' : ''}">${st === 'spaet' ? '+' : '−'}${f.minuten}′</span>` : '';
      return `<div class="sd-kachel ${st} ${befreit && st === 'da' ? 'befreit' : ''} ${auswahl.has(s.id) ? 'gewaehlt' : ''}" data-sid="${s.id}" role="button" tabindex="0" aria-pressed="${auswahl.has(s.id)}" title="${befreit ? 'vom Unterricht befreit' : st === 'weg' ? 'abwesend' : st === 'spaet' ? `verspätet (${f.minuten} min)` : st === 'frueh' ? `vorzeitig gegangen (${f.minuten} min)` : 'anwesend'}${f?.fehlstunden && st !== 'weg' ? ' – als Fehlstunde gewertet' : ''}">
        ${status}${zusatz}
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
          <button type="button" class="sd-pille klein grau" data-setzen="zeitraum" ${auswahl.size ? '' : 'disabled'} title="Abwesenheit minutengenau erfassen (Verspätung, vorzeitig gegangen, Zeitraum)"><i class="bi bi-clock me-1"></i>Abwesenheit …</button>
          <button type="button" class="sd-pille klein grau" data-vermerk-auswahl ${auswahl.size ? '' : 'disabled'}>Klassenbucheintrag</button></div>` : ''}`;
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
    const stift = daten.darf ? '<button type="button" class="sd-stift" data-bearbeiten="thema" title="Lehrstoff bearbeiten" aria-label="Lehrstoff bearbeiten"><i class="bi bi-pencil"></i></button>' : '';
    const zusatz = [daten.eintrag?.lehrform ? `Lehrform: ${daten.eintrag.lehrform}` : '', daten.eintrag?.anmerkung ? `Anmerkung: ${daten.eintrag.anmerkung}` : ''].filter(Boolean).join(' · ');
    const inhalt = (wert ? `<p class="sd-text">${e(wert)}</p>` : '<p class="sd-text text-body-tertiary">Kein Lehrstoff eingetragen</p>')
      + (zusatz ? `<p class="sd-text small text-body-secondary mt-1">${e(zusatz)}</p>` : '');
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
      // minutengenau: Zeitraum der ganzen Abwesenheit (vom Server), sonst aus der Stunde
      const beginn = !vor && f.vonZeit ? f.vonZeit : (sv.beginn || k.beginn || '');
      const ende = vor ? (zeitVon(f.stunde).ende || '') : f.bisZeit ? f.bisZeit : (bisStunde.ende || k.ende || '');
      const vonD = !vor && f.vonDatum ? f.vonDatum : (f.datum || k.datum), bisD = !vor && f.bisDatum ? f.bisDatum : (f.datum || k.datum);
      const anfang = !vor && f.art === 'fehlt' && fruehere && a.darf
        ? `<button type="button" class="kb-zeitknopf" data-tagesbeginn="${f.schuelerId}" title="Abwesenheit auf Tagesbeginn verlängern"><img src="${W}legacy/absence-start.png" alt=""></button>` : '';
      const schluss = vor && a.darf
        ? `<button type="button" class="kb-zeitknopf" data-bisende="${f.schuelerId}" title="Abwesenheit bis Stundenende verlängern"><img src="${W}legacy/absence-end.png" alt=""></button>` : '';
      const gewertet = f.fehlstunden && f.art !== 'fehlt' ? ` · ${f.fehlstunden === 1 ? 'eine Fehlstunde' : `${f.fehlstunden} Fehlstunden`}` : '';
      const st = { offen: 'offen', entschuldigt: 'entschuldigt', unentschuldigt: 'nicht entschuldigt' }[f.status] ?? '';
      const status = vor ? 'Vorstunde – verlängern' : f.art === 'verspaetet' ? `${st} · verspätet ${f.minuten} min${gewertet}` : f.art === 'vorzeitig' ? `${st} · vorzeitig gegangen ${f.minuten} min${gewertet}` : st;
      return `<tr class="${vor ? 'vorstunde' : ''}">
        <td class="mitte"><input type="checkbox" data-abw="${f.schuelerId}" ${abwAuswahl.has(f.schuelerId) ? 'checked' : ''} aria-label="${e(name(f.schuelerId))} auswählen"></td>
        <td class="mitte">${a.darf && !vor ? `<button type="button" class="kb-stift" data-abw-bearbeiten="${f.schuelerId}" title="Abwesenheit bearbeiten"><img src="${W}legacy/absence-edit.png" alt="Bearbeiten"></button>` : ''}</td>
        <td>${e(name(f.schuelerId))}</td><td>${datumDe(vonD)}</td><td class="zeit">${anfang}${e(beginn)}</td><td>${datumDe(bisD)}</td><td class="zeit">${e(ende)}${schluss}</td>
        <td>${e(grundText(f.grund))}</td><td>${e(status)}</td><td class="text">${e(f.text ?? f.bemerkung)}</td></tr>`;
    };
    const tabelle = zeilen.length ? `<div class="table-responsive"><table class="alt-liste kb-abwesend">
        <thead><tr><th class="mitte">Auswahl<br><input type="checkbox" data-abw-alle aria-label="Alle auswählen" ${abwAuswahl.size && abwAuswahl.size === zeilen.length ? 'checked' : ''}></th><th></th><th>Schüler*innen</th><th>Von</th><th>Beginnzeit</th><th>Bis</th><th>Endzeit</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th></tr></thead>
        <tbody>${zeilen.map(reihe).join('')}</tbody></table></div>
        ${a.darf ? `<button type="button" class="sd-pille klein grau mt-3" data-verlaengern ${abwAuswahl.size ? '' : 'disabled'}>Verlängern</button>` : ''}`
      : '<p class="sd-text small">Keine Abwesenheiten vorhanden</p>';
    return abschnitt(bildIcon('klassenbuch/chair-1.svg'), 'Abwesende Schüler*innen', (a.fehlzeiten ?? []).length, '', tabelle);
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

  function vermerkeHtml() {
    const v = daten.vermerke;
    const plus = kollegium ? '<button type="button" class="sd-plus" data-vermerk-neu title="Klassenbucheintrag erfassen" aria-label="Klassenbucheintrag erfassen"><i class="bi bi-plus-lg"></i></button>' : '';
    const kurz = (t, n) => t.length > n ? t.slice(0, n - 1) + '…' : t;
    const tabelle = v.length ? `<div class="table-responsive"><table class="alt-liste kb-eintraege"><tbody>${v.map(x => `<tr title="${e(x.text)}">
        <td>${e(x.schuelerId ? schuelerName(x.schuelerId) || '–' : 'Klasse ' + x.klasse)}</td><td>${datumDe(x.datum)}</td><td>${e((x.erstellt || '').slice(11, 16))}</td>
        <td>${e((x.art || '').slice(0, 3))}</td><td class="text">${e(kurz(x.text, 30))}</td><td>${e(x.lehrer)}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="sd-text small">Keine Klassenbucheinträge vorhanden</p>';
    return abschnitt(maskeIcon('class_register_entries.svg'), 'Klassenbucheinträge', v.length, plus, tabelle);
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
    if (darfRaum()) {
      const raeume = kuerzel(k.raeume.filter(w => w.k));
      teile.push(`<div class="sd-detail sd-raeume">${maskeIcon('shared_roomEntity.svg')}<div class="d-flex flex-wrap align-items-center gap-2">
        ${raeume.map(x => `<div class="sd-raumchip"><button type="button" class="sd-chipknopf" data-raum-menue="${e(x)}" aria-haspopup="menu" aria-expanded="false">${e(x)}</button>
          <div class="sd-raummenue d-none" role="menu" data-menue-fuer="${e(x)}"><button type="button" role="menuitem" data-raum-ersetzen="${e(x)}">Ersetzen</button>
          <button type="button" role="menuitem" data-raum-loeschen="${e(x)}">Löschen</button></div></div>`).join('')}
        <button type="button" class="sd-raum-plus" data-raum-plus title="Raum hinzufügen" aria-label="Raum hinzufügen"><i class="bi bi-plus-lg"></i></button></div></div>`);
    }
    const vertretung = k.text && (k.fach || k.status !== 'veranstaltung') ? k.text : '';
    if (vertretung && k.status !== 'klausur') teile.push(reihe(datei, k.fach ? 'Vertretungstext' : 'Bezeichnung', `<p class="sd-text">${e(vertretung)}</p>`));
    if (k.status === 'klausur') teile.push(reihe(datei, 'Prüfung', `<p class="sd-text">${e([k.art, k.text].filter(Boolean).join(' – '))}</p>`));
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
    const eintraege = [...neu].map(([schuelerId, f]) => ({ schuelerId, art: f.art, minuten: f.minuten || 5, bemerkung: f.bemerkung ?? '', grund: f.grund ?? '', restTag: f.restTag ?? null, von: f.von ?? null, bis: f.bis ?? null }));
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

  // bisherige Abwesenheiten der Stunde mit ihrem minutengenauen Fenster (bleiben beim Speichern anderer Schüler*innen erhalten)
  const fzKarte = () => new Map((daten.anwesenheit?.fehlzeiten ?? []).map(f => [f.schuelerId, { art: f.art, minuten: f.minuten, bemerkung: f.text ?? f.bemerkung, grund: f.grund, von: f.beginn || null, bis: f.ende || null }]));

  async function eintragSpeichern(aenderung) {
    const alt = daten.eintrag ?? {};
    const neu = { thema: alt.thema ?? '', hausaufgabe: alt.hausaufgabe ?? '', faellig: alt.faellig ?? null, notiz: alt.notiz ?? '', notizSchueler: alt.notizSchueler ?? '', ...aenderung };
    try {
      const lk = kuerzel(k.lehrer);
      daten.eintrag = await senden('/api/app/klassenbuch/stunde', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, ...neu, faellig: neu.faellig || null, lehrer: lk.length === 1 ? lk[0] : null }, 'PUT');
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
    box.onclick = ev => { if (!ev.target.closest('.sd-raumchip')) box.querySelectorAll('.sd-raummenue').forEach(x => x.classList.add('d-none')); };
    alle('[data-reiter]', b => b.onclick = () => { if (reiter === 'pruefung' || reiter === 'raum') return; reiter = b.dataset.reiter; offen = null; zeichnen(); });
    alle('[data-blaettern]', b => b.onclick = async () => {
      const x = liste[liste.indexOf(k) + Number(b.dataset.blaettern)];
      if (!x) return;
      k = x; auswahl.clear(); abwAuswahl.clear(); offen = null; hwBearbeiten = null; pruefungSchueler = null;
      box.querySelector('.sd-rumpf').innerHTML = '<div class="py-5"></div>';
      await laden();
      if (reiter === 'klassenbuch' && !mitKlassenbuch()) reiter = 'details';
      zeichnen();
    });
    q('[data-pruefung]')?.addEventListener('click', async () => {
      if (!daten.anwesenheit && k.unterricht) daten.anwesenheit = await holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`).catch(() => null);
      klausurDialog();
    });
    // Prüfung löschen bzw. verlegen (noch nicht übernommene werden zurückgezogen, übernommene gehen als Rückmeldung an UntPlan)
    const pruefungAlt = () => ({ datum: k.datum, stundeVon: k.von, stundeBis: k.bis, klassen: kuerzel(k.klassen), titel: k.text || null });
    q('[data-pruefung-loeschen]')?.addEventListener('click', async () => {
      if (!await bestaetigen('Prüfung löschen', `Prüfung${k.text ? ` „${k.text}“` : ''} am ${datumDe(k.datum)} (${k.von === k.bis ? `${k.von}.` : `${k.von}.–${k.bis}.`} Stunde) löschen?`)) return;
      try {
        await senden('/api/app/meldungen/klausur/loeschen', pruefungAlt());
        toast('Prüfung gelöscht – UntPlan übernimmt die Löschung beim nächsten Import.');
        geaendert = true; modal.hide();
      } catch (err) { toast(err.message, true); }
    });
    q('[data-pruefung-verlegen]')?.addEventListener('click', () => {
      const wahl = (sel) => zeiten.map(z => `<option value="${z.nr}" ${z.nr === sel ? 'selected' : ''}>${z.nr}. (${e(z.beginn)}–${e(z.ende)})</option>`).join('');
      altDialog('Prüfung verlegen', `
        <div class="ad-info">${e(k.text || 'Prüfung')} · bisher ${datumDe(k.datum)}, ${k.von === k.bis ? `${k.von}.` : `${k.von}.–${k.bis}.`} Stunde</div>
        <label class="ad-feld">Neues Datum<input type="date" data-pv-datum value="${k.datum}" min="${heuteIso()}"></label>
        <div class="ad-reihe"><label class="ad-feld">von Stunde<select data-pv-von>${wahl(k.von)}</select></label>
          <label class="ad-feld">bis Stunde<select data-pv-bis>${wahl(k.bis)}</select></label></div>
        <label class="ad-feld">Räume<input data-pv-raeume value="${e(kuerzel(k.raeume.filter(w => w.k)).join(', '))}"></label>
        <div class="ad-knoepfe"><button type="button" class="ad-pille" data-pv-speichern>Verlegen</button><button type="button" class="ad-pille" data-ad-zu>Schließen</button></div>`, (o, zu) => {
        o.querySelector('[data-pv-speichern]').onclick = async ev => {
          const von = Number(o.querySelector('[data-pv-von]').value), bis = Math.max(von, Number(o.querySelector('[data-pv-bis]').value));
          ev.currentTarget.disabled = true;
          try {
            await senden('/api/app/meldungen/klausur/verlegen', { alt: pruefungAlt(), neu: { datum: o.querySelector('[data-pv-datum]').value, stundeVon: von, stundeBis: bis,
              klassen: kuerzel(k.klassen), raeume: o.querySelector('[data-pv-raeume]').value.split(/[,;\s]+/).filter(Boolean), aufsicht: kuerzel(k.lehrer.filter(w => w.k)), titel: k.text || 'Prüfung', text: '' } });
            toast('Prüfung verlegt.');
            zu(); geaendert = true; modal.hide();
          } catch (err) { toast(err.message, true); ev.currentTarget.disabled = false; }
        };
      }, 420);
    });
    // Kopf: Klasse, Gruppe, Lehrkraft, Raum öffnen den Stundenplan
    alle('[data-plan]', a => a.onclick = ev => {
      ev.preventDefault();
      const ziel = `#/plan/${a.dataset.plan}/${encodeURIComponent(a.dataset.element)}/${k.datum}`;
      modal.hide();
      setTimeout(() => kontext.navigieren ? kontext.navigieren(ziel) : (location.hash = ziel));
    });
    // Raum: Menü am Chip (Ersetzen, Löschen), Plus für einen zusätzlichen Raum
    alle('[data-raum-menue]', b => b.onclick = ev => {
      ev.stopPropagation();
      const m = box.querySelector(`[data-menue-fuer="${CSS.escape(b.dataset.raumMenue)}"]`);
      const auf = m.classList.contains('d-none');
      box.querySelectorAll('.sd-raummenue').forEach(x => x.classList.add('d-none'));
      m.classList.toggle('d-none', !auf);
      b.setAttribute('aria-expanded', String(auf));
    });
    alle('[data-raum-ersetzen]', b => b.onclick = () => { raumModus = { art: 'ersetzen', raum: b.dataset.raumErsetzen, gewaehlt: null, suche: '', abteilung: '', arten: new Set() }; reiter = 'raum'; zeichnen(); });
    alle('[data-raum-loeschen]', b => b.onclick = () => raumAendern(kuerzel(k.raeume.filter(w => w.k)).filter(x => x !== b.dataset.raumLoeschen), `Raum ${b.dataset.raumLoeschen} entfernt.`));
    q('[data-raum-plus]')?.addEventListener('click', () => { raumModus = { art: 'hinzufuegen', raum: null, gewaehlt: null, suche: '', abteilung: '', arten: new Set() }; reiter = 'raum'; zeichnen(); });
    q('[data-raum-speichern]')?.addEventListener('click', () => {
      const aktuell = kuerzel(k.raeume.filter(w => w.k));
      const neu = raumModus.art === 'ersetzen' ? aktuell.map(x => x === raumModus.raum ? raumModus.gewaehlt : x) : [...aktuell, raumModus.gewaehlt];
      raumAendern([...new Set(neu)], raumModus.art === 'ersetzen' ? `${raumModus.raum} durch ${raumModus.gewaehlt} ersetzt.` : `Raum ${raumModus.gewaehlt} hinzugefügt.`);
    });
    q('[data-abbrechen]')?.addEventListener('click', () => { reiter = 'details'; raumModus = null; zeichnen(); });
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
      // abwesend → anwesend ohne weiteren Dialog (die ganze Abwesenheit dieser Stunde entfällt); Verspätung über den Stift bzw. „Abwesenheit …“
      if (m.has(id)) { abwesenheitEntfernen([id]); return; }
      m.set(id, { art: 'fehlt', minuten: 0 });
      anwesenheitSpeichern(m);
    });
    q('[data-alle]')?.addEventListener('click', () => { (daten.anwesenheit?.schueler ?? []).forEach(s => auswahl.add(s.id)); zeichnen(); });
    q('[data-keine]')?.addEventListener('click', () => { auswahl.clear(); zeichnen(); });
    alle('[data-setzen]', b => b.onclick = () => {
      if (b.dataset.setzen !== 'fehlt') { abwesenheitDialog([...auswahl]); return; }
      const m = fzKarte();
      for (const id of auswahl) m.set(id, { ...(m.get(id) ?? {}), art: 'fehlt', minuten: 0 });
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

    // Lehrstoff (Dialog), Hausaufgaben-Editor öffnen
    alle('[data-bearbeiten]', b => b.onclick = () => {
      if (b.dataset.bearbeiten === 'thema') {
        if (reiter !== 'klassenbuch' && mitKlassenbuch()) { reiter = 'klassenbuch'; zeichnen(); }
        lehrstoffDialog();
        return;
      }
      offen = b.dataset.bearbeiten; hwBearbeiten = null;
      if (offen === 'thema' && reiter !== 'klassenbuch' && mitKlassenbuch()) reiter = 'klassenbuch';
      zeichnen();
      box.querySelector('[data-hw-neu]')?.scrollIntoView({ block: 'center' });
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
    alle('[data-abw-bearbeiten]', b => b.onclick = () => {
      const f = (daten.anwesenheit?.fehlzeiten ?? []).find(x => x.schuelerId === Number(b.dataset.abwBearbeiten));
      if (f) abwesenheitDialog([f.schuelerId], f);
    });
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
    q('[data-vermerk-neu]')?.addEventListener('click', () => eintragDialog([]));
    q('[data-vermerk-auswahl]')?.addEventListener('click', () => eintragDialog([...auswahl]));

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

  // ---------------------------------------------------------------- Raumbuchung am Desktop (Ersetzen, Löschen, zusätzlicher Raum)

  /** Räume nach der Buchung wie auf dem Server: ersetzter Raum „alt → neu“, zusätzlicher Raum neu, entfernter Raum durchgestrichen. */
  function raeumeNach(bisher, neu) {
    const entfernt = bisher.filter(w => w.k && !neu.includes(w.k)).map(w => w.alt || w.k);
    let j = 0;
    const res = neu.map(x => bisher.find(w => w.k === x) ?? (() => { const a = entfernt[j++] ?? null; return a === x ? { k: x } : { k: x, alt: a, neu: true }; })());
    for (; j < entfernt.length; j++) res.push({ k: '', alt: entfernt[j], neu: true });
    return res;
  }

  let raumLaeuft = false;
  async function raumAendern(raeume, meldung) {
    // nur eine Änderung gleichzeitig (Doppelklick), Knöpfe währenddessen gesperrt
    if (raumLaeuft) return;
    raumLaeuft = true;
    box.querySelectorAll('[data-raum-loeschen], [data-raum-ersetzen], [data-raum-plus], [data-raum-uebernehmen]').forEach(x => { x.disabled = true; });
    try {
      await senden('/api/app/meldungen/raumaenderung', { datum: k.datum, stundeVon: k.von, stundeBis: k.bis, unterricht: k.unterricht, raeume });
      const i = liste.indexOf(k);
      k = { ...k, raeume: raeumeNach(k.raeume, raeume), status: k.status === 'regulaer' ? 'geaendert' : k.status };
      if (i >= 0) liste[i] = k;
      geaendert = true;
      toast(meldung);
      reiter = 'details'; raumModus = null;
      zeichnen();
    } catch (err) {
      toast(err.message, true);
      box.querySelectorAll('[data-raum-loeschen], [data-raum-ersetzen], [data-raum-plus], [data-raum-uebernehmen]').forEach(x => { x.disabled = false; });
    } finally { raumLaeuft = false; }
  }

  /** Auswahlliste wie im Original: Suche, Abteilung als Auswahl-Pille, Raumarten als Pillen, Zeilen mit Radio-Auswahl. */
  async function raumWahl(ziel) {
    ziel.innerHTML = '<div class="py-2"></div>';
    let raeume;
    try { raeume = await holen(`/api/app/meldungen/freieraeume?datum=${k.datum}&von=${k.von}&bis=${k.bis}${k.unterricht ? `&unterricht=${k.unterricht}` : ''}`); }
    catch (err) { ziel.innerHTML = `<p class="small text-danger mt-2">${e(err.message)}</p>`; return; }
    const aktuell = kuerzel(k.raeume.filter(w => w.k));
    raeume = raeume.filter(r => !aktuell.includes(r.kurz));
    const abteilung = r => (r.kurz.match(/^[A-Za-zÄÖÜäöü]+/)?.[0] ?? '').toUpperCase();
    const abteilungen = [...new Set(raeume.map(abteilung).filter(Boolean))].sort();
    const arten = [...new Set(raeume.map(r => r.raumart).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de'));
    const titel = raumModus.art === 'ersetzen' ? `${raumModus.raum} ersetzen` : 'Räume hinzufügen';
    const zeichneListe = () => {
      const m = raumModus;
      const sichtbar = raeume.filter(r => (!m.suche || `${r.kurz} ${r.name}`.toLowerCase().includes(m.suche)) && (!m.abteilung || abteilung(r) === m.abteilung) && (!m.arten.size || m.arten.has(r.raumart)));
      ziel.querySelector('[data-raumliste]').innerHTML = sichtbar.length ? sichtbar.map(r => `<li><button type="button" class="sd-raumzeile ${m.gewaehlt === r.kurz ? 'gewaehlt' : ''}" data-r="${e(r.kurz)}" role="radio" aria-checked="${m.gewaehlt === r.kurz}">
          ${maskeIcon('shared_roomEntity.svg')}<span class="name">${e(r.kurz)}${r.urspruenglich ? ' <span class="sd-ursprung">ursprünglicher Raum</span>' : ''}${r.name && r.name !== r.kurz ? `<small>${e(r.name)}</small>` : ''}</span><span class="radio" aria-hidden="true"></span></button></li>`).join('')
        : '<li class="sd-raumleer">In dieser Zeit ist kein passender buchbarer Raum frei.</li>';
      ziel.querySelectorAll('[data-r]').forEach(b => b.onclick = () => { raumModus.gewaehlt = b.dataset.r; zeichneListe(); box.querySelector('[data-raum-speichern]').disabled = false; });
    };
    ziel.innerHTML = `<div class="sd-raumwahl"><h3>${e(titel)}</h3>
      <div class="sd-raumsuche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-raumsuche placeholder="Suchen Sie einen Raum" aria-label="Raum suchen"></div>
      <div class="sd-raumfilter" role="group" aria-label="Filter">
        <label class="sd-filterpille wahl"><select data-abteilung aria-label="Abteilung"><option value="">Abteilung</option>${abteilungen.map(a => `<option>${e(a)}</option>`).join('')}</select></label>
        ${arten.map(a => `<button type="button" class="sd-filterpille" data-art="${e(a)}" aria-pressed="false">${e(a)}</button>`).join('')}</div>
      <ul class="sd-raumliste" role="radiogroup" aria-label="Räume" data-raumliste></ul></div>`;
    ziel.querySelector('[data-raumsuche]').oninput = ev => { raumModus.suche = ev.target.value.trim().toLowerCase(); zeichneListe(); };
    ziel.querySelector('[data-abteilung]').onchange = ev => { raumModus.abteilung = ev.target.value; ev.target.closest('.sd-filterpille').classList.toggle('an', !!ev.target.value); zeichneListe(); };
    ziel.querySelectorAll('[data-art]').forEach(b => b.onclick = () => {
      if (raumModus.arten.has(b.dataset.art)) raumModus.arten.delete(b.dataset.art); else raumModus.arten.add(b.dataset.art);
      b.classList.toggle('an'); b.setAttribute('aria-pressed', String(b.classList.contains('an'))); zeichneListe();
    });
    zeichneListe();
    ziel.querySelector('[data-raumsuche]').focus();
  }

  // ---------------------------------------------------------------- Dialoge wie im alten Klassenbuch (Lehrstoff, Abwesenheit, Klassenbucheintrag)

  /** Kleiner Dialog über den Stundendetails (Kopf bleibt frei, Navigation bleibt sichtbar). */
  function altDialog(titel, inhalt, beimOeffnen, breite = 380) {
    box.querySelector('.sd-overlay')?.remove();
    const o = document.createElement('div');
    o.className = 'sd-overlay';
    o.style.top = `${box.querySelector('.modal-header')?.offsetHeight ?? 0}px`;
    o.innerHTML = `<div class="sd-altdialog" role="dialog" aria-modal="true" aria-label="${e(titel)}" style="width:min(${breite}px, calc(100% - 24px))">
      <div class="sd-altdialog-kopf"><span>${e(titel)}</span><button type="button" data-ad-zu title="Schließen" aria-label="Schließen">×</button></div>
      <div class="sd-altdialog-rumpf">${inhalt}</div></div>`;
    box.append(o);
    const zu = () => o.remove();
    o.querySelectorAll('[data-ad-zu]').forEach(b => b.onclick = zu);
    o.addEventListener('keydown', ev => { if (ev.key === 'Escape') { ev.stopPropagation(); zu(); } });
    o.addEventListener('click', ev => { if (ev.target === o) zu(); });
    beimOeffnen(o, zu);
    o.querySelector('textarea, input:not([readonly]):not([disabled]), select:not([disabled])')?.focus();
    return o;
  }

  /**
   * Klausuranlage als Dialog direkt über den Stundendetails (Knopf „Prüfung erstellen“ unten neben „Nächste Stunde“):
   * Name, Text, Prüfungsart, Datum und Stunden, freie Räume zur Auswahl, Aufsicht, teilnehmende Schüler*innen.
   */
  async function klausurDialog() {
    pruefungSchueler = null;
    let frei = [];
    try { frei = await holen(`/api/app/meldungen/freieraeume?datum=${k.datum}&von=${k.von}&bis=${k.bis}`); } catch { /* ohne Vorschläge */ }
    const vorschlaege = `<datalist id="sd-klausur-raeume">${[...kuerzel(k.raeume), ...frei.map(r => r.kurz)].filter((x, i, a) => x && a.indexOf(x) === i)
      .map(x => `<option value="${e(x)}">`).join('')}</datalist>`;
    altDialog('Prüfung erstellen', `${kopfDl()}${pruefungHtml()}${vorschlaege}
      <div class="ad-knoepfe justify-content-end"><button type="button" class="ad-pille" data-ad-zu>Abbrechen</button>
        <button type="button" class="sd-orange" data-klausur-speichern>Speichern</button></div>`, (o, zu) => {
      o.querySelector('[name=raeume]')?.setAttribute('list', 'sd-klausur-raeume');
      o.querySelectorAll('[data-chip]').forEach(c => c.onclick = () => {
        const id = Number(c.dataset.chip);
        pruefungSchueler.has(id) ? pruefungSchueler.delete(id) : pruefungSchueler.add(id);
        c.classList.toggle('an');
      });
      o.querySelector('[data-klausur-speichern]').onclick = async ev => {
        const f = n => o.querySelector(`[name=${n}]`).value;
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
          zu();
          modal.hide();
        } catch (err) { toast(err.message, true); knopf.disabled = false; }
      };
    }, 640);
  }

  const kopfDl = (zusatz = '') => `<dl class="ad-kopf"><dt>Klasse</dt><dd>${e(kuerzel(k.klassen).join(', '))}</dd><dt>Lehrkraft</dt><dd>${e(kuerzel(k.lehrer).join(', '))}</dd><dt>Fach</dt><dd>${e(k.fach)}</dd>${zusatz}</dl>`;

  async function lehrstoffDialog() {
    const ein = daten.eintrag ?? {};
    let vorstunden = [];
    try { vorstunden = await holen(`/api/app/klassenbuch/lehrstoff/vorstunden?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`); } catch { /* ohne Vorstunden */ }
    const wt = iso => WT_KURZ[(new Date(iso + 'T12:00').getDay() + 6) % 7];
    altDialog('Lehrstoff', `${kopfDl(`<dt>Datum</dt><dd>${datumDe(k.datum)} ${e(k.beginn || '')}${k.ende ? ' - ' + e(k.ende) : ''}</dd>`)}
      <label class="ad-feld">Lehrstoff<textarea rows="4" data-ad-thema maxlength="2000">${e(ein.thema ?? '')}</textarea></label>
      <div class="ad-feld">Vorstunden<table class="ad-liste">${vorstunden.length ? vorstunden.map((v, i) => `<tr><td>${datumDe(v.datum)}</td><td>${wt(v.datum)}</td>
        <td class="text" title="${e(v.thema)}">${e(v.thema || '–')}</td><td class="mitte">${v.thema ? `<button type="button" data-ad-kopie="${i}" title="Lehrstoff übernehmen" aria-label="Lehrstoff übernehmen"><img src="${W}legacy/absence-edit.png" alt=""></button>` : ''}</td></tr>`).join('')
        : '<tr><td class="leer">Keine Vorstunden</td></tr>'}</table></div>
      <label class="ad-feld">Lehrform<select data-ad-lehrform><option value="">&lt;Auswahl&gt;</option>${LEHRFORMEN.map(l => `<option ${l === ein.lehrform ? 'selected' : ''}>${e(l)}</option>`).join('')}</select></label>
      <label class="ad-feld">Anmerkung<input data-ad-anmerkung maxlength="1000" value="${e(ein.anmerkung ?? '')}"></label>
      <div class="ad-knoepfe"><button type="button" class="ad-pille" data-ad-speichern="1">Speichern</button><button type="button" class="ad-pille" data-ad-speichern="0">Übernehmen</button><button type="button" class="ad-pille" data-ad-zu>Schließen</button></div>
      ${ein.geaendert ? `<div class="ad-fuss">Geändert am ${zeitstempel(ein.geaendert)} von ${e(ein.geaendertVon || ein.lehrer || '')}</div>` : ''}`, (o, zu) => {
      o.querySelectorAll('[data-ad-kopie]').forEach(b => b.onclick = () => { o.querySelector('[data-ad-thema]').value = vorstunden[Number(b.dataset.adKopie)].thema; o.querySelector('[data-ad-thema]').focus(); });
      o.querySelectorAll('[data-ad-speichern]').forEach(b => b.onclick = async () => {
        const ok = await eintragSpeichern({ thema: o.querySelector('[data-ad-thema]').value, lehrform: o.querySelector('[data-ad-lehrform]').value, anmerkung: o.querySelector('[data-ad-anmerkung]').value });
        if (!ok) return;
        if (b.dataset.adSpeichern === '1') { zu(); zeichnen(); return; }
        const t = `Geändert am ${zeitstempel(daten.eintrag?.geaendert)} von ${daten.eintrag?.geaendertVon ?? ''}`;
        const f = o.querySelector('.ad-fuss');
        if (f) f.textContent = t; else o.querySelector('.sd-altdialog-rumpf').insertAdjacentHTML('beforeend', `<div class="ad-fuss">${e(t)}</div>`);
      });
    }, 400);
  }

  async function abwesenheitEntfernen(ids) {
    try {
      await senden('/api/app/klassenbuch/abwesenheit/loeschen', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, schuelerIds: ids });
      await anwesenheitNeu();
      geaendert = true;
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  /**
   * Abwesenheitsdialog wie im Original (ein Dialog für alles): Schüler*in, Von (Datum + Uhrzeit), Bis (Datum + Uhrzeit), Abwesenheitsgrund,
   * Text, Status; Speichern, Abbrechen, Löschen. Minutengenau vorbelegt mit Stundenbeginn und -ende: Verspätung = abwesend von Stundenbeginn
   * bis zur Ankunft, vorzeitig gegangen = abwesend ab der Uhrzeit bis Stundenende bzw. Tagesende. Über den Tag hinaus und den Status
   * ändert nur die Klassenleitung.
   */
  function abwesenheitDialog(ids, f = null) {
    if (!ids.length) return;
    const a = daten.anwesenheit ?? {};
    const verwalten = !!a.verwalten;
    const start = a.beginn || k.beginn || zeitVon(k.von).beginn || '08:00', ende = a.ende || k.ende || zeitVon(k.bis).ende || '08:45';
    const tagesende = zeiten.length ? zeiten[zeiten.length - 1].ende : ende;
    const namen = ids.map(schuelerName).filter(Boolean);
    const vorgabe = { vonDatum: f?.vonDatum || k.datum, vonZeit: f?.vonZeit || start, bisDatum: f?.bisDatum || k.datum, bisZeit: f?.bisZeit || ende };
    const status = f?.status || 'offen';
    const jetzt = new Date(), jetztMin = jetzt.getHours() * 60 + jetzt.getMinutes();
    const laeuft = k.datum === heuteIso() && jetztMin > minutenVon(start) && jetztMin < minutenVon(ende);
    const jetztZeit = `${String(jetzt.getHours()).padStart(2, '0')}:${String(jetzt.getMinutes()).padStart(2, '0')}`;
    const gruendeWahl = `<option value="">–</option>${daten.gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === f?.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}`;
    const radio = (wert, text) => `<label class="ab-radio"><input type="radio" name="ab-status" value="${wert}" ${status === wert ? 'checked' : ''} ${verwalten ? '' : 'disabled'}><span>${text}</span></label>`;
    altDialog('Abwesenheit', `
      <div class="ab-formular">
        <label>Schüler*in</label><div class="ab-wert">${e(namen.length > 3 ? `${namen.slice(0, 3).join(', ')} und ${namen.length - 3} weitere` : namen.join(', '))}</div>
        <label for="ab-vd">Von</label><div class="ab-zeile"><input type="date" id="ab-vd" data-ab-vd value="${vorgabe.vonDatum}" ${verwalten ? '' : 'disabled'}><input type="time" step="60" data-ab-vz value="${e(vorgabe.vonZeit)}" aria-label="Von Uhrzeit"></div>
        <label for="ab-bd">Bis</label><div class="ab-zeile"><input type="date" id="ab-bd" data-ab-bd value="${vorgabe.bisDatum}" ${verwalten ? '' : 'disabled'}><input type="time" step="60" data-ab-bz value="${e(vorgabe.bisZeit)}" aria-label="Bis Uhrzeit"></div>
        <span></span><div class="ab-schnell">
          <button type="button" class="ad-pille klein" data-ab-schnell="stunde" title="${e(start)} – ${e(ende)}">ganze Stunde</button>
          ${laeuft ? '<button type="button" class="ad-pille klein" data-ab-schnell="spaet" title="abwesend von Stundenbeginn bis jetzt">verspätet bis jetzt</button><button type="button" class="ad-pille klein" data-ab-schnell="frueh" title="abwesend ab jetzt bis Stundenende">ab jetzt gegangen</button>' : ''}
          <button type="button" class="ad-pille klein" data-ab-schnell="rest" title="abwesend bis ${e(tagesende)}">Rest des Tages</button></div>
        <span></span><div class="vs-vorschau" data-ab-vorschau></div>
        <label for="ab-grund">Abwesenheitsgrund</label><select id="ab-grund" data-ab-grund>${gruendeWahl}</select>
        <label for="ab-text">Text</label><input id="ab-text" data-ab-text maxlength="500" value="${e(f?.text ?? '')}">
        <label>Status</label><div class="ab-status">${radio('entschuldigt', 'entschuldigt')}${radio('unentschuldigt', 'nicht entschuldigt')}${radio('offen', 'offen')}
          ${verwalten ? '' : '<div class="ab-hinweis">Status und Zeiträume über den Tag hinaus ändert die Klassenleitung.</div>'}</div>
      </div>
      ${f?.erstellt ? `<div class="ad-fuss">Erfasst am ${zeitstempel(f.erstellt)} von ${e(f.lehrer || '')}</div>` : ''}
      <div class="ad-knoepfe ab-knoepfe"><button type="button" class="sd-orange" data-ab-speichern>Speichern</button><button type="button" class="ad-pille" data-ad-zu>Abbrechen</button>
        ${f ? '<button type="button" class="ad-pille rot" data-ab-loeschen>Löschen</button>' : ''}</div>`, (o, zu) => {
      const q = x => o.querySelector(x);
      const vorschau = () => {
        const vd = q('[data-ab-vd]').value, bd = q('[data-ab-bd]').value, vz = q('[data-ab-vz]').value, bz = q('[data-ab-bz]').value;
        const el = q('[data-ab-vorschau]');
        if (!vd || !bd || !vz || !bz || `${bd}T${bz}` <= `${vd}T${vz}`) { el.className = 'vs-vorschau warnung'; el.innerHTML = '<i class="bi bi-exclamation-triangle-fill"></i> Das Ende muss nach dem Beginn liegen.'; return false; }
        const mehrtaegig = vd !== bd || vd !== k.datum;
        const ab = vd === k.datum ? Math.max(minutenVon(vz), minutenVon(start)) : minutenVon(start);
        const bis = bd === k.datum ? Math.min(minutenVon(bz), minutenVon(ende)) : minutenVon(ende);
        const min = Math.max(0, bis - ab);
        let text;
        if (min === 0) text = 'In dieser Stunde anwesend.';
        else if (ab === minutenVon(start) && bis === minutenVon(ende)) text = 'Diese Stunde ganz abwesend.';
        else if (ab === minutenVon(start)) text = `Verspätung: ${min} min (gekommen um ${uhrPlus(start, min)}).`;
        else text = `Vorzeitig gegangen: ${min} min (ab ${uhrPlus(start, ab - minutenVon(start))}).`;
        const regeln = a.regeln ?? {};
        const gewertet = min > 0 && min < minutenVon(ende) - minutenVon(start) && regeln.abwesendAbMinuten > 0 && min >= regeln.abwesendAbMinuten;
        el.className = `vs-vorschau ${gewertet ? 'warnung' : ''}`;
        el.innerHTML = `<i class="bi bi-info-circle"></i> ${e(text)}${gewertet ? ' Zählt als Fehlstunde.' : ''}${mehrtaegig || bz > ende ? ' Weitere Stunden im Zeitraum werden ebenfalls als Fehlzeiten eingetragen.' : ''}`;
        return true;
      };
      o.querySelectorAll('[data-ab-vd], [data-ab-bd], [data-ab-vz], [data-ab-bz]').forEach(x => x.oninput = vorschau);
      o.querySelectorAll('[data-ab-schnell]').forEach(b => b.onclick = () => {
        const art = b.dataset.abSchnell;
        q('[data-ab-vd]').value = k.datum; q('[data-ab-bd]').value = k.datum;
        if (art === 'stunde') { q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = ende; }
        if (art === 'spaet') { q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = jetztZeit; }
        if (art === 'frueh') { q('[data-ab-vz]').value = jetztZeit; q('[data-ab-bz]').value = ende; }
        if (art === 'rest') { if (q('[data-ab-vz]').value >= ende) q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = tagesende > ende ? tagesende : ende; }
        vorschau();
      });
      vorschau();
      q('[data-ab-speichern]').onclick = async ev => {
        if (!vorschau()) return;
        const knopf = ev.currentTarget;
        knopf.disabled = true;
        try {
          await senden('/api/app/klassenbuch/abwesenheit', {
            schuelerIds: ids, von: `${q('[data-ab-vd]').value}T${q('[data-ab-vz]').value}`, bis: `${q('[data-ab-bd]').value}T${q('[data-ab-bz]').value}`,
            grund: q('[data-ab-grund]').value, text: q('[data-ab-text]').value, status: verwalten ? o.querySelector('[name=ab-status]:checked')?.value ?? 'offen' : null,
            datum: k.datum, stunde: k.von, unterricht: k.unterricht,
          });
          auswahl.clear();
          zu();
          await anwesenheitNeu();
          geaendert = true;
          toast('Abwesenheit gespeichert.');
        } catch (err) { toast(err.message, true); knopf.disabled = false; return; }
        zeichnen();
      };
      q('[data-ab-loeschen]')?.addEventListener('click', () => { zu(); abwesenheitEntfernen(ids); });
    }, 520);
  }

  function eintragDialog(vorauswahl) {
    const sl = daten.anwesenheit?.schueler ?? [];
    const kl = klassenListe();
    const zeitWahl = [...new Set([...zeiten.filter(z => z.nr >= k.von && z.nr <= k.bis).map(z => z.beginn), k.beginn].filter(Boolean))].sort();
    altDialog('Klassenbucheintrag', `${kopfDl()}
      <div class="ad-reihe"><label class="ad-feld">Datum<input type="date" data-ad-datum value="${k.datum}"></label>
        <label class="ad-feld">Zeit<select data-ad-zeit>${zeitWahl.map(z => `<option>${e(z)}</option>`).join('')}</select></label></div>
      <div class="ad-reihe"><label class="ad-feld breit">Schüler*innen<select multiple size="9" data-ad-schueler>${sl.map(x => `<option value="${x.id}" ${vorauswahl.includes(x.id) ? 'selected' : ''}>${e(x.name)} ${e(x.vorname)}</option>`).join('')}</select></label>
        <label class="ad-feld">Klasse<select multiple size="9" data-ad-klassen>${kl.map(x => `<option>${e(x)}</option>`).join('')}</select></label></div>
      <label class="ad-feld">Eintragskategorie<select data-ad-kategorie><option value="">Bitte wählen Sie</option>${(daten.arten?.length ? daten.arten : ['Vermerk']).map(a => `<option>${e(a)}</option>`).join('')}</select></label>
      <label class="ad-feld">Klassenbucheintrag<textarea rows="3" maxlength="1000" data-ad-text></textarea></label>
      <div class="ad-zeichen">Noch mögliche Zeichen: <input readonly data-ad-rest value="1000" aria-label="Noch mögliche Zeichen"></div>
      <div class="ad-knoepfe"><button type="button" class="ad-pille" data-ad-speichern>Speichern</button><button type="button" class="ad-pille" data-ad-zu>Schließen</button></div>`, (o, zu) => {
      const text = o.querySelector('[data-ad-text]');
      text.oninput = () => { o.querySelector('[data-ad-rest]').value = 1000 - text.value.length; };
      o.querySelector('[data-ad-speichern]').onclick = async () => {
        const art = o.querySelector('[data-ad-kategorie]').value;
        if (!art) return toast('Bitte eine Eintragskategorie wählen.', true);
        if (!text.value.trim()) return toast('Bitte einen Text eingeben.', true);
        const datum = o.querySelector('[data-ad-datum]').value, zeit = o.querySelector('[data-ad-zeit]').value;
        const ids = [...o.querySelector('[data-ad-schueler]').selectedOptions].map(x => Number(x.value));
        const klassen = [...o.querySelector('[data-ad-klassen]').selectedOptions].map(x => x.value);
        try {
          if (ids.length) for (const id of ids) daten.vermerke.unshift(await senden('/api/app/klassenbuch/vermerke', { datum, klasse: sl.find(x => x.id === id)?.klasse || kl[0], schuelerId: id, art, text: text.value, zeit }));
          else for (const x of (klassen.length ? klassen : kl.slice(0, 1))) daten.vermerke.unshift(await senden('/api/app/klassenbuch/vermerke', { datum, klasse: x, schuelerId: null, art, text: text.value, zeit }));
          auswahl.clear();
          toast('Klassenbucheintrag gespeichert.');
          zu(); zeichnen();
        } catch (err) { toast(err.message, true); }
      };
    }, 470);
  }

  // Start
  box.innerHTML = '<div class="py-5"></div>';
  modal.show();
  laden().then(() => { reiter = mitKlassenbuch() ? 'klassenbuch' : 'details'; zeichnen(); });
  return box;
}

