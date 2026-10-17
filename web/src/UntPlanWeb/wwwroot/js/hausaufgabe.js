// Hausaufgaben als Fenster (nl-design): Detailfenster (Fach, Klasse, Lehrkraft, Aufgabedatum, Fälligkeit, Text, Bemerkung, Anhänge,
// Erledigt-Status; Bearbeiten/Löschen für die Lehrkraft) und Formular zum Anlegen bzw. Bearbeiten. Gemeinsam für
// Klassenbuch → Hausaufgaben und die Stundendetails.
import { e, holen, senden, api, toast, heuteIso, plusTage } from './ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from './kbdialog.js';

const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const mitTag = iso => iso ? `${WT[new Date(`${iso}T12:00`).getDay()]}, ${datumDe(iso)}` : '–';

/** Kopfzeile einer Stunde: „7a | D · Mi, 14.10.2026 · 3. Std.“ */
const stundeText = x => `${(x.klassen ?? []).join(', ')}${x.fach ? ` | ${x.fach}` : ''} · ${mitTag(x.datum)}${x.stunde || x.von ? ` · ${x.stunde || x.von}. Std.` : ''}`;

/**
 * Detailfenster einer Hausaufgabe. h: Eintrag aus /api/app/hausaufgaben/eintraege bzw. der Hausaufgabenliste einer Stunde
 * (id, datum, stunde, klassen, fach, lehrer, text, faellig, bemerkung, erledigt, darf). geaendert(): nach Speichern/Löschen.
 */
export function hausaufgabeDetails(h, { darf = !!h.darf, geaendert = () => {} } = {}) {
  const ueberfaellig = h.faellig && h.faellig < heuteIso() && !h.erledigt;
  const zeile = (titel, wert) => `<div class="hw-d-zeile"><dt>${titel}</dt><dd>${wert}</dd></div>`;
  const box = dialog({
    titel: 'Hausaufgabe',
    inhalt: `<div class="hw-details">
      <div class="hw-d-kopf"><span class="hw-d-fach">${e(h.fach || '–')}</span><span class="hw-d-klassen">${e((h.klassen ?? []).join(', '))}</span>
        <span class="hw-d-status ${h.erledigt ? 'erledigt' : ueberfaellig ? 'ueberfaellig' : ''}">${h.erledigt ? '<i class="bi bi-check-circle-fill"></i>Erledigt' : ueberfaellig ? 'Überfällig' : 'Offen'}</span></div>
      <p class="hw-d-text">${e(h.text)}</p>
      <dl class="hw-d-liste">
        ${zeile('Fach', e(h.fach || '–'))}
        ${zeile('Klasse', e((h.klassen ?? []).join(', ') || '–'))}
        ${zeile('Lehrkraft', e(h.lehrer || '–'))}
        ${zeile('Aufgabedatum', `${e(mitTag(h.datum))}${h.stunde ? ` <span class="hw-d-grau">· ${h.stunde}. Std.</span>` : ''}`)}
        ${zeile('Fälligkeit', h.faellig ? e(mitTag(h.faellig)) : '<span class="hw-d-grau">ohne Fälligkeit</span>')}
        ${h.bemerkung ? zeile('Bemerkung', e(h.bemerkung)) : ''}
        ${zeile('Anhänge', '<span class="hw-d-grau">Keine Anhänge</span>')}
        ${zeile('Status', darf ? `<label class="hw-d-erledigt"><input type="checkbox" class="nl-rund" data-erledigt ${h.erledigt ? 'checked' : ''}> Erledigt</label>` : h.erledigt ? 'Erledigt' : 'Offen')}
      </dl>
      ${!h.id ? '<p class="hw-d-hinweis"><i class="bi bi-info-circle"></i>Diese Hausaufgabe steht nur im Klassenbuch-Feld der Stunde und kann in den Stundendetails geändert werden.</p>' : ''}
    </div>`,
    knoepfe: [
      ...(darf ? [{ text: 'Löschen', art: 'gefahr', aktion: async () => {
        if (!await bestaetigen('Hausaufgabe löschen', 'Diese Hausaufgabe endgültig löschen?')) return false;
        await api(`/api/app/klassenbuch/hausaufgabenliste/${h.id}`, { methode: 'DELETE' });
        toast('Hausaufgabe gelöscht.');
        geaendert();
      } },
      { text: 'Bearbeiten', aktion: () => { setTimeout(() => hausaufgabeFormular({ h, geaendert }), 200); } }] : []),
      { text: 'Schließen', art: 'haupt' },
    ],
    beimOeffnen: b => {
      b.querySelector('[data-erledigt]')?.addEventListener('change', async ev => {
        try {
          await senden(`/api/app/klassenbuch/hausaufgabenliste/${h.id}`, { erledigt: ev.target.checked }, 'PUT');
          h.erledigt = ev.target.checked;
          const st = b.querySelector('.hw-d-status');
          st.className = `hw-d-status ${h.erledigt ? 'erledigt' : ''}`;
          st.innerHTML = h.erledigt ? '<i class="bi bi-check-circle-fill"></i>Erledigt' : 'Offen';
          toast(h.erledigt ? 'Als erledigt markiert.' : 'Wieder offen.');
          geaendert(true);
        } catch (err) { ev.target.checked = !ev.target.checked; toast(err.message, true); }
      });
    },
  });
  return box;
}

