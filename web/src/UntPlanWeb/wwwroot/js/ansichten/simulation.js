// Administration → Schulbetrieb simulieren: Simulierte Lehrkräfte, Klassenleitungen und Erziehungsberechtigte tragen im Hintergrund ein
// (Lehrstoff, Abwesenheiten, Verspätungen, Entschuldigungen, Krankmeldungen an die Verteilerliste, Prüfungen, Sprechtagsbuchungen).
// Gliederung: Status und Ein/Aus, dann Bereiche, Personen, Prüfungen, Räume, Krankmeldungen und Intensität (einklappbar); Speichern fest unten;
// getrennt darunter die Wartung (Nachpflegen, Nachbessern mit Fortschritt und Abbrechen, Entfernen). Mehrfachauswahlen als kompakte Dropdowns.
// Ausgenommene Lehrkräfte (Standard: Schulleitung) bleiben unberührt. Aufgerufen aus admin.js.
import { e, holen, senden, toast, bestaetigen } from '../ui.js';
import { auswahl } from '../auswahl.js';

const zeit = s => s ? new Date(s.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '';
const tag = s => s ? new Date(`${s}T12:00`).toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' }) : '';

/** Anzeige eines Intensitätsreglers: Prozent, beim Standardwert mit Hinweis. */
const reglertext = (v, std, art) => `${v} %${art === 'faktor' && v === 0 ? ' (aus)' : ''}${v === std ? ' (Standard)' : ''}`;

const BEREICHE = [
  ['klassenbuch', 'Klassenbuch', 'Lehrstoff, Hausaufgaben und Klassenbucheinträge der gehaltenen Stunden'],
  ['abwesenheiten', 'Abwesenheiten', 'Abwesenheitskontrolle mit kranken Schüler*innen, Arztterminen und Verspätungen (nur 1. bis 3. Stunde)'],
  ['entschuldigungen', 'Entschuldigungen', 'Klassenleitungen entschuldigen unregelmäßig mit Begründung und Nachweis'],
  ['krankmeldungen', 'Krankmeldungen', 'Lehrkräfte melden sich an der Verteilerliste krank, jeden Schultag mindestens eine Meldung'],
  ['pruefungen', 'Prüfungen', 'Eine Klassenarbeit bzw. Klausur je Fach und Halbjahr, einige Wochen im Voraus. Sie werden wie echte Prüfungen an UntPlan übergeben.'],
  ['sprechtag', 'Sprechtag', 'Einladungen der Lehrkräfte und Buchungen der Erziehungsberechtigten'],
  ['lehreraktionen', 'Lehreraktionen', 'Gelegentliche Raumänderungen (nur freie passende Räume), Lehrstoff schon am Vorabend, Hinweise an die Klasse'],
];

/** Abschnitt mit Überschrift und kurzer Erläuterung. */
const abschnitt = (titel, hinweis, inhalt, attr = '') => `<section class="wt-karte sim-abschnitt" ${attr}><h4>${titel}</h4>${hinweis ? `<p class="ug-hinweis mt-0">${hinweis}</p>` : ''}${inhalt}</section>`;

/** Feld mit Beschriftung und Platz für eine Mehrfachauswahl. */
const feld = (name, titel, hilfe = '') => `<div class="sim-wahlfeld"><label class="sim-wahltitel" id="sim-${name}-titel">${titel}</label><div data-wahl="${name}"></div>${hilfe ? `<small class="sim-wahlhilfe">${hilfe}</small>` : ''}</div>`;

/** Stand eines Hintergrundauftrags: Balken, Tage, aktueller Tag, Zähler, Wartet, Abgebrochen, Fehler. */
function auftragsstand(n, art) {
  if (!n || (!n.laeuft && !n.ende && !n.fehler)) return '';
  const pro = n.tage ? Math.round(n.fertig * 100 / n.tage) : 0;
  const zaehler = art === 'np'
    ? `${n.eintraege} Einträge nachgepflegt${n.uebersprungen ? `, ${n.uebersprungen} übersprungen` : ''}`
    : `${n.geprueft} geprüft, ${n.korrigiert} korrigiert, ${n.entfernt} entfernt`;
  const zeile = n.laeuft
    ? n.wartet ? `Wartet auf: ${e(n.wartet)}. Der Auftrag beginnt, sobald dieser abgeschlossen ist.`
      : `Läuft: ${n.fertig} von ${n.tage} Tagen (${pro} %)${n.tag ? `, aktuell ${e(tag(n.tag))}` : ''}. ${zaehler}.`
    : n.abgebrochen ? `Abgebrochen am ${e(zeit(n.ende))}: ${n.fertig} von ${n.tage} Tagen bearbeitet, ${zaehler}. Bereits bearbeitete Tage bleiben erhalten.`
      : n.fehler ? `Fehler am ${e(zeit(n.ende))} nach ${n.fertig} von ${n.tage} Tagen.`
        : `Abgeschlossen am ${e(zeit(n.ende))}: ${n.tage} Tage, ${zaehler}.`;
  const liste = art === 'nb' && !n.laeuft && (n.zusammenfassung ?? []).length ? `<ul class="sim-liste">${n.zusammenfassung.map(x => `<li>${e(x)}</li>`).join('')}</ul>` : '';
  return `<div class="sim-balken ${n.laeuft ? 'laeuft' : ''}" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${pro}"><span style="width:${n.laeuft || n.abgebrochen ? pro : 100}%"></span></div>
    <small>${zeile}</small>${n.fehler ? `<div class="sim-fehler" role="alert">${e(n.fehler)}</div>` : ''}${liste}`;
}

export async function zeigen(ziel, kontext, neu) {
  const d = await holen('/api/app/admin/simulation');
  const w = d.einstellungen;
  const z = d.zahlen;
  ziel.classList.add('wt-seite');
  ziel.innerHTML = `
    <div class="wt-status ${w.aktiv ? 'sim-an' : 'sim-aus'}">
      <span class="wt-symbol"><i class="bi bi-${w.aktiv ? 'play-circle' : 'pause-circle'}" aria-hidden="true"></i></span>
      <div class="flex-grow-1"><h3>${w.aktiv ? 'Simulation läuft' : 'Simulation ausgeschaltet'}</h3>
        <p>${w.aktiv ? `Seit ${e(zeit(w.start))}. Zuletzt ausgeführt: ${e(zeit(d.stand) || 'noch nicht')}.` : 'Es werden keine Einträge erzeugt. Vorhandene Simulationsdaten bleiben erhalten, bis Sie sie entfernen.'}</p></div>
    </div>
    <div class="wt-raster">
      ${[['Lehrstoff', z.lehrstoff], ['Abwesenheiten', z.abwesenheiten], ['Entschuldigungen', z.entschuldigt], ['Klassenbucheinträge', z.vermerke],
        ['Krankmeldungen', z.krankmeldungen], ['Prüfungen', z.pruefungen], ['Sprechtagstermine', z.sprechtag], ['Raumänderungen', z.raumaenderungen]]
        .map(([t, n]) => `<div class="wt-karte wt-wert mb-0"><small>${t}</small><b>${n}</b></div>`).join('')}
    </div>
    <form class="sim-formular">
      ${abschnitt('Simulation', '', `
        <label class="wt-schalter sim-schalter"><input type="checkbox" name="aktiv" ${w.aktiv ? 'checked' : ''}>Schulbetrieb simulieren</label>
        <p class="ug-hinweis mb-0">Simulierte Lehrkräfte arbeiten wie im echten Schulbetrieb: zu den gehaltenen Stunden, zu realistischen Uhrzeiten und nicht immer vollständig.
          Kranke bzw. abwesende Lehrkräfte tragen nichts ein, bei Vertretungen trägt die Vertretungslehrkraft ein. Absenzen in der Vertretungsplanung tragen Sie selbst ein.
          Alle Einträge stammen von eigenen, gesperrten Konten und lassen sich unten vollständig entfernen.</p>`)}
      ${abschnitt('Bereiche', 'Welche Aufgaben die simulierten Personen übernehmen.',
        `<div class="wt-module">${BEREICHE.map(([n, t, h]) => `<label class="wt-modul sim-modul"><input class="form-check-input mt-1" type="checkbox" name="${n}" ${w[n] ? 'checked' : ''}>
          <span>${t}<small>${h}</small></span></label>`).join('')}</div>`)}
      ${abschnitt('Personen', '', feld('aus', 'Ausgenommene Lehrkräfte',
        'Für diese Lehrkräfte läuft keine Automatik (kein Lehrstoff, keine Krankmeldungen, keine Entschuldigungen als Klassenleitung, keine Prüfungen). Standard: die Konten der Schulleitung und Administration.'))}
      ${abschnitt('Prüfungen', 'Nur in den gewählten Fächern trägt die Simulation Prüfungen ein, je Klasse und Fach eine im Halbjahr.',
        `<div class="sim-wahlraster">${feld('sek1', 'Prüfungsfächer Sekundarstufe I', 'Klassen 5 bis 10')}${feld('sek2', 'Prüfungsfächer Sek II und Berufskolleg', 'Oberstufe (EF, Q1, Q2) und Bildungsgänge des Berufskollegs')}</div>
        <div class="sim-wahlfeld"><span class="sim-wahltitel">Eigener Prüfungsraum</span>
          <label class="sim-anteil"><input type="range" name="klausurraumanteil" min="0" max="100" step="5" value="${Number(w.klausurraumanteil ?? 30)}" aria-label="Anteil eigener Prüfungsräume">
            <output>${Number(w.klausurraumanteil ?? 30)} %</output></label>
          <small class="sim-wahlhilfe">Anteil der Klausuren in der Oberstufe, für die die Lehrkraft einen eigenen freien Raum festlegt (in der Sekundarstufe I ein Drittel davon).</small></div>`)}
      ${abschnitt('Räume', '',
        `<div class="sim-wahlraster">${feld('kr', 'Erlaubte Prüfungsräume', 'Ohne Auswahl wählt die Simulation selbst einen freien Raum derselben Art und Größe.')}
          ${feld('ra', 'Von Buchungen ausgeschlossene Räume', 'Nie bei Raumänderungen oder als Prüfungsraum, z. B. Aula, Lehrerzimmer, Verwaltung.')}</div>`)}
      ${abschnitt('Krankmeldungen', '', `
        <label class="sim-wahltitel" for="sim-verteiler">Verteilerliste im Mitteilungsmodul</label>
        <input id="sim-verteiler" class="form-control" name="verteiler" maxlength="100" value="${e(w.verteiler)}" style="max-width: 320px">
        <small class="sim-wahlhilfe">${d.verteilerVorhanden ? 'Die Liste ist vorhanden. Die Krankmeldungen gehen an ihre Mitglieder.'
          : 'Die Liste gibt es noch nicht. Beim Einschalten wird sie mit der Schulleitung und Administration angelegt.'}</small>`)}
      <details class="wt-karte sim-abschnitt sim-klapp" data-intensitaet>
        <summary><h4>Intensität</h4><span class="sim-klapphinweis">${(d.intensitaetRegler ?? []).filter(r => Number(w.intensitaet?.[r.schluessel] ?? r.standard) !== r.standard).length || w.intensitaet?.grippewellen === false
          ? 'Angepasst' : 'Standardwerte'}</span></summary>
        <p class="ug-hinweis mt-0">Wie häufig und wie vollständig die Simulation einträgt. Faktoren beziehen sich auf den Standard (100 %), Anteile auf alle Stunden.
          Die Markierung zeigt den Standardwert. Änderungen gelten sofort für neue Einträge und für das Nachpflegen; vorhandene Daten bleiben unverändert.</p>
        ${[...new Set((d.intensitaetRegler ?? []).map(r => r.gruppe))].map(g => `<h5 class="sim-unter">${e(g)}</h5>
          ${(d.intensitaetRegler ?? []).filter(r => r.gruppe === g).map(r => {
            const v = Number(w.intensitaet?.[r.schluessel] ?? r.standard);
            return `<label class="sim-regelzeile"><span>${e(r.name)}</span>
              <span class="sim-reglerbahn" style="--std: ${(r.standard / r.max * 100).toFixed(1)}%"><input type="range" data-int="${e(r.schluessel)}" data-std="${r.standard}" data-art="${e(r.art)}"
                min="0" max="${r.max}" step="5" value="${v}" aria-label="${e(r.name)}"></span>
              <output>${reglertext(v, r.standard, r.art)}</output></label>`;
          }).join('')}`).join('')}
        <label class="wt-modul sim-modul mt-2"><input class="form-check-input mt-1" type="checkbox" name="grippewellen" ${w.intensitaet?.grippewellen !== false ? 'checked' : ''}>
          <span>Grippewellen<small>Im Winter fehlen in einzelnen Klassen über rund zwei Wochen deutlich mehr Schüler*innen.</small></span></label>
        <button class="btn btn-outline-secondary btn-sm mt-2" type="button" data-int-standard>Standard wiederherstellen</button>
      </details>
      <div class="sim-leiste">
        <button class="btn btn-primary" type="submit">Speichern</button>
        ${w.aktiv ? '<button class="btn btn-outline-secondary" type="button" data-lauf>Jetzt ausführen</button>' : ''}
        <span class="sim-leistehinweis" data-geaendert hidden>Ungespeicherte Änderungen</span>
      </div>
    </form>
    <h3 class="sim-wartungstitel">Wartung</h3>
    ${abschnitt('Vergangenheit nachpflegen', `Die Lehrkräfte tragen fehlende Abwesenheitskontrollen, Lehrstoff und Klassenbucheinträge nach, die Klassenleitungen entschuldigen,
        wie im Alltag gesammelt einige Tage später. Ausgenommene und abwesende Lehrkräfte tragen nichts ein, Ihre eigenen Einträge bleiben unverändert.
        Mehrfaches Nachpflegen trägt nichts doppelt ein.`, `
      <div class="d-flex flex-wrap align-items-end gap-2">
        <label class="sim-feld"><span>Von</span><input class="form-control" type="date" data-np-von value="${e(d.schuljahrVon)}" min="${e(d.schuljahrVon)}" max="${e(d.gestern)}"></label>
        <label class="sim-feld"><span>Bis</span><input class="form-control" type="date" data-np-bis value="${e(d.gestern)}" min="${e(d.schuljahrVon)}" max="${e(d.gestern)}"></label>
        <button class="btn btn-outline-secondary" type="button" data-nachpflegen>Vergangenheit nachpflegen</button>
        <button class="btn btn-outline-danger" type="button" data-np-abbrechen hidden>Abbrechen</button>
      </div>
      <div class="sim-fortschritt" data-np-stand aria-live="polite"></div>`, 'data-wartung')}
    ${abschnitt('Simulationsdaten nachbessern', `Passt bereits erzeugte Simulationsdaten an die aktuellen Einstellungen und Regeln an: Einträge ausgenommener Lehrkräfte, Raumänderungen
        und Prüfungsräume in ausgeschlossenen bzw. nicht erlaubten Räumen, Prüfungen außerhalb der Prüfungsliste, Hausaufgaben in der Berufsschule, Unterrichtsform,
        Lehrstoff in Vertretungsstunden und zu Prüfungen, getrennte Einzelstunden. Nur simulierte Einträge werden geändert; schon an UntPlan übertragene Änderungen laufen über den regulären Weg.`, `
      <div class="d-flex flex-wrap align-items-end gap-2">
        <label class="sim-feld"><span>Von</span><input class="form-control" type="date" data-nb-von value="${e(d.schuljahrVon)}"></label>
        <label class="sim-feld"><span>Bis</span><input class="form-control" type="date" data-nb-bis value="${e(d.schuljahrBis)}"></label>
        <button class="btn btn-outline-secondary" type="button" data-nachbessern>Simulationsdaten nachbessern</button>
        <button class="btn btn-outline-danger" type="button" data-nb-abbrechen hidden>Abbrechen</button>
      </div>
      <div class="sim-fortschritt" data-nb-stand aria-live="polite"></div>`, 'data-wartung')}
    ${abschnitt('Simulationsdaten entfernen', 'Löscht alle von der Simulation erzeugten Einträge und ihre Konten und schaltet die Simulation aus. Ihre eigenen Einträge bleiben erhalten.',
      '<button class="btn btn-outline-danger" type="button" data-entfernen>Simulationsdaten entfernen</button>', 'data-wartung')}`;

  const form = ziel.querySelector('form');
  const geaendert = () => { ziel.querySelector('[data-geaendert]').hidden = false; };
  form.addEventListener('input', geaendert);
  form.addEventListener('change', geaendert);

  // Mehrfachauswahlen (kompakte Dropdowns mit Suche, Gruppen, Alle/Keine)
  const lehrer = (d.lehrer ?? []).map(l => ({ wert: l.kurz, text: l.kurz, zweit: l.name || '' }));
  const faecher = (d.faecher ?? []).map(f => ({ wert: f.kurz, text: f.kurz, zweit: f.name && f.name !== f.kurz ? f.name : '', gruppe: f.gruppe || null }))
    .sort((a, b) => (a.gruppe ?? '').localeCompare(b.gruppe ?? '', 'de') || a.text.localeCompare(b.text, 'de'));
  const raeume = (d.raeume ?? []).map(r => ({ wert: r.kurz, text: r.kurz, zweit: [r.name !== r.kurz ? r.name : '', r.raumart].filter(Boolean).join(', '), gruppe: r.gruppe || 'Weitere Räume' }))
    .sort((a, b) => a.gruppe.localeCompare(b.gruppe, 'de') || a.text.localeCompare(b.text, 'de', { numeric: true }));
  const wahl = {};
  const anlegen = (name, eintraege, wert, platzhalter, einheit) => {
    // gespeicherte Kürzel ohne Rücksicht auf Groß- und Kleinschreibung den Einträgen zuordnen
    wert = wert.map(v => eintraege.find(x => x.wert.toUpperCase() === v.toUpperCase())?.wert ?? v);
    const el = auswahl({ eintraege, wert, mehrfach: true, platzhalter, suchePlatzhalter: 'Suchen', alleKeine: true, label: ziel.querySelector(`#sim-${name}-titel`)?.textContent,
      zusammenfassung: einheit ? n => `${n} ${einheit} ausgewählt` : undefined, beiWahl: geaendert, klasse: 'sim-wahl' });
    ziel.querySelector(`[data-wahl="${name}"]`).replaceWith(el);
    wahl[name] = el;
  };
  const gross = l => (l ?? []).map(x => String(x));
  anlegen('aus', lehrer, gross(w.ausgenommen), 'Keine Lehrkraft ausgenommen', 'Lehrkräfte');
  anlegen('sek1', faecher, gross(w.pruefungSekI), 'Keine Prüfungsfächer');
  anlegen('sek2', faecher, gross(w.pruefungSekII), 'Keine Prüfungsfächer');
  anlegen('kr', raeume, gross(w.klausurraeume), 'Automatisch nach Raumart und Größe', 'Räume');
  anlegen('ra', raeume, gross(w.raeumeAusgeschlossen), 'Keine Räume ausgeschlossen', 'Räume');

  form.klausurraumanteil.oninput = ev => { ev.target.nextElementSibling.textContent = `${ev.target.value} %`; };
  const regler = [...form.querySelectorAll('[data-int]')];
  const reglerZeigen = r => { r.closest('.sim-regelzeile').querySelector('output').textContent = reglertext(Number(r.value), Number(r.dataset.std), r.dataset.art); };
  regler.forEach(r => r.addEventListener('input', () => reglerZeigen(r)));
  form.querySelector('[data-int-standard]').onclick = () => {
    regler.forEach(r => { r.value = r.dataset.std; reglerZeigen(r); });
    form.grippewellen.checked = true;
    geaendert();
  };
  form.onsubmit = async ev => {
    ev.preventDefault();
    const daten = { aktiv: form.aktiv.checked, verteiler: form.verteiler.value, klausurraumanteil: Number(form.klausurraumanteil.value),
      ausgenommen: wahl.aus.wert, pruefungSekI: wahl.sek1.wert, pruefungSekII: wahl.sek2.wert, klausurraeume: wahl.kr.wert, raeumeAusgeschlossen: wahl.ra.wert };
    BEREICHE.forEach(([n]) => { daten[n] = form[n].checked; });
    daten.intensitaet = { grippewellen: form.grippewellen.checked };
    regler.forEach(r => { daten.intensitaet[r.dataset.int] = Number(r.value); });
    try {
      await senden('/api/app/admin/simulation', daten, 'PUT');
      toast(daten.aktiv ? (w.aktiv ? 'Einstellungen gespeichert.' : 'Die Simulation läuft ab jetzt.') : 'Die Simulation ist ausgeschaltet.');
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelector('[data-lauf]')?.addEventListener('click', async () => {
    try {
      const r = await senden('/api/app/admin/simulation/lauf', {});
      toast(r.ausgefuehrt ? `${r.ausgefuehrt} Einträge ausgeführt.` : 'Derzeit ist nichts fällig.');
      neu();
    } catch (err) { kontext.fehler(err); }
  });

  // Hintergrundaufträge: Stand alle zwei Sekunden, Abbrechen nach dem aktuellen Tag
  const auftrag = (art, pfad, schluessel, startKnopf, abbrechenKnopf, standEl, fertigText) => {
    const knopf = ziel.querySelector(startKnopf);
    const abbr = ziel.querySelector(abbrechenKnopf);
    const stand = ziel.querySelector(standEl);
    let t = null;
    const zeigenStand = n => {
      stand.innerHTML = auftragsstand(n, art);
      knopf.disabled = !!n?.laeuft;
      abbr.hidden = !n?.laeuft;
    };
    const verfolgen = () => {
      clearInterval(t);
      t = setInterval(async () => {
        if (!document.body.contains(stand)) { clearInterval(t); return; }
        try {
          const n = (await holen('/api/app/admin/simulation'))[schluessel];
          zeigenStand(n);
          if (!n.laeuft) { clearInterval(t); toast(n.abgebrochen ? 'Der Auftrag wurde abgebrochen.' : n.fehler ? 'Der Auftrag wurde mit einem Fehler beendet.' : fertigText); }
        } catch { clearInterval(t); }
      }, 2000);
    };
    zeigenStand(d[schluessel]);
    if (d[schluessel]?.laeuft) verfolgen();
    knopf.onclick = async () => {
      try {
        const n = await senden(`/api/app/admin/simulation/${pfad}`, { von: ziel.querySelector(`[data-${art}-von]`).value || null, bis: ziel.querySelector(`[data-${art}-bis]`).value || null });
        zeigenStand(n);
        verfolgen();
      } catch (err) { kontext.fehler(err); }
    };
    abbr.onclick = async () => {
      try {
        abbr.disabled = true;
        zeigenStand(await senden(`/api/app/admin/simulation/${pfad}/abbrechen`, {}));
        toast('Der Auftrag endet nach dem aktuellen Tag.');
      } catch (err) { kontext.fehler(err); } finally { abbr.disabled = false; }
    };
  };
  auftrag('np', 'nachpflegen', 'nachpflegen', '[data-nachpflegen]', '[data-np-abbrechen]', '[data-np-stand]', 'Das Nachpflegen ist abgeschlossen.');
  auftrag('nb', 'nachbessern', 'nachbessern', '[data-nachbessern]', '[data-nb-abbrechen]', '[data-nb-stand]', 'Das Nachbessern ist abgeschlossen.');

  ziel.querySelector('[data-entfernen]').onclick = async () => {
    if (!await bestaetigen('Simulationsdaten entfernen', 'Alle von der Simulation erzeugten Einträge (Lehrstoff, Abwesenheiten, Entschuldigungen, Krankmeldungen, Prüfungen, Raumänderungen, Sprechtagstermine) und ihre Konten werden gelöscht. Die Simulation wird ausgeschaltet.', 'Entfernen')) return;
    try {
      const r = await senden('/api/app/admin/simulation/entfernen', {});
      toast(`${r.entfernt} Datensätze entfernt.`);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
}
