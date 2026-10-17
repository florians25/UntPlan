// Administration → Benutzer und Personen & Konten (neues Design):
// - Benutzerliste als Tabelle mit Suche, Filtern (Rolle, Status, Zuordnung, letzte Anmeldung), Sortierung, Mehrfachauswahl und Sammelaktionen.
// - Seitenpanel je Konto: Stammdaten, Rolle und Zusatzrechte, Personenzuordnung (Lehrkraft, Schüler*in, Kinder, Person), Status,
//   Anmeldehistorie und Konto-Aktionen.
// - Personen (Lehrkräfte, Schüler*innen, Erziehungsberechtigte, sonstiges Personal) mit Konto-Status; fehlende Konten einzeln oder
//   gesammelt nach Login-Schema anlegen, Zugangsdaten als CSV bzw. PDF (Schwarz-Weiß).
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, heuteIso, leer, alarm, initialen, herunterladen, pdfAusrichtung, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';

const ROLLEN = { admin: 'Administration', leitung: 'Schulleitung', lehrer: 'Lehrkraft', schueler: 'Schüler*in / Klasse', eltern: 'Erziehungsberechtigte*r' };
const KOLLEGIUM = ['admin', 'leitung', 'lehrer'];
const ARTEN = { lehrer: 'Lehrkräfte', schueler: 'Schüler*innen', eltern: 'Erziehungsberechtigte', personal: 'Sonstiges Personal' };
const SCHEMATA = { kuerzel: 'Kürzel (z. B. bec)', 'vorname.nachname': 'vorname.nachname', 'v.nachname': 'v.nachname', 'nachname.vorname': 'nachname.vorname', nachname: 'nachname' };
const ERGEBNIS = { ok: ['gruen', 'erfolgreich'], falsch: ['rot', 'falsches Passwort'], gesperrt: ['gelb', 'Konto gesperrt'] };

const zeit = s => s ? new Date(s.replace(' ', 'T')) : null;
const kurzZeit = s => s ? zeit(s).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '';
/** Relative Angabe („heute“, „vor 3 Tagen“) für die letzte Anmeldung. */
function seit(s) {
  if (!s) return 'nie';
  const t = Math.floor((Date.now() - zeit(s).getTime()) / 864e5);
  if (t <= 0) return `heute, ${zeit(s).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
  if (t === 1) return 'gestern';
  if (t < 30) return `vor ${t} Tagen`;
  if (t < 365) return `vor ${Math.round(t / 30)} Mon.`;
  return `vor ${Math.round(t / 365)} J.`;
}
const tageSeit = s => s ? (Date.now() - zeit(s).getTime()) / 864e5 : Infinity;
const avatar = (name, voll = true) => `<span class="nl-avatar ${voll ? 'voll' : ''}">${e(initialen(name))}</span>`;
const csvZelle = x => `"${String(x ?? '').replace(/"/g, '""')}"`;
const optionen = (werte, aktuell) => Object.entries(werte).map(([w, t]) => `<option value="${e(w)}" ${w === aktuell ? 'selected' : ''}>${e(t)}</option>`).join('');
const pille = (name, html, aktiv) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}"><span class="visually-hidden">${e(name)}</span>${html}</label>`;
const personName = p => [p.vorname, p.name].filter(Boolean).join(' ');

/** Zugangsdaten anzeigen (nur einmal sichtbar) mit CSV- und PDF-Export. */
export function zugangsdaten(titel, r) {
  if (!r?.anzahl) return dialog({ titel, inhalt: '<p class="mb-0">Es wurden keine Konten angelegt – alle gewählten Personen haben bereits ein Konto.</p>', knoepfe: [{ text: 'OK', art: 'haupt' }] });
  dialog({
    titel, breit: true,
    inhalt: `${alarm('warning', 'Die Startpasswörter werden nur jetzt angezeigt. Bitte die Zugangsdaten herunterladen bzw. drucken und sicher verteilen.')}
      <div class="nl-tabelle bv-zugang"><table><thead><tr><th>Name</th><th>Klasse / Kürzel</th><th>Rolle</th><th>Benutzername</th><th>Startpasswort</th></tr></thead><tbody>
      ${r.konten.map(k => `<tr><td>${e(k.name)}</td><td>${e(k.klasse || k.zuordnung)}</td><td>${e(k.rollenname)}</td><td class="fw-semibold">${e(k.login)}</td><td class="font-monospace">${e(k.passwort)}</td></tr>`).join('')}
      </tbody></table></div>`,
    knoepfe: [
      { text: `${icon('csv')} CSV`, aktion: () => { herunterladen(`zugangsdaten-${heuteIso()}.csv`, r.csv); return false; } },
      { text: `${icon('drucken')} PDF (S/W)`, aktion: async () => {
        const ausrichtung = await pdfAusrichtung('hoch', 'zugangsdaten');
        if (!ausrichtung) return false;
        const antwort = await fetch('/api/app/admin/personen/zugangsdaten.pdf', { method: 'POST', credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1', 'Content-Type': 'application/json' },
          body: JSON.stringify({ konten: r.konten, titel, ausrichtung }) });
        if (!antwort.ok) throw new Error((await antwort.json().catch(() => ({}))).fehler ?? 'PDF konnte nicht erzeugt werden.');
        herunterladen(`zugangsdaten-${heuteIso()}.pdf`, await antwort.blob());
        return false;
      } },
      { text: 'Schließen', art: 'haupt' },
    ],
  });
}

// ================================================================== Benutzer

