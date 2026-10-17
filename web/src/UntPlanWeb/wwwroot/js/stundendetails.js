// Stundendetails wie im Original (Screenshots „Klassenbuch“ und „Details“):
// Kopf „Klassen | Unterricht | Fach“, darunter „Wochentag | Datum | Zeit | Raum | Lehrkraft“, Reiter Klassenbuch und Details.
// Klassenbuch: Schülerkacheln (Foto/Platzhalter, Statuspunkt), Alle auswählen/Auswahl löschen, abwesend/Klassenbucheintrag,
// Lehrstoff mit Stift, Abwesende Schüler*innen als Tabelle im neuen Design (Fußzeile mit Anzahl und „Verlängern“), Hausaufgaben (Liste mit Fälligkeit),
// Klassenbucheinträge mit „+“, Klassendienste mit Besen, „Ticket erstellen“, unten die Statusleiste
// („Lehrstoffeintragung vorhanden“, „Abwesenheiten wurden kontrolliert“).
// Details: nur „Findet online statt“, Lehrstoff und Vertretungstext (wenn vorhanden), Hausaufgaben, Notizen für Schüler*innen
// und für Lehrkräfte; unten „Vorherige Stunde“, „Nächste Stunde“, „Prüfung erstellen“ und „Raum buchen“.
import { e, heuteIso, holen, senden, toast, WOCHENTAGE } from './ui.js';
import { kbDialog, kbBestaetigen as bestaetigen, stufe, fussUebernehmen } from './kbdialog.js';
import * as sitz from './ansichten/sitzplan.js';
import { hausaufgabeDetails, hausaufgabeFormular } from './hausaufgabe.js';
import { raumwahl, raumListe } from './raumwahl.js';

// Prüfungsarten und Notenschemen aus den Stammdaten (einmal je Sitzung geladen; bis dahin der Grundbestand)
let PRUEFUNGSARTEN = ['Klassenarbeit', 'Klausur', 'Test', 'Abschlussprüfung', 'Präsentation', 'Referat', 'Vergleichsarbeit'].map(name => ({ name, schema: '' }));
let NOTENSCHEMEN = [];
let pruefungsartenGeladen = null;
const pruefungsartenLaden = () => pruefungsartenGeladen ??= Promise.all([holen('/api/app/pruefungsarten'), holen('/api/app/notenschemen')]).then(([a, n]) => {
  if (a.arten?.length) PRUEFUNGSARTEN = a.arten.map(x => ({ name: x.name || x.kurz, schema: x.notenschema }));
  NOTENSCHEMEN = (n.schemen ?? []).filter(x => x.aktiv);
}).catch(() => { pruefungsartenGeladen = null; });
/** Text einer neuen Prüfung: „Art: Text – Notenschema: … – Teilnehmende: …“. */
const pruefungText = (f, teil) => [f('art') ? `${f('art')}${f('text') ? ': ' + f('text') : ''}` : f('text'),
  f('notenschema') ? `Notenschema: ${f('notenschema')}` : '', teil].filter(Boolean).join(' – ');
/** Prüfungsart ohne Notenschema: die Lehrkraft wählt ein individuelles Schema (Auswahl nur dann sichtbar). */
function notenschemaBinden(wurzel) {
  const art = wurzel.querySelector('[name=art]');
  const feld = wurzel.querySelectorAll('[data-ns]');
  if (!art || !feld.length) return;
  const zeigen = () => {
    const a = PRUEFUNGSARTEN.find(x => x.name === art.value);
    const ohne = !!a && !a.schema;
    feld.forEach(x => { x.hidden = !ohne; });
    if (!ohne) wurzel.querySelector('[name=notenschema]').value = '';
  };
  art.addEventListener('change', zeigen);
  zeigen();
}
const W = '/img/wu/';

