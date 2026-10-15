// Stundendetails wie im Original: Kopf mit Klasse | Fach, Zeile mit Tag | Datum | Zeit | Raum | Lehrkraft,
// Reiter „Klassenbuch“ (Schülerkacheln, Lehrstoff, Hausaufgaben, Einträge, Abwesende) und „Details“ (Raum, Notizen),
// unten „Vorherige Stunde“, „Nächste Stunde“ und „Prüfung erstellen“.
import { e, icon, heuteIso, datumKurz, holen, senden, toast, WOCHENTAGE } from './ui.js';
import { statusText } from './plangitter.js';

const PRUEFUNGSARTEN = ['Klassenarbeit', 'Klausur', 'Test', 'Abschlussprüfung', 'Präsentation', 'Referat', 'Vergleichsarbeit'];
const KOPFSTATUS = { entfall: 'ENTFÄLLT', abwesend: 'ENTFÄLLT', offen: 'VERTRETUNG OFFEN' };

const wochentag = iso => WOCHENTAGE[(new Date(iso + 'T12:00').getDay() + 6) % 7];
const datumDe = iso => { const [j, m, t] = iso.split('-'); return `${Number(t)}.${Number(m)}.${j}`; };
const kuerzel = l => (l || []).map(w => w.k || w.alt).filter(Boolean);

/** Wert wie im Original: geänderter Wert „alt → neu“, offener Wert durchgestrichen. */
function wertKopf(liste, max = 6) {
  const l = liste || [];
  const teil = l.length > max + 1 ? l.slice(0, max - 2) : l;
  return teil.map(w => w.alt && !w.k ? `<span class="lbl"><s>${e(w.alt)}</s></span>`
    : w.alt ? `<s class="text-body-tertiary">${e(w.alt)}</s> → <span class="lbl">${e(w.k)}</span>` : e(w.k)).join(', ')
    + (teil.length < l.length ? ` +${l.length - teil.length}` : '');
}

function initialen(s) { return ((s.vorname?.[0] ?? '') + (s.name?.[0] ?? '')).toUpperCase(); }

