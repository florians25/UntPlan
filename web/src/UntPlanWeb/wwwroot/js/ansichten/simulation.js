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
];

export async function zeigen(ziel, kontext, neu) {
  const d = await holen('/api/app/admin/simulation');
  const w = d.einstellungen;
  const aus = new Set((w.ausgenommen ?? []).map(x => x.toUpperCase()));
  const ohne = new Set((w.ohnePruefung ?? []).map(x => x.toUpperCase()));
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
        ['Krankmeldungen', z.krankmeldungen], ['Prüfungen', z.pruefungen], ['Sprechtagstermine', z.sprechtag]]
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
        <h4>Fächer ohne Prüfungen</h4>
        <p class="ug-hinweis mt-0">In diesen Fächern trägt die Simulation keine Klassenarbeiten bzw. Klausuren ein (z. B. Sport, Religion, Kunst, Musik).</p>
        <div class="sim-auswahl">${d.faecher.map(f => `<label class="sim-chip"><input type="checkbox" data-ohne value="${e(f.kurz)}" ${ohne.has(f.kurz.toUpperCase()) ? 'checked' : ''}>
          <b>${e(f.kurz)}</b>${f.name && f.name !== f.kurz ? `<span>${e(f.name)}</span>` : ''}</label>`).join('')}</div>
      </div>
      <div class="wt-karte">
        <h4>Verteilerliste für Krankmeldungen</h4>
        <label class="form-label" for="sim-verteiler">Name der Liste im Mitteilungsmodul</label>
        <input id="sim-verteiler" class="form-control" name="verteiler" maxlength="100" value="${e(w.verteiler)}" style="max-width: 320px">
        <p class="ug-hinweis mb-0">${d.verteilerVorhanden ? 'Die Liste ist vorhanden. Die Krankmeldungen gehen an ihre Mitglieder.'
          : 'Die Liste gibt es noch nicht. Beim Einschalten wird sie mit der Schulleitung und Administration angelegt.'}</p>
      </div>
      <div class="d-flex flex-wrap gap-2">
        <button class="btn btn-primary" type="submit">Speichern</button>
        ${w.aktiv ? '<button class="btn btn-outline-secondary" type="button" data-lauf>Jetzt ausführen</button>' : ''}
        <button class="btn btn-outline-danger ms-auto" type="button" data-entfernen>Simulationsdaten entfernen</button>
      </div>
    </form>`;
  const form = ziel.querySelector('form');
  form.onsubmit = async ev => {
    ev.preventDefault();
    const daten = { aktiv: form.aktiv.checked, verteiler: form.verteiler.value,
      ausgenommen: [...form.querySelectorAll('[data-aus]:checked')].map(x => x.value), ohnePruefung: [...form.querySelectorAll('[data-ohne]:checked')].map(x => x.value) };
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
  ziel.querySelector('[data-entfernen]').onclick = async () => {
    if (!await bestaetigen('Simulationsdaten entfernen', 'Alle von der Simulation erzeugten Einträge (Lehrstoff, Abwesenheiten, Entschuldigungen, Krankmeldungen, Prüfungen, Sprechtagstermine) und ihre Konten werden gelöscht. Die Simulation wird ausgeschaltet.', 'Entfernen')) return;
    try {
      const r = await senden('/api/app/admin/simulation/entfernen', {});
      toast(`${r.entfernt} Datensätze entfernt.`);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
}