const wochentag = iso => WOCHENTAGE[(new Date(iso + 'T12:00').getDay() + 6) % 7];
const datumDe = iso => { if (!iso) return ''; const [j, m, t] = iso.split('-'); return `${t}.${m}.${j}`; };
const kuerzel = l => (l || []).map(w => w.k || w.alt).filter(Boolean);
const plusTage = (iso, n) => { const d = new Date(iso + 'T12:00'); d.setDate(d.getDate() + n); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
const bildIcon = (datei, klasse = '') => `<img class="kb-icon ${klasse}" src="${W}${datei}" alt="" aria-hidden="true">`;
/** Uhrzeit „HH:MM“ in Minuten seit Mitternacht bzw. um n Minuten verschoben. */
const minutenVon = hhmm => { const [h, m] = String(hhmm || '0:0').split(':').map(Number); return (h || 0) * 60 + (m || 0); };
const uhrPlus = (hhmm, n) => { if (!hhmm) return ''; const x = Math.max(0, Math.min(1439, minutenVon(hhmm) + Number(n || 0))); return `${String(Math.floor(x / 60)).padStart(2, '0')}:${String(x % 60).padStart(2, '0')}`; };
/** Link zum Online-Unterricht: nur absolute http-/https-Adressen (wie auf dem Server geprüft). */
const sichererLink = x => { try { const u = new URL(x); return (u.protocol === 'http:' || u.protocol === 'https:') && !!u.hostname && !/\s/.test(x); } catch { return false; } };
const maskeIcon = datei => `<span class="u-sym wu kb-icon" style="--wu:url(${W}${datei})" aria-hidden="true"></span>`;
/** Weiche Linien-Symbole (dünne, abgerundete Striche) für „abwesend“ bzw. „anwesend“ in den Schülerkacheln. */
const KREUZ = '<svg class="sd-linie" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.6"/><path d="M5.9 5.9l4.2 4.2M10.1 5.9l-4.2 4.2"/></svg>';
const HAKEN = '<svg class="sd-linie" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.6"/><path d="M5.3 8.2l1.9 1.9 3.6-3.9"/></svg>';
/** Plus als feines Linien-Symbol (runde Enden, genau mittig im runden Knopf). */
const PLUS_SYM = '<svg class="sd-plus-sym" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 3.25v9.5M3.25 8h9.5"/></svg>';

/**
 * Wert im Kopf wie im Original, ohne zusätzliches Label: geänderter Wert „B070 → C197“ mit altem Wert grau durchgestrichen,
 * entfallener Wert durchgestrichen. Mit typ werden Klassen, Lehrkräfte und Räume zu Links auf den Stundenplan.
 */
const KEIN_ELEMENT = /^(-{3}|\?{3}|\+{1,3})$/;
function planLink(typ, kurz) {
  if (!typ || !kurz || KEIN_ELEMENT.test(kurz)) return e(kurz);
  return `<a href="#" class="sd-link" data-plan="${typ}" data-element="${e(kurz)}" data-hover="Im Stundenplan anzeigen" data-hover-art="dunkel" data-hover-pos="unten">${e(kurz)}</a>`;
}
function wertKopf(liste, max = 6, typ = null, lehrkraft = typ === 'lehrer') {
  const l = (liste || []).filter(w => w.k || w.alt);
  const teil = l.length > max + 1 ? l.slice(0, max - 1) : l;
  // Kopf ohne Labels: Änderungen nur als Text – alt durchgestrichen → neu, entfallener Wert (z. B. offene Vertretung) nur durchgestrichen
  // auch der ersetzte (durchgestrichene) Wert – z. B. die abwesende Lehrkraft – führt zu seinem Plan
  const alt = w => typ ? `<s class="sd-alt">${planLink(typ, w.alt)}</s>` : `<s class="sd-alt">${e(w.alt)}</s>`;
  // Wechsel bei Lehrkraft, Raum und Fach einheitlich mit Pfeil: „alt (durchgestrichen) → neu“
  const zwischen = `<span class="sd-pfeil${lehrkraft ? ' sd-pfeil-lehrer' : ''}">→</span>`;
  return teil.map(w => w.alt && !w.k ? alt(w)
    : w.alt ? `${alt(w)} ${zwischen} ${planLink(typ, w.k)}` : planLink(typ, w.k)).join(', ')
    // sehr viele Elemente (z. B. alle Lehrkräfte): gekürzt „… +N“, Hover mit der vollständigen Liste
    + (teil.length < l.length ? ` <span class="sd-mehr" data-hover="${e(l.map(w => w.k || w.alt).join(', '))}" data-hover-art="dunkel" data-hover-pos="unten">… +${l.length - teil.length}</span>` : '');
}

export const LEHRFORMEN = ['Einzelarbeit', 'Partnerarbeit', 'Gruppenarbeit', 'Frontalunterricht', 'Unterrichtsgespräch', 'Projektarbeit', 'Stationenlernen', 'Präsentation', 'Diskussion', 'Distanzunterricht'];
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
  let geaendert = false;
  // Abmelden bei offenem Dialog: sofort schließen, keine Folgeaktionen (Neuladen, Navigation) und kein Zeichnen nach laufenden Anfragen
  let beendet = false;
  const abmeldung = () => { beendet = true; geaendert = false; try { modal.dispose(); } catch { /* schon weg */ } el.remove(); };
  window.addEventListener('untplan:abmeldung', abmeldung, { once: true });
  el.addEventListener('hidden.bs.modal', () => setTimeout(() => { window.removeEventListener('untplan:abmeldung', abmeldung); if (!beendet) { modal.dispose(); el.remove(); } }));
  el.addEventListener('hide.bs.modal', () => { if (geaendert && !beendet) setTimeout(() => kontext.nachSpeichern ? kontext.nachSpeichern() : kontext.navigieren?.(location.hash)); });

  let k = karte;
  let reiter = null;
  let daten = null;
  let zeiten = [];
  const auswahl = new Set();
  const abwAuswahl = new Set();
  let offen = null;          // geöffneter Editor: 'thema', 'hausaufgaben', 'vermerk', 'ticket', 'abw:<id>'
  let hwBearbeiten = null;   // Id der bearbeiteten Hausaufgabe
  let sortierung = 'name';
  let raumModus = null;      // Raumwahl: { art: 'ersetzen' | 'hinzufuegen', raum, gewaehlt, filter }
  const raumFilter = {};     // Filter der Raumliste (Suche, Gebäude, Abteilung, Kapazität, nur freie) – bleibt für das Fenster erhalten
  // Doppel-/Blockstunden: null = ganzer Block, sonst die gewählte Einzelstunde (Einträge gelten dann nur für sie)
  let einzel = null;

  const istEntfall = () => k.status === 'entfall' || (k.status === 'abwesend' && kontext.typ !== 'lehrer' && (kontext.typ === 'raum' || !k.lehrerAbwesend));
  const mitKlassenbuch = () => kollegium && k.unterricht && !['entfall', 'abwesend', 'vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
  // Prüfung auch in Vertretungs- und Zusatzstunden (ohne eigenen Unterricht), nur nicht in entfallenen Stunden oder Veranstaltungen
  const darfPruefung = () => kollegium && kuerzel(k.klassen).length && k.datum >= heuteIso() && !['entfall', 'abwesend', 'veranstaltung', 'vorgemerkt'].includes(k.status);
  const darfRaum = () => darfPruefung() && k.unterricht && (daten?.darf || ich.admin);
  const klassenListe = () => kuerzel(k.klassen);
  // gekoppelte Karten mit unterschiedlichen Fächern teilen sich den Unterricht: Klassenbuch der Lehrkraft dieser Karte
  /** Block mit mehreren Stunden (Auswahl „Ganzer Block“ / Einzelstunde oben rechts). */
  const istBlock = () => !!k.unterricht && k.bis > k.von && !['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
  /** Abfrage- bzw. Body-Zusatz für die gewählte Einzelstunde. */
  const ep = () => einzel ? `&einzelstunde=${einzel}` : '';
  const eb = () => einzel ? { einzelstunde: einzel } : {};
  /** Gewählter Bereich: Einzelstunde bzw. ganzer Block mit Zeiten. */
  const bereich = () => {
    if (!einzel) return { von: k.von, bis: k.bis, beginn: k.beginn, ende: k.ende };
    const z = (daten?.einzelstunden ?? []).find(x => x.stunde === einzel) ?? zeiten.find(x => x.nr === einzel) ?? {};
    return { von: einzel, bis: einzel, beginn: z.beginn || k.beginn, ende: z.ende || k.ende };
  };
  /** Einzelstunden des Blocks für das Auswahlmenü (vom Server, sonst aus dem Zeitraster). */
  const einzelstunden = () => daten?.einzelstunden?.length ? daten.einzelstunden
    : Array.from({ length: k.bis - k.von + 1 }, (_, i) => k.von + i).map(n => { const z = zeiten.find(x => x.nr === n) ?? {}; return { stunde: n, beginn: z.beginn || '', ende: z.ende || '', eintrag: false }; });
  const einzigeLehrkraft = () => { const l = kuerzel(k.lehrer); return l.length === 1 && !(k.lehrer[0].alt && !k.lehrer[0].k) ? `&lehrer=${encodeURIComponent(l[0])}` : ''; };

  async function laden() {
    daten = { eintrag: null, blockEintrag: null, einzelstunden: [], einzelEintraege: [], darf: false, anwesenheit: null, vermerke: [], arten: [], befreit: new Set(), hausaufgaben: { liste: [], faellig: [], darf: false }, dienste: [], gruende: [] };
    if (!zeiten.length) try { zeiten = (await kontext.elemente?.())?.stunden ?? []; } catch { /* ohne Zeiten */ }
    if (!k.unterricht || ['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status)) return;
    const kl = klassenListe();
    const [st, hw] = await Promise.all([
      holen(`/api/app/klassenbuch/stunde?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}${einzigeLehrkraft()}${ep()}`).catch(() => null),
      holen(`/api/app/klassenbuch/hausaufgabenliste?datum=${k.datum}&stunde=${k.von}&bis=${k.bis}&unterricht=${k.unterricht}&fach=${encodeURIComponent(k.fach || '')}&klassen=${encodeURIComponent(kl.join(','))}${ep()}`).catch(() => null),
    ]);
    if (st) {
      daten.eintrag = st.eintrag; daten.darf = st.darf;
      daten.blockEintrag = st.blockEintrag ?? (einzel ? null : st.eintrag);
      daten.einzelstunden = st.einzelstunden ?? []; daten.einzelEintraege = st.einzelEintraege ?? [];
    }
    if (hw) daten.hausaufgaben = hw;
    if (kollegium) pruefungsartenLaden();
    if (mitKlassenbuch()) {
      const [anw, verm, dienste, gruende, befreit] = await Promise.all([
        holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}${ep()}`).catch(() => null),
        Promise.all(kl.map(x => holen(`/api/app/klassenbuch/vermerke?klasse=${encodeURIComponent(x)}&von=${plusTage(k.datum, -365)}&bis=${k.datum}`).catch(() => null))),
        Promise.all(kl.map(x => holen(`/api/app/klassendienste?klasse=${encodeURIComponent(x)}&von=${k.datum}&bis=${k.datum}`).catch(() => null))),
        holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => []),
        holen(`/api/app/klassenbuch/befreit?datum=${k.datum}&klassen=${encodeURIComponent(kl.join(','))}&fach=${encodeURIComponent(k.fach || '')}&beginn=${encodeURIComponent(k.beginn || '')}`).catch(() => []),
      ]);
      daten.anwesenheit = anw;
      // Klassenbuch-Einstellungen der Hauptseite: Sortierung (Vorgabe), Einträge ab, fällige Hausaufgaben
      const kbe = anw?.einstellungen;
      if (kbe) {
        sitz.schulVorgabeSetzen?.({ sortierung: kbe.sortierung === 'vorname' ? 'vorname' : 'name', richtung: kbe.richtung, nachKlasse: !!kbe.nachKlasse });
        if (kbe.faelligeHausaufgaben === false) daten.hausaufgaben = { ...daten.hausaufgaben, faellig: [] };
      }
      daten.vermerke = verm.filter(Boolean).flatMap(v => v.vermerke ?? []).filter(v => !kbe?.eintraegeAb || v.datum >= kbe.eintraegeAb)
        .sort((a, b) => b.datum.localeCompare(a.datum) || b.id - a.id);
      daten.arten = verm.find(Boolean)?.arten ?? [];
      daten.dienste = dienste.filter(Boolean).flatMap(d => d.zuordnungen ?? []);
      daten.gruende = (gruende ?? []).filter(g => g.aktiv !== false);
      daten.befreit = new Set(befreit ?? []);
      // Sitzplan der Stunde (Unterricht vor Raum vor Klasse) für die Ansicht „Sitzplan“ und den Editor
      sitz.cssLaden();
      daten.sitz = await sitz.stundeLaden(sitz.kontextVon(k)).catch(() => null);
    }
  }

  // ---------------------------------------------------------------- Kopf, Fuß, Aufbau

  /** Lehrkräfte im Kopf; weggefallene Bereitschaft (Lehrkraft abwesend): Kürzel durchgestrichen im grünen Label wie auf der Karte. */
  function lehrerKopf() {
    const typ = kollegium ? 'lehrer' : null;
    if (k.bereitschaft && k.status === 'abwesend' && k.lehrer.length && k.lehrer.every(w => !w.k && w.alt))
      return k.lehrer.map(w => `<span class="sd-lbl-weg">${planLink(typ, w.alt)}</span>`).join(' ');
    return wertKopf(k.lehrer, 6, typ, true);
  }

  function kopf() {
    // „Klassen | Gruppe | Fach“ – Klassen und Gruppe verlinkt („Im Stundenplan anzeigen“)
    // Mitklassen (gekoppelte Parallelklassen) mit anzeigen; fehlende Mitklasse nur durchgestrichen (ohne Label)
    const alleKlassen = [...k.klassen, ...(k.mitklassen || [])];
    const klassenTeil = alleKlassen.length ? wertKopf(alleKlassen, 6, kollegium || ich.element ? 'klasse' : null) : '';
    const ersteKlasse = kuerzel(k.klassen)[0];
    const gruppe = k.gruppe ? (ersteKlasse ? `<a href="#" class="sd-link" data-plan="klasse" data-element="${e(ersteKlasse)}" data-hover="Im Stundenplan anzeigen" data-hover-art="dunkel" data-hover-pos="unten">${e(k.gruppe)}</a>` : e(k.gruppe))
      : ''; // kein Fach-Langname im Kopf – nur Kürzel
    // Veranstaltung: oben keine Bezeichnung (der Name steht unten unter „Information zur Stunde“) – nur Fach/Klassen, wenn eingetragen
    const fachTeil = k.fachAlt && !istEntfall() ? `<s class="sd-alt">${e(k.fachAlt)}</s> <span class="sd-pfeil">→</span> ${e(k.fach)}`
      : k.status === 'veranstaltung' ? e(k.fach || '') : e(k.fach || k.text || k.art || 'Stunde');
    const titel = [klassenTeil, gruppe, fachTeil].filter(Boolean);
    const b = bereich();
    const zeile = [wochentag(k.datum), datumDe(k.datum), b.beginn ? `${b.beginn}-${b.ende}` : `${b.von === b.bis ? b.von + '.' : b.von + '.–' + b.bis + '.'} Std.`,
      wertKopf(k.raeume, 6, 'raum'), lehrerKopf()].filter(Boolean);
    // Wie im Original: bei Entfall Kopf hellgrau und darunter roter Balken „ENTFÄLLT“; Prüfung/Veranstaltung als dezente graue Pille,
    // sonst keine Labels (Änderungen stehen als Text: alt durchgestrichen | neu bzw. → beim Raum)
    // zusätzliche Unterrichtsstunde: Badge (ein von Hand eingegebener Vertretungstext steht zusätzlich unter „Details“).
    // Badges in der oberen Zeile bei Klasse/Fach, dunkelgrau
    const pille = istEntfall() ? '' : k.status === 'klausur' ? 'Prüfung' : k.zusaetzlich ? 'Zusätzl. Unterrichtsstunde' : '';
    const leiste = istEntfall();
    const reiterListe = reiter === 'pruefung' ? [['pruefung', 'Prüfung erstellen']] : reiter === 'raum' ? [['raum', raumModus?.art === 'ersetzen' ? `${raumModus.raum} ersetzen` : 'Räume hinzufügen']]
      : [...(mitKlassenbuch() ? [['klassenbuch', 'Klassenbuch']] : []), ['details', 'Details']];
    return `<div class="sd-kopf ${leiste ? 'grau' : ''}">
        <div class="sd-titel"><h2>${titel.join('<span class="trenn">|</span>')}${pille ? `<span class="sd-statuspille">${pille}</span>` : ''}${einzel ? `<span class="sd-statuspille sd-einzelpille">${einzel}. Stunde</span>` : ''}</h2>
          <div class="sd-zeile">${zeile.join('<span class="trenn">|</span>')}</div></div>
        <div class="sd-knoepfe">${istBlock() && !reiter?.match(/^(pruefung|raum)$/) ? blockWahl()
          : '<button type="button" data-bs-dismiss="modal" title="Zur Ansicht zurück" aria-label="Zur Ansicht zurück"><i class="bi bi-chevron-down"></i></button>'}
          <button type="button" data-bs-dismiss="modal" title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg"></i></button></div>
      </div>
      ${leiste ? '<div class="sd-entfall-leiste" role="status">ENTFÄLLT</div>' : ''}
      <ul class="nav sd-reiter">${reiterListe.map(([id, t]) => `<li><button type="button" data-reiter="${id}" class="${reiter === id ? 'aktiv' : ''}">${t}</button></li>`).join('')}</ul>`;
  }

  /** Pfeil-runter oben rechts: Umschalten zwischen ganzem Block und den Einzelstunden (Einträge gelten dann nur für die gewählte Stunde). */
  function blockWahl() {
    const punkt = x => x ? '<span class="sd-blockwahl-punkt" title="Eigene Einträge vorhanden" aria-label="Eigene Einträge vorhanden"></span>' : '';
    const opt = (wert, text) => `<button type="button" role="menuitemradio" aria-checked="${(einzel ?? 0) === wert}" data-einzel="${wert}" class="${(einzel ?? 0) === wert ? 'an' : ''}">
        <i class="bi bi-check2" aria-hidden="true"></i><span>${text}</span></button>`;
    return `<div class="sd-blockwahl"><button type="button" data-blockwahl aria-haspopup="menu" aria-expanded="false" title="Ganzer Block oder Einzelstunde" aria-label="Ganzer Block oder Einzelstunde"><i class="bi bi-chevron-down"></i></button>
      <div class="sd-blockwahl-menue d-none" role="menu">${opt(0, `Ganzer Block (${k.von}.–${k.bis}. Stunde)`)}
        ${einzelstunden().map(z => opt(z.stunde, `${z.stunde}. Stunde${z.beginn ? ` ${e(z.beginn)}–${e(z.ende)}` : ''}${punkt(z.eintrag)}`)).join('')}</div></div>`;
  }

  function fuss() {
    if (reiter === 'pruefung') return `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-speichern-pruefung>Speichern</button></div>`;
    if (reiter === 'raum') return `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-raum-speichern ${raumModus?.gewaehlt ? '' : 'disabled'}>Speichern</button></div>`;
    if (reiter === 'klassenbuch') {
      const a = daten.anwesenheit;
      // Lehrstoff des Blocks gilt auch für jede seiner Einzelstunden
      const stoff = !!(daten.eintrag?.thema || (einzel && daten.blockEintrag?.thema));
      const b = bereich();
      const kontrolliert = !!a?.geprueft;
      // Kontrolle erst ab Beginn der Stunde (davor steht – wie im Vorbild – „Abwesenheitskontrolle ab: …“)
      const jetzt = new Date(), jetztMin = jetzt.getHours() * 60 + jetzt.getMinutes();
      const kannKontrolle = a?.darf && (k.datum < heuteIso() || (k.datum === heuteIso() && (!b.beginn || minutenVon(b.beginn) <= jetztMin)));
      // wie im Vorbild: rechts orange Pille(n), daneben „Abwesenheitskontrolle ab: Datum - Uhrzeit“ (Beginn der Stunde)
      const ab = `${datumDe(k.datum)} - ${e(b.beginn || '')}`;
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
      ${(k.status === 'klausur' || (k.status === 'vorgemerkt' && /^klausur/i.test(k.art || ''))) && kollegium && k.datum >= heuteIso() ? `<button type="button" class="sd-pille gross" data-pruefung-editieren>Prüfung editieren</button>
        <button type="button" class="sd-pille gross rot" data-pruefung-loeschen>Prüfung löschen</button>`
        : darfPruefung() ? '<button type="button" class="sd-pille gross" data-pruefung>Prüfung erstellen</button>' : ''}
      </div>`;
  }

  function zeichnen() {
    if (beendet || !el.isConnected) return;
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
    return sitz.sortieren(l, sitz.einstellungen()); // Klassenbuch-Einstellungen (Zahnrad)
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
    const sitzW = sitz.einstellungen();
    const kachel = s => {
      const f = fehlzeitVon(s.id);
      const st = !f ? 'da' : f.art === 'verspaetet' ? 'spaet' : f.art === 'vorzeitig' ? 'frueh' : 'weg';
      const befreit = daten.befreit?.has(s.id);
      const status = st === 'da' ? bildIcon('exact/status_present.svg', 'sd-statusbild') : st === 'weg' ? `<span class="sd-statusbild weg">${KREUZ}</span>`
        : st === 'frueh' ? '<span class="sd-statusbild frueh"><i class="bi bi-box-arrow-right"></i></span>' : '<span class="sd-statusbild spaet"><i class="bi bi-clock-fill"></i></span>';
      const zusatz = f && st !== 'weg' ? `<span class="sd-minuten ${f.fehlstunden ? 'gewertet' : ''}">${st === 'spaet' ? '+' : '−'}${f.minuten}′</span>` : '';
      return `<div class="sd-kachel ${st} ${befreit && st === 'da' ? 'befreit' : ''} ${auswahl.has(s.id) ? 'gewaehlt' : ''}" data-sid="${s.id}" role="button" tabindex="0" aria-pressed="${auswahl.has(s.id)}" title="${befreit ? 'vom Unterricht befreit' : st === 'weg' ? 'abwesend' : st === 'spaet' ? `verspätet (${f.minuten} min)` : st === 'frueh' ? `vorzeitig gegangen (${f.minuten} min)` : 'anwesend'}${f?.fehlstunden && st !== 'weg' ? ' – als Fehlstunde gewertet' : ''}">
        ${status}${zusatz}
        <span class="sd-avatar"><img src="${W}avatar-default-white.svg" alt=""></span>
        <span class="sd-name">${sitz.nameHtml(s, sitzW)}</span>
        ${darf && daten.anwesenheit?.einstellungen?.direkteEingabe !== false ? `<button type="button" class="sd-umschalten" data-umschalten="${s.id}" title="${st === 'da' ? 'Als abwesend markieren' : 'Als anwesend markieren'}" aria-label="${st === 'da' ? 'Als abwesend markieren' : 'Als anwesend markieren'}">${st === 'da' ? KREUZ : HAKEN}</button>` : ''}
      </div>`;
    };
    const kacheln = sl.map(kachel).join('');
    const werkzeuge = `<button type="button" class="sd-werkzeug" data-werkzeug="mail" title="Mitteilung schreiben" aria-label="Mitteilung schreiben"><i class="bi bi-envelope"></i></button>
      <button type="button" class="sd-werkzeug" data-werkzeug="druck" title="Drucken" aria-label="Drucken"><i class="bi bi-printer"></i></button>
      <button type="button" class="sd-werkzeug" data-werkzeug="einst" title="Klassenbuch-Einstellungen" aria-label="Klassenbuch-Einstellungen"><i class="bi bi-gear"></i></button>`;
    const sitzUmschalter = sl.length ? `<span class="sp-seg klein sp-umschalter" role="radiogroup" aria-label="Ansicht der Schüler*innen">${[['liste', 'grid-3x3-gap', 'Liste'], ['sitzplan', 'easel', 'Sitzplan']]
      .map(([v, i, t]) => `<button type="button" role="radio" data-sitz-ansicht="${v}" class="${(sitzW.ansicht === 'sitzplan') === (v === 'sitzplan') ? 'an' : ''}" aria-checked="${(sitzW.ansicht === 'sitzplan') === (v === 'sitzplan')}"><i class="bi bi-${i}"></i>${t}</button>`).join('')}</span>` : '';
    const listeHtml = `<div class="sd-kacheln ${sitzW.richtung === 'spalten' ? 'sp-spalten' : ''}">${kacheln}</div>`;
    const sitzHinweis = `<div class="sp-a-hinweis"><i class="bi bi-easel"></i>Für diese Stunde ist noch kein Sitzplan hinterlegt.${sitz.darfIrgendwas(daten.sitz) ? ' <button type="button" class="sp-knopf primaer" data-sitz-bearbeiten>Sitzplan erstellen</button>' : ''}</div>`;
    const schuelerTeil = !a ? '<p class="small text-body-secondary">Anwesenheit kann nicht geladen werden.</p>'
      : !sl.length ? '<p class="small text-body-secondary mb-0">Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen (UntPlanWeb → Einstellungen → „Schülerliste übertragen“).</p>'
      : `${darf ? '<div class="d-flex flex-wrap gap-2 mb-3"><button type="button" class="sd-pille klein" data-alle>Alle auswählen</button><button type="button" class="sd-pille klein" data-keine>Auswahl löschen</button></div>' : ''}
        ${sitzW.ansicht !== 'sitzplan' ? listeHtml : daten.sitz?.gueltig ? sitz.rasterHtml(daten.sitz, sl, kachel, sitzW) : sitzHinweis + listeHtml}
        ${darf ? `<div class="d-flex flex-wrap gap-2 mt-3">
          <button type="button" class="sd-pille klein grau" data-setzen="fehlt" ${auswahl.size ? '' : 'disabled'}>abwesend</button>
          <button type="button" class="sd-pille klein grau" data-setzen="zeitraum" ${auswahl.size ? '' : 'disabled'} title="Abwesenheit minutengenau erfassen (Verspätung, vorzeitig gegangen, Zeitraum)"><i class="bi bi-clock me-1"></i>Abwesenheit …</button>
          <button type="button" class="sd-pille klein grau" data-vermerk-auswahl ${auswahl.size ? '' : 'disabled'}>Klassenbucheintrag</button></div>` : ''}`;
    return `
      ${abschnitt(maskeIcon('klassenbuch/multiple-man-woman-2.svg'), 'Schüler*innen im Unterricht', sl.length, a ? sitzUmschalter + werkzeuge : '', schuelerTeil, 'kb-schueler')}
      ${lehrstoffHtml()}
      ${abwesendeHtml()}
      ${hausaufgabenHtml()}
      ${vermerkeHtml()}
      ${diensteHtml()}
      <div class="sd-abschnitt">${offen === 'ticket' ? ticketFormular() : '<button type="button" class="sd-pille klein" data-ticket>Ticket erstellen</button>'}</div>`;
  }

  /** Einträge der anderen Ebene: in der Einzelstunde der Blockeintrag (gilt mit), im Block die Einzelstunden-Einträge. */
  function ebenenHinweis(feld) {
    if (einzel) {
      const w = daten.blockEintrag?.[feld];
      return w ? `<p class="sd-text sd-ebene"><span class="sd-ebene-marke">Ganzer Block</span>${e(w)}</p>` : '';
    }
    return (daten.einzelEintraege ?? []).filter(x => x[feld]).map(x => `<p class="sd-text sd-ebene"><span class="sd-ebene-marke">${x.einzelstunde ?? x.stunde}. Stunde</span>${e(x[feld])}</p>`).join('');
  }

  function lehrstoffHtml() {
    const wert = daten.eintrag?.thema;
    const stift = daten.darf ? '<button type="button" class="sd-stift" data-bearbeiten="thema" title="Lehrstoff bearbeiten" aria-label="Lehrstoff bearbeiten"><i class="bi bi-pencil"></i></button>' : '';
    const zusatz = [daten.eintrag?.lehrform ? `Lehrform: ${daten.eintrag.lehrform}` : '', daten.eintrag?.anmerkung ? `Anmerkung: ${daten.eintrag.anmerkung}` : ''].filter(Boolean).join(' · ');
    const hinweis = ebenenHinweis('thema');
    const inhalt = (wert ? `<p class="sd-text">${e(wert)}</p>` : einzel && hinweis ? '' : `<p class="sd-text text-body-tertiary">${einzel ? 'Kein Lehrstoff für diese Stunde eingetragen' : 'Kein Lehrstoff eingetragen'}</p>`)
      + (zusatz ? `<p class="sd-text small text-body-secondary mt-1">${e(zusatz)}</p>` : '') + hinweis;
    return abschnitt(maskeIcon('klassenbuch/common-file-text.svg'), 'Lehrstoff', null, stift, inhalt);
  }

  function abwesendeHtml() {
    const a = daten.anwesenheit;
    if (!a) return '';
    const sl = a.schueler ?? [];
    const name = id => { const s = sl.find(x => x.id === id); return s ? `${s.name} ${s.vorname}` : ''; };
    const grundText = g => { const x = daten.gruende.find(y => y.kurz === g); return x ? x.kurz : g; };
    const b = bereich();
    const vonStunde = zeitVon(b.von), bisStunde = zeitVon(b.bis);
    const fruehere = zeiten.some(z => z.nr < b.von);
    const zeilen = [
      ...(a.fehlzeiten ?? []).map(f => ({ f, vor: false })),
      ...(a.vorstunden ?? []).map(f => ({ f, vor: true })),
      // Einstellung „Alle Abwesenheiten des Tages anzeigen“: Abwesenheiten späterer Stunden (nur zur Information)
      ...(a.weitere ?? []).map(f => ({ f, vor: true, spaeter: true })),
    ];
    const reihe = ({ f, vor, spaeter }) => {
      const sv = vor ? zeitVon(f.stunde) : vonStunde;
      // minutengenau: Zeitraum der ganzen Abwesenheit (vom Server), sonst aus der Stunde
      const beginn = !vor && f.vonZeit ? f.vonZeit : (sv.beginn || b.beginn || '');
      const ende = vor ? (zeitVon(f.stunde).ende || '') : f.bisZeit ? f.bisZeit : (bisStunde.ende || b.ende || '');
      const vonD = !vor && f.vonDatum ? f.vonDatum : (f.datum || k.datum), bisD = !vor && f.bisDatum ? f.bisDatum : (f.datum || k.datum);
      const anfang = !vor && f.art === 'fehlt' && fruehere && a.darf
        ? `<button type="button" class="sd-kreisknopf klein" data-tagesbeginn="${f.schuelerId}" title="Abwesenheit auf Tagesbeginn verlängern" aria-label="Abwesenheit auf Tagesbeginn verlängern"><i class="bi bi-arrow-bar-left"></i></button>` : '';
      const schluss = vor && !spaeter && a.darf
        ? `<button type="button" class="sd-kreisknopf klein" data-bisende="${f.schuelerId}" title="Abwesenheit bis Stundenende verlängern" aria-label="Abwesenheit bis Stundenende verlängern"><i class="bi bi-arrow-bar-right"></i></button>` : '';
      const gewertet = f.fehlstunden && f.art !== 'fehlt' ? ` · ${f.fehlstunden === 1 ? 'eine Fehlstunde' : `${f.fehlstunden} Fehlstunden`}` : '';
      const st = { offen: 'offen', entschuldigt: 'entschuldigt', unentschuldigt: 'nicht entschuldigt' }[f.status] ?? '';
      const farbe = { offen: 'gelb', entschuldigt: 'gruen', unentschuldigt: 'rot' }[f.status] ?? 'grau';
      const label = spaeter ? '<span class="nl-chip grau">spätere Stunde</span>' : vor ? '<span class="nl-chip grau">Vorstunde</span>' : st ? `<span class="nl-chip ${farbe}">${st}</span>` : '';
      const zusatz = spaeter ? `${f.stunde}. Stunde` : vor ? 'verlängern' : f.art === 'verspaetet' ? `verspätet ${f.minuten} min${gewertet}` : f.art === 'vorzeitig' ? `vorzeitig gegangen ${f.minuten} min${gewertet}` : '';
      return `<tr class="${vor ? 'vorstunde' : ''} ${abwAuswahl.has(f.schuelerId) ? 'nl-gewaehlt' : ''}">
        <td class="nl-wahl">${spaeter ? '' : `<input type="checkbox" class="nl-rund" data-abw="${f.schuelerId}" ${abwAuswahl.has(f.schuelerId) ? 'checked' : ''} aria-label="${e(name(f.schuelerId))} auswählen">`}</td>
        <td class="sd-abw-name">${e(name(f.schuelerId))}</td>
        <td class="text-nowrap">${anfang}${datumDe(vonD)} <span class="sd-abw-zeit">${e(beginn)}</span></td>
        <td class="text-nowrap">${datumDe(bisD)} <span class="sd-abw-zeit">${e(ende)}</span>${schluss}</td>
        <td>${e(grundText(f.grund) || '–')}</td>
        <td class="text-nowrap">${label}${zusatz ? `<span class="sd-abw-zusatz">${e(zusatz)}</span>` : ''}</td>
        <td class="nl-text sd-abw-text" title="${e(f.text ?? f.bemerkung ?? '')}">${e(f.text ?? f.bemerkung)}</td>
        <td class="text-end">${a.darf && !vor ? `<button type="button" class="sd-kreisknopf" data-abw-bearbeiten="${f.schuelerId}" title="Abwesenheit bearbeiten" aria-label="Abwesenheit von ${e(name(f.schuelerId))} bearbeiten"><i class="bi bi-pencil"></i></button>` : ''}</td></tr>`;
    };
    const n = zeilen.length;
    const tabelle = n ? `<div class="nl-tabelle sd-abw"><table>
        <thead><tr><th class="nl-wahl"><input type="checkbox" class="nl-rund" data-abw-alle aria-label="Alle auswählen" title="Alle auswählen" ${abwAuswahl.size && abwAuswahl.size === n ? 'checked' : ''}></th>
          <th>Schüler*in</th><th>Von</th><th>Bis</th><th>Grund</th><th>Status</th><th>Text</th><th class="text-end"><span class="visually-hidden">Bearbeiten</span></th></tr></thead>
        <tbody>${zeilen.map(reihe).join('')}</tbody></table>
        <div class="nl-fuss"><span class="sd-abw-anzahl">${n} ${n === 1 ? 'Element' : 'Elemente'}</span>
          ${a.darf ? `<button type="button" class="nl-knopf ms-auto" data-verlaengern ${abwAuswahl.size ? '' : 'disabled'}><i class="bi bi-arrow-left-right"></i> Verlängern</button>` : ''}</div></div>`
      : '<p class="sd-text small">Keine Abwesenheiten vorhanden</p>';
    return abschnitt(maskeIcon('klassenbuch/chair-1.svg'), 'Abwesende Schüler*innen', (a.fehlzeiten ?? []).length, '', tabelle);
  }

  /** Hausaufgabe als ruhige Zeile; Klick öffnet das Detailfenster (Bearbeiten/Löschen/Erledigt für die Lehrkraft). */
  function hausaufgabeZeile(h) {
    const ebene = istBlock() && h.unterricht === k.unterricht && h.datum === k.datum
      ? (h.einzelstunde && !einzel ? `<span class="sd-ebene-marke">${h.einzelstunde}. Stunde</span>` : !h.einzelstunde && einzel ? '<span class="sd-ebene-marke">Ganzer Block</span>' : '') : '';
    return `<button type="button" class="kb-hw klickbar ${h.erledigt ? 'erledigt' : ''}" data-hw-details="${h.id}" title="Details anzeigen">${ebene}${h.faellig ? `<span class="faellig">bis ${datumDe(h.faellig)}</span>` : ''}<span class="text">${e(h.text)}</span>${h.bemerkung ? `<small>${e(h.bemerkung)}</small>` : ''}
      ${h.erledigt ? '<i class="bi bi-check-circle-fill kb-hw-ok" title="Erledigt" aria-label="Erledigt"></i>' : ''}<i class="bi bi-chevron-right kb-hw-pfeil" aria-hidden="true"></i></button>`;
  }

  function hausaufgabenHtml() {
    const hw = daten.hausaufgaben;
    const plus = hw.darf ? `<button type="button" class="sd-plus" data-hw-neu title="Neue Hausaufgabe" aria-label="Neue Hausaufgabe">${PLUS_SYM}</button>` : '';
    const liste = hw.liste.length ? hw.liste.map(hausaufgabeZeile).join('') : '<p class="sd-text small">Keine Hausaufgaben vorhanden</p>';
    const faellig = hw.faellig.length ? `<div class="kb-hw-faellig"><div class="small fw-semibold mb-1">Fällige Hausaufgaben</div>${hw.faellig.map(hausaufgabeZeile).join('')}</div>` : '';
    return abschnitt(maskeIcon('klassenbuch/common-file-text-home-kopie.svg'), 'Hausaufgaben', hw.liste.length || null, plus, liste + faellig);
  }

  function vermerkeHtml() {
    const v = daten.vermerke;
    const plus = kollegium ? `<button type="button" class="sd-plus" data-vermerk-neu title="Klassenbucheintrag erfassen" aria-label="Klassenbucheintrag erfassen">${PLUS_SYM}</button>` : '';
    const kurz = (t, n) => t.length > n ? t.slice(0, n - 1) + '…' : t;
    const tabelle = v.length ? `<div class="table-responsive"><table class="alt-liste kb-eintraege"><tbody>${v.map(x => `<tr title="${e(x.text)}">
        <td>${e(x.schuelerId ? schuelerName(x.schuelerId) || '–' : 'Klasse ' + x.klasse)}</td><td>${datumDe(x.datum)}</td><td>${e((x.erstellt || '').slice(11, 16))}</td>
        <td>${e((x.art || '').slice(0, 3))}</td><td class="text">${e(kurz(x.text, 30))}</td><td>${e(x.lehrer)}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="sd-text small">Keine Klassenbucheinträge vorhanden</p>';
    return abschnitt(maskeIcon('class_register_entries.svg'), 'Klassenbucheinträge', v.length, plus, tabelle);
  }

  /** Klassendienste der Stunde in der Optik der Seite Klassendienste: je Dienst eine Zeile mit Symbol, Zeitraum und Schüler*innen-Chips. */
  function diensteHtml() {
    const d = daten.dienste;
    const ini = n => String(n || '').split(/[\s,]+/).filter(Boolean).slice(0, 2).map(x => x[0]).join('').toUpperCase();
    const kurz = iso => { const [, m, t] = iso.split('-'); return `${Number(t)}.${Number(m)}.`; };
    const gruppen = [];
    for (const z of d) {
      const g = gruppen.find(x => x.dienstId === z.dienstId && x.von === z.von && x.bis === z.bis && x.klasse === z.klasse);
      if (g) g.eintraege.push(z); else gruppen.push({ dienstId: z.dienstId, dienst: z.dienst, von: z.von, bis: z.bis, klasse: z.klasse, text: z.text, eintraege: [z] });
    }
    const mehrereKlassen = new Set(gruppen.map(g => g.klasse)).size > 1;
    const inhalt = gruppen.length ? `<div class="kb-kd">${gruppen.map(g => `<div class="kb-kd-zeile" title="${e(g.dienst)}${g.text ? ' · ' + e(g.text) : ''}">
        <span class="kd-dienst"><span class="kd-symbol"><i class="bi bi-person-gear"></i></span><span><b>${e(g.dienst)}</b><small>${g.von === g.bis ? kurz(g.von) : `${kurz(g.von)} – ${kurz(g.bis)}`}${mehrereKlassen ? ` · ${e(g.klasse)}` : ''}${g.text ? ` · ${e(g.text)}` : ''}</small></span></span>
        <span class="kd-schueler">${g.eintraege.map(z => `<span class="kd-person"><span class="kd-avatar">${e(ini(z.schueler))}</span>${e(z.schueler)}</span>`).join('')}</span></div>`).join('')}</div>`
      : '<p class="sd-text small mb-0">Für diesen Tag ist niemand eingeteilt.</p>';
    const kl = klassenListe()[0];
    const rechts = kl ? `<a class="sd-link-klein" href="#/dienste/${encodeURIComponent(kl)}/alle/liste" data-dienste-link>Klassendienste</a>` : '';
    return abschnitt(maskeIcon('class_services.svg'), 'Klassendienste', gruppen.length || null, rechts, inhalt);
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
    const datei = maskeIcon('klassenbuch/common-file-text.svg');
    const teile = [];
    const unterricht = k.unterricht && !['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
    if (unterricht && !istEntfall()) teile.push(onlineHtml(ein));
    if (darfRaum()) {
      const raeume = kuerzel(k.raeume.filter(w => w.k));
      teile.push(`<div class="sd-detail sd-raeume">${maskeIcon('shared_roomEntity.svg')}<div class="d-flex flex-wrap align-items-center gap-2">
        ${raeume.map(x => `<div class="sd-raumchip"><button type="button" class="sd-chipknopf" data-raum-menue="${e(x)}" aria-haspopup="menu" aria-expanded="false">${e(x)}</button>
          <div class="sd-raummenue d-none" role="menu" data-menue-fuer="${e(x)}"><button type="button" role="menuitem" data-raum-ersetzen="${e(x)}">Ersetzen</button>
          <button type="button" role="menuitem" data-raum-loeschen="${e(x)}">Löschen</button></div></div>`).join('')}
        <button type="button" class="sd-raum-plus" data-raum-plus title="Raum hinzufügen" aria-label="Raum hinzufügen">${PLUS_SYM}</button></div></div>`);
    }
    const vertretung = k.text && (k.fach || k.status !== 'veranstaltung') ? k.text : '';
    if (vertretung && k.status !== 'klausur') teile.push(reihe(datei, k.fach ? 'Vertretungstext' : 'Bezeichnung', `<p class="sd-text">${e(vertretung)}</p>`));
    // Veranstaltung: der Name steht nur hier (oben im Kopf keine Bezeichnung)
    if (k.status === 'veranstaltung' && !k.fach && (k.text || k.art)) teile.push(reihe(datei, 'Information zur Stunde', `<p class="sd-text">${e(k.text || k.art)}</p>`));
    if (k.status === 'klausur') teile.push(reihe(datei, 'Prüfung', `<p class="sd-text">${e([k.art, k.text].filter(Boolean).join(' – '))}</p>`));
    const stoffHinweis = ebenenHinweis('thema');
    if (ein?.thema || stoffHinweis) teile.push(reihe(datei, 'Lehrstoff', (ein?.thema ? `<p class="sd-text">${e(ein.thema)}</p>` : '') + stoffHinweis));
    const hw = daten.hausaufgaben;
    if (hw.faellig.length) teile.push(reihe(maskeIcon('exact/homework.svg'), 'Fällige Hausaufgaben', hw.faellig.map(hausaufgabeZeile).join('')));
    if (hw.liste.length) teile.push(reihe(maskeIcon('exact/homework.svg'), 'Hausaufgaben', hw.liste.map(hausaufgabeZeile).join('')));
    if (unterricht) {
      // Einzelstunde: nur ihre eigenen Notizen (die des Blocks stehen als Hinweis darunter); Block: Karte nur ohne Einzelstunden-Einträge als Ersatz
      const ersatz = einzel || daten.einzelEintraege?.length;
      teile.push(notizFeld('notizSchueler', 'Notizen für Schüler*innen', ein ? ein.notizSchueler : ersatz ? '' : k.notizSchueler, ebenenHinweis('notizSchueler')));
      if (kollegium) teile.push(notizFeld('notiz', 'Notizen für Lehrkräfte', ein ? ein.notiz : ersatz ? '' : k.notizLehrer, ebenenHinweis('notiz')));
    }
    const html = teile.filter(Boolean).join('');
    return html || '<div class="sd-leer"><b>Keine weiteren Details</b><span>Zu dieser Stunde gibt es keine zusätzlichen Informationen.</span></div>';
  }

  /**
   * „Findet online statt“: Lehrkräfte der Stunde (bzw. Administration) schalten um und hinterlegen den Link zur Videokonferenz;
   * alle anderen sehen den Hinweis und – falls hinterlegt – „Online-Unterricht beitreten“ (neuer Tab).
   */
  /** Schalter „Findet online statt“ im Portal-Stil: graue Spur (aus), orange Spur (an), weißer Knopf mit sanftem Schatten. */
  const onlineSchalter = (an, attr = '') => `<label class="sd-schalter" for="sd-online"><span class="sd-detail-titel">Findet online statt</span>
      <input type="checkbox" role="switch" id="sd-online" ${attr} ${an ? 'checked' : ''}><span class="sd-schalter-spur" aria-hidden="true"></span></label>`;

  function onlineHtml(ein) {
    // Findet der ganze Block online statt, gilt das auch für jede Einzelstunde (nur Anzeige)
    const blockOnline = einzel && daten.blockEintrag?.online;
    if (blockOnline) ein = daten.blockEintrag;
    const online = ein ? !!ein.online : !einzel && !!k.online;
    const link = (ein ? ein.onlineLink : einzel ? '' : k.onlineLink) || '';
    const beitreten = online && sichererLink(link)
      ? `<a class="sd-online-link" href="${e(link)}" target="_blank" rel="noopener noreferrer" data-online-beitreten><span class="u-sym wu sd-online-link-sym" style="--wu:url(${W}klassenbuch/online_lesson.svg)" aria-hidden="true"></span>Online-Unterricht beitreten</a>` : '';
    const ic = maskeIcon('klassenbuch/online_lesson.svg');
    if (!kollegium || !daten.darf || blockOnline) {
      if (!online) return kollegium ? `<div class="sd-detail sd-online">${ic}<div class="flex-grow-1">${onlineSchalter(false, 'disabled')}</div></div>` : '';
      return `<div class="sd-detail sd-online">${ic}<div class="flex-grow-1"><div class="sd-detail-titel">Findet online statt${blockOnline ? ' <span class="sd-ebene-marke">Ganzer Block</span>' : ''}</div>
          ${beitreten || '<p class="sd-text sd-online-ohne">Es wurde noch kein Link zum Online-Unterricht hinterlegt.</p>'}</div></div>`;
    }
    return `<div class="sd-detail sd-online">${ic}<div class="flex-grow-1">
        ${onlineSchalter(online, 'data-online')}
        ${online ? `<div class="sd-online-feld">
          <label class="form-label small mb-1" for="sd-online-link">Link zum Online-Unterricht (Videokonferenz)</label>
          <div class="d-flex flex-wrap gap-2 align-items-center">
            <input type="url" class="form-control form-control-sm" id="sd-online-link" data-online-link inputmode="url" maxlength="1000" placeholder="https://…" value="${e(link)}">
            <button type="button" class="sd-pille klein primaer" data-online-speichern>Speichern</button></div>
          <div class="invalid-feedback d-block d-none" data-online-fehler>Bitte einen gültigen Link mit http:// oder https:// angeben.</div>
          ${beitreten ? `<div class="mt-2">${beitreten}</div>` : ''}</div>` : ''}</div></div>`;
  }

  function notizFeld(feld, titel, wert, hinweis = '') {
    const ic = maskeIcon('klassenbuch/common-file-text.svg');
    if (!daten.darf) return wert || hinweis ? `<div class="sd-detail">${ic}<div><div class="sd-detail-titel">${titel}</div>${wert ? `<p class="sd-text">${e(wert)}</p>` : ''}${hinweis}</div></div>` : '';
    return `<div class="sd-detail">${ic}<div class="flex-grow-1"><div class="sd-detail-titel">${titel}</div>${hinweis}
      <textarea class="form-control sd-notiz" rows="2" data-notiz="${feld}" ${einzel ? `placeholder="Nur für die ${einzel}. Stunde"` : ''}>${e(wert ?? '')}</textarea>
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
      <label>Prüfungsart</label><select class="form-select" name="art"><option value=""></option>${PRUEFUNGSARTEN.map(a => `<option value="${e(a.name)}">${e(a.name)}${a.schema ? ` (${e(a.schema)})` : ''}</option>`).join('')}</select>
      <label data-ns hidden>Notenschema</label><div data-ns hidden><select class="form-select" name="notenschema"><option value="">– individuell (im Text beschreiben) –</option>${NOTENSCHEMEN.map(n => `<option value="${e(n.name)}">${e(n.name)}</option>`).join('')}</select>
        <div class="small text-body-secondary mt-1">Für diese Prüfungsart ist kein Notenschema hinterlegt – bitte ein Notenschema für diese Prüfung wählen.</div></div>
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
      await senden('/api/app/klassenbuch/anwesenheit', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, eintraege, ...eb() }, 'PUT');
      await anwesenheitNeu();
      geaendert = true;
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  async function anwesenheitNeu() {
    daten.anwesenheit = await holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}${ep()}`);
  }

  // bisherige Abwesenheiten der Stunde mit ihrem minutengenauen Fenster (bleiben beim Speichern anderer Schüler*innen erhalten)
  const fzKarte = () => new Map((daten.anwesenheit?.fehlzeiten ?? []).map(f => [f.schuelerId, { art: f.art, minuten: f.minuten, bemerkung: f.text ?? f.bemerkung, grund: f.grund, von: f.beginn || null, bis: f.ende || null }]));

  async function eintragSpeichern(aenderung) {
    const alt = daten.eintrag ?? {};
    const neu = { thema: alt.thema ?? '', hausaufgabe: alt.hausaufgabe ?? '', faellig: alt.faellig ?? null, notiz: alt.notiz ?? '', notizSchueler: alt.notizSchueler ?? '', ...aenderung };
    try {
      const lk = kuerzel(k.lehrer);
      daten.eintrag = await senden('/api/app/klassenbuch/stunde', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, ...neu, faellig: neu.faellig || null, lehrer: lk.length === 1 ? lk[0] : null, ...eb() }, 'PUT');
      if (einzel) daten.einzelstunden = (daten.einzelstunden ?? []).map(z => z.stunde === einzel ? { ...z, eintrag: true } : z);
      else daten.blockEintrag = daten.eintrag;
      geaendert = true;
      toast('Gespeichert.');
      return true;
    } catch (err) { toast(err.message, true); return false; }
  }

  async function hausaufgabenNeu() {
    daten.hausaufgaben = await holen(`/api/app/klassenbuch/hausaufgabenliste?datum=${k.datum}&stunde=${k.von}&bis=${k.bis}&unterricht=${k.unterricht}&fach=${encodeURIComponent(k.fach || '')}&klassen=${encodeURIComponent(klassenListe().join(','))}${ep()}`);
    geaendert = true;
  }

  async function verlaengern(ids, bis) {
    try {
      await senden('/api/app/klassenbuch/abwesenheit/verlaengern', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, schuelerIds: ids, bis, ...eb() });
      await anwesenheitNeu();
      abwAuswahl.clear();
      geaendert = true;
      toast(bis === 'tagesbeginn' ? 'Abwesenheit auf Tagesbeginn verlängert.' : 'Abwesenheit bis Stundenende verlängert.');
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  // ---------------------------------------------------------------- Ereignisse

  // ---------------------------------------------------------------- Sitzplan: Klassenbuch-Einstellungen (Zahnrad) und Editor

  function sitzEditor() {
    if (!daten.sitz) { toast('Der Sitzplan kann nicht geladen werden.', true); return; }
    const kx = sitz.kontextVon(k);
    sitz.editorOeffnen({
      wurzel: el, d: daten.sitz, kontext: { ...kx, klasse: daten.sitz.kontext?.klasse ?? null }, titel: 'Sitzplan',
      untertitel: [kuerzel(k.klassen).join(', '), k.fach, kuerzel(k.lehrer).join(', '), kuerzel(k.raeume).join(', ')].filter(Boolean).join(' · '),
      onGespeichert: async () => {
        daten.sitz = await sitz.stundeLaden(kx).catch(() => null);
        sitz.einstellungenSpeichern({ ...sitz.einstellungen(), ansicht: 'sitzplan' });
        zeichnen();
      },
    });
  }
  function sitzEinstellungen() {
    sitz.einstellungenPanel({ wurzel: el, onAendern: () => zeichnen(), sitzplan: { darf: sitz.darfIrgendwas(daten.sitz), onOeffnen: sitzEditor } });
  }

  function binden() {
    const q = s => box.querySelector(s);
    const alle = (s, f) => box.querySelectorAll(s).forEach(f);
    if (reiter === 'pruefung') { notenschemaBinden(box); raumwahl(box.querySelector('[name=raeume]'), { wurzel: box, unterricht: k.unterricht }); }
    box.onclick = ev => {
      if (!ev.target.closest('.sd-raumchip')) box.querySelectorAll('.sd-raummenue').forEach(x => x.classList.add('d-none'));
      if (!ev.target.closest('.sd-blockwahl')) { box.querySelector('.sd-blockwahl-menue')?.classList.add('d-none'); box.querySelector('[data-blockwahl]')?.setAttribute('aria-expanded', 'false'); }
    };
    // Ganzer Block / Einzelstunde
    const blockKnopf = q('[data-blockwahl]'), blockMenue = q('.sd-blockwahl-menue');
    if (blockKnopf) {
      const menue = auf => { blockMenue.classList.toggle('d-none', !auf); blockKnopf.setAttribute('aria-expanded', String(auf)); if (auf) blockMenue.querySelector('.an')?.focus(); };
      blockKnopf.onclick = ev => { ev.stopPropagation(); menue(blockMenue.classList.contains('d-none')); };
      blockMenue.onkeydown = ev => { if (ev.key === 'Escape') { ev.stopPropagation(); menue(false); blockKnopf.focus(); } };
      alle('[data-einzel]', b => b.onclick = async () => {
        const neu = Number(b.dataset.einzel) || null;
        menue(false);
        if (neu === einzel) return;
        einzel = neu; auswahl.clear(); abwAuswahl.clear(); offen = null; hwBearbeiten = null;
        box.querySelector('.sd-rumpf').innerHTML = '<div class="py-5"></div>';
        await laden();
        zeichnen();
      });
    }
    alle('[data-reiter]', b => b.onclick = () => { if (reiter === 'pruefung' || reiter === 'raum') return; reiter = b.dataset.reiter; offen = null; zeichnen(); });
    alle('[data-blaettern]', b => b.onclick = async () => {
      const x = liste[liste.indexOf(k) + Number(b.dataset.blaettern)];
      if (!x) return;
      k = x; einzel = null; auswahl.clear(); abwAuswahl.clear(); offen = null; hwBearbeiten = null; pruefungSchueler = null;
      box.querySelector('.sd-rumpf').innerHTML = '<div class="py-5"></div>';
      await laden();
      if (reiter === 'klassenbuch' && !mitKlassenbuch()) reiter = 'details';
      zeichnen();
    });
    q('[data-pruefung]')?.addEventListener('click', async () => {
      if (!daten.anwesenheit && k.unterricht) daten.anwesenheit = await holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}${ep()}`).catch(() => null);
      klausurDialog();
    });
    // Prüfung löschen bzw. editieren (noch nicht übernommene werden zurückgezogen, übernommene gehen als Rückmeldung an UntPlan)
    const pruefungAlt = () => ({ datum: k.datum, stundeVon: k.von, stundeBis: k.bis, klassen: kuerzel(k.klassen), titel: k.text || null });
    const stdText = (v, b) => v === b ? `${v}.` : `${v}.–${b}.`;
    /** Prüfung löschen (mit Rückfrage; Rechte und Ablauf wie Rueckmeldungsdienst.KlausurLoeschen). Ergebnis: gelöscht? */
    const pruefungLoeschen = async () => {
      if (!await bestaetigen('Prüfung löschen', `Prüfung${k.text ? ` „${k.text}“` : ''} am ${datumDe(k.datum)} (${stdText(k.von, k.bis)} Stunde) löschen?`)) return false;
      try {
        await senden('/api/app/meldungen/klausur/loeschen', pruefungAlt());
        toast('Prüfung gelöscht – UntPlan übernimmt die Löschung beim nächsten Import.');
        geaendert = true; modal.hide();
        return true;
      } catch (err) { toast(err.message, true); return false; }
    };
    q('[data-pruefung-loeschen]')?.addEventListener('click', pruefungLoeschen);
    // Prüfung editieren: Name, Datum, Stunden, Räume (Raumwahl mit Belegung), Aufsicht – Speichern verlegt bzw. ändert, Löschen links im Fuß
    q('[data-pruefung-editieren]')?.addEventListener('click', () => {
      const wahl = (sel) => zeiten.map(z => `<option value="${z.nr}" ${z.nr === sel ? 'selected' : ''} data-zweit="${e(z.beginn)}–${e(z.ende)}">${z.nr}. Stunde</option>`).join('');
      altDialog('Prüfung editieren', `
        <div class="ad-info">${e(k.text || 'Prüfung')} · bisher ${datumDe(k.datum)}, ${stdText(k.von, k.bis)} Stunde</div>
        <label class="ad-feld">Name<input name="titel" maxlength="200" value="${e(k.text || '')}" placeholder="Prüfung"></label>
        <label class="ad-feld">Datum<input type="date" name="datum" value="${k.datum}" min="${heuteIso()}"></label>
        <div class="ad-reihe"><label class="ad-feld">von Stunde<select name="von">${wahl(k.von)}</select></label>
          <label class="ad-feld">bis Stunde<select name="bis">${wahl(k.bis)}</select></label></div>
        <label class="ad-feld">Räume<input name="raeume" value="${e(kuerzel(k.raeume.filter(w => w.k)).join(', '))}"></label>
        <label class="ad-feld">Aufsicht<input name="aufsicht" value="${e(kuerzel(k.lehrer.filter(w => w.k)).join(', '))}"></label>
        <div class="ad-knoepfe"><button type="button" class="ad-pille rot" data-pe-loeschen><i class="bi bi-trash" aria-hidden="true"></i> Löschen</button>
          <button type="button" class="ad-pille" data-ad-zu>Abbrechen</button><button type="button" class="ad-pille" data-pv-speichern>Speichern</button></div>`, (o, zu) => {
        const f = n => o.querySelector(`[name=${n}]`).value;
        const listeVon = n => f(n).split(/[,;\s]+/).map(x => x.trim()).filter(Boolean);
        o.querySelector('[name=von]').addEventListener('change', () => { if (Number(f('bis')) < Number(f('von'))) o.querySelector('[name=bis]').value = f('von'); });
        raumwahl(o.querySelector('[name=raeume]'), { wurzel: o, unterricht: k.unterricht ?? null });
        o.querySelector('[data-pe-loeschen]').onclick = async () => { if (await pruefungLoeschen()) zu(); };
        o.querySelector('[data-pv-speichern]').onclick = async ev => {
          const knopf = ev.currentTarget;
          const von = Number(f('von')), bis = Math.max(von, Number(f('bis')));
          if (!f('datum')) { toast('Bitte ein Datum wählen.', true); return; }
          knopf.disabled = true;
          try {
            await senden('/api/app/meldungen/klausur/verlegen', { alt: pruefungAlt(), neu: { datum: f('datum'), stundeVon: von, stundeBis: bis,
              klassen: kuerzel(k.klassen), raeume: listeVon('raeume'), aufsicht: listeVon('aufsicht'), titel: f('titel').trim() || k.text || 'Prüfung', text: '' } });
            toast(f('datum') !== k.datum || von !== k.von || bis !== k.bis ? 'Prüfung verlegt.' : 'Prüfung gespeichert.');
            zu(); geaendert = true; modal.hide();
          } catch (err) { toast(err.message, true); knopf.disabled = false; }
        };
      }, 480);
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
    alle('[data-raum-ersetzen]', b => b.onclick = () => { raumModus = { art: 'ersetzen', raum: b.dataset.raumErsetzen, gewaehlt: null, filter: raumFilter }; reiter = 'raum'; zeichnen(); });
    alle('[data-raum-loeschen]', b => b.onclick = () => raumAendern(kuerzel(k.raeume.filter(w => w.k)).filter(x => x !== b.dataset.raumLoeschen), `Raum ${b.dataset.raumLoeschen} entfernt.`));
    q('[data-raum-plus]')?.addEventListener('click', () => { raumModus = { art: 'hinzufuegen', raum: null, gewaehlt: null, filter: raumFilter }; reiter = 'raum'; zeichnen(); });
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
      const text = pruefungText(f, teil);
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
      else sitzEinstellungen();
    });
    alle('[data-sitz-ansicht]', b => b.onclick = () => { sitz.einstellungenSpeichern({ ...sitz.einstellungen(), ansicht: b.dataset.sitzAnsicht }); zeichnen(); });
    q('[data-sitz-bearbeiten]')?.addEventListener('click', sitzEditor);

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

    q('[data-dienste-link]')?.addEventListener('click', ev => { ev.preventDefault(); const z = ev.currentTarget.getAttribute('href'); modal.hide(); setTimeout(() => kontext.navigieren ? kontext.navigieren(z) : (location.hash = z)); });
    // Hausaufgaben: Anlegen im Fenster, Klick auf eine Hausaufgabe öffnet die Details
    const hwNeuGeladen = async () => { try { await hausaufgabenNeu(); } catch (err) { toast(err.message, true); } zeichnen(); };
    q('[data-hw-neu]')?.addEventListener('click', () => hausaufgabeFormular({ stunde: { datum: k.datum, von: k.von, unterricht: k.unterricht, fach: k.fach, klassen: klassenListe(), einzelstunde: einzel }, geaendert: hwNeuGeladen }));
    alle('[data-hw-details]', b => b.onclick = () => {
      const id = Number(b.dataset.hwDetails);
      const eigen = daten.hausaufgaben.liste.find(x => x.id === id);
      const h = eigen ?? daten.hausaufgaben.faellig.find(x => x.id === id);
      if (h) hausaufgabeDetails({ ...h, lehrer: h.lehrer }, { darf: !!eigen && daten.hausaufgaben.darf, geaendert: hwNeuGeladen });
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
      else if (zeiten.some(z => z.nr < bereich().von)) verlaengern(ids, 'tagesbeginn');
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
        await senden('/api/app/mitteilungen', { empfaenger: ['gruppe:leitung'], betreff: `Ticket: ${klassenListe().join(', ')} ${k.fach || ''} ${datumDe(k.datum)} ${einzel ?? k.von}. Std.`.trim(), text, wichtig: false });
        offen = null; toast('Ticket gesendet.'); zeichnen();
      } catch (err) { toast(err.message, true); }
    });

    // Details: online, Notizen (Speichern/Abbrechen erscheinen beim Bearbeiten)
    const onlineMerken = (online, link) => {
      daten.eintrag = { ...(daten.eintrag ?? {}), online, ...(link !== undefined ? { onlineLink: link } : {}) };
      if (!einzel) k = { ...k, online, onlineLink: online ? daten.eintrag.onlineLink || '' : '' };
      geaendert = true;
    };
    q('[data-online]')?.addEventListener('change', async ev => {
      try {
        await senden('/api/app/klassenbuch/online', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, online: ev.target.checked, ...eb() }, 'PUT');
        onlineMerken(ev.target.checked);
        toast(ev.target.checked ? 'Die Stunde findet online statt.' : 'Die Stunde findet im Raum statt.');
        zeichnen();
        if (ev.target.checked && !daten.eintrag.onlineLink) box.querySelector('[data-online-link]')?.focus();
      } catch (err) { ev.target.checked = !ev.target.checked; toast(err.message, true); }
    });
    q('[data-online-speichern]')?.addEventListener('click', async () => {
      const feld = box.querySelector('[data-online-link]'), fehler = box.querySelector('[data-online-fehler]');
      const link = feld.value.trim();
      const gueltig = !link || sichererLink(link);
      feld.classList.toggle('is-invalid', !gueltig);
      fehler.classList.toggle('d-none', gueltig);
      if (!gueltig) return feld.focus();
      try {
        await senden('/api/app/klassenbuch/online', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, online: true, onlineLink: link, ...eb() }, 'PUT');
        onlineMerken(true, link);
        toast(link ? 'Link zum Online-Unterricht gespeichert.' : 'Link zum Online-Unterricht entfernt.');
        zeichnen();
      } catch (err) { toast(err.message, true); }
    });
    q('[data-online-link]')?.addEventListener('keydown', ev => { if (ev.key === 'Enter') { ev.preventDefault(); box.querySelector('[data-online-speichern]')?.click(); } });
    alle('[data-notiz]', t => t.oninput = () => box.querySelector(`[data-notiz-knoepfe="${t.dataset.notiz}"]`)?.classList.remove('d-none'));
    alle('[data-notiz-abbrechen]', b => b.onclick = () => zeichnen());
    alle('[data-notiz-speichern]', b => b.onclick = async () => {
      const feld = b.dataset.notizSpeichern;
      if (await eintragSpeichern({ [feld]: box.querySelector(`[data-notiz="${feld}"]`).value })) {
        if (!einzel) k = { ...k, [feld === 'notiz' ? 'notizLehrer' : 'notizSchueler']: box.querySelector(`[data-notiz="${feld}"]`).value };
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

  /**
   * Raumbuchung (Raum ersetzen bzw. hinzufügen): gemeinsame Raumliste (raumwahl.js) mit Suche und Filtern Gebäude, Abteilung, Raumart,
   * Kapazität und „Nur freie“ (einheitliche Auswahl-Pillen); je Raum Kapazität und Status frei/belegt zur Zeit der Stunde – wählbar sind
   * freie buchbare Räume und die ursprünglichen Räume der Stunde.
   */
  async function raumWahl(ziel) {
    ziel.innerHTML = '<div class="py-2"></div>';
    let raeume;
    try { raeume = await holen(`/api/app/buchen/raumstatus?datum=${k.datum}&von=${k.von}&bis=${k.bis}${k.unterricht ? `&unterricht=${k.unterricht}` : ''}`); }
    catch (err) { ziel.innerHTML = `<p class="small text-danger mt-2">${e(err.message)}</p>`; return; }
    const aktuell = kuerzel(k.raeume.filter(w => w.k));
    raeume = raeume.filter(r => (r.buchbar || r.eigen) && !aktuell.includes(r.kurz));
    raumModus.filter ??= {};
    const liste = raumListe(ziel, {
      raeume, titel: raumModus.art === 'ersetzen' ? `${raumModus.raum} ersetzen` : 'Räume hinzufügen', gewaehlt: raumModus.gewaehlt, filter: raumModus.filter,
      waehlbar: r => r.frei, zusatz: r => r.eigen ? '<span class="sd-ursprung">ursprünglicher Raum</span>' : '',
      leerText: 'In dieser Zeit ist kein passender buchbarer Raum frei.',
      beiWahl: kurz => { raumModus.gewaehlt = kurz; box.querySelector('[data-raum-speichern]').disabled = !kurz; },
    });
    liste.fokus();
  }

  // ---------------------------------------------------------------- Dialoge wie im alten Klassenbuch (Lehrstoff, Abwesenheit, Klassenbucheintrag)

  /**
   * Klassenbuch-Dialog über den Stundendetails – gemeinsame Modal-Komponente (kbdialog.js): Kopf mit Titel und X, Inhalt, die Knopfleiste
   * des Inhalts („.ad-knoepfe“) im einheitlichen Fuß (Speichern rechts, Abbrechen davor, Löschen links), ESC/Hintergrund schließen.
   */
  function altDialog(titel, inhalt, beimOeffnen, breite = 380) {
    const fenster = kbDialog({ titel, inhalt, breite: stufe(breite), wurzel: el });
    fussUebernehmen(fenster);
    beimOeffnen(fenster, () => fenster.schliessen());
    return fenster;
  }

  /**
   * Klausuranlage als Dialog direkt über den Stundendetails (Knopf „Prüfung erstellen“ unten neben „Nächste Stunde“):
   * Name, Text, Prüfungsart, Datum und Stunden, freie Räume zur Auswahl, Aufsicht, teilnehmende Schüler*innen.
   */
  async function klausurDialog() {
    pruefungSchueler = null;
    altDialog('Prüfung erstellen', `${kopfDl()}${pruefungHtml()}
      <div class="ad-knoepfe justify-content-end"><button type="button" class="ad-pille" data-ad-zu>Abbrechen</button>
        <button type="button" class="sd-orange" data-klausur-speichern>Speichern</button></div>`, (o, zu) => {
      // Raumwahl: Suche nach Kürzel/Name, frei/belegt zur gewählten Zeit (eigene Stunde zählt nicht), Kapazität, buchbare zuerst
      raumwahl(o.querySelector('[name=raeume]'), { wurzel: o, unterricht: k.unterricht });
      notenschemaBinden(o);
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
        const text = pruefungText(f, teil);
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
    try { vorstunden = await holen(`/api/app/klassenbuch/lehrstoff/vorstunden?datum=${k.datum}&stunde=${bereich().von}&unterricht=${k.unterricht}${einzigeLehrkraft()}`); } catch { /* ohne Vorstunden */ }
    const wt = iso => WT_KURZ[(new Date(iso + 'T12:00').getDay() + 6) % 7];
    const b = bereich();
    altDialog('Lehrstoff', `${kopfDl(`<dt>Datum</dt><dd>${datumDe(k.datum)} ${e(b.beginn || '')}${b.ende ? ' - ' + e(b.ende) : ''}${istBlock() ? ` (${einzel ? `nur ${einzel}. Stunde` : `ganzer Block ${k.von}.–${k.bis}. Stunde`})` : ''}</dd>`)}
      <label class="ad-feld">Lehrstoff<textarea rows="4" data-ad-thema maxlength="2000">${e(ein.thema ?? '')}</textarea></label>
      <div class="ad-feld">Lehrstoff aus Vorwochen<table class="ad-liste ad-vorwochen">${vorstunden.length ? vorstunden.map((v, i) => `<tr><td>${wt(v.datum)} ${datumDe(v.datum)}</td><td>${v.stunde}.</td>
        <td class="text" title="${e(v.thema)}">${e(v.thema)}</td><td class="mitte"><button type="button" class="ad-vw-knopf" data-ad-kopie="${i}" title="Lehrstoff übernehmen (ersetzt den Text)">Übernehmen</button>
        <button type="button" class="ad-vw-knopf" data-ad-anhaengen="${i}" title="Lehrstoff an den Text anhängen">Anhängen</button></td></tr>`).join('')
        : '<tr><td class="leer">Kein Lehrstoff aus Vorwochen</td></tr>'}</table></div>
      <label class="ad-feld">Lehrform<select data-ad-lehrform><option value="">&lt;Auswahl&gt;</option>${LEHRFORMEN.map(l => `<option ${l === ein.lehrform ? 'selected' : ''}>${e(l)}</option>`).join('')}</select></label>
      <label class="ad-feld">Anmerkung<input data-ad-anmerkung maxlength="1000" value="${e(ein.anmerkung ?? '')}"></label>
      <div class="ad-knoepfe"><button type="button" class="ad-pille" data-ad-speichern="1">Speichern</button><button type="button" class="ad-pille" data-ad-speichern="0">Übernehmen</button><button type="button" class="ad-pille" data-ad-zu>Schließen</button></div>
      ${ein.geaendert ? `<div class="ad-fuss">Geändert am ${zeitstempel(ein.geaendert)} von ${e(ein.geaendertVon || ein.lehrer || '')}</div>` : ''}`, (o, zu) => {
      const feld = o.querySelector('[data-ad-thema]');
      o.querySelectorAll('[data-ad-kopie]').forEach(b => b.onclick = () => { feld.value = vorstunden[Number(b.dataset.adKopie)].thema; feld.focus(); });
      // anhängen: neue Zeile unter den bisherigen Text
      o.querySelectorAll('[data-ad-anhaengen]').forEach(b => b.onclick = () => {
        const t = vorstunden[Number(b.dataset.adAnhaengen)].thema;
        feld.value = feld.value.trim() ? `${feld.value.replace(/\s+$/, '')}\n${t}` : t; feld.focus();
      });
      o.querySelectorAll('[data-ad-speichern]').forEach(b => b.onclick = async () => {
        const ok = await eintragSpeichern({ thema: o.querySelector('[data-ad-thema]').value, lehrform: o.querySelector('[data-ad-lehrform]').value, anmerkung: o.querySelector('[data-ad-anmerkung]').value });
        if (!ok) return;
        if (b.dataset.adSpeichern === '1') { zu(); zeichnen(); return; }
        const t = `Geändert am ${zeitstempel(daten.eintrag?.geaendert)} von ${daten.eintrag?.geaendertVon ?? ''}`;
        const f = o.querySelector('.ad-fuss');
        if (f) f.textContent = t; else o.querySelector('.kbd-rumpf').insertAdjacentHTML('beforeend', `<div class="ad-fuss">${e(t)}</div>`);
      });
    }, 600);   // etwas breiter (Lehrstoff aus Vorwochen mit Übernehmen/Anhängen)
  }

  async function abwesenheitEntfernen(ids) {
    try {
      await senden('/api/app/klassenbuch/abwesenheit/loeschen', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, schuelerIds: ids, ...eb() });
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
    const b = bereich();
    const start = a.beginn || b.beginn || zeitVon(b.von).beginn || '08:00', ende = a.ende || b.ende || zeitVon(b.bis).ende || '08:45';
    const tagesende = zeiten.length ? zeiten[zeiten.length - 1].ende : ende;
    // „Ganztägig“: ganzer Schultag (bzw. alle Tage von–bis) – z. B. Krankmeldung vor Unterrichtsbeginn; bestehende Abwesenheit über den ganzen Tag: an
    const tagesbeginn = zeiten.length ? zeiten[0].beginn : start;
    const ganzVorgabe = !!f && f.vonZeit <= tagesbeginn && f.bisZeit >= tagesende;
    const namen = ids.map(schuelerName).filter(Boolean);
    // Klassenbuch-Einstellungen: Ende einer neuen Abwesenheit (Stundenende, Tagesende, fixer Zeitpunkt), Standardgrund, Grund/Text gesperrt
    const kbe = a.einstellungen ?? {};
    const endeVorgabe = kbe.abwesenheitEnde === 'tag' ? (tagesende > ende ? tagesende : ende)
      : kbe.abwesenheitEnde === 'zeit' && kbe.abwesenheitEndeZeit > ende ? kbe.abwesenheitEndeZeit : ende;
    const vorgabe = { vonDatum: f?.vonDatum || k.datum, vonZeit: f?.vonZeit || start, bisDatum: f?.bisDatum || k.datum, bisZeit: f?.bisZeit || endeVorgabe };
    const grundGesperrt = !!kbe.grundTextGesperrt && !verwalten;
    const grundVorgabe = f ? f.grund : (a.standardgruende?.abwesend ?? '');
    const status = f?.status || 'offen';
    const jetzt = new Date(), jetztMin = jetzt.getHours() * 60 + jetzt.getMinutes();
    const laeuft = k.datum === heuteIso() && jetztMin > minutenVon(start) && jetztMin < minutenVon(ende);
    const jetztZeit = `${String(jetzt.getHours()).padStart(2, '0')}:${String(jetzt.getMinutes()).padStart(2, '0')}`;
    const gruendeWahl = `<option value="">–</option>${daten.gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === grundVorgabe ? 'selected' : ''}>${e(g.name)}</option>`).join('')}`;
    // Status als Segment-Schalter; bei einer bestehenden Abwesenheit entschuldigt die Klassenleitung damit direkt (ohne Speichern)
    const radio = (wert, text) => `<label class="ab-radio ab-seg-${wert}"><input type="radio" name="ab-status" value="${wert}" ${status === wert ? 'checked' : ''} ${verwalten ? '' : 'disabled'}><span>${text}</span></label>`;
    altDialog('Abwesenheit', `
      <div class="ab-formular">
        <label>Schüler*in</label><div class="ab-wert">${e(namen.length > 3 ? `${namen.slice(0, 3).join(', ')} und ${namen.length - 3} weitere` : namen.join(', '))}</div>
        <label for="ab-vd">Von</label><div class="ab-zeile"><input type="date" id="ab-vd" data-ab-vd value="${vorgabe.vonDatum}" ${verwalten ? '' : 'disabled'}><input type="time" step="60" data-ab-vz value="${e(vorgabe.vonZeit)}" aria-label="Von Uhrzeit"></div>
        <label for="ab-bd">Bis</label><div class="ab-zeile"><input type="date" id="ab-bd" data-ab-bd value="${vorgabe.bisDatum}" ${verwalten ? '' : 'disabled'}><input type="time" step="60" data-ab-bz value="${e(vorgabe.bisZeit)}" aria-label="Bis Uhrzeit"></div>
        <span></span><label class="ab-ganz"><input type="checkbox" data-ab-ganz ${ganzVorgabe ? 'checked' : ''}> Ganztägig <span class="ab-hinweis">alle Unterrichtsstunden ${verwalten ? 'der gewählten Tage' : 'des Tages'}</span></label>
        <span></span><div class="ab-schnell">
          <button type="button" class="ad-pille klein" data-ab-schnell="stunde" title="${e(start)} – ${e(ende)}">ganze Stunde</button>
          ${laeuft ? '<button type="button" class="ad-pille klein" data-ab-schnell="spaet" title="abwesend von Stundenbeginn bis jetzt">verspätet bis jetzt</button><button type="button" class="ad-pille klein" data-ab-schnell="frueh" title="abwesend ab jetzt bis Stundenende">ab jetzt gegangen</button>' : ''}
          <button type="button" class="ad-pille klein" data-ab-schnell="rest" title="abwesend bis ${e(tagesende)}">Rest des Tages</button></div>
        <span></span><div class="vs-vorschau" data-ab-vorschau></div>
        <label for="ab-grund">Abwesenheitsgrund</label><select id="ab-grund" data-ab-grund ${grundGesperrt ? 'disabled' : ''}>${gruendeWahl}</select>
        <label for="ab-text">Text</label><input id="ab-text" data-ab-text maxlength="500" value="${e(f?.text ?? '')}" ${grundGesperrt ? 'disabled title="Grund und Text trägt die Klassenleitung ein."' : ''}>
        <label>Status</label><div class="ab-status ab-segment ${verwalten ? '' : 'gesperrt'}" role="radiogroup" aria-label="Entschuldigungsstatus">${radio('entschuldigt', 'entschuldigt')}${radio('unentschuldigt', 'nicht entschuldigt')}${radio('offen', 'offen')}
          ${verwalten ? (f ? '<div class="ab-hinweis">Wird sofort gespeichert.</div>' : '') : '<div class="ab-hinweis"><i class="bi bi-lock"></i> Entschuldigen und Zeiträume über den Tag hinaus nur durch die Klassenleitung.</div>'}</div>
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
        if (q('[data-ab-ganz]').checked) {
          el.className = 'vs-vorschau';
          el.innerHTML = `<i class="bi bi-info-circle"></i> Ganztägig abwesend: alle Unterrichtsstunden ${vd === bd ? 'des Tages' : `vom ${e(vd.split('-').reverse().join('.'))} bis ${e(bd.split('-').reverse().join('.'))}`} werden als Fehlstunden eingetragen.`;
          return true;
        }
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
      const ganz = () => {
        const an = q('[data-ab-ganz]').checked;
        q('[data-ab-vz]').disabled = an; q('[data-ab-bz]').disabled = an;
        if (an) { q('[data-ab-vz]').value = tagesbeginn; q('[data-ab-bz]').value = tagesende; }
        vorschau();
      };
      q('[data-ab-ganz]').onchange = ganz;
      o.querySelectorAll('[data-ab-schnell]').forEach(b => b.onclick = () => {
        const art = b.dataset.abSchnell;
        q('[data-ab-vd]').value = k.datum; q('[data-ab-bd]').value = k.datum;
        q('[data-ab-ganz]').checked = false; q('[data-ab-vz]').disabled = false; q('[data-ab-bz]').disabled = false;
        if (art === 'stunde') { q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = ende; }
        if (art === 'spaet') { q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = jetztZeit; }
        if (art === 'frueh') { q('[data-ab-vz]').value = jetztZeit; q('[data-ab-bz]').value = ende; }
        if (art === 'rest') { if (q('[data-ab-vz]').value >= ende) q('[data-ab-vz]').value = start; q('[data-ab-bz]').value = tagesende > ende ? tagesende : ende; }
        vorschau();
      });
      if (ganzVorgabe) ganz(); else vorschau();
      q('[data-ab-speichern]').onclick = async ev => {
        if (!vorschau()) return;
        const knopf = ev.currentTarget;
        knopf.disabled = true;
        try {
          await senden('/api/app/klassenbuch/abwesenheit', {
            schuelerIds: ids, von: `${q('[data-ab-vd]').value}T${q('[data-ab-vz]').value}`, bis: `${q('[data-ab-bd]').value}T${q('[data-ab-bz]').value}`,
            grund: q('[data-ab-grund]').value, text: q('[data-ab-text]').value, status: verwalten ? o.querySelector('[name=ab-status]:checked')?.value ?? 'offen' : null,
            datum: k.datum, stunde: k.von, unterricht: k.unterricht, ganztaegig: q('[data-ab-ganz]').checked || null, ...eb(),
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
      if (f && verwalten) o.querySelectorAll('[name=ab-status]').forEach(r => r.onchange = async () => {
        try {
          await senden('/api/app/klassenbuch/abwesenheit/status', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, schuelerIds: ids, status: r.value, ...eb() });
          geaendert = true;
          toast({ entschuldigt: 'Als entschuldigt markiert.', unentschuldigt: 'Als nicht entschuldigt markiert.', offen: 'Status auf offen gesetzt.' }[r.value]);
          await anwesenheitNeu();
          zeichnen();
          // Dialog bleibt offen (liegt über den Stundendetails und überdauert das Neuzeichnen)
        } catch (err) { toast(err.message, true); }
      });
    }, 520);
  }

  function eintragDialog(vorauswahl) {
    const sl = daten.anwesenheit?.schueler ?? [];
    const kl = klassenListe();
    const b = bereich();
    const zeitWahl = [...new Set([...zeiten.filter(z => z.nr >= b.von && z.nr <= b.bis).map(z => z.beginn), b.beginn].filter(Boolean))].sort();
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

