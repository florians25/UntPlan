// Klassenbuch → Befreiungen vom Unterricht: Liste mit Filter (Klasse, Schüler*in, Zeitraum, Grund), Neu/Bearbeiten/Löschen
// durch die Klassenleitung, Bericht. Befreite Schüler*innen sind in den Stundendetails gekennzeichnet.
import { e, icon, holen, senden, api, toast, heuteIso, plusTage, leer, WOCHENTAGE, suchfeldMitFilter, suchfeldMitFilterBinden, zeitraumPillen, zeilenFiltern } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';

export async function zeigen(seite, kontext, [klasseParam, vonParam, bisParam, grundParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const klasse = klasseParam === '-' ? '' : klasseParam ?? eigene ?? '';
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(heuteIso(), 180);
  const grund = grundParam && grundParam !== '-' ? grundParam : '';
  const ziel = (k = klasse, v = von, b = bis, g = grund) => `#/befreiungen/${encodeURIComponent(k || '-')}/${v}/${b}/${encodeURIComponent(g || '-')}`;
  const d = await holen(`/api/app/befreiungen?${klasse ? `klasse=${encodeURIComponent(klasse)}&` : ''}von=${von}&bis=${bis}`);
  const liste = d.befreiungen.filter(b => !grund || b.grund === grund);
  const gruende = Object.fromEntries(d.gruende.map(g => [g.kurz, g.name]));
  kontext.titel('Befreiungen vom Unterricht', `${d.verwalten ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}
    <a class="btn btn-outline-secondary" href="#/berichte/befreiungen/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Bericht Befreiungen</a>`, `${liste.length}`);
  seite.innerHTML = `
    <div class="u-sf-leiste u-kopfteil">
      ${suchfeldMitFilter({ platzhalter: 'Schüler*in, Fach oder Text suchen', label: 'Befreiungen durchsuchen', filter: [
        { name: 'klasse', label: 'Klasse', alle: 'Alle Klassen', wert: klasse, optionen: el.klassen.map(k => [k.kurz, k.kurz]) },
        { name: 'grund', label: 'Befreiungsgrund', alle: 'Alle Gründe', wert: grund, optionen: d.gruende.map(g => [g.kurz, g.name]) },
      ] })}
      ${zeitraumPillen(von, bis)}
    </div>
    <div class="u-tabelle-karte">${liste.length ? `<table class="u-tabelle"><thead><tr><th>Schüler*in</th><th>Klasse</th><th>Von</th><th>Bis</th><th>Wochentag</th><th>Fach</th><th>Beginn</th><th>Ende</th><th>Befreiungsgrund</th><th>Text</th><th></th></tr></thead><tbody>
      ${liste.map(b => `<tr><td class="fw-semibold" style="width:auto">${e(b.schueler)}</td><td>${e(b.klasse)}</td><td>${datumDe(b.von)}</td><td>${datumDe(b.bis)}</td>
        <td>${b.wochentag ? WOCHENTAGE[b.wochentag - 1] : 'jeden Tag'}</td><td>${e(b.fach)}</td><td>${e(b.beginn || '')}</td><td>${e(b.ende || '')}</td>
        <td>${e(gruende[b.grund] ?? b.grund)}</td><td class="small">${e(b.text)}</td>
        <td class="text-end text-nowrap">${d.verwalten ? `<button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${b.id}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${b.id}" title="Löschen">${icon('loeschen')}</button>` : ''}</td></tr>`).join('')}
      </tbody></table>` : leer('Keine Befreiungen gefunden.', 'file-earmark-x')}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  const sf = suchfeldMitFilterBinden(seite, {
    beiSuche: t => zeilenFiltern(seite.querySelectorAll('.u-tabelle tbody tr'), t),
    beiFilter: () => filterNeu(),
  });
  const filterNeu = () => kontext.navigieren(ziel(sf.wert('klasse'), f('von') || von, f('bis') || bis, sf.wert('grund')));
  seite.querySelectorAll('[data-von], [data-bis]').forEach(x => x.onchange = filterNeu);
  const neu = () => kontext.navigieren(location.hash);

  async function formular(b = null) {
    const kl = b?.klasse ?? klasse ?? eigene ?? el.klassen[0]?.kurz;
    const schueler = kl ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(kl)}`) : [];
    const faecher = el.faecher.map(x => x.kurz);
    // wie im Original: Klasse | Schüler*innen (Klasse) mit „Bitte wählen Sie“ und Liste der gewählten (✓ Name (Klasse)), Zeitraum | Wochentag,
    // Fach, Beginnzeit | Endzeit, Befreiungsgrund, Text; Speichern | Abbrechen
    const gewaehlt = new Map(b ? [[b.schuelerId, `${b.schueler} (${b.klasse})`]] : []);
    const schuelerOptionen = (liste, k) => `<option value="">Bitte wählen Sie</option>${liste.map(s => `<option value="${s.id}" data-text="${e(`${s.name} ${s.vorname} (${k})`)}">${e(s.name)} ${e(s.vorname)}</option>`).join('')}`;
    dialog({
      titel: b ? 'Befreiung' : 'Neue Befreiung', breit: true,
      inhalt: `<div class="row g-3">
        <div class="col-sm-6"><label class="form-label">Klasse</label><select class="form-select" name="klasse" ${b ? 'disabled' : ''}>${el.klassen.map(k => `<option ${k.kurz === kl ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label" data-sch-titel>Schüler*innen (${e(kl ?? '')})</label><select class="form-select" name="schuelerwahl" ${b ? 'disabled' : ''}>${schuelerOptionen(schueler, kl)}</select></div>
        <div class="col-12"><div class="bf-gewaehlt" data-gewaehlt></div></div>
        <div class="col-sm-8"><label class="form-label">Zeitraum</label><div class="d-flex gap-2"><input type="date" class="form-control" name="von" value="${b?.von ?? heuteIso()}" aria-label="Von"><input type="date" class="form-control" name="bis" value="${b?.bis ?? plusTage(heuteIso(), 30)}" aria-label="Bis"></div></div>
        <div class="col-sm-4"><label class="form-label">Wochentag</label><select class="form-select" name="wochentag"><option value="0">jeden Tag</option>${WOCHENTAGE.map((w, i) => `<option value="${i + 1}" ${b?.wochentag === i + 1 ? 'selected' : ''}>${w}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Fach</label><select class="form-select" name="fach"><option value="">&lt;Auswahl&gt;</option>${faecher.map(x => `<option ${x === b?.fach ? 'selected' : ''}>${e(x)}</option>`).join('')}</select></div>
        <div class="col-sm-6"></div>
        <div class="col-sm-3"><label class="form-label">Beginnzeit</label><input type="time" class="form-control" name="beginn" value="${b?.beginn || '00:00'}"></div>
        <div class="col-sm-3"><label class="form-label">Endzeit</label><input type="time" class="form-control" name="ende" value="${b?.ende || '00:00'}"></div>
        <div class="col-sm-6"></div>
        <div class="col-sm-6"><label class="form-label">Befreiungsgrund</label><select class="form-select" name="grund">${d.gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === b?.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="text" value="${e(b?.text ?? '')}"></div></div>`,
      beimOeffnen: box => {
        const zeichnen = () => {
          box.querySelector('[data-gewaehlt]').innerHTML = [...gewaehlt].map(([id, t]) => `<span class="bf-chip"><i class="bi bi-check-lg"></i>${e(t)}${b ? '' : `<button type="button" data-weg="${id}" aria-label="${e(t)} entfernen"><i class="bi bi-x"></i></button>`}</span>`).join('');
          box.querySelectorAll('[data-weg]').forEach(x => x.onclick = () => { gewaehlt.delete(Number(x.dataset.weg)); zeichnen(); });
        };
        zeichnen();
        const wahl = box.querySelector('[name=schuelerwahl]');
        wahl.onchange = () => { const o = wahl.selectedOptions[0]; if (o?.value) gewaehlt.set(Number(o.value), o.dataset.text); wahl.value = ''; zeichnen(); };
        box.querySelector('[name=klasse]').onchange = async ev => {
          const s = await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(ev.target.value)}`);
          wahl.innerHTML = schuelerOptionen(s, ev.target.value);
          box.querySelector('[data-sch-titel]').textContent = `Schüler*innen (${ev.target.value})`;
        };
      },
      knoepfe: [{ text: 'Speichern', art: 'haupt', aktion: async box => {
          const v = n => box.querySelector(`[name=${n}]`).value;
          if (!gewaehlt.size) { toast('Bitte Schüler*innen wählen.', true); return false; }
          const daten = sid => ({ schuelerId: sid, von: v('von'), bis: v('bis'), wochentag: Number(v('wochentag')), fach: v('fach'),
            beginn: v('beginn') === '00:00' ? '' : v('beginn'), ende: v('ende') === '00:00' ? '' : v('ende'), grund: v('grund'), text: v('text') });
          if (b) await senden(`/api/app/befreiungen/${b.id}`, daten(b.schuelerId), 'PUT');
          else for (const sid of gewaehlt.keys()) await senden('/api/app/befreiungen', daten(sid));
          toast('Gespeichert.'); neu();
        } },
        ...(b ? [{ text: 'Löschen', art: 'gefahr', aktion: async () => { await api(`/api/app/befreiungen/${b.id}`, { methode: 'DELETE' }); toast('Gelöscht.'); neu(); } }] : []),
        { text: 'Abbrechen' }],
    });
  }
  document.querySelector('[data-neu]')?.addEventListener('click', () => formular());
  seite.querySelectorAll('[data-bearbeiten]').forEach(x => x.onclick = () => formular(liste.find(b => b.id === Number(x.dataset.bearbeiten))));
  seite.querySelectorAll('[data-loeschen]').forEach(x => x.onclick = async () => {
    if (!await bestaetigen('Befreiung löschen', 'Diese Befreiung löschen?')) return;
    await api(`/api/app/befreiungen/${x.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}