/**
 * Fenster zum Anlegen bzw. Bearbeiten. h: vorhandene Hausaufgabe (Bearbeiten); stunde: feste Stunde { datum, von, unterricht, fach, klassen }
 * (aus den Stundendetails); ohne beides wählt die Lehrkraft Datum und Stunde aus ihren Unterrichten (lehrer = eigenes Kürzel).
 */
export function hausaufgabeFormular({ h = null, stunde = null, lehrer = '', geaendert = () => {} } = {}) {
  const neu = !h;
  const fest = h ? { datum: h.datum, von: h.stunde, klassen: h.klassen, fach: h.fach } : stunde;
  const start = fest?.datum ?? heuteIso();
  let stunden = [];
  const box = dialog({
    titel: neu ? 'Neue Hausaufgabe' : 'Hausaufgabe bearbeiten',
    inhalt: `<div class="hw-formular">
      ${fest ? `<div class="hw-f-stunde"><i class="bi bi-journal-text"></i>${e(stundeText(fest))}</div>`
        : `<div class="hw-f-reihe">
          <label class="kd-dfeld"><span>Aufgabedatum</span><input type="date" class="form-control" name="datum" value="${start}"></label>
          <label class="kd-dfeld hw-f-breit"><span>Stunde</span><select class="form-select" name="stunde"><option value="">wird geladen …</option></select></label></div>`}
      <label class="kd-dfeld"><span>Fälligkeitsdatum</span><input type="date" class="form-control hw-f-datum" name="faellig" value="${e(h?.faellig ?? plusTage(start, 7))}" min="${start}"></label>
      <label class="kd-dfeld"><span>Hausaufgabe <b class="text-danger">*</b></span><textarea class="form-control" name="text" rows="4" maxlength="2000" placeholder="z. B. S. 45 Nr. 3 a–d">${e(h?.text ?? '')}</textarea></label>
      <label class="kd-dfeld"><span>Bemerkung</span><input class="form-control" name="bemerkung" maxlength="500" value="${e(h?.bemerkung ?? '')}" placeholder="optional"></label>
      <div class="kd-dfeld"><span>Anhang</span><div class="hw-f-anhang"><i class="bi bi-paperclip"></i>Anhänge werden nicht unterstützt</div></div>
    </div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: neu ? 'Erstellen' : 'Speichern', art: 'haupt', aktion: async b => {
      const v = n => b.querySelector(`[name=${n}]`)?.value ?? '';
      const daten = { text: v('text').trim(), faellig: v('faellig') || null, bemerkung: v('bemerkung').trim() };
      if (!daten.text) { toast('Bitte die Hausaufgabe eingeben.', true); return false; }
      if (!neu) await senden(`/api/app/klassenbuch/hausaufgabenliste/${h.id}`, daten, 'PUT');
      else if (fest) await senden('/api/app/klassenbuch/hausaufgabenliste', { datum: fest.datum, stunde: fest.von, unterricht: fest.unterricht, ...daten, ...(fest.einzelstunde ? { einzelstunde: fest.einzelstunde } : {}) });
      else {
        const s = stunden[Number(v('stunde'))];
        if (!s) { toast('Bitte eine Stunde wählen.', true); return false; }
        await senden('/api/app/klassenbuch/hausaufgabenliste', { datum: v('datum'), stunde: s.von, unterricht: s.unterricht, ...daten });
      }
      toast(neu ? 'Hausaufgabe erstellt.' : 'Hausaufgabe gespeichert.');
      geaendert();
    } }],
    beimOeffnen: b => {
      if (fest) return;
      // Stunden der Lehrkraft an diesem Tag (ohne Entfall)
      const datum = b.querySelector('[name=datum]'), wahl = b.querySelector('[name=stunde]'), faellig = b.querySelector('[name=faellig]');
      const laden = async () => {
        const d = datum.value;
        if (!d) return;
        faellig.min = d;
        if (!faellig.value || faellig.value < d) faellig.value = plusTage(d, 7);
        wahl.innerHTML = '<option value="">wird geladen …</option>';
        try {
          const r = await holen(`/api/app/klassenbuch/uebersicht?von=${d}&bis=${d}${lehrer ? `&lehrer=${encodeURIComponent(lehrer)}` : ''}`);
          stunden = (r.tage[0]?.stunden ?? []).filter(s => s.status !== 'entfall');
        } catch { stunden = []; }
        wahl.innerHTML = stunden.length ? stunden.map((s, i) => `<option value="${i}">${s.von === s.bis ? s.von : `${s.von}.–${s.bis}`}. Std. · ${e(s.klassen.join(', '))} | ${e(s.fach)}${s.beginn ? ` (${e(s.beginn)})` : ''}</option>`).join('')
          : '<option value="">An diesem Tag haben Sie keinen Unterricht</option>';
        wahl.dispatchEvent(new Event('change'));
      };
      datum.onchange = laden;
      laden();
    },
  });
  return box;
}
