// Klassenbuch → Offene Stunden pro Klasse (Leitfaden Klassenleitung, Kapitel 7 „Regelmäßige Erinnerung an die Eintragungen“):
// „Unerledigte Unterrichtsstunden in der Klasse …“ vom ersten Schultag bis heute mit „Offene Stunden: …, Unterrichtsstunden: …“, Filtern
// Klassen, Lehrkräfte, Eintragung und der Liste Auswahl | Lehrkraft | Klasse | Fach | Datum | Von | Bis | Art | Raum | Klassenbuch.
// „Nachricht senden“ füllt „An“ mit allen betroffenen Lehrkräften (bzw. denen der ausgewählten Stunden); Betreff und Text sind vorbelegt.
// Adresse: #/offenklasse/<Klasse>/<von>/<Lehrkraft | ->/<Eintragung | ->
import { e, holen, senden, toast, leer, heuteIso } from '../ui.js';
import { kbDialog as dialog } from '../kbdialog.js';
import { karteDialog } from '../plangitter.js';

const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const wochentag = iso => WT[new Date(`${iso}T12:00:00`).getDay()] ?? '';
const EINTRAGUNG = [['', '- Alle -'], ['kontrolle', 'Abwesenheitskontrolle fehlt'], ['lehrstoff', 'Lehrstoff fehlt']];
const istDatum = s => /^\d{4}-\d{2}-\d{2}$/.test(s ?? '');