/** Öffnet die Stundendetails. kontext.liste = alle Karten des Plans (für Vorherige/Nächste Stunde). */
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

  const mitKlassenbuch = () => kollegium && k.unterricht && !['entfall', 'vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status);
  const darfPruefung = () => kollegium && k.unterricht && k.klassen.length && k.datum >= heuteIso() && !['entfall', 'abwesend', 'veranstaltung', 'vorgemerkt'].includes(k.status);

  async function laden() {
    daten = { eintrag: null, darf: false, anwesenheit: null, vermerke: [], befreit: new Set() };
    if (!k.unterricht || ['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status)) return;
    try {
      const r = await holen(`/api/app/klassenbuch/stunde?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`);
      daten.eintrag = r.eintrag; daten.darf = r.darf;
    } catch { /* Klassenbuch optional */ }
    if (mitKlassenbuch()) {
      const kl = k.klassen.map(x => x.k).filter(Boolean)[0];
      const [anw, verm] = await Promise.all([
        holen(`/api/app/klassenbuch/anwesenheit?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`).catch(() => null),
        kl ? holen(`/api/app/klassenbuch/vermerke?klasse=${encodeURIComponent(kl)}&von=${k.datum}&bis=${k.datum}`).catch(() => null) : null,
      ]);
      daten.anwesenheit = anw;
      daten.vermerke = verm?.vermerke ?? [];
      // Befreiungen vom Unterricht (Wochentag, Fach, Uhrzeit) kennzeichnen
      daten.befreit = new Set(await holen(`/api/app/klassenbuch/befreit?datum=${k.datum}&klassen=${encodeURIComponent(k.klassen.map(x => x.k).filter(Boolean).join(','))}&fach=${encodeURIComponent(k.fach || '')}&beginn=${encodeURIComponent(k.beginn || '')}`).catch(() => []));
      daten.arten = verm?.arten ?? [];
    }
  }

  function kopf() {
    const titel = [k.klassen.length && k.klassen.length < 6 ? k.klassen.map(x => x.k || x.alt).join(', ') : '', k.fachName && k.fachName !== k.fach ? k.fachName : '', k.fach || k.text || k.art || 'Stunde']
      .filter(Boolean);
    const zeile = [wochentag(k.datum), datumDe(k.datum), k.beginn ? `${k.beginn}-${k.ende}` : `${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'} Std.`,
      wertKopf(k.raeume), wertKopf(k.lehrer)].filter(Boolean);
    const badge = k.status === 'klausur' ? '<span class="sd-badge">PRÜFUNG</span>' : '';
    return `<div class="sd-kopf ${KOPFSTATUS[k.status] ? 'grau' : ''}">
        <div class="sd-titel"><h2>${titel.map(e).join('<span class="trenn">|</span>')}${badge}</h2>
          <div class="sd-zeile">${zeile.join('<span class="trenn">|</span>')}</div></div>
        <div class="sd-knoepfe"><button type="button" data-bs-dismiss="modal" title="Zur Ansicht zurück" aria-label="Zur Ansicht zurück"><i class="bi bi-chevron-down"></i></button>
          <button type="button" data-bs-dismiss="modal" title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg"></i></button></div>
      </div>
      ${KOPFSTATUS[k.status] ? `<div class="sd-leiste">${KOPFSTATUS[k.status]}</div>` : ''}
      <ul class="nav sd-reiter">${mitKlassenbuch() ? `<li><button type="button" data-reiter="klassenbuch" class="${reiter === 'klassenbuch' ? 'aktiv' : ''}">Klassenbuch</button></li>` : ''}
        <li><button type="button" data-reiter="details" class="${reiter === 'details' ? 'aktiv' : ''}">Details</button></li></ul>`;
  }

  function fuss() {
    const i = liste.indexOf(k);
    return `<div class="sd-fuss">
      <button type="button" class="sd-pille" data-blaettern="-1" ${i <= 0 ? 'disabled' : ''}>${icon('links')} Vorherige Stunde</button>
      <button type="button" class="sd-pille" data-blaettern="1" ${i < 0 || i >= liste.length - 1 ? 'disabled' : ''}>Nächste Stunde ${icon('rechts')}</button>
      ${darfPruefung() ? '<button type="button" class="sd-pille" data-pruefung>Prüfung erstellen</button>' : ''}</div>`;
  }

  function zeichnen() {
    box.innerHTML = `<div class="modal-header p-0 d-block border-0">${kopf()}</div>
      <div class="modal-body sd-rumpf">${reiter === 'klassenbuch' ? klassenbuchHtml() : reiter === 'pruefung' ? pruefungHtml() : detailsHtml()}</div>
      <div class="modal-footer p-0 d-block">${reiter === 'pruefung' ? `<div class="sd-fuss rechts"><button type="button" class="sd-pille" data-abbrechen>Abbrechen</button>
        <button type="button" class="sd-pille primaer" data-speichern-pruefung>Speichern</button></div>` : fuss()}</div>`;
    binden();
    // Fokus im Dialog halten, damit Esc weiter schließt
    if (!el.contains(document.activeElement)) el.focus();
  }

  // ---------------------------------------------------------------- Klassenbuch

  function zustand() {
    const a = daten.anwesenheit;
    const m = new Map((a?.fehlzeiten ?? []).map(f => [f.schuelerId, f]));
    return m;
  }

  function klassenbuchHtml() {
    const a = daten.anwesenheit;
    const ein = daten.eintrag;
    const schueler = a?.schueler ?? [];
    const fz = zustand();
    const fehlend = schueler.filter(s => fz.has(s.id));
    const darf = !!a?.darf;
    const kacheln = schueler.map(s => {
      const f = fz.get(s.id);
      const st = !f ? 'da' : f.art === 'verspaetet' ? 'spaet' : 'weg';
      const befreit = daten.befreit?.has(s.id);
      return `<div class="sd-kachel ${st} ${befreit && st === 'da' ? 'befreit' : ''} ${auswahl.has(s.id) ? 'gewaehlt' : ''}" data-sid="${s.id}" role="button" tabindex="0" ${befreit ? 'title="vom Unterricht befreit"' : ''}>
        <span class="sd-status">${st === 'da' ? '<i class="bi bi-check-lg"></i>' : st === 'spaet' ? '<i class="bi bi-clock"></i>' : '<i class="bi bi-x-lg"></i>'}</span>
        <span class="sd-avatar">${e(initialen(s))}</span>
        <span class="sd-name"><span>${e(s.name)}</span><span>${e(s.vorname)}</span>${befreit ? '<span class="small text-body-secondary">befreit</span>' : ''}</span>
        ${darf ? `<button type="button" class="sd-umschalten" data-umschalten="${s.id}" title="${st === 'da' ? 'abwesend setzen' : 'anwesend setzen'}">${st === 'da' ? '<i class="bi bi-x-circle"></i>' : '<i class="bi bi-check-circle"></i>'}</button>` : ''}
      </div>`;
    }).join('');
    return `
      <section class="sd-abschnitt">
        <div class="sd-abschnitt-kopf"><i class="bi bi-people"></i><span>Schüler*innen im Unterricht</span><span class="sd-zahl">${schueler.length}</span></div>
        ${!a ? '<p class="small text-body-secondary">Anwesenheit kann nicht geladen werden.</p>'
          : !schueler.length ? '<p class="small text-body-secondary mb-0">Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen (UntPlanWeb → Einstellungen → „Schülerliste übertragen“).</p>'
          : `${darf ? `<div class="d-flex flex-wrap gap-2 mb-3"><button type="button" class="sd-pille klein" data-alle>Alle auswählen</button><button type="button" class="sd-pille klein" data-keine>Auswahl löschen</button></div>` : ''}
            <div class="sd-kacheln">${kacheln}</div>
            ${darf ? `<div class="d-flex flex-wrap gap-2 mt-3">
              <button type="button" class="sd-pille klein grau" data-setzen="fehlt" ${auswahl.size ? '' : 'disabled'}>abwesend</button>
              <button type="button" class="sd-pille klein grau" data-setzen="verspaetet" ${auswahl.size ? '' : 'disabled'}>verspätet</button>
              <button type="button" class="sd-pille klein grau" data-setzen="da" ${auswahl.size ? '' : 'disabled'}>anwesend</button>
              <button type="button" class="sd-pille klein grau" data-eintrag ${auswahl.size ? '' : 'disabled'}>Klassenbucheintrag</button></div>` : ''}`}
      </section>
      ${textAbschnitt('thema', 'Lehrstoff', 'bi-file-text', ein?.thema)}
      ${textAbschnitt('hausaufgabe', 'Hausaufgaben', 'bi-house', ein?.hausaufgabe, ein?.faellig)}
      <section class="sd-abschnitt">
        <div class="sd-abschnitt-kopf"><i class="bi bi-journal-text"></i><span>Klassenbucheinträge</span><span class="sd-zahl">${daten.vermerke.length}</span></div>
        ${daten.vermerke.length ? `<ul class="list-unstyled mb-0 small">${daten.vermerke.map(v => `<li class="py-1 d-flex gap-2"><span class="badge text-bg-light border fw-normal">${e(v.art)}</span>
          <span>${v.schuelerId ? `<b>${e(schuelerName(v.schuelerId))}:</b> ` : ''}${e(v.text)}</span><span class="ms-auto text-body-secondary">${e(v.lehrer)}</span></li>`).join('')}</ul>`
          : '<p class="small text-body-secondary mb-0">Keine Einträge.</p>'}
      </section>
      <section class="sd-abschnitt">
        <div class="sd-abschnitt-kopf"><i class="bi bi-person-dash"></i><span>Abwesende Schüler*innen</span><span class="sd-zahl">${fehlend.length}</span></div>
        <div class="table-responsive"><table class="table table-sm sd-tabelle mb-0"><thead><tr><th>Schüler*innen</th><th>Von</th><th>Beginnzeit</th><th>Bis</th><th>Endzeit</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th></tr></thead><tbody>
          ${fehlend.length ? fehlend.map(s => { const f = fz.get(s.id); return `<tr><td>${e(s.name)} ${e(s.vorname)}</td><td>${datumKurz(k.datum)}</td><td>${e(k.beginn)}</td><td>${datumKurz(k.datum)}</td><td>${e(k.ende)}</td>
            <td>${f.art === 'verspaetet' ? `verspätet (${f.minuten} min)` : '—'}</td><td>${{ offen: 'offen', entschuldigt: 'entschuldigt', unentschuldigt: 'unentschuldigt' }[f.status] ?? ''}</td><td>${e(f.bemerkung)}</td></tr>`; }).join('')
          : '<tr><td colspan="8" class="text-body-secondary">Keine abwesenden Schüler*innen.</td></tr>'}
        </tbody></table></div>
      </section>
      <div class="sd-kontrolle">
        <span class="${ein?.thema ? 'ok' : ''}"><i class="bi ${ein?.thema ? 'bi-check-circle-fill' : 'bi-circle'}"></i> ${ein?.thema ? 'Lehrstoffeintragung vorhanden' : 'Keine Lehrstoffeintragung'}</span>
        <span class="${a?.geprueft ? 'ok' : ''}"><i class="bi ${a?.geprueft ? 'bi-check-circle-fill' : 'bi-circle'}"></i> ${a?.geprueft ? 'Abwesenheiten wurden kontrolliert' : 'Abwesenheiten nicht kontrolliert'}</span>
      </div>`;
  }

  const schuelerName = id => { const s = daten.anwesenheit?.schueler.find(x => x.id === id); return s ? `${s.name} ${s.vorname}` : ''; };

  /** Abschnitt mit Text und Bleistift zum Bearbeiten (Lehrstoff, Hausaufgaben, Notizen). */
  function textAbschnitt(feld, titel, ic, wert, faellig) {
    const darf = daten.darf;
    const bearbeiten = offenesFeld === feld;
    return `<section class="sd-abschnitt">
      <div class="sd-abschnitt-kopf"><i class="bi ${ic}"></i><span>${titel}</span>
        ${darf && !bearbeiten ? `<button type="button" class="sd-stift ms-auto" data-bearbeiten="${feld}" title="Bearbeiten"><i class="bi bi-pencil"></i></button>` : ''}</div>
      ${bearbeiten ? `<textarea class="form-control mb-2" rows="3" data-feldwert>${e(wert)}</textarea>
          ${feld === 'hausaufgabe' ? `<div class="d-flex align-items-center gap-2 mb-2 small"><span>fällig am</span><input type="date" class="form-control form-control-sm" style="width:auto" data-faellig value="${e(faellig ?? '')}"></div>` : ''}
          <div class="d-flex gap-2"><button type="button" class="sd-pille klein primaer" data-feld-speichern="${feld}">${icon('haken')} Speichern</button><button type="button" class="sd-pille klein" data-feld-abbrechen>Abbrechen</button></div>`
        : wert ? `<p class="sd-text">${e(wert)}${faellig ? ` <span class="badge text-bg-light border fw-normal">bis ${datumKurz(faellig)}</span>` : ''}</p>`
        : `<p class="sd-text text-body-tertiary">${darf ? 'Noch nichts eingetragen.' : '—'}</p>`}
    </section>`;
  }

  // ---------------------------------------------------------------- Details

  function detailsHtml() {
    const ein = daten.eintrag;
    const st = statusText(k);
    const raumAenderbar = darfPruefung();
    const zeile = (ic, titel, inhalt) => inhalt ? `<div class="sd-detail"><i class="bi ${ic}"></i><div><div class="sd-detail-titel">${titel}</div><div>${inhalt}</div></div></div>` : '';
    const pillen = liste => (liste || []).map(w => w.alt && !w.k ? `<span class="sd-wert neu"><s>${e(w.alt)}</s></span>`
      : w.alt ? `<span class="sd-wert neu"><s class="text-body-tertiary">${e(w.alt)}</s> → ${e(w.k)}</span>` : `<span class="sd-wert">${e(w.k)}</span>`).join('');
    const online = k.unterricht && !['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status) && kollegium
      ? `<div class="sd-detail"><i class="bi bi-camera-video"></i><div class="form-check form-switch mb-0 d-flex align-items-center gap-2 ps-0">
          <label class="sd-detail-titel mb-0" for="sd-online">Findet online statt</label><input class="form-check-input ms-1 mt-0" type="checkbox" role="switch" id="sd-online" data-online ${ein?.online ? 'checked' : ''} ${daten.darf ? '' : 'disabled'}></div></div>` : '';
    return `
      ${online}
      ${k.status !== 'regulaer' && st ? zeile('bi-info-circle', k.status === 'klausur' ? 'Prüfung' : 'Status', `<span class="fw-semibold">${e(st)}</span>${k.art && k.art !== st ? ` <span class="text-body-secondary">· ${e(k.art)}</span>` : ''}`) : ''}
      ${k.status === 'klausur' && k.text ? zeile('bi-file-earmark-check', 'Prüfung', e(k.text)) : ''}
      <div class="sd-detail"><i class="bi bi-door-open"></i><div class="flex-grow-1"><div class="sd-detail-titel">Raum</div>
        <div class="d-flex flex-wrap align-items-center gap-2">${pillen(k.raeume) || '<span class="text-body-tertiary">—</span>'}
          ${raumAenderbar ? `<button type="button" class="sd-plus" data-raum title="Raum ändern / buchen"><i class="bi bi-plus-lg"></i></button>` : ''}</div>
        <div data-raumwahl></div></div></div>
      ${zeile('bi-book', 'Fach', k.fach ? `${/^#[0-9a-f]{6}$/i.test(k.farbe || '') ? `<span class="fachpunkt" style="background:${k.farbe}"></span>` : ''}${k.fachAlt ? `<s class="text-body-tertiary">${e(k.fachAlt)}</s> → ` : ''}${e(k.fachName || k.fach)}${k.fachName && k.fachName !== k.fach ? ` (${e(k.fach)})` : ''}` : '')}
      ${zeile('bi-person', k.lehrer.length > 1 ? 'Lehrkräfte' : 'Lehrkraft', pillen(k.lehrer))}
      ${zeile('bi-people', 'Klasse(n)', pillen(k.klassen))}
      ${k.text && k.status !== 'klausur' ? zeile('bi-chat-left-text', k.fach ? 'Vertretungstext' : 'Bezeichnung', e(k.text)) : ''}
      ${k.unterricht && !['vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status) ? `
        ${zeile('bi-file-text', 'Lehrstoff', ein?.thema ? e(ein.thema) : '<span class="text-body-tertiary">—</span>')}
        ${!kollegium && ein?.hausaufgabe ? zeile('bi-house', 'Hausaufgaben', `${e(ein.hausaufgabe)}${ein.faellig ? ` <span class="badge text-bg-light border fw-normal">bis ${datumKurz(ein.faellig)}</span>` : ''}`) : ''}
        ${notizFeld('notizSchueler', 'Notizen für Schüler*innen', ein?.notizSchueler)}
        ${kollegium ? notizFeld('notiz', 'Notizen für Lehrkräfte', ein?.notiz) : ''}` : ''}`;
  }

  function notizFeld(feld, titel, wert) {
    if (!daten.darf) return wert ? `<div class="sd-detail"><i class="bi bi-sticky"></i><div><div class="sd-detail-titel">${titel}</div><div>${e(wert)}</div></div></div>` : '';
    return `<div class="sd-detail"><i class="bi bi-sticky"></i><div class="flex-grow-1"><div class="sd-detail-titel">${titel}</div>
      <textarea class="form-control sd-notiz" rows="2" data-notiz="${feld}">${e(wert)}</textarea></div></div>`;
  }

  // ---------------------------------------------------------------- Prüfung erstellen

  let pruefungSchueler = null;
  function pruefungHtml() {
    const schueler = daten.anwesenheit?.schueler ?? [];
    if (!pruefungSchueler) pruefungSchueler = new Set(schueler.map(s => s.id));
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
      <label>Schüler*innen</label><div class="sd-chips">${schueler.length ? schueler.map(s => `<button type="button" class="sd-chip ${pruefungSchueler.has(s.id) ? 'an' : ''}" data-chip="${s.id}">${e(s.name)} ${e(s.vorname)}</button>`).join('')
        : `<span class="small text-body-secondary">Klasse(n) ${e(kuerzel(k.klassen).join(', '))}</span>`}</div>
    </div>`;
  }

  // ---------------------------------------------------------------- Ereignisse

  const auswahl = new Set();
  let offenesFeld = null;

  async function anwesenheitSpeichern(neu) {
    const eintraege = [...neu].map(([schuelerId, f]) => ({ schuelerId, art: f.art, minuten: f.minuten || 5, bemerkung: f.bemerkung ?? '' }));
    try {
      await senden('/api/app/klassenbuch/anwesenheit', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, eintraege }, 'PUT');
      daten.anwesenheit.fehlzeiten = eintraege.map(x => ({ ...x, status: daten.anwesenheit.fehlzeiten.find(f => f.schuelerId === x.schuelerId && f.art === x.art)?.status ?? 'offen' }));
      daten.anwesenheit.geprueft = 'soeben';
      geaendert = true;
    } catch (err) { toast(err.message, true); }
    zeichnen();
  }

  async function eintragSpeichern(aenderung) {
    const alt = daten.eintrag ?? {};
    const neu = { thema: alt.thema ?? '', hausaufgabe: alt.hausaufgabe ?? '', faellig: alt.faellig ?? null, notiz: alt.notiz ?? '', notizSchueler: alt.notizSchueler ?? '', ...aenderung };
    try {
      daten.eintrag = await senden('/api/app/klassenbuch/stunde', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, ...neu }, 'PUT');
      geaendert = true;
      toast('Gespeichert.');
    } catch (err) { toast(err.message, true); }
  }

  function binden() {
    box.querySelectorAll('[data-reiter]').forEach(b => b.onclick = () => { reiter = b.dataset.reiter; offenesFeld = null; zeichnen(); });
    box.querySelectorAll('[data-blaettern]').forEach(b => b.onclick = async () => {
      const x = liste[liste.indexOf(k) + Number(b.dataset.blaettern)];
      if (!x) return;
      k = x; auswahl.clear(); offenesFeld = null; pruefungSchueler = null;
      box.querySelector('.sd-rumpf').innerHTML = '<div class="py-5 text-center"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
      await laden();
      if (reiter === 'klassenbuch' && !mitKlassenbuch()) reiter = 'details';
      if (reiter === 'pruefung') reiter = 'details';
      zeichnen();
    });
    box.querySelector('[data-pruefung]')?.addEventListener('click', () => { reiter = 'pruefung'; zeichnen(); });
    box.querySelector('[data-abbrechen]')?.addEventListener('click', () => { reiter = mitKlassenbuch() ? 'klassenbuch' : 'details'; zeichnen(); });
    box.querySelectorAll('[data-chip]').forEach(c => c.onclick = () => {
      const id = Number(c.dataset.chip);
      pruefungSchueler.has(id) ? pruefungSchueler.delete(id) : pruefungSchueler.add(id);
      c.classList.toggle('an');
    });
    box.querySelector('[data-speichern-pruefung]')?.addEventListener('click', async ev => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      const listeVon = n => f(n).split(/[,;\s]+/).map(x => x.trim()).filter(Boolean);
      const schueler = daten.anwesenheit?.schueler ?? [];
      const teil = schueler.length && pruefungSchueler.size < schueler.length
        ? `Teilnehmende: ${schueler.filter(s => pruefungSchueler.has(s.id)).map(s => `${s.vorname} ${s.name}`).join(', ')}` : '';
      const text = [f('art') ? `${f('art')}${f('text') ? ': ' + f('text') : ''}` : f('text'), teil].filter(Boolean).join(' – ');
      ev.currentTarget.disabled = true;
      try {
        await senden('/api/app/meldungen/klausur', { datum: f('datum'), stundeVon: Number(f('von')), stundeBis: Number(f('bis')), klassen: kuerzel(k.klassen), raeume: listeVon('raeume'), aufsicht: listeVon('aufsicht'), titel: f('name') || 'Prüfung', text });
        toast('Prüfung erstellt. Sie erscheint als „vorgemerkt“, bis UntPlan sie übernimmt.');
        geaendert = true;
        modal.hide();
      } catch (err) { toast(err.message, true); ev.currentTarget.disabled = false; }
    });

    // Schülerkacheln
    const fz = () => new Map(zustand());
    box.querySelectorAll('[data-sid]').forEach(t => t.onclick = ev => {
      if (ev.target.closest('[data-umschalten]') || !daten.anwesenheit?.darf) return;
      const id = Number(t.dataset.sid);
      auswahl.has(id) ? auswahl.delete(id) : auswahl.add(id);
      zeichnen();
    });
    box.querySelectorAll('[data-umschalten]').forEach(b => b.onclick = () => {
      const id = Number(b.dataset.umschalten);
      const m = fz();
      m.has(id) ? m.delete(id) : m.set(id, { art: 'fehlt', minuten: 0 });
      anwesenheitSpeichern(m);
    });
    box.querySelector('[data-alle]')?.addEventListener('click', () => { (daten.anwesenheit?.schueler ?? []).forEach(s => auswahl.add(s.id)); zeichnen(); });
    box.querySelector('[data-keine]')?.addEventListener('click', () => { auswahl.clear(); zeichnen(); });
    box.querySelectorAll('[data-setzen]').forEach(b => b.onclick = () => {
      const m = fz();
      let minuten = 5;
      if (b.dataset.setzen === 'verspaetet') {
        const w = prompt('Verspätung in Minuten', '5');
        if (w === null) return;
        minuten = Math.max(1, Math.min(90, Number(w) || 5));
      }
      for (const id of auswahl) {
        if (b.dataset.setzen === 'da') m.delete(id);
        else m.set(id, { art: b.dataset.setzen, minuten: b.dataset.setzen === 'verspaetet' ? minuten : 0, bemerkung: m.get(id)?.bemerkung ?? '' });
      }
      auswahl.clear();
      anwesenheitSpeichern(m);
    });
    box.querySelector('[data-eintrag]')?.addEventListener('click', () => vermerkFormular());

    // Lehrstoff / Hausaufgaben
    box.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => { offenesFeld = b.dataset.bearbeiten; zeichnen(); box.querySelector('[data-feldwert]')?.focus(); });
    box.querySelector('[data-feld-abbrechen]')?.addEventListener('click', () => { offenesFeld = null; zeichnen(); });
    box.querySelector('[data-feld-speichern]')?.addEventListener('click', async b => {
      const feld = b.currentTarget.dataset.feldSpeichern;
      const aenderung = { [feld]: box.querySelector('[data-feldwert]').value };
      if (feld === 'hausaufgabe') aenderung.faellig = box.querySelector('[data-faellig]').value || null;
      await eintragSpeichern(aenderung);
      offenesFeld = null;
      zeichnen();
    });
    box.querySelector('[data-online]')?.addEventListener('change', async ev => {
      try { await senden('/api/app/klassenbuch/online', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, online: ev.target.checked }, 'PUT'); if (daten.eintrag) daten.eintrag.online = ev.target.checked; geaendert = true; toast(ev.target.checked ? 'Die Stunde findet online statt.' : 'Die Stunde findet im Raum statt.'); }
      catch (err) { ev.target.checked = !ev.target.checked; toast(err.message, true); }
    });
    // Notizen (Details): speichern beim Verlassen
    box.querySelectorAll('[data-notiz]').forEach(t => t.onchange = () => eintragSpeichern({ [t.dataset.notiz]: t.value }));
    // Raum ändern / buchen
    box.querySelector('[data-raum]')?.addEventListener('click', () => raumWahl(box.querySelector('[data-raumwahl]')));
  }

  function vermerkFormular() {
    const ziel = box.querySelector('[data-eintrag]').parentElement;
    const arten = daten.arten?.length ? daten.arten : ['Vermerk'];
    ziel.insertAdjacentHTML('afterend', `<div class="sd-vermerk border rounded p-3 mt-2">
      <div class="small text-body-secondary mb-2">Klassenbucheintrag für ${auswahl.size} ${auswahl.size === 1 ? 'Schüler*in' : 'Schüler*innen'}</div>
      <div class="d-flex flex-wrap gap-2"><select class="form-select form-select-sm" style="width:auto" data-vart>${arten.map(a => `<option>${e(a)}</option>`).join('')}</select>
        <input class="form-control form-control-sm flex-grow-1" style="min-width:200px" data-vtext placeholder="Text">
        <button type="button" class="sd-pille klein primaer" data-vok>Speichern</button></div></div>`);
    box.querySelector('[data-vok]').onclick = async () => {
      const text = box.querySelector('[data-vtext]').value.trim();
      if (!text) return toast('Bitte einen Text eingeben.', true);
      const kl = kuerzel(k.klassen)[0];
      try {
        for (const id of auswahl) {
          const s = daten.anwesenheit.schueler.find(x => x.id === id);
          daten.vermerke.unshift(await senden('/api/app/klassenbuch/vermerke', { datum: k.datum, klasse: s?.klasse || kl, schuelerId: id, art: box.querySelector('[data-vart]').value, text }));
        }
        auswahl.clear();
        toast('Klassenbucheintrag gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); }
    };
  }

  async function raumWahl(ziel) {
    ziel.innerHTML = '<div class="py-2"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
    let raeume;
    try { raeume = await holen(`/api/app/meldungen/freieraeume?datum=${k.datum}&von=${k.von}&bis=${k.bis}`); }
    catch (err) { ziel.innerHTML = `<p class="small text-danger mt-2">${e(err.message)}</p>`; return; }
    if (!raeume.length) { ziel.innerHTML = '<p class="small text-body-secondary mt-2 mb-0">In dieser Zeit ist kein buchbarer Raum frei.</p>'; return; }
    ziel.innerHTML = `<div class="small text-body-secondary mt-3 mb-2">Freie buchbare Räume</div><div class="raum-wahl">${raeume.map((r, i) => `<button type="button" class="btn btn-sm btn-outline-secondary text-start" data-r="${i}"><b>${e(r.kurz)}</b>
      <small class="d-block text-body-secondary text-truncate">${e([r.name !== r.kurz ? r.name : '', r.raumart, r.kapazitaet ? r.kapazitaet + ' Plätze' : ''].filter(Boolean).join(' · ') || ' ')}</small></button>`).join('')}</div>`;
    ziel.querySelectorAll('[data-r]').forEach(b => b.onclick = async () => {
      const r = raeume[Number(b.dataset.r)];
      try {
        await senden('/api/app/meldungen/raumbuchung', { datum: k.datum, stundeVon: k.von, stundeBis: k.bis, raum: r.kurz, klassen: kuerzel(k.klassen), titel: `${k.fach || 'Unterricht'} – Raumänderung` });
        toast(`Raum ${r.kurz} gebucht.`);
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