export async function benutzer(ziel, kontext, neu, [rolleParam, idParam]) {
  const [liste, grundlagen, personen, el] = await Promise.all([
    holen('/api/app/admin/benutzer'), holen('/api/app/admin/benutzerverwaltung/grundlagen'), holen('/api/app/admin/personen').catch(() => []), kontext.elemente(),
  ]);
  const ich = kontext.ich;
  const schueler = personen.filter(p => p.art === 'schueler');
  const lehrer = personen.filter(p => p.art === 'lehrer');
  const andere = personen.filter(p => p.art === 'eltern' || p.art === 'personal');
  const schuelerNach = Object.fromEntries(schueler.map(s => [Number(s.schluessel), s]));
  const lehrerNach = Object.fromEntries(lehrer.map(l => [l.kurz.toLowerCase(), l]));
  const personNach = Object.fromEntries(andere.map(p => [Number(p.schluessel), p]));

  /** Zuordnung eines Kontos als Text (für Tabelle, Suche und Filter). */
  const zuordnung = b => {
    if (KOLLEGIUM.includes(b.rolle) && b.element) { const l = lehrerNach[b.element.toLowerCase()]; return { art: 'lehrkraft', text: b.element, unter: l ? personName(l) : 'nicht in den Stammdaten' }; }
    if (b.rolle === 'schueler' && b.schuelerIds?.length) { const s = schuelerNach[b.schuelerIds[0]]; return { art: 'schueler', text: s ? personName(s) : `#${b.schuelerIds[0]}`, unter: s ? `Klasse ${s.klasse}` : 'nicht mehr in der Schülerliste' }; }
    if (b.rolle === 'eltern' && b.schuelerIds?.length) return { art: 'kinder', text: b.schuelerIds.map(i => schuelerNach[i] ? personName(schuelerNach[i]) : `#${i}`).join(', '), unter: `${b.schuelerIds.length === 1 ? 'Kind' : 'Kinder'}${b.klassen?.length ? ' · ' + b.klassen.join(', ') : ''}` };
    if (b.personId && personNach[b.personId]) { const p = personNach[b.personId]; return { art: 'person', text: personName(p), unter: p.funktion || ARTEN[p.art] }; }
    if (b.rolle === 'schueler' && b.element) return { art: 'klasse', text: `Klasse ${b.element}`, unter: 'Klassenkonto' };
    return null;
  };

  const zustand = { suche: '', rolle: rolleParam && ROLLEN[rolleParam] ? rolleParam : '', status: '', zuordnung: '', anmeldung: '', sortierung: 'name', richtung: 1 };
  kontext.titel('Benutzer', '', `${liste.length}`);
  const anzahl = r => liste.filter(b => b.rolle === r).length;
  ziel.innerHTML = `
    <div class="nl-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Benutzername, Name, Kürzel oder E-Mail', label: 'Benutzer suchen', filter: [
        { name: 'rolle', attr: 'data-f="rolle"', label: 'Rolle', alle: 'Alle Rollen', wert: zustand.rolle, optionen: Object.entries(ROLLEN) },
        { name: 'status', attr: 'data-f="status"', label: 'Status', alle: 'Jeder Status', optionen: [['aktiv', 'Aktiv'], ['gesperrt', 'Gesperrt'], ['start', 'Startpasswort offen']] },
        { name: 'zuordnung', attr: 'data-f="zuordnung"', label: 'Zuordnung', alle: 'Jede Zuordnung', optionen: [['ohne', 'Ohne Zuordnung'], ['mit', 'Mit Zuordnung']] },
        { name: 'anmeldung', attr: 'data-f="anmeldung"', label: 'Letzte Anmeldung', alle: 'Jede Anmeldung', optionen: [['7', 'In den letzten 7 Tagen'], ['30', 'In den letzten 30 Tagen'], ['alt', 'Vor mehr als 90 Tagen'], ['nie', 'Noch nie angemeldet']] },
      ] })}
      <span class="ms-auto d-flex gap-2 flex-wrap">
        <button type="button" class="nl-knopf" data-bericht>${icon('csv')}Bericht</button>
        <a class="nl-knopf" href="#/admin/personen">${icon('lehrkraft')}Personen & Konten</a>
        <button type="button" class="nl-hinzu" data-neu><i class="bi bi-plus-lg"></i>Benutzer anlegen</button>
      </span>
    </div>
    <div class="nl-zahlen" data-zahlen>
      <button type="button" class="nl-zahl" data-kz=""><b>${liste.length}</b> alle</button>
      ${Object.entries(ROLLEN).map(([r, n]) => anzahl(r) ? `<button type="button" class="nl-zahl" data-kz="${r}"><b>${anzahl(r)}</b> ${e(n)}</button>` : '').join('')}
      <button type="button" class="nl-zahl rot" data-kz-status="gesperrt"><b>${liste.filter(b => b.gesperrt).length}</b> gesperrt</button>
      <button type="button" class="nl-zahl gelb" data-kz-zuordnung="ohne"><b>${liste.filter(b => !zuordnung(b) && b.rolle !== 'admin').length}</b> ohne Zuordnung</button>
    </div>
    <div class="nl-tabelle bv-tabelle"><table><thead><tr>
        <th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle sichtbaren auswählen"></th>
        <th colspan="2" data-sort="name" class="bv-sort">Benutzer</th><th data-sort="rolle" class="bv-sort">Rolle</th><th data-sort="zuordnung" class="bv-sort">Zuordnung</th>
        <th data-sort="status" class="bv-sort">Status</th><th data-sort="anmeldung" class="bv-sort">Letzte Anmeldung</th><th></th></tr></thead>
      <tbody data-zeilen></tbody></table>
      <div class="nl-fuss bv-fuss">
        <span class="nl-unter me-2" data-anzahl></span>
        <span class="bv-auswahl" data-auswahlinfo></span>
        <button class="nl-knopf" data-aktion="sperren">${icon('schloss')}Sperren</button>
        <button class="nl-knopf" data-aktion="entsperren">Entsperren</button>
        <button class="nl-knopf" data-aktion="passwort_zuruecksetzen">${icon('schluessel')}Passwort zurücksetzen</button>
        <button class="nl-knopf" data-aktion="passwort_aendern">Passwortänderung erzwingen</button>
        <span class="bv-rolle-setzen"><select data-sammelrolle data-platzhalter="Rolle setzen …" aria-label="Rolle für die Auswahl setzen"><option value="">Rolle setzen …</option>${optionen(ROLLEN)}</select></span>
        <button class="nl-knopf" data-aktion="email_uebernehmen" title="E-Mail-Adresse der Lehrkraft aus den Stammdaten übernehmen">E-Mail übernehmen</button>
        <button class="nl-knopf rot" data-aktion="loeschen">${icon('loeschen')}Löschen</button>
        <button class="nl-knopf ms-auto" data-aktion="inaktive_sperren" title="Konten sperren, deren Kürzel bzw. Person nicht mehr in den Stammdaten steht">Inaktive Personen sperren</button>
      </div></div>`;

  const tbody = ziel.querySelector('[data-zeilen]');
  const statusWert = b => b.gesperrt ? 2 : b.passwortAendern ? 1 : 0;
  const sichtbar = () => {
    const f = zustand.suche.trim().toLowerCase();
    const res = liste.filter(b => {
      const z = zuordnung(b);
      if (zustand.rolle && b.rolle !== zustand.rolle) return false;
      if (zustand.status === 'aktiv' && b.gesperrt) return false;
      if (zustand.status === 'gesperrt' && !b.gesperrt) return false;
      if (zustand.status === 'start' && !b.passwortAendern) return false;
      if (zustand.zuordnung === 'ohne' && (z || b.rolle === 'admin')) return false;
      if (zustand.zuordnung === 'mit' && !z) return false;
      const t = tageSeit(b.letzteAnmeldung);
      if (zustand.anmeldung === '7' && t > 7) return false;
      if (zustand.anmeldung === '30' && t > 30) return false;
      if (zustand.anmeldung === 'alt' && (t <= 90 || t === Infinity)) return false;
      if (zustand.anmeldung === 'nie' && b.letzteAnmeldung) return false;
      return !f || `${b.login} ${b.anzeigename} ${b.element} ${b.email} ${z?.text ?? ''} ${z?.unter ?? ''}`.toLowerCase().includes(f);
    });
    const schluessel = {
      name: b => (b.anzeigename || b.login).toLowerCase(), rolle: b => Object.keys(ROLLEN).indexOf(b.rolle), zuordnung: b => zuordnung(b)?.text?.toLowerCase() ?? '~',
      status: statusWert, anmeldung: b => b.letzteAnmeldung ?? '',
    }[zustand.sortierung];
    return res.sort((a, b) => { const x = schluessel(a), y = schluessel(b); return (x < y ? -1 : x > y ? 1 : 0) * zustand.richtung || a.login.localeCompare(b.login); });
  };
  const gewaehlt = new Set();
  const zeichnen = () => {
    const l = sichtbar();
    tbody.innerHTML = l.length ? l.map(b => {
      const z = zuordnung(b);
      return `<tr data-id="${b.id}" class="${gewaehlt.has(b.id) ? 'nl-gewaehlt' : ''} ${b.gesperrt ? 'bv-gesperrt' : ''}" tabindex="0">
        <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${b.id}" ${gewaehlt.has(b.id) ? 'checked' : ''} aria-label="${e(b.login)} auswählen"></td>
        <td class="nl-avatar-zelle">${avatar(b.anzeigename || b.login, !b.gesperrt)}</td>
        <td><div class="nl-name">${e(b.anzeigename || b.login)}</div><div class="nl-unter">${e(b.login)}${b.email ? ` · ${e(b.email)}` : ''}</div></td>
        <td><span class="nl-tag">${e(b.rollenname)}</span>${(b.zusatzrechte ?? []).map(r => `<span class="nl-chip lila ms-1" title="Zusatzrecht">+ ${e(grundlagen.zusatzrechte.find(x => x.id === r)?.name ?? r)}</span>`).join('')}</td>
        <td>${z ? `<div class="bv-zuordnung"><i class="bi bi-${{ lehrkraft: 'person-workspace', schueler: 'mortarboard', kinder: 'people', person: 'person-badge', klasse: 'easel' }[z.art]}" aria-hidden="true"></i><span><span class="bv-z-text">${e(z.text)}</span><span class="nl-unter d-block">${e(z.unter)}</span></span></div>`
          : b.rolle === 'admin' ? '<span class="nl-unter">–</span>' : '<span class="nl-chip gelb">ohne Zuordnung</span>'}</td>
        <td>${b.gesperrt ? '<span class="nl-chip rot"><i class="bi bi-lock"></i>gesperrt</span>' : '<span class="nl-chip gruen">aktiv</span>'}${b.passwortAendern ? ' <span class="nl-chip gelb" title="Das Startpasswort muss bei der nächsten Anmeldung geändert werden">Startpasswort</span>' : ''}</td>
        <td class="text-nowrap" title="${e(kurzZeit(b.letzteAnmeldung))}">${e(seit(b.letzteAnmeldung))}</td>
        <td class="text-end nl-aktionen"><button class="nl-icon" data-oeffnen="${b.id}" title="Details" aria-label="Details zu ${e(b.login)}">${icon('stift')}</button></td></tr>`;
    }).join('') : `<tr><td colspan="8">${leer('Keine Benutzer gefunden.', 'people')}</td></tr>`;
    ziel.querySelector('[data-anzahl]').textContent = `${l.length} von ${liste.length} Benutzern`;
    ziel.querySelector('[data-auswahlinfo]').textContent = gewaehlt.size ? `${gewaehlt.size} ausgewählt` : '';
    ziel.querySelector('.bv-fuss').classList.toggle('bv-hat-auswahl', gewaehlt.size > 0);
    const alle = ziel.querySelector('[data-alle]');
    alle.checked = l.length > 0 && l.every(b => gewaehlt.has(b.id));
    alle.indeterminate = !alle.checked && l.some(b => gewaehlt.has(b.id));
    ziel.querySelectorAll('[data-sort]').forEach(th => { th.classList.toggle('auf', th.dataset.sort === zustand.sortierung && zustand.richtung > 0); th.classList.toggle('ab', th.dataset.sort === zustand.sortierung && zustand.richtung < 0); });
    ziel.querySelectorAll('[data-kz]').forEach(k => k.classList.toggle('aktiv', k.dataset.kz === zustand.rolle && (k.dataset.kz !== '' || (!zustand.status && !zustand.zuordnung))));
    ziel.querySelector('[data-kz-status]').classList.toggle('aktiv', zustand.status === 'gesperrt');
    ziel.querySelector('[data-kz-zuordnung]').classList.toggle('aktiv', zustand.zuordnung === 'ohne');
    ziel.querySelectorAll('select[data-sf]').forEach(s => sf?.setzen(s.dataset.sf, s.value));
  };
  // gemeinsames Suchfeld mit Filter: x am Filter-Chip, Rücktaste, Tastatur (die Werte laufen über die data-f-Auswahlen)
  const sf = suchfeldMitFilterBinden(ziel);
  const filterSetzen = (feld, wert) => { zustand[feld] = wert; const s = ziel.querySelector(`[data-f="${feld}"]`); if (s) s.value = wert; zeichnen(); };

  ziel.querySelector('[data-suche]').addEventListener('input', ev => { zustand.suche = ev.target.value; zeichnen(); });
  ziel.querySelectorAll('[data-f]').forEach(s => s.addEventListener('change', () => { zustand[s.dataset.f] = s.value; zeichnen(); }));
  ziel.querySelectorAll('[data-kz]').forEach(k => k.onclick = () => { zustand.status = ''; zustand.zuordnung = ''; ['status', 'zuordnung'].forEach(f => { ziel.querySelector(`[data-f="${f}"]`).value = ''; }); filterSetzen('rolle', k.dataset.kz); });
  ziel.querySelector('[data-kz-status]').onclick = () => filterSetzen('status', zustand.status === 'gesperrt' ? '' : 'gesperrt');
  ziel.querySelector('[data-kz-zuordnung]').onclick = () => filterSetzen('zuordnung', zustand.zuordnung === 'ohne' ? '' : 'ohne');
  ziel.querySelectorAll('[data-sort]').forEach(th => {
    th.tabIndex = 0;
    th.setAttribute('role', 'button');
    const sortieren = () => { zustand.richtung = zustand.sortierung === th.dataset.sort ? -zustand.richtung : th.dataset.sort === 'anmeldung' ? -1 : 1; zustand.sortierung = th.dataset.sort; zeichnen(); };
    th.onclick = sortieren;
    th.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); sortieren(); } };
  });
  ziel.querySelector('[data-alle]').onchange = ev => { sichtbar().forEach(b => (ev.target.checked ? gewaehlt.add(b.id) : gewaehlt.delete(b.id))); zeichnen(); };
  tbody.addEventListener('change', ev => { const c = ev.target.closest('[data-wahl]'); if (!c) return; const id = Number(c.dataset.wahl); if (c.checked) gewaehlt.add(id); else gewaehlt.delete(id); zeichnen(); });
  tbody.addEventListener('click', ev => {
    if (ev.target.closest('input, a, .nl-wahl')) return;
    const tr = ev.target.closest('tr[data-id]');
    if (tr) panelOeffnen(liste.find(b => b.id === Number(tr.dataset.id)));
  });
  tbody.addEventListener('keydown', ev => { if (ev.key === 'Enter' && ev.target.matches('tr[data-id]')) panelOeffnen(liste.find(b => b.id === Number(ev.target.dataset.id))); });

  // Sammelaktionen
  const sammel = async (aktion, rolle) => {
    const ids = [...gewaehlt];
    if (aktion !== 'inaktive_sperren' && !ids.length) return toast('Bitte zuerst Benutzer auswählen.', true);
    const texte = {
      loeschen: [`${ids.length} Benutzer löschen?`, 'Löschen'], inaktive_sperren: ['Alle Konten sperren, deren Lehrer- bzw. Klassenkürzel oder Person nicht mehr in den Stammdaten steht?', 'Sperren'],
      passwort_zuruecksetzen: [`Für ${ids.length} Benutzer ein neues Startpasswort erzeugen? Die bisherigen Passwörter werden ungültig.`, 'Zurücksetzen'],
      rolle: [`${ids.length} Benutzer erhalten die Rolle „${ROLLEN[rolle]}“. Fortfahren?`, 'Rolle setzen'],
    }[aktion];
    if (texte && !await bestaetigen('Sammelaktion', texte[0], texte[1])) return;
    try {
      const r = await senden('/api/app/admin/benutzer/sammelaktion', { aktion, ids, rolle });
      toast(`${r.anzahl} Benutzer geändert.`);
      if (r.konten) zugangsdaten('Neue Startpasswörter', r.konten);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelectorAll('[data-aktion]').forEach(b => b.onclick = () => sammel(b.dataset.aktion));
  const sammelRolle = ziel.querySelector('[data-sammelrolle]');
  sammelRolle.addEventListener('change', () => { const r = sammelRolle.value; sammelRolle.value = ''; if (r) sammel('rolle', r); });

  ziel.querySelector('[data-bericht]').onclick = () => herunterladen(`benutzer-${heuteIso()}.csv`, ['Benutzername;Name;Rolle;Zuordnung;Kürzel;E-Mail;Zusatzrechte;gesperrt;Startpasswort offen;Letzte Anmeldung',
    ...sichtbar().map(b => [b.login, b.anzeigename, b.rollenname, zuordnung(b)?.text ?? '', b.element, b.email, (b.zusatzrechte ?? []).join(', '), b.gesperrt ? 'ja' : 'nein', b.passwortAendern ? 'ja' : 'nein', b.letzteAnmeldung ?? ''].map(csvZelle).join(';'))].join('\r\n'));
  ziel.querySelector('[data-neu]').onclick = () => panelOeffnen(null);

  // ---------------------------------------------------------------- Seitenpanel (Details bzw. Neu)

  async function panelOeffnen(b) {
    document.querySelector('.bv-panel')?.schliessen?.();
    const d = b ? await holen(`/api/app/admin/benutzer/${b.id}`).catch(err => { kontext.fehler(err); return null; }) : null;
    if (b && !d) return;
    const k = d?.benutzer ?? { rolle: 'lehrer', zusatzrechte: [], schuelerIds: [] };
    const schleier = document.createElement('div');
    schleier.className = 'bv-schleier';
    const panel = document.createElement('aside');
    panel.className = 'bv-panel';
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    panel.setAttribute('aria-label', b ? `Benutzer ${k.login}` : 'Benutzer anlegen');
    const klassen = [...new Set(schueler.map(s => s.klasse))];
    const lehrerOptionen = `<option value="">Keine Lehrkraft zugeordnet</option>${lehrer.map(l => {
      const fremd = l.konto && l.konto.id !== k.id;
      return `<option value="${e(l.kurz)}" data-zweit="${e(personName(l) + (fremd ? ` · Konto: ${l.konto.login}` : ''))}" ${fremd ? 'disabled' : ''} ${l.kurz.toLowerCase() === (k.element ?? '').toLowerCase() ? 'selected' : ''}>${e(l.kurz)}</option>`;
    }).join('')}`;
    const schuelerOptionen = (mehrfach) => `${mehrfach ? '' : '<option value="">Keine Schülerin / kein Schüler (Klassenkonto)</option>'}${klassen.map(kl => `<optgroup label="Klasse ${e(kl)}">${schueler.filter(s => s.klasse === kl).map(s => {
      const fremd = !mehrfach && s.konto && s.konto.id !== k.id;
      return `<option value="${s.schluessel}" data-zweit="${e(`Klasse ${s.klasse}${fremd ? ` · Konto: ${s.konto.login}` : ''}`)}" ${fremd ? 'disabled' : ''} ${(k.schuelerIds ?? []).includes(Number(s.schluessel)) ? 'selected' : ''}>${e(`${s.name}, ${s.vorname}`)}</option>`;
    }).join('')}</optgroup>`).join('')}`;
    const personOptionen = art => `<option value="0">Keine Person zugeordnet</option>${andere.filter(p => !art || p.art === art).map(p => {
      const fremd = p.konto && p.konto.id !== k.id;
      return `<option value="${p.schluessel}" data-zweit="${e(`${p.funktion || ARTEN[p.art]}${fremd ? ` · Konto: ${p.konto.login}` : ''}`)}" ${fremd ? 'disabled' : ''} ${Number(p.schluessel) === k.personId ? 'selected' : ''}>${e(personName(p))}</option>`;
    }).join('')}`;
    const rechte = grundlagen.zusatzrechte.map(r => `<label class="bv-recht"><input type="checkbox" class="form-check-input" data-recht="${e(r.id)}" ${(k.zusatzrechte ?? []).includes(r.id) ? 'checked' : ''}>
      <span><b>${e(r.name)}</b><small>${e(r.beschreibung)}</small></span></label>`).join('');
    const historie = d?.anmeldungen?.length ? `<table class="bv-historie"><tbody>${d.anmeldungen.map(a => `<tr><td class="text-nowrap">${e(kurzZeit(a.zeit))}</td>
        <td><span class="nl-chip ${(ERGEBNIS[a.ergebnis] ?? ['grau'])[0]}">${e((ERGEBNIS[a.ergebnis] ?? ['', a.ergebnis])[1])}</span></td><td class="nl-unter">${e(a.adresse)}</td>
        <td class="nl-unter bv-geraet" title="${e(a.geraet)}">${e(geraetKurz(a.geraet))}</td></tr>`).join('')}</tbody></table>` : '<p class="nl-unter mb-0">Noch keine Anmeldungen aufgezeichnet.</p>';
    panel.innerHTML = `
      <header class="bv-kopf">${b ? avatar(k.anzeigename || k.login, !k.gesperrt) : '<span class="nl-avatar">+</span>'}
        <div class="bv-kopf-text"><h2>${e(b ? k.anzeigename || k.login : 'Benutzer anlegen')}</h2>
          <div class="nl-unter">${b ? `${e(k.login)} · ${e(k.rollenname)}` : 'Neues Konto mit Rolle und Personenzuordnung'}</div>
          ${b ? `<div class="nl-chips mt-1">${k.gesperrt ? '<span class="nl-chip rot"><i class="bi bi-lock"></i>gesperrt</span>' : '<span class="nl-chip gruen">aktiv</span>'}${k.passwortAendern ? '<span class="nl-chip gelb">Startpasswort offen</span>' : ''}</div>` : ''}</div>
        <button type="button" class="btn-close" data-zu aria-label="Schließen"></button></header>
      <form class="bv-inhalt" novalidate>
        <section class="bv-abschnitt"><h3>Stammdaten</h3>
          <div class="row g-3">
            <div class="col-sm-6"><label class="form-label" for="bv-login">Benutzername</label><input class="form-control" id="bv-login" name="login" value="${e(k.login ?? '')}" ${b ? 'disabled' : 'required'} autocomplete="off"></div>
            <div class="col-sm-6"><label class="form-label" for="bv-name">Anzeigename</label><input class="form-control" id="bv-name" name="anzeigename" value="${e(k.anzeigename ?? '')}" autocomplete="off"></div>
            <div class="col-12"><label class="form-label" for="bv-mail">E-Mail <span class="text-body-secondary fw-normal">(optional)</span></label><input class="form-control" id="bv-mail" name="email" type="email" value="${e(k.email ?? '')}"></div>
            ${b ? '' : '<div class="col-12"><label class="form-label" for="bv-pw">Passwort</label><input class="form-control" id="bv-pw" name="passwort" type="password" minlength="8" autocomplete="new-password"><div class="form-text">Leer lassen, um ein Startpasswort zu erzeugen (muss bei der ersten Anmeldung geändert werden).</div></div>'}
          </div></section>
        <section class="bv-abschnitt"><h3>Rolle & Rechte</h3>
          <label class="form-label" for="bv-rolle">Rolle</label>
          <select class="form-select" id="bv-rolle" name="rolle" ${b && k.id === ich.id ? 'disabled' : ''}>${optionen(ROLLEN, k.rolle)}</select>
          ${b && k.id === ich.id ? '<div class="form-text">Die eigene Rolle kann nicht geändert werden.</div>' : ''}
          <div class="bv-rechte" data-rechtebereich><div class="form-label mt-3 mb-1">Zusatzrechte</div>${rechte}</div></section>
        <section class="bv-abschnitt"><h3>Personenzuordnung</h3>
          <div data-z="kollegium"><label class="form-label" for="bv-lehrer">Lehrkraft (Kürzel aus den Stammdaten)</label>
            <select class="form-select" id="bv-lehrer" data-suche="Kürzel oder Name suchen">${lehrerOptionen}</select>
            <div class="form-text">Ein Kürzel gehört genau zu einem Konto des Kollegiums (eigener Plan, Klassenbuch, Meldungen).</div>
            <label class="form-label mt-3" for="bv-personal">Person (sonstiges Personal)</label><select class="form-select" id="bv-personal" data-suche="Person suchen">${personOptionen('personal')}</select></div>
          <div data-z="schueler"><label class="form-label" for="bv-schueler">Schüler*in</label>
            <select class="form-select" id="bv-schueler" data-suche="Name oder Klasse suchen">${schuelerOptionen(false)}</select>
            <label class="form-label mt-3" for="bv-klasse">Klasse (nur für Klassenkonten ohne Schülerzuordnung)</label>
            <select class="form-select" id="bv-klasse"><option value="">–</option>${el.klassen.map(x => `<option value="${e(x.kurz)}" ${x.kurz === k.element && !(k.schuelerIds ?? []).length ? 'selected' : ''} data-zweit="${e(x.name && x.name !== x.kurz ? x.name : '')}">${e(x.kurz)}</option>`).join('')}</select>
            <div class="form-text">Ein Schülerkonto gehört zu genau einer Person (Abwesenheiten, Fehlzeiten, Hausaufgaben, Klassenplan).</div></div>
          <div data-z="eltern"><label class="form-label" for="bv-kinder">Kinder</label>
            <select class="form-select" id="bv-kinder" multiple data-platzhalter="Kinder zuordnen …" data-suche="Name oder Klasse suchen">${schuelerOptionen(true)}</select>
            <label class="form-label mt-3" for="bv-eltern">Person (Erziehungsberechtigte*r)</label><select class="form-select" id="bv-eltern" data-suche="Person suchen">${personOptionen('eltern')}</select>
            <div class="form-text">Konten von Erziehungsberechtigten können mehreren Kindern zugeordnet sein.</div></div>
          <div data-z="admin" class="nl-unter">Konten der Administration benötigen keine Personenzuordnung; optional kann ein Kürzel zugeordnet werden.</div>
        </section>
        ${b ? `<section class="bv-abschnitt"><h3>Status</h3>
          <div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="bv-gesperrt" name="gesperrt" ${k.gesperrt ? 'checked' : ''} ${k.id === ich.id ? 'disabled' : ''}>
            <label class="form-check-label" for="bv-gesperrt">Konto gesperrt (keine Anmeldung möglich)</label></div>
          <dl class="bv-fakten"><dt>Erstellt</dt><dd>${e(kurzZeit(k.erstellt) || k.erstellt || '–')}</dd><dt>Letzte Anmeldung</dt><dd>${k.letzteAnmeldung ? e(kurzZeit(k.letzteAnmeldung)) : 'nie'}</dd>
            <dt>Passwort</dt><dd>${k.passwortAendern ? 'Startpasswort – muss bei der nächsten Anmeldung geändert werden' : 'selbst gewählt'}</dd></dl></section>
        <section class="bv-abschnitt"><h3>Anmeldehistorie</h3>${historie}</section>
        <section class="bv-abschnitt"><h3>Konto-Aktionen</h3><div class="d-flex flex-wrap gap-2">
          <button type="button" class="nl-knopf" data-k="passwort">${icon('schluessel')}Passwort zurücksetzen</button>
          <button type="button" class="nl-knopf" data-k="erzwingen" ${k.passwortAendern ? 'disabled' : ''}>Passwortänderung erzwingen</button>
          ${k.id !== ich.id ? `<button type="button" class="nl-knopf rot" data-k="loeschen">${icon('loeschen')}Konto löschen</button>` : ''}</div></section>` : ''}
      </form>
      <footer class="bv-fuss-panel"><button type="button" class="btn btn-outline-secondary" data-zu>Abbrechen</button><button type="button" class="btn btn-primary" data-speichern>${b ? 'Speichern' : 'Anlegen'}</button></footer>`;
    document.body.append(schleier, panel);
    document.body.classList.add('bv-offen');
    const vorher = document.activeElement;
    panel.schliessen = () => { panel.remove(); schleier.remove(); document.body.classList.remove('bv-offen'); document.removeEventListener('keydown', taste); vorher?.focus?.(); };
    const taste = ev => { if (ev.key === 'Escape' && !document.querySelector('.modal.show')) panel.schliessen(); };
    document.addEventListener('keydown', taste);
    schleier.onclick = panel.schliessen;
    panel.querySelectorAll('[data-zu]').forEach(x => x.onclick = panel.schliessen);
    requestAnimationFrame(() => panel.classList.add('offen'));
    (b ? panel.querySelector('#bv-name') : panel.querySelector('#bv-login')).focus({ preventScroll: true });

    const rolle = panel.querySelector('#bv-rolle');
    const bereiche = () => {
      const r = rolle.value;
      panel.querySelector('[data-z="kollegium"]').hidden = !KOLLEGIUM.includes(r);
      panel.querySelector('[data-z="schueler"]').hidden = r !== 'schueler';
      panel.querySelector('[data-z="eltern"]').hidden = r !== 'eltern';
      panel.querySelector('[data-z="admin"]').hidden = r !== 'admin';
      panel.querySelector('[data-rechtebereich]').hidden = !KOLLEGIUM.includes(r) || r === 'admin';
    };
    rolle.addEventListener('change', bereiche);
    bereiche();

    const werte = () => {
      const r = rolle.value;
      const v = Object.fromEntries(['login', 'anzeigename', 'email', 'passwort'].map(n => [n, panel.querySelector(`[name=${n}]`)?.value ?? '']));
      const element = KOLLEGIUM.includes(r) ? panel.querySelector('#bv-lehrer').value : r === 'schueler' ? panel.querySelector('#bv-klasse').value : '';
      const schuelerIds = r === 'schueler' ? [panel.querySelector('#bv-schueler').value].filter(Boolean).map(Number)
        : r === 'eltern' ? [...panel.querySelector('#bv-kinder').selectedOptions].map(o => Number(o.value)) : [];
      const personId = KOLLEGIUM.includes(r) ? Number(panel.querySelector('#bv-personal').value) : r === 'eltern' ? Number(panel.querySelector('#bv-eltern').value) : 0;
      const zusatzrechte = KOLLEGIUM.includes(r) && r !== 'admin' ? [...panel.querySelectorAll('[data-recht]:checked')].map(c => c.dataset.recht) : [];
      return { ...v, rolle: r, element, schuelerIds, personId, zusatzrechte, gesperrt: panel.querySelector('#bv-gesperrt')?.checked ?? false };
    };
    panel.querySelector('[data-speichern]').onclick = async ev => {
      const knopf = ev.currentTarget;
      knopf.disabled = true;
      try {
        if (b) {
          await senden(`/api/app/admin/benutzer/${b.id}`, werte(), 'PUT');
          toast('Gespeichert.');
          panel.schliessen();
          neu();
        } else {
          const w = werte();
          if (!w.login.trim()) throw new Error('Bitte einen Benutzernamen eingeben.');
          const r = await senden('/api/app/admin/benutzer', w);
          toast('Benutzer angelegt.');
          panel.schliessen();
          neu();
          if (r.passwort) zugangsdaten('Startpasswort', { anzahl: 1, konten: [{ login: r.benutzer.login, passwort: r.passwort, rollenname: r.benutzer.rollenname, rolle: r.benutzer.rolle, name: r.benutzer.anzeigename, zuordnung: r.benutzer.element, klasse: (r.benutzer.klassen ?? []).join(', ') }],
            csv: `Benutzername;Startpasswort\r\n${r.benutzer.login};${r.passwort}\r\n` });
        }
      } catch (err) { kontext.fehler(err); }
      finally { knopf.disabled = false; }
    };
    panel.querySelectorAll('[data-k]').forEach(x => x.onclick = async () => {
      try {
        if (x.dataset.k === 'passwort') {
          if (!await bestaetigen('Passwort zurücksetzen', `Für ${k.login} ein neues Startpasswort erzeugen?`, 'Zurücksetzen')) return;
          const r = await senden(`/api/app/admin/benutzer/${k.id}/passwort`, {});
          zugangsdaten('Neues Startpasswort', { anzahl: 1, konten: [{ login: k.login, passwort: r.passwort, rollenname: k.rollenname, rolle: k.rolle, name: k.anzeigename, zuordnung: k.element, klasse: (k.klassen ?? []).join(', ') }],
            csv: `Benutzername;Startpasswort\r\n${k.login};${r.passwort}\r\n` });
        } else if (x.dataset.k === 'erzwingen') {
          await senden('/api/app/admin/benutzer/sammelaktion', { aktion: 'passwort_aendern', ids: [k.id] });
          toast('Das Passwort muss bei der nächsten Anmeldung geändert werden.');
        } else if (x.dataset.k === 'loeschen') {
          if (!await bestaetigen('Benutzer löschen', `${k.login} wirklich löschen?`)) return;
          await api(`/api/app/admin/benutzer/${k.id}`, { methode: 'DELETE' });
          toast('Benutzer gelöscht.');
        }
        panel.schliessen();
        neu();
      } catch (err) { kontext.fehler(err); }
    });
  }

  zeichnen();
  if (idParam) { const b = liste.find(x => x.id === Number(idParam)); if (b) panelOeffnen(b); }
}

/** Browser und System aus dem User-Agent (kurz). */
function geraetKurz(ua) {
  if (!ua) return '';
  const browser = /Edg\//.test(ua) ? 'Edge' : /Firefox\//.test(ua) ? 'Firefox' : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : ua.split(/[ /]/)[0];
  const system = /iPhone|iPad/.test(ua) ? 'iOS' : /Android/.test(ua) ? 'Android' : /Windows/.test(ua) ? 'Windows' : /Mac OS X/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : '';
  return [browser, system].filter(Boolean).join(' · ');
}

// ================================================================== Personen & Konten

export async function personen(ziel, kontext, neu, [artParam]) {
  const art = ARTEN[artParam] ? artParam : 'lehrer';
  const [alle, el] = await Promise.all([holen('/api/app/admin/personen'), kontext.elemente()]);
  const liste = alle.filter(p => p.art === art);
  const schueler = alle.filter(p => p.art === 'schueler');
  const schuelerNach = Object.fromEntries(schueler.map(s => [Number(s.schluessel), s]));
  const zustand = { suche: '', klasse: '', konto: '' };
  kontext.titel('Personen & Konten', '', `${liste.length}`);
  const ohne = l => l.filter(p => !p.konto).length;
  const verwaltet = art === 'eltern' || art === 'personal';
  ziel.innerHTML = `
    <nav class="u-reiter mb-3">${Object.entries(ARTEN).map(([a, t]) => { const l = alle.filter(p => p.art === a); return `<a href="#/admin/personen/${a}" class="${a === art ? 'aktiv' : ''}">${t} <span class="bv-reiterzahl">${l.length}</span></a>`; }).join('')}</nav>
    <div class="nl-leiste">
      ${suchfeldMitFilter({ platzhalter: `Name${art === 'lehrer' ? ', Kürzel' : ''} oder Benutzername`, label: 'Personen suchen', filter: [
        ...(art === 'schueler' ? [{ name: 'klasse', attr: 'data-f="klasse"', label: 'Klasse', alle: 'Alle Klassen', optionen: [...new Set(schueler.map(s => s.klasse))].map(k => [k, k]) }] : []),
        { name: 'konto', attr: 'data-f="konto"', label: 'Konto', alle: 'Mit und ohne Konto', optionen: [['fehlt', 'Ohne Konto'], ['vorhanden', 'Mit Konto']] },
      ] })}
      <span class="ms-auto d-flex gap-2 flex-wrap">
        <a class="nl-knopf" href="#/admin/benutzer">${icon('benutzer')}Benutzer</a>
        ${verwaltet ? `<button type="button" class="nl-knopf" data-person-neu>${icon('plus')}${art === 'eltern' ? 'Erziehungsberechtigte*n' : 'Person'} erfassen</button>` : ''}
        <button type="button" class="nl-hinzu" data-sammel ${ohne(liste) ? '' : 'disabled'}><i class="bi bi-person-plus"></i>Konten anlegen …</button>
      </span>
    </div>
    ${art === 'eltern' || art === 'personal' ? `<p class="nl-hinweis"><i class="bi bi-info-circle"></i>${art === 'eltern' ? 'Erziehungsberechtigte werden in UntPlanWeb erfasst und mit ihren Kindern verknüpft; das Elternkonto erhält die Kinder automatisch.' : 'Sonstiges Personal (z. B. Sekretariat, Hausmeister, Schulsozialarbeit) wird in UntPlanWeb erfasst. Konten erhalten standardmäßig die Rolle Lehrkraft ohne Kürzel (Pläne und Vertretungsplan des Kollegiums).'}</p>` : ''}
    <div class="nl-zahlen">
      <button type="button" class="nl-zahl" data-kz=""><b>${liste.length}</b> ${ARTEN[art]}</button>
      <button type="button" class="nl-zahl" data-kz="vorhanden"><b>${liste.length - ohne(liste)}</b> mit Konto</button>
      <button type="button" class="nl-zahl gelb" data-kz="fehlt"><b>${ohne(liste)}</b> ohne Konto</button>
    </div>
    <div class="nl-tabelle"><table><thead><tr><th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle sichtbaren auswählen"></th>
      <th colspan="2">Name</th><th>${art === 'schueler' ? 'Klasse' : art === 'lehrer' ? 'Kürzel' : art === 'eltern' ? 'Kinder' : 'Funktion'}</th><th>${art === 'schueler' ? 'Elternkonten' : 'E-Mail'}</th><th>Konto</th><th></th></tr></thead>
      <tbody data-zeilen></tbody></table>
      <div class="nl-fuss"><span class="nl-unter me-2" data-anzahl></span><span class="bv-auswahl" data-auswahlinfo></span>
        <button class="nl-knopf" data-sammel-auswahl>${icon('plus')}Konten für Auswahl anlegen</button></div></div>`;

  const tbody = ziel.querySelector('[data-zeilen]');
  const gewaehlt = new Set();
  const sichtbar = () => {
    const f = zustand.suche.trim().toLowerCase();
    return liste.filter(p => (!zustand.klasse || p.klasse === zustand.klasse) && (!zustand.konto || (zustand.konto === 'fehlt') === !p.konto)
      && (!f || `${p.vorname} ${p.name} ${p.kurz} ${p.klasse} ${p.email} ${p.funktion} ${p.konto?.login ?? ''}`.toLowerCase().includes(f)));
  };
  const spalte3 = p => art === 'schueler' ? `<span class="nl-tag">${e(p.klasse)}</span>` : art === 'lehrer' ? `<b>${e(p.kurz)}</b>`
    : art === 'eltern' ? (p.kinder.length ? p.kinder.map(i => schuelerNach[i] ? `${e(personName(schuelerNach[i]))} <span class="nl-unter">${e(schuelerNach[i].klasse)}</span>` : `#${i}`).join('<br>') : '<span class="nl-unter">–</span>')
      : e(p.funktion);
  const spalte4 = p => art === 'schueler' ? (p.elternkonten.length ? p.elternkonten.map(k => `<a class="nl-chip grau" href="#/admin/benutzer/-/${k.id}">${e(k.login)}</a>`).join(' ') : '<span class="nl-unter">–</span>') : e(p.email);
  const zeichnen = () => {
    const l = sichtbar();
    tbody.innerHTML = l.length ? l.map(p => `<tr data-s="${e(p.schluessel)}" class="${gewaehlt.has(p.schluessel) ? 'nl-gewaehlt' : ''}">
        <td class="nl-wahl">${p.konto ? '' : `<input type="checkbox" class="nl-rund" data-wahl="${e(p.schluessel)}" ${gewaehlt.has(p.schluessel) ? 'checked' : ''} aria-label="${e(personName(p))} auswählen">`}</td>
        <td class="nl-avatar-zelle">${avatar(personName(p) || p.kurz, !!p.konto)}</td>
        <td><div class="nl-name">${e(art === 'schueler' ? `${p.name}, ${p.vorname}` : personName(p) || p.kurz)}</div>${art === 'lehrer' && p.email ? '' : ''}</td>
        <td>${spalte3(p)}</td><td>${spalte4(p)}</td>
        <td>${p.konto ? `<a class="bv-konto" href="#/admin/benutzer/-/${p.konto.id}"><span class="nl-chip ${p.konto.gesperrt ? 'rot' : 'gruen'}"><i class="bi bi-${p.konto.gesperrt ? 'lock' : 'check-lg'}"></i>${e(p.konto.login)}</span></a>
            <span class="nl-unter d-block">${p.konto.letzteAnmeldung ? `zuletzt ${e(seit(p.konto.letzteAnmeldung))}` : 'noch nie angemeldet'}</span>`
          : `<span class="nl-chip grau">fehlt</span> <button type="button" class="nl-knopf bv-klein" data-einzeln="${e(p.schluessel)}">Konto anlegen</button>`}</td>
        <td class="text-end nl-aktionen">${verwaltet ? `<button class="nl-icon" data-bearbeiten="${e(p.schluessel)}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>
          <button class="nl-icon rot" data-loeschen="${e(p.schluessel)}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button>` : ''}</td></tr>`).join('')
      : `<tr><td colspan="7">${leer(liste.length ? 'Keine Personen gefunden.' : art === 'eltern' ? 'Noch keine Erziehungsberechtigten erfasst.' : art === 'personal' ? 'Noch kein sonstiges Personal erfasst.' : 'Keine Personen – bitte zuerst Daten aus UntPlan übertragen.', 'people')}</td></tr>`;
    ziel.querySelector('[data-anzahl]').textContent = `${l.length} von ${liste.length}`;
    ziel.querySelector('[data-auswahlinfo]').textContent = gewaehlt.size ? `${gewaehlt.size} ausgewählt` : '';
    const ohneKonto = l.filter(p => !p.konto);
    const alleBox = ziel.querySelector('[data-alle]');
    alleBox.checked = ohneKonto.length > 0 && ohneKonto.every(p => gewaehlt.has(p.schluessel));
    ziel.querySelectorAll('[data-kz]').forEach(k => k.classList.toggle('aktiv', k.dataset.kz === zustand.konto));
    ziel.querySelectorAll('select[data-sf]').forEach(s => sfp?.setzen(s.dataset.sf, s.value));
  };
  ziel.querySelector('[data-suche]').addEventListener('input', ev => { zustand.suche = ev.target.value; zeichnen(); });
  ziel.querySelectorAll('[data-f]').forEach(s => s.addEventListener('change', () => { zustand[s.dataset.f] = s.value; zeichnen(); }));
  const sfp = suchfeldMitFilterBinden(ziel);
  ziel.querySelectorAll('[data-kz]').forEach(k => k.onclick = () => { zustand.konto = k.dataset.kz; ziel.querySelector('[data-f="konto"]').value = zustand.konto; zeichnen(); });
  ziel.querySelector('[data-alle]').onchange = ev => { sichtbar().filter(p => !p.konto).forEach(p => (ev.target.checked ? gewaehlt.add(p.schluessel) : gewaehlt.delete(p.schluessel))); zeichnen(); };
  tbody.addEventListener('change', ev => { const c = ev.target.closest('[data-wahl]'); if (!c) return; if (c.checked) gewaehlt.add(c.dataset.wahl); else gewaehlt.delete(c.dataset.wahl); zeichnen(); });
  tbody.addEventListener('click', async ev => {
    const einzeln = ev.target.closest('[data-einzeln]');
    if (einzeln) return kontenDialog([einzeln.dataset.einzeln]);
    const bearb = ev.target.closest('[data-bearbeiten]');
    if (bearb) return personDialog(liste.find(p => p.schluessel === bearb.dataset.bearbeiten));
    const weg = ev.target.closest('[data-loeschen]');
    if (weg) {
      const p = liste.find(x => x.schluessel === weg.dataset.loeschen);
      if (!await bestaetigen('Person löschen', `${personName(p)} aus der Personenliste löschen?${p.konto ? ` Das Konto „${p.konto.login}“ bleibt erhalten, verliert aber die Zuordnung.` : ''}`)) return;
      try { await api(`/api/app/admin/personen/${p.schluessel}`, { methode: 'DELETE' }); toast('Gelöscht.'); neu(); } catch (err) { kontext.fehler(err); }
    }
  });
  ziel.querySelector('[data-sammel]').onclick = () => kontenDialog(null);
  ziel.querySelector('[data-sammel-auswahl]').onclick = () => (gewaehlt.size ? kontenDialog([...gewaehlt]) : toast('Bitte zuerst Personen ohne Konto auswählen.', true));
  ziel.querySelector('[data-person-neu]')?.addEventListener('click', () => personDialog(null));

  /** Konten anlegen: einzelne, ausgewählte oder alle Personen ohne Konto (bei Schüler*innen optional einer Klasse). */
  function kontenDialog(auswahl) {
    const beispiel = liste.find(p => !p.konto && (!auswahl || auswahl.includes(p.schluessel))) ?? liste[0];
    const schemata = art === 'lehrer' ? SCHEMATA : Object.fromEntries(Object.entries(SCHEMATA).filter(([k]) => k !== 'kuerzel'));
    const klassen = [...new Set(schueler.filter(s => !s.konto).map(s => s.klasse))];
    const rollen = art === 'lehrer' ? { '': 'Automatisch (Zuordnungsgruppen: Schulleitung, sonst Lehrkraft)', lehrer: 'Lehrkraft', leitung: 'Schulleitung', admin: 'Administration' }
      : art === 'personal' ? { lehrer: 'Lehrkraft ohne Kürzel (Kollegium)', leitung: 'Schulleitung', admin: 'Administration' } : null;
    dialog({
      titel: auswahl?.length === 1 ? `Konto anlegen – ${personName(liste.find(p => p.schluessel === auswahl[0]))}` : `Konten für ${ARTEN[art]} anlegen`,
      inhalt: `<div class="row g-3">
        ${auswahl ? `<div class="col-12"><div class="nl-hinweis mb-0"><i class="bi bi-people"></i>${auswahl.length} ${auswahl.length === 1 ? 'Person' : 'Personen'} ausgewählt.</div></div>`
          : `<div class="col-12"><label class="form-label" for="kd-umfang">Umfang</label><select class="form-select" id="kd-umfang" name="umfang">
              <option value="">Alle ${ARTEN[art]} ohne Konto (${ohne(liste)})</option>
              ${art === 'schueler' ? klassen.map(k => `<option value="${e(k)}" data-zweit="${schueler.filter(s => s.klasse === k && !s.konto).length} ohne Konto">Nur Klasse ${e(k)}</option>`).join('') : ''}</select></div>`}
        <div class="col-sm-6"><label class="form-label" for="kd-schema">Login-Schema</label><select class="form-select" id="kd-schema" name="schema">${optionen(schemata, art === 'lehrer' ? 'kuerzel' : 'vorname.nachname')}</select></div>
        <div class="col-sm-6"><label class="form-label">Beispiel</label><div class="form-control bg-body-tertiary font-monospace" data-beispiel></div></div>
        ${rollen ? `<div class="col-12"><label class="form-label" for="kd-rolle">Rolle</label><select class="form-select" id="kd-rolle" name="rolle">${optionen(rollen, art === 'personal' ? 'lehrer' : '')}</select></div>` : ''}
        <div class="col-12 form-text mt-2">Jedes Konto erhält ein zufälliges Startpasswort, das bei der ersten Anmeldung geändert werden muss. Gibt es den Benutzernamen schon, wird eine Zahl angehängt.
          ${art === 'schueler' ? 'Schülerkonten werden der Person zugeordnet (eine Person – ein Konto).' : art === 'eltern' ? 'Elternkonten erhalten die erfassten Kinder.' : ''}</div></div>`,
      beimOeffnen: box => {
        const schema = box.querySelector('[name=schema]');
        const zeigen = () => { box.querySelector('[data-beispiel]').textContent = beispiel ? loginBeispiel(schema.value, beispiel) : '–'; };
        schema.addEventListener('change', zeigen);
        zeigen();
      },
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Konten anlegen', art: 'haupt', aktion: async box => {
        const r = await senden('/api/app/admin/personen/konten', {
          art, schluessel: auswahl ?? [], klasse: box.querySelector('[name=umfang]')?.value || null,
          schema: box.querySelector('[name=schema]').value, rolle: box.querySelector('[name=rolle]')?.value || null,
        });
        toast(`${r.anzahl} ${r.anzahl === 1 ? 'Konto' : 'Konten'} angelegt.`);
        neu();
        zugangsdaten(`Zugangsdaten ${ARTEN[art]}`, r);
      } }],
    });
  }

  /** Erziehungsberechtigte bzw. sonstiges Personal erfassen und bearbeiten. */
  function personDialog(p) {
    const klassen = [...new Set(schueler.map(s => s.klasse))];
    dialog({
      titel: p ? `${personName(p)} bearbeiten` : art === 'eltern' ? 'Erziehungsberechtigte*n erfassen' : 'Person erfassen',
      inhalt: `<div class="row g-3">
        <div class="col-sm-6"><label class="form-label" for="pd-v">Vorname</label><input class="form-control" id="pd-v" name="vorname" value="${e(p?.vorname ?? '')}"></div>
        <div class="col-sm-6"><label class="form-label" for="pd-n">Nachname</label><input class="form-control" id="pd-n" name="name" value="${e(p?.name ?? '')}" required></div>
        ${art === 'personal' ? `<div class="col-12"><label class="form-label" for="pd-f">Funktion</label><input class="form-control" id="pd-f" name="funktion" value="${e(p?.funktion ?? '')}" placeholder="z. B. Sekretariat"></div>` : ''}
        <div class="col-12"><label class="form-label" for="pd-m">E-Mail <span class="text-body-secondary fw-normal">(optional)</span></label><input class="form-control" id="pd-m" name="email" type="email" value="${e(p?.email ?? '')}"></div>
        ${art === 'eltern' ? `<div class="col-12"><label class="form-label" for="pd-k">Kinder</label><select class="form-select" id="pd-k" multiple data-platzhalter="Kinder zuordnen …" data-suche="Name oder Klasse suchen">
          ${klassen.map(k => `<optgroup label="Klasse ${e(k)}">${schueler.filter(s => s.klasse === k).map(s => `<option value="${s.schluessel}" data-zweit="Klasse ${e(k)}" ${(p?.kinder ?? []).includes(Number(s.schluessel)) ? 'selected' : ''}>${e(`${s.name}, ${s.vorname}`)}</option>`).join('')}</optgroup>`).join('')}</select></div>` : ''}
      </div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const daten = { art, vorname: box.querySelector('[name=vorname]').value, name: box.querySelector('[name=name]').value, funktion: box.querySelector('[name=funktion]')?.value ?? '',
          email: box.querySelector('[name=email]').value, kinder: art === 'eltern' ? [...box.querySelector('#pd-k').selectedOptions].map(o => Number(o.value)) : [] };
        await senden(p ? `/api/app/admin/personen/${p.schluessel}` : '/api/app/admin/personen', daten, p ? 'PUT' : 'POST');
        toast('Gespeichert.');
        neu();
      } }],
    });
  }

  zeichnen();
}

/** Login nach Schema (wie auf dem Server) für die Vorschau. */
function loginBeispiel(schema, p) {
  const n = s => String(s ?? '').trim().toLowerCase().replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
    .normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[\s-]+/g, '-').replace(/[^a-z0-9-]/g, '').replace(/^-+|-+$/g, '');
  const v = n(p.vorname), na = n(p.name), k = n(p.kurz);
  const l = schema === 'kuerzel' && k ? k : schema === 'v.nachname' && v ? `${v[0]}.${na}` : schema === 'nachname.vorname' && v ? `${na}.${v}` : schema === 'nachname' ? na : v ? `${v}.${na}` : na;
  return l || '–';
}