export async function zeigen(seite, kontext, [klasseParam, vonParam, lehrerParam, eintragParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const eigene = el.klassen.filter(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase())).map(k => k.kurz);
  const klasse = (klasseParam && klasseParam !== '-' ? klasseParam : '') || eigene[0] || el.klassen[0]?.kurz || '';
  const von = istDatum(vonParam) ? vonParam : (kontext.sitzung.schuljahr?.von ?? heuteIso());
  const lehrer = lehrerParam && lehrerParam !== '-' ? lehrerParam : '';
  const eintrag = EINTRAGUNG.some(([k]) => k && k === eintragParam) ? eintragParam : '';
  const ziel = (k = klasse, v = von, l = lehrer, en = eintrag) => `#/offenklasse/${encodeURIComponent(k || '-')}/${v}/${encodeURIComponent(l || '-')}/${en || '-'}`;
  kontext.titel(`Unerledigte Unterrichtsstunden in der Klasse ${klasse}`);
  if (!klasse) { seite.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  const q = new URLSearchParams({ art: 'alle', bereich: 'individuell', von, bis: heuteIso(), klasse });
  const r = await holen(`/api/app/klassenbuch/offene-stunden?${q}`);
  const offen = z => r.offenFuer === 'kontrolle' ? !z.geprueft : r.offenFuer === 'lehrstoff' ? !z.lehrstoff : !z.geprueft || !z.lehrstoff;
  const unerledigt = r.zeilen.filter(offen);
  const lehrkraefte = [...new Set(unerledigt.map(z => z.lehrer))].sort((a, b) => a.localeCompare(b, 'de'));
  const liste = unerledigt.filter(z => (!lehrer || z.lehrer === lehrer) && (!eintrag || (eintrag === 'kontrolle' ? !z.geprueft : !z.lehrstoff)));
  const klassenWahl = r.alle ? el.klassen.map(k => k.kurz) : [...new Set([...eigene, klasse])];
  const auswahl = new Set();
  seite.innerHTML = `<div class="ok-seite">
    <div class="ok-kopf">
      <label class="ok-von"><span>Von</span><input type="date" data-von value="${von}" max="${heuteIso()}"></label><span>- ${datumDe(r.bis)}</span>
      <span class="ok-zahlen">Offene Stunden: <b>${r.offen}</b>, Unterrichtsstunden: <b>${r.gesamt}</b></span>
    </div>
    <div class="nl-leiste">
      <label class="sg-feld"><span>Klassen</span><select class="form-select" data-klasse>${klassenWahl.map(k => `<option ${k === klasse ? 'selected' : ''}>${e(k)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Lehrkräfte</span><select class="form-select" data-lehrer><option value="">- Alle -</option>${lehrkraefte.map(l => `<option ${l === lehrer ? 'selected' : ''}>${e(l)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Eintragung</span><select class="form-select" data-eintrag>${EINTRAGUNG.map(([k, t]) => `<option value="${k}" ${k === eintrag ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
    </div>
    <div class="ok-aktionen"><button type="button" class="nl-knopf" data-nachricht ${liste.length ? '' : 'disabled'}><i class="bi bi-send"></i>Nachricht senden</button></div>
    <div class="nl-tabelle" data-inhalt></div></div>`;
  seite.querySelector('[data-von]').onchange = ev => istDatum(ev.target.value) && kontext.navigieren(ziel(klasse, ev.target.value));
  seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(ziel(ev.target.value, von, '', ''));
  seite.querySelector('[data-lehrer]').onchange = ev => kontext.navigieren(ziel(klasse, von, ev.target.value));
  seite.querySelector('[data-eintrag]').onchange = ev => kontext.navigieren(ziel(klasse, von, lehrer, ev.target.value));
  const inhalt = seite.querySelector('[data-inhalt]');
  const karte = z => ({ datum: z.datum, von: z.von, bis: z.bis, beginn: z.beginn, ende: z.ende, status: z.status === 'klausur' ? 'regulaer' : z.status, fach: z.fach, fachName: z.fachName,
    farbe: z.farbe, klassen: z.klassen.map(k => ({ k })), lehrer: [{ k: z.lehrer }], raeume: z.raeume.map(k => ({ k })), art: z.art, text: '', unterricht: z.unterricht });
  inhalt.innerHTML = liste.length ? `<table><thead><tr><th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></span></th>
      <th>Lehrkraft</th><th>Klasse</th><th>Fach</th><th></th><th>Datum</th><th>Von</th><th>Bis</th><th>Art</th><th>Raum</th><th>Klassenbuch</th></tr></thead><tbody>
    ${liste.map((z, i) => `<tr data-i="${i}">
      <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${i}" aria-label="Stunde auswählen"></td>
      <td>${e(z.lehrer)}</td><td>${e(z.klassen.join(', '))}</td><td>${e(z.fach)}</td><td>${wochentag(z.datum)}.</td><td class="text-nowrap">${datumDe(z.datum)}</td>
      <td>${e(z.beginn)}</td><td>${e(z.ende)}</td><td>${e(z.art || '')}</td><td>${e(z.raeume.join(', '))}</td>
      <td class="text-nowrap"><button type="button" class="ok-kb ${z.lehrstoff ? 'ok' : 'fehlt'}" data-oeffnen="${i}" title="${z.lehrstoff ? 'Lehrstoff eingetragen' : 'Lehrstoff fehlt'}" aria-label="${z.lehrstoff ? 'Lehrstoff eingetragen' : 'Lehrstoff fehlt'}"><i class="bi bi-journal-text"></i></button>
        <button type="button" class="ok-kb ${z.geprueft ? 'ok' : 'fehlt'}" data-oeffnen="${i}" title="${z.geprueft ? 'Abwesenheiten kontrolliert' : 'Abwesenheitskontrolle fehlt'}" aria-label="${z.geprueft ? 'Abwesenheiten kontrolliert' : 'Abwesenheitskontrolle fehlt'}"><i class="bi bi-people"></i></button></td></tr>`).join('')}
    </tbody></table>` : `<div class="p-3">${leer(unerledigt.length ? 'Keine Stunde passt zum Filter.' : 'Alle Eintragungen sind vollständig.', 'offen')}</div>`;
  inhalt.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => { const i = Number(c.dataset.wahl); if (c.checked) auswahl.add(i); else auswahl.delete(i); });
  inhalt.querySelector('[data-alle]')?.addEventListener('change', ev => inhalt.querySelectorAll('[data-wahl]').forEach(c => { c.checked = ev.target.checked; c.dispatchEvent(new Event('change')); }));
  const karten = liste.map(karte);
  inhalt.querySelectorAll('[data-oeffnen]').forEach(b => b.onclick = () => karteDialog(karten[Number(b.dataset.oeffnen)], { ...kontext, liste: karten, nachSpeichern: () => kontext.navigieren(location.hash) }));
  // Keine Auswahl nötig: ohne Auswahl gehen alle betroffenen Lehrkräfte in „An“
  seite.querySelector('[data-nachricht]').onclick = () => nachricht(auswahl.size ? [...auswahl].map(i => liste[i]) : liste, klasse, ich);
}

async function nachricht(stunden, klasse, ich) {
  const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
  const kuerzel = [...new Set(stunden.map(z => z.lehrer))];
  const finde = k => empfaenger.find(x => x.id.startsWith('benutzer:') && x.name.toLowerCase().includes(`(${k.toLowerCase()})`))
    ?? empfaenger.find(x => x.id.startsWith('benutzer:') && x.name.toLowerCase().split(/[\s,()]+/).includes(k.toLowerCase()));
  let an = kuerzel.map(k => ({ k, ziel: finde(k) }));
  const betreff = `Erinnerung an vergessene Eintragungen im Klassenbuch der ${klasse}`;
  const text = `Liebe Kolleginnen und Kollegen,\nbitte rufen Sie in UntPlanWeb den Bericht Klassenbuch -> Offene Stunden auf und lassen Sie sich anzeigen, welche Eintragungen Sie noch nicht getätigt haben. `
    + `Holen Sie die Eintragungen bitte zeitnah nach. Danke.\nMit freundlichen Grüßen\n${ich.anzeigename || ich.login}`;
  const chips = () => an.map((x, i) => `<span class="ok-an ${x.ziel ? '' : 'ohne'}" title="${e(x.ziel?.name ?? 'Kein Konto für diese Lehrkraft')}">${e(x.k)}<button type="button" data-weg="${i}" aria-label="${e(x.k)} entfernen"><i class="bi bi-x"></i></button></span>`).join('');
  const box = dialog({
    titel: betreff, breit: true,
    inhalt: `<div class="ok-nachricht">
      <div class="ok-zeile"><span class="ok-label">An</span><div class="ok-chips" data-chips>${chips()}</div>
        <select class="form-select ok-plus" data-plus aria-label="Empfänger hinzufügen"><option value="">+</option>${empfaenger.filter(x => x.id.startsWith('benutzer:')).map(x => `<option value="${e(x.id)}">${e(x.name)}</option>`).join('')}</select></div>
      <div class="ok-zeile"><span class="ok-label">Betreff</span><input class="form-control" name="betreff" value="${e(betreff)}"></div>
      <textarea class="form-control" name="text" rows="8">${e(text)}</textarea></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Senden', art: 'haupt', aktion: async b => {
      const ziele = [...new Set(an.filter(x => x.ziel).map(x => x.ziel.id))];
      if (!ziele.length) { toast('Keine Empfänger mit Konto.', true); return false; }
      await senden('/api/app/mitteilungen', { empfaenger: ziele, betreff: b.querySelector('[name=betreff]').value, text: b.querySelector('[name=text]').value, wichtig: false });
      toast('Nachricht gesendet.');
    } }],
  });
  const binden = () => {
    box.querySelector('[data-chips]').innerHTML = chips();
    box.querySelectorAll('[data-weg]').forEach(b => b.onclick = () => { an.splice(Number(b.dataset.weg), 1); binden(); });
  };
  binden();
  box.querySelector('[data-plus]').onchange = ev => {
    const x = empfaenger.find(y => y.id === ev.target.value);
    if (x && !an.some(y => y.ziel?.id === x.id)) an = [...an, { k: x.name.match(/\(([^)]+)\)/)?.[1] ?? x.name, ziel: x }];
    ev.target.value = '';
    binden();
  };
}
