// Administration → Schulbetrieb simulieren: Simulierte Lehrkräfte, Klassenleitungen und Erziehungsberechtigte tragen im Hintergrund ein
// (Lehrstoff, Abwesenheiten, Verspätungen, Entschuldigungen, Krankmeldungen an die Verteilerliste, Prüfungen, Sprechtagsbuchungen).
// Ausgenommene Lehrkräfte (Standard: Schulleitung) bleiben unberührt. Aufgerufen aus admin.js.
import { e, holen, senden, toast, bestaetigen } from '../ui.js';

const zeit = s => s ? new Date(s.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '';

const BEREICHE = [
  ['klassenbuch', 'Klassenbuch', 'Lehrstoff, Hausaufgaben und Klassenbucheinträge der gehaltenen Stunden'],
  ['abwesenheiten', 'Abwesenheiten', 'Abwesenheitskontrolle mit kranken Schüler*innen, Arztterminen und Verspätungen (nur 1. bis 3. Stunde)'],
  ['entschuldigungen', 'Entschuldigungen', 'Klassenleitungen entschuldigen unregelmäßig mit Begründung und Nachweis'],
  ['krankmeldungen', 'Krankmeldungen', 'Lehrkräfte melden sich an der Verteilerliste krank, jeden Schultag mindestens eine Meldung'],
  ['pruefungen', 'Prüfungen', 'Eine Klassenarbeit bzw. Klausur je Fach und Halbjahr, einige Wochen im Voraus. Sie werden wie echte Prüfungen an UntPlan übergeben.'],
  ['sprechtag', 'Sprechtag', 'Einladungen der Lehrkräfte und Buchungen der Erziehungsberechtigten'],
  ['lehreraktionen', 'Lehreraktionen', 'Gelegentliche Raumänderungen (nur freie passende Räume), Lehrstoff schon am Vorabend, Hinweise an die Klasse'],
];

/** Auswahl der Fächer mit Häkchen (Prüfungsfächer der Sek I bzw. Oberstufe). */
const faecherwahl = (faecher, gewaehlt, name) => `<div class="sim-auswahl">${faecher.map(f => `<label class="sim-chip"><input type="checkbox" data-${name} value="${e(f.kurz)}" ${gewaehlt.has(f.kurz.toUpperCase()) ? 'checked' : ''}>
  <b>${e(f.kurz)}</b>${f.name && f.name !== f.kurz ? `<span>${e(f.name)}</span>` : ''}</label>`).join('')}</div>`;

export async function zeigen(ziel, kontext, neu) {
  const d = await holen('/api/app/admin/simulation');
  const w = d.einstellungen;
  const aus = new Set((w.ausgenommen ?? []).map(x => x.toUpperCase()));
  const sekI = new Set((w.pruefungSekI ?? []).map(x => x.toUpperCase()));
  const sekII = new Set((w.pruefungSekII ?? []).map(x => x.toUpperCase()));
  const z = d.zahlen;
  ziel.classList.add('wt-seite');
  ziel.innerHTML = `
    <div class="wt-status ${w.aktiv ? 'sim-an' : 'sim-aus'}">
      <span class="wt-symbol"><i class="bi bi-${w.aktiv ? 'play-circle' : 'pause-circle'}" aria-hidden="true"></i></span>
      <div><h3>${w.aktiv ? 'Simulation läuft' : 'Simulation ausgeschaltet'}</h3>
        <p>${w.aktiv ? `Seit ${e(zeit(w.start))}. Zuletzt ausgeführt: ${e(zeit(d.stand) || 'noch nicht')}.` : 'Es werden keine Einträge erzeugt. Vorhandene Simulationsdaten bleiben erhalten, bis Sie sie entfernen.'}</p></div>
    </div>
    <div class="wt-raster">
      ${[['Lehrstoff', z.lehrstoff], ['Abwesenheiten', z.abwesenheiten], ['Entschuldigungen', z.entschuldigt], ['Klassenbucheinträge', z.vermerke],
        ['Krankmeldungen', z.krankmeldungen], ['Prüfungen', z.pruefungen], ['Sprechtagstermine', z.sprechtag], ['Raumänderungen', z.raumaenderungen]]
        .map(([t, n]) => `<div class="wt-karte wt-wert mb-0"><small>${t}</small><b>${n}</b></div>`).join('')}
    </div>
    <form>
      <div class="wt-karte">
        <label class="wt-schalter sim-schalter"><input type="checkbox" name="aktiv" ${w.aktiv ? 'checked' : ''}>Schulbetrieb simulieren</label>
        <p class="ug-hinweis mt-0">Simulierte Lehrkräfte arbeiten wie im echten Schulbetrieb: zu den gehaltenen Stunden, zu realistischen Uhrzeiten und nicht immer vollständig.
          Kranke bzw. abwesende Lehrkräfte tragen nichts ein, bei Vertretungen trägt die Vertretungslehrkraft ein. Absenzen in der Vertretungsplanung tragen Sie selbst ein.
          Alle Einträge stammen von eigenen, gesperrten Konten und lassen sich unten vollständig entfernen.</p>
        <div class="wt-module">${BEREICHE.map(([n, t, h]) => `<label class="wt-modul sim-modul"><input class="form-check-input mt-1" type="checkbox" name="${n}" ${w[n] ? 'checked' : ''}>
          <span>${t}<small>${h}</small></span></label>`).join('')}</div>
      </div>
      <div class="wt-karte">
        <h4>Ausgenommene Lehrkräfte</h4>
        <p class="ug-hinweis mt-0">Für diese Lehrkräfte läuft keine Automatik (kein Lehrstoff, keine Krankmeldungen, keine Entschuldigungen als Klassenleitung, keine Prüfungen).
          Standardmäßig sind es die Konten der Schulleitung und Administration.</p>
        <div class="sim-auswahl">${d.lehrer.map(l => `<label class="sim-chip"><input type="checkbox" data-aus value="${e(l.kurz)}" ${aus.has(l.kurz.toUpperCase()) ? 'checked' : ''}>
          <b>${e(l.kurz)}</b>${l.name ? `<span>${e(l.name)}</span>` : ''}</label>`).join('') || '<span class="text-body-secondary">Noch keine Daten aus UntPlan übertragen.</span>'}</div>
      </div>
      <div class="wt-karte">
        <h4>Prüfungsfächer</h4>
        <p class="ug-hinweis mt-0">Nur in den gewählten Fächern trägt die Simulation Prüfungen ein, je Klasse und Fach eine im Halbjahr.
          In der Sekundarstufe I (Klassen 5 bis 10) gilt die erste Liste, in der Oberstufe (EF, Q1, Q2) und in den Bildungsgängen des Berufskollegs die zweite.</p>
        <h5 class="sim-unter">Sekundarstufe I</h5>
        ${faecherwahl(d.faecher, sekI, 'sek1')}
        <h5 class="sim-unter">Sek II und Berufskolleg</h5>
        ${faecherwahl(d.faecher, sekII, 'sek2')}
        <h5 class="sim-unter">Eigener Prüfungsraum</h5>
        <label class="sim-anteil"><input type="range" name="klausurraumanteil" min="0" max="100" step="5" value="${Number(w.klausurraumanteil ?? 30)}">
          <output>${Number(w.klausurraumanteil ?? 30)} %</output></label>
        <p class="ug-hinweis mb-0">Anteil der Klausuren in der Oberstufe, für die die Lehrkraft einen eigenen freien Raum festlegt (in der Sekundarstufe I ein Drittel davon).</p>
      </div>
      <div class="wt-karte">
        <h4>Verteilerliste für Krankmeldungen</h4>
        <label class="form-label" for="sim-verteiler">Name der Liste im Mitteilungsmodul</label>
        <input id="sim-verteiler" class="form-control" name="verteiler" maxlength="100" value="${e(w.verteiler)}" style="max-width: 320px">
        <p class="ug-hinweis mb-0">${d.verteilerVorhanden ? 'Die Liste ist vorhanden. Die Krankmeldungen gehen an ihre Mitglieder.'
          : 'Die Liste gibt es noch nicht. Beim Einschalten wird sie mit der Schulleitung und Administration angelegt.'}</p>
      </div>
      <div class="wt-karte">
        <h4>Vergangenheit nachpflegen</h4>
        <p class="ug-hinweis mt-0">Die Lehrkräfte tragen fehlende Abwesenheitskontrollen, Lehrstoff und Klassenbucheinträge nach, die Klassenleitungen entschuldigen,
          wie im Alltag gesammelt einige Tage später. Ausgenommene und abwesende Lehrkräfte tragen nichts ein, Ihre eigenen Einträge bleiben unverändert.
          Mehrfaches Nachpflegen trägt nichts doppelt ein.</p>
        <div class="d-flex flex-wrap align-items-end gap-2">
          <label class="sim-feld"><span>Von</span><input class="form-control" type="date" data-np-von value="${e(d.schuljahrVon)}" min="${e(d.schuljahrVon)}" max="${e(d.gestern)}"></label>
          <label class="sim-feld"><span>Bis</span><input class="form-control" type="date" data-np-bis value="${e(d.gestern)}" min="${e(d.schuljahrVon)}" max="${e(d.gestern)}"></label>
          <button class="btn btn-outline-secondary" type="button" data-nachpflegen ${d.nachpflegen.laeuft ? 'disabled' : ''}>Vergangenheit nachpflegen</button>
        </div>
        <div class="sim-fortschritt" data-np-stand></div>
      </div>
      <div class="d-flex flex-wrap gap-2">
        <button class="btn btn-primary" type="submit">Speichern</button>
        ${w.aktiv ? '<button class="btn btn-outline-secondary" type="button" data-lauf>Jetzt ausführen</button>' : ''}
        <button class="btn btn-outline-danger ms-auto" type="button" data-entfernen>Simulationsdaten entfernen</button>
      </div>
    </form>`;
  const form = ziel.querySelector('form');
  form.klausurraumanteil.oninput = ev => { ev.target.nextElementSibling.textContent = `${ev.target.value} %`; };
  form.onsubmit = async ev => {
    ev.preventDefault();
    const daten = { aktiv: form.aktiv.checked, verteiler: form.verteiler.value, klausurraumanteil: Number(form.klausurraumanteil.value),
      ausgenommen: [...form.querySelectorAll('[data-aus]:checked')].map(x => x.value),
      pruefungSekI: [...form.querySelectorAll('[data-sek1]:checked')].map(x => x.value), pruefungSekII: [...form.querySelectorAll('[data-sek2]:checked')].map(x => x.value) };
    BEREICHE.forEach(([n]) => { daten[n] = form[n].checked; });
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
  const stand = ziel.querySelector('[data-np-stand]');
  const zeigeStand = n => {
    if (!n || (!n.laeuft && !n.ende)) { stand.innerHTML = ''; return; }
    const pro = n.tage ? Math.round(n.fertig * 100 / n.tage) : 0;
    stand.innerHTML = `<div class="sim-balken"><span style="width:${pro}%"></span></div>
      <small>${n.laeuft ? `Läuft: ${n.fertig} von ${n.tage} Schultagen (${pro} %)` : `Abgeschlossen am ${e(zeit(n.ende))}: ${n.tage} Schultage`}, ${n.eintraege} Einträge nachgepflegt${n.uebersprungen ? `, ${n.uebersprungen} übersprungen` : ''}.${n.fehler ? ` Fehler: ${e(n.fehler)}` : ''}</small>`;
  };
  zeigeStand(d.nachpflegen);
  const verfolgen = () => {
    const t = setInterval(async () => {
      if (!document.body.contains(stand)) { clearInterval(t); return; }
      try {
        const n = (await holen('/api/app/admin/simulation')).nachpflegen;
        zeigeStand(n);
        if (!n.laeuft) { clearInterval(t); ziel.querySelector('[data-nachpflegen]').disabled = false; toast('Das Nachpflegen ist abgeschlossen.'); }
      } catch { clearInterval(t); }
    }, 2000);
  };
  if (d.nachpflegen.laeuft) verfolgen();
  ziel.querySelector('[data-nachpflegen]').onclick = async ev => {
    try {
      const n = await senden('/api/app/admin/simulation/nachpflegen', { von: ziel.querySelector('[data-np-von]').value || null, bis: ziel.querySelector('[data-np-bis]').value || null });
      ev.target.disabled = true;
      zeigeStand(n);
      verfolgen();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelector('[data-entfernen]').onclick = async () => {
    if (!await bestaetigen('Simulationsdaten entfernen', 'Alle von der Simulation erzeugten Einträge (Lehrstoff, Abwesenheiten, Entschuldigungen, Krankmeldungen, Prüfungen, Raumänderungen, Sprechtagstermine) und ihre Konten werden gelöscht. Die Simulation wird ausgeschaltet.', 'Entfernen')) return;
    try {
      const r = await senden('/api/app/admin/simulation/entfernen', {});
      toast(`${r.entfernt} Datensätze entfernt.`);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
}
