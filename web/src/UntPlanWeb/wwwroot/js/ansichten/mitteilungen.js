// Mitteilungen wie im Original:
// - Ordner Posteingang, Gesendet, Entwürfe, Listen (Kollegium) und Einstellungen (Administration) im Untermenü der Seitenleiste;
//   Liste mit einer Zeile je Mitteilung, Lesen und Schreiben im großen Fenster.
// - Posteingang: „Lesebestätigung erforderlich“ oben, Lesebestätigung senden, Antworten, Allen antworten, Weiterleiten (mit Anhängen),
//   als ungelesen markieren, Drucken, Löschen. Text mit Markdown (Überschriften, fett, kursiv, durchgestrichen, Links, Listen);
//   externe Links mit Warnung, interne Links (/heute) ohne.
// - Gesendet: „Blase“ bestätigt | Empfänger*innen – Klick zeigt die Lesebestätigungsliste.
// - Neue Mitteilung: „An:“ mit Empfängerauswahl nach Gruppen (Lehrkräfte, Klassen, Schüler*innen, Erziehungsberechtigte, Gruppen, Listen) mit
//   Suche und Klassenfilter, Betreff, Text mit Vorschau, Lesebestätigung anfordern, wichtig, Anhänge (Größe und Anzahl laut Einstellungen),
//   Speichern als Entwurf (serverseitig) bzw. Senden.
// - Einstellungen (Administration): wer an wen schreiben darf, Sperren, Anhänge, Aufbewahrung.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, initialen, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';

const BASIS = '/api/app/mitteilungen';

function stileLaden() {
  if (document.querySelector('link[data-mitteilungen-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/mitteilungen.css', import.meta.url).href;
  l.dataset.mitteilungenCss = '1';
  document.head.append(l);
}

function zeit(s) {
  const d = new Date(String(s).replace(' ', 'T'));
  if (isNaN(d)) return s;
  const heute = new Date();
  return d.toDateString() === heute.toDateString()
    ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('de-DE', { day: 'numeric', month: 'short', year: d.getFullYear() === heute.getFullYear() ? undefined : 'numeric' });
}
const zeitLang = s => { const d = new Date(String(s).replace(' ', 'T')); return isNaN(d) ? s : `${d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })} | ${d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`; };

const kuerzel = name => initialen(String(name || '?').replace(/\(.*\)/, '').replace(/–.*/, ''));
const groesse = n => n > 1048576 ? `${(n / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`;
const TITEL = { eingang: 'Posteingang', gesendet: 'Gesendete Mitteilungen', entwuerfe: 'Entwürfe', listen: 'Listen', einstellungen: 'Einstellungen Mitteilungen' };

// ------------------------------------------------------------------ Markdown (sicher: erst escapen, dann formatieren)

/** Markdown-Text als HTML: Überschriften, **fett**, *kursiv*, ~~durchgestrichen~~, [Links](…), automatisch erkannte Links, Listen, Zitate („> “). */
export function markdown(text) {
  const token = [];
  const merke = html => `\u0000${token.push(html) - 1}\u0000`;
  const link = (url, inhalt) => {
    const u = url.replace(/&amp;/g, '&');
    if (/^https?:\/\//i.test(u)) return merke(`<a href="${e(u)}" data-extern target="_blank" rel="noopener noreferrer">${inhalt}</a>`);
    if (u.startsWith('/') && !u.startsWith('//')) return merke(`<a href="#${e(u)}">${inhalt}</a>`);
    if (u.startsWith('#/')) return merke(`<a href="${e(u)}">${inhalt}</a>`);
    return inhalt;
  };
  const inline = s => {
    s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, t, u) => link(u, t));
    s = s.replace(/\bhttps?:\/\/[^\s<]+[^\s<.,;:!?)]/gi, u => link(u, u));
    s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>').replace(/__(.+?)__/g, '<strong>$1</strong>');
    s = s.replace(/(^|[^*])\*(?!\s)(.+?)\*/g, '$1<em>$2</em>').replace(/(^|\W)_(?!\s)(.+?)_(?=\W|$)/g, '$1<em>$2</em>');
    s = s.replace(/~~(.+?)~~/g, '<del>$1</del>');
    return s.replace(/\u0000(\d+)\u0000/g, (_, i) => token[Number(i)]);
  };
  const zeilen = e(text).split('\n');
  const aus = [];
  let liste = null;
  const listeZu = () => { if (liste) { aus.push(`<${liste.art}>${liste.punkte.map(p => `<li>${p}</li>`).join('')}</${liste.art}>`); liste = null; } };
  for (const z of zeilen) {
    let m;
    if ((m = z.match(/^(#{1,6})\s+(.*)$/))) { listeZu(); aus.push(`<div class="mt-h mt-h${m[1].length}">${inline(m[2])}</div>`); continue; }
    if ((m = z.match(/^\s*(?:[-*•])\s+(.*)$/)) || (m = z.match(/^\s*\d+[.)]\s+(.*)$/))) {
      const art = /^\s*\d/.test(z) ? 'ol' : 'ul';
      if (liste && liste.art !== art) listeZu();
      liste ??= { art, punkte: [] };
      liste.punkte.push(inline(m[1]));
      continue;
    }
    listeZu();
    if ((m = z.match(/^&gt;\s?(.*)$/))) { aus.push(`<div class="mt-zitat">${inline(m[1]) || '&nbsp;'}</div>`); continue; }
    aus.push(z.trim() ? `<div>${inline(z)}</div>` : '<div class="mt-leer"></div>');
  }
  listeZu();
  return aus.join('');
}

/** Externe Links mit Warnung öffnen, interne direkt. */
function linksBinden(wurzel) {
  wurzel.querySelectorAll('a[data-extern]').forEach(a => a.addEventListener('click', async ev => {
    ev.preventDefault();
    let host = a.href;
    try { host = new URL(a.href).host; } catch { /* ungültig */ }
    if (await bestaetigen('Externer Link', `Sie verlassen UntPlanWeb und öffnen eine externe Seite (${host}). Bitte prüfen Sie, ob Sie der Zielseite vertrauen:\n${a.href}`, 'Link öffnen'))
      window.open(a.href, '_blank', 'noopener,noreferrer');
  }));
}

// ------------------------------------------------------------------ Ansicht

export async function zeigen(seite, kontext, [ordnerParam, idParam]) {
  stileLaden();
  if (ordnerParam === 'listen') return listen(seite, kontext);
  if (ordnerParam === 'einstellungen') return einstellungen(seite, kontext);
  const ordner = ['gesendet', 'entwuerfe'].includes(ordnerParam) ? ordnerParam : 'eingang';
  // „Verfassen“ (Untermenü): Posteingang zeigen und gleich das Schreibfenster öffnen
  const verfassen = ordnerParam === 'verfassen';
  const [liste, optionen] = await Promise.all([holen(`${BASIS}?ordner=${ordner}`), holen(`${BASIS}/optionen`)]);
  kontext.titel(TITEL[ordner], optionen.darfSenden ? `<button class="btn btn-primary mt-neu" data-neu>${icon('plus')}Neu</button>` : '');
  document.querySelector('[data-neu]')?.addEventListener('click', () => schreiben(kontext));
  if (verfassen && optionen.darfSenden) {
    // nach dem Schließen zurück auf den Posteingang, damit „Verfassen“ erneut auswählbar ist
    schreiben(kontext).then(() => document.querySelector('.mt-schreibdialog')?.addEventListener('hidden.bs.modal', () => {
      if (location.hash === '#/mitteilungen/verfassen') kontext.navigieren('#/mitteilungen', true);
    }, { once: true })).catch(kontext.fehler ?? (() => {}));
  }

  const eingang = ordner === 'eingang';
  const offenBestaetigen = m => eingang && m.lesebestaetigung && !m.bestaetigt;
  // Kopf der Zeile: Absender (Posteingang) bzw. Empfänger (Gesendet, Entwürfe)
  const kopfName = m => eingang ? m.absender : ordner === 'entwuerfe' ? (m.empfaengerText || 'Entwurf') : (m.empfaengerText || m.empfaenger.join(', '));
  const blase = m => ordner === 'gesendet' && m.lesebestaetigung
    ? `<span class="mt-blase" role="button" tabindex="0" data-blase="${m.id}" title="Lesebestätigungen anzeigen" aria-label="${m.bestaetigtAnzahl} von ${m.anzahl} Lesebestätigungen">${m.bestaetigtAnzahl} <i></i> ${m.anzahl}</span>` : '';
  const vorschau = m => m.text.replace(/[#*_~>`]/g, '').replace(/\s+/g, ' ').trim().slice(0, 240);
  // Wie im Original eine Zeile je Mitteilung: Avatar (Initiale) | Absender | fetter Betreff | Vorschautext; rechts dezent Anhang, Zeit
  const eintrag = (m, i) => `
    <button type="button" class="mt-zeile ${eingang && !m.gelesen ? 'neu' : ''}" data-i="${i}" data-merkmale="${[eingang && !m.gelesen ? 'ungelesen' : '', m.wichtig ? 'wichtig' : '', m.anhaenge?.length ? 'anhang' : '', offenBestaetigen(m) ? 'bestaetigen' : ''].join(' ')}"
      data-text="${e((m.betreff + ' ' + m.absender + ' ' + m.text + ' ' + m.empfaenger.join(' ') + ' ' + (m.empfaengerText || '') + ' ' + (m.anhaenge || []).map(a => a.name).join(' ')).toLowerCase())}">
      <span class="mt-ava" aria-hidden="true">${e(kuerzel(kopfName(m) || 'E').slice(0, 1))}</span>
      <span class="mt-wer" title="${e(kopfName(m))}">${e(kopfName(m))}</span>
      ${blase(m)}
      <span class="mt-was">${m.wichtig ? '<span class="mt-wichtig">wichtig</span>' : ''}${m.antwortAuf ? '<i class="bi bi-reply mt-antwort-symbol" aria-hidden="true"></i>' : ''}<b>${e(m.betreff || '(ohne Betreff)')}</b><span class="mt-sep">|</span>${e(vorschau(m))}</span>
      <span class="mt-wann">${offenBestaetigen(m) ? '<i class="bi bi-envelope-exclamation mt-lb-offen" title="Lesebestätigung erforderlich" aria-label="Lesebestätigung erforderlich"></i>' : ''}${m.anhaenge?.length ? '<i class="bi bi-paperclip" aria-label="mit Anhang"></i>' : ''}${e(zeit(m.erstellt))}</span>
    </button>`;
  // Mitteilungen mit offener Lesebestätigung zuerst
  const reihe = liste.map((m, i) => [m, i]).sort((a, b) => offenBestaetigen(b[0]) - offenBestaetigen(a[0]));
  seite.innerHTML = `
    <div class="mt-seite">
      <div class="u-sf-leiste">${suchfeldMitFilter({ platzhalter: 'Inhalt oder Person suchen', label: 'Inhalt oder Person suchen', klasse: 'voll', filter: [
        { name: 'merkmal', label: 'Anzeigen', alle: 'Alle', optionen: [...(eingang ? [['ungelesen', 'Ungelesen'], ['bestaetigen', 'Lesebestätigung offen']] : []), ['wichtig', 'Wichtig'], ['anhang', 'Mit Anhang']] },
      ] })}</div>
      <div class="mt-liste" data-liste>${liste.length ? reihe.map(([m, i]) => eintrag(m, i)).join('')
        : leer(ordner === 'gesendet' ? 'Noch keine Mitteilungen gesendet.' : ordner === 'entwuerfe' ? 'Keine Entwürfe.' : 'Keine Mitteilungen.', ordner === 'gesendet' ? 'send' : 'inbox')}
        <div class="mt-keine d-none" data-keine>${leer('Keine Treffer.', 'search')}</div></div>
    </div>`;
  const listeEl = seite.querySelector('[data-liste]');
  listeEl.classList.toggle('leer', !liste.length);
  // gemeinsames Suchfeld mit Filter: Freitext und Dropdown (Ungelesen, Wichtig, Mit Anhang …) im Suchfeld
  let suchtext = '', merkmal = '';
  const filtern = () => {
    let n = 0;
    seite.querySelectorAll('[data-i]').forEach(b => {
      const ja = (!suchtext || b.dataset.text.includes(suchtext)) && (!merkmal || b.dataset.merkmale.split(' ').includes(merkmal));
      b.classList.toggle('d-none', !ja); n += ja;
    });
    seite.querySelector('[data-keine]').classList.toggle('d-none', !(suchtext || merkmal) || n > 0 || !liste.length);
  };
  suchfeldMitFilterBinden(seite, { beiSuche: t => { suchtext = t.toLowerCase(); filtern(); }, beiFilter: (_, w) => { merkmal = w; filtern(); } });
  seite.querySelectorAll('[data-blase]').forEach(b => {
    const auf = ev => { ev.stopPropagation(); lesebestaetigungen(liste.find(m => String(m.id) === b.dataset.blase)); };
    b.addEventListener('click', auf);
    b.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') auf(ev); });
  });

  const neuLaden = ziel => kontext.navigieren(ziel ?? location.hash);
  const oeffnen = i => {
    const m = liste[i];
    if (ordner === 'entwuerfe') { schreiben(kontext, { entwurf: m }); return; }
    lesenDialog(m, i);
  };

  /** Mitteilung lesen im großen Fenster wie im Original: Titel, „Von:“ | Datum, Text, Anhänge; unten Drucken und die Aktionen. */
  function lesenDialog(m, i) {
    const kannAntworten = eingang && optionen.darfSenden;
    const viele = eingang && m.empfaenger.length > 1;
    const knopf = (daten, symbol, titel, art = '') => `<button type="button" class="mt-aktion ${art}" ${daten} title="${titel}" aria-label="${titel}"><i class="bi bi-${symbol}"></i></button>`;
    const an = m.empfaenger.length > 6 ? `${m.empfaenger.slice(0, 6).join(', ')} und ${m.empfaenger.length - 6} weitere` : m.empfaenger.join(', ');
    const box = dialog({
      titel: m.betreff || '(ohne Betreff)',
      inhalt: `<div class="mt-meta">
          <span class="mt-von">${eingang ? 'Von:' : 'An:'}</span>
          <div class="mt-personen">${eingang ? `<b>${e(m.absender)}</b>${m.absenderRolle ? `<span class="mt-rolle">${e(m.absenderRolle)}</span>` : ''}${m.empfaenger.length ? `<div class="mt-an">an ${e(an)}</div>` : ''}`
            : `<b>${e(m.empfaengerText || m.empfaenger.join(', '))}</b><div class="mt-an">${m.anzahl} Empfänger*innen · ${m.gelesenAnzahl} gelesen</div>`}</div>
          <span class="mt-datum">${e(zeitLang(m.erstellt))}</span></div>
        <div class="mt-lesetext">
          <div class="mt-text" data-text>${markdown(m.text)}</div>
          ${m.anhaenge?.length ? `<div class="mt-anhaenge">${m.anhaenge.map(a => `<a class="mt-anhang" href="${BASIS}/anhang/${a.id}" download="${e(a.name)}"><i class="bi bi-file-earmark"></i>${e(a.name)} <span class="mt-groesse">${groesse(a.groesse)}</span></a>`).join('')}</div>` : ''}
        </div>`,
    });
    const modal = box.closest('.modal');
    modal.classList.add('mt-dialog', 'mt-lesedialog');
    if (m.wichtig) box.querySelector('.modal-title').insertAdjacentHTML('afterbegin', '<span class="mt-wichtig gross">wichtig</span>');
    const rechts = eingang && m.lesebestaetigung && !m.bestaetigt
      ? '<button type="button" class="btn btn-primary mt-pille" data-bestaetigen>Lesebestätigung senden</button>'
      : m.bestaetigt ? `<span class="mt-gelesen"><i class="bi bi-check2-circle"></i>Lesebestätigung gesendet am ${e(zeitLang(m.bestaetigt))}</span>` : '';
    box.insertAdjacentHTML('beforeend', `<div class="modal-footer mt-fuss nicht-drucken">
        ${knopf('data-drucken', 'printer', 'Drucken')}
        ${kannAntworten ? knopf('data-antworten', 'reply', 'Antworten') : ''}
        ${kannAntworten && viele ? knopf('data-alle', 'reply-all', 'Allen antworten') : ''}
        ${optionen.darfSenden ? knopf('data-weiterleiten', 'forward', 'Weiterleiten') : ''}
        ${!eingang && m.lesebestaetigung ? `<button type="button" class="mt-blase gross" data-liste title="Lesebestätigungen anzeigen">${m.bestaetigtAnzahl} <i></i> ${m.anzahl}</button>` : ''}
        ${eingang ? knopf('data-ungelesen', 'envelope', 'Als ungelesen markieren') : ''}
        ${knopf('data-loeschen', 'trash', 'Löschen', 'gefahr')}
        <span class="mt-fuss-rechts">${rechts}</span></div>`);
    linksBinden(box);
    const zu = () => box.schliessen();
    const antworten = async alle => {
      try { const v = await holen(`${BASIS}/${m.id}/antwort${alle ? '?alle=true' : ''}`); zu(); schreiben(kontext, { vorgabe: v }); }
      catch (err) { toast(err.message, true); }
    };
    box.querySelector('[data-antworten]')?.addEventListener('click', () => antworten(false));
    box.querySelector('[data-alle]')?.addEventListener('click', () => antworten(true));
    box.querySelector('[data-weiterleiten]')?.addEventListener('click', async () => {
      try { const w = await senden(`${BASIS}/${m.id}/weiterleiten`, {}); zu(); schreiben(kontext, { entwurf: w, titel: 'Weiterleiten' }); }
      catch (err) { toast(err.message, true); }
    });
    box.querySelector('[data-liste]')?.addEventListener('click', () => lesebestaetigungen(m));
    box.querySelector('[data-ungelesen]')?.addEventListener('click', async () => {
      await senden(`${BASIS}/${m.id}/ungelesen`, {});
      await kontext.sitzungNeu();
      toast('Als ungelesen markiert.');
      zu();
      neuLaden('#/mitteilungen');
    });
    box.querySelector('[data-bestaetigen]')?.addEventListener('click', async ev => {
      const b = ev.currentTarget;
      b.disabled = true;
      try {
        const r = await senden(`${BASIS}/${m.id}/bestaetigen`, {});
        m.bestaetigt = r.bestaetigt;
        toast('Lesebestätigung gesendet.');
        b.closest('.mt-fuss-rechts').innerHTML = `<span class="mt-gelesen"><i class="bi bi-check2-circle"></i>Lesebestätigung gesendet am ${e(zeitLang(m.bestaetigt))}</span>`;
        seite.querySelector(`[data-i="${i}"] .mt-lb-offen`)?.remove();
      } catch (err) { toast(err.message, true); b.disabled = false; }
    });
    box.querySelector('[data-drucken]').onclick = () => {
      document.body.classList.add('mt-druck');
      window.print();
      setTimeout(() => document.body.classList.remove('mt-druck'), 500);
    };
    box.querySelector('[data-loeschen]').onclick = async () => {
      if (!await bestaetigen('Mitteilung löschen', `„${m.betreff}“ wirklich löschen?`)) return;
      await api(`${BASIS}/${m.id}`, { methode: 'DELETE' });
      toast('Mitteilung gelöscht.');
      zu();
      neuLaden(ordner === 'gesendet' ? '#/mitteilungen/gesendet' : '#/mitteilungen');
    };
    if (eingang && !m.gelesen) {
      senden(`${BASIS}/${m.id}/gelesen`, {}).then(async () => {
        m.gelesen = new Date().toISOString();
        seite.querySelectorAll(`[data-i="${i}"]`).forEach(x => x.classList.remove('neu'));
        await kontext.sitzungNeu();
      }).catch(() => { /* nicht kritisch */ });
    }
  }
  seite.querySelectorAll('[data-i]').forEach(b => b.onclick = () => oeffnen(Number(b.dataset.i)));
  const gewuenscht = liste.findIndex(m => String(m.id) === idParam);
  if (gewuenscht >= 0) oeffnen(gewuenscht);
}

/** Lesebestätigungsliste: wer wann bestätigt bzw. gelesen hat. */
async function lesebestaetigungen(m) {
  if (!m) return;
  const r = await holen(`${BASIS}/${m.id}/lesebestaetigungen`);
  dialog({
    titel: `Lesebestätigungen ${r.bestaetigt} | ${r.anzahl}`,
    inhalt: `<div class="mt-lb-betreff">Betreff: ${e(m.betreff)}</div>
      <ul class="mt-lb">${r.empfaenger.map(x => `<li class="${x.bestaetigt ? 'ja' : ''}">
        <span class="mt-lb-symbol">${x.bestaetigt ? '<i class="bi bi-check-circle"></i>' : x.gelesen ? '<i class="bi bi-eye" title="gelesen, nicht bestätigt"></i>' : ''}</span>
        <span class="mt-lb-name">${e(x.name)} <small>${e(x.rolle)}</small></span>
        <span class="mt-lb-zeit">${x.bestaetigt ? e(zeitLang(x.bestaetigt)) : x.gelesen ? `gelesen ${e(zeitLang(x.gelesen))}` : '–'}</span></li>`).join('')}</ul>`,
    knoepfe: [{ text: 'Schließen' }],
  });
}

// ------------------------------------------------------------------ Empfängerauswahl

const KATEGORIEN = [['lehrkraefte', 'Lehrkräfte'], ['klassen', 'Klassen'], ['schueler', 'Schüler*innen'], ['eltern', 'Erziehungsberechtigte'], ['gruppen', 'Gruppen'], ['listen', 'Listen']];

/**
 * Empfängerauswahl (in einem Bereich des Dialogs): Reiter je Kategorie, Suche, Klassenfilter, alle auswählen; gewaehlt: Map id → Name.
 * fertig(Map) übernimmt, abbrechen() schließt ohne Änderung.
 */
function auswahl(bereich, empfaenger, gewaehlt, fertig, abbrechen) {
  const kategorien = KATEGORIEN.filter(([k]) => empfaenger.some(x => x.kategorie === k));
  let reiter = kategorien[0]?.[0];
  const neu = new Map(gewaehlt);
  const zeichnen = () => {
    const inKat = empfaenger.filter(x => x.kategorie === reiter);
    const klassen = [...new Set(inKat.map(x => x.klasse).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de', { numeric: true }));
    bereich.innerHTML = `
      <div class="mt-wahl-kopf"><b>Empfänger*innen</b><span class="text-body-secondary small ms-2" data-anzahl>${neu.size} ausgewählt</span></div>
      <div class="mt-reiter" role="tablist">${kategorien.map(([k, t]) => `<button type="button" role="tab" class="${k === reiter ? 'aktiv' : ''}" data-reiter="${k}">${t}</button>`).join('')}</div>
      <div class="mt-wahl-filter"><div class="mt-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" class="form-control form-control-sm" data-wsuche placeholder="Suchen" aria-label="Empfänger suchen"></div>
        ${klassen.length > 1 ? `<select class="form-select form-select-sm" data-wklasse aria-label="Klasse"><option value="">Alle Klassen</option>${klassen.map(k => `<option>${e(k)}</option>`).join('')}</select>` : ''}</div>
      <label class="mt-wahl-alle"><input type="checkbox" class="form-check-input" data-walle> Alle auswählen</label>
      <div class="mt-wahl-liste">${inKat.map(x => `<label class="mt-wahl-zeile" data-name="${e(x.name.toLowerCase())}" data-klasse="${e(x.klasse || '')}">
          <input type="checkbox" class="form-check-input" value="${e(x.id)}" ${neu.has(x.id) ? 'checked' : ''}>
          <span class="u-avatar">${e(kuerzel(x.name))}</span><span class="mt-wahl-name">${e(x.name)}</span>${x.art && !['gruppe', 'klasse', 'liste'].includes(x.art) ? `<span class="mt-wahl-art">${e(x.art)}</span>` : ''}</label>`).join('') || leer('Keine Empfänger in dieser Gruppe.', 'people')}</div>
      <div class="mt-wahl-fuss"><button type="button" class="btn btn-outline-secondary" data-wabbrechen>Abbrechen</button><button type="button" class="btn btn-primary" data-wuebernehmen>Übernehmen</button></div>`;
    const sichtbar = () => [...bereich.querySelectorAll('.mt-wahl-zeile:not(.d-none) input')];
    const filtern = () => {
      const f = bereich.querySelector('[data-wsuche]').value.trim().toLowerCase();
      const k = bereich.querySelector('[data-wklasse]')?.value ?? '';
      bereich.querySelectorAll('.mt-wahl-zeile').forEach(z => z.classList.toggle('d-none', (!!f && !z.dataset.name.includes(f)) || (!!k && z.dataset.klasse !== k)));
      const s = sichtbar();
      bereich.querySelector('[data-walle]').checked = s.length > 0 && s.every(x => x.checked);
    };
    const zaehlen = () => { bereich.querySelector('[data-anzahl]').textContent = `${neu.size} ausgewählt`; filtern(); };
    bereich.querySelectorAll('[data-reiter]').forEach(b => b.onclick = () => { reiter = b.dataset.reiter; zeichnen(); });
    bereich.querySelector('[data-wsuche]').oninput = filtern;
    bereich.querySelector('[data-wklasse]')?.addEventListener('change', filtern);
    bereich.querySelectorAll('.mt-wahl-zeile input').forEach(c => c.onchange = () => {
      const x = empfaenger.find(y => y.id === c.value);
      if (c.checked) neu.set(x.id, x.name); else neu.delete(x.id);
      zaehlen();
    });
    bereich.querySelector('[data-walle]').onchange = ev => {
      sichtbar().forEach(c => { c.checked = ev.target.checked; const x = empfaenger.find(y => y.id === c.value); if (c.checked) neu.set(x.id, x.name); else neu.delete(x.id); });
      zaehlen();
    };
    bereich.querySelector('[data-wabbrechen]').onclick = abbrechen;
    bereich.querySelector('[data-wuebernehmen]').onclick = () => fertig(neu);
    filtern();
    bereich.querySelector('[data-wsuche]').focus();
  };
  zeichnen();
}

// ------------------------------------------------------------------ Empfänger*innen (Personensuche wie im Original)

/** Rollen-Pills, die dunkel dargestellt werden (Grundrolle); weitere Pills (Klassenlehrkraft | Klasse) heller. */
const PILL_DUNKEL = new Set(['lehrkraft', 'schulleitung', 'administration', 'schueler', 'eltern']);
/** Filter-Pills mit Ausklappmenü: Schlüssel, Beschriftung, Optionen aus der Antwort von /personen, Prüfung je Person. */
const EW_FILTER = [
  ['liste', 'Liste (indiv.)', d => d.filter.listen.map(l => ({ id: l.id, name: l.name })), (p, w, d) => (d.filter.listen.find(l => l.id === w)?.mitglieder ?? []).includes(p.id)],
  ['klasse', 'Klasse', d => d.filter.klassen.map(k => ({ id: k, name: k })), (p, w) => p.klassen.includes(w)],
  ['abteilung', 'Abteilung', d => d.filter.abteilungen.map(a => ({ id: a, name: a })), (p, w) => p.abteilungen.includes(w)],
  ['rolle', 'Rolle', d => d.filter.rollen ?? [], (p, w) => p.rollen.some(r => r.art === w)],
  ['gruppe', 'Gruppe', d => d.filter.gruppen ?? [], (p, w) => (p.gruppen ?? []).includes(w)],
];
/** Anzeigename einer Person für die Chips im Feld „An“ (Kürzel in Klammern wird dort ausgeblendet). */
const ewChipName = p => p.kuerzel && p.anzeigename && p.anzeigename !== p.kuerzel ? `${p.anzeigename} (${p.kuerzel})` : (p.anzeigename || p.name);

/**
 * Dialog „Empfänger*innen“: großes Suchfeld (Kürzel, Name, Anmeldename), Filter-Pills Ausgewählt, Liste (indiv.), Klasse,
 * Abteilung, Rolle, Gruppe; „Alle auswählen“; je Person runder Auswahlkreis, Avatar mit Initiale, Kürzel und Rollen-Pills.
 * Gruppen/Klassen als Empfänger (Kollegium) stehen unter den Personen. gewaehlt: Map id → Name; fertig(Map) beim Übernehmen.
 * Tastatur: ↓/↑ zwischen Suchfeld und Zeilen, Leertaste/Enter wählt, Esc schließt (Menü bzw. Dialog).
 */
async function empfaengerDialog(kollegium, gewaehlt, fertig) {
  const d = await holen(`${BASIS}/personen?typ=${kollegium ? 'individuell' : 'kollegium'}`);
  const neu = new Map(gewaehlt);
  const filter = { ausgewaehlt: false };
  const menge = d.personen;
  const gruppen = (d.gruppen ?? []).filter(g => g.art !== 'liste');
  const suchtext = p => `${p.name} ${p.anzeigename} ${p.login} ${p.kuerzel}`.toLowerCase();
  const filterAktiv = () => EW_FILTER.some(([k]) => filter[k]);
  const passt = (p, f) => (!f || suchtext(p).includes(f)) && (!filter.ausgewaehlt || neu.has(p.id))
    && EW_FILTER.every(([k, , , pruef]) => !filter[k] || pruef(p, filter[k], d));
  // Gruppen und Klassen als Empfänger: nur ohne Personenfilter (Klassenfilter grenzt auf die Klasse ein)
  const gruppePasst = (g, f) => !(filter.liste || filter.abteilung || filter.rolle || filter.gruppe) && (!filter.klasse || g.klasse === filter.klasse)
    && (!filter.ausgewaehlt || neu.has(g.id)) && (!f || g.name.toLowerCase().includes(f));
  const zeile = (id, avatar, titel, pills, langname) => `<div class="mt-ew-zeile" role="checkbox" tabindex="-1" aria-checked="${neu.has(id)}" data-id="${e(id)}"${langname ? ` title="${e(langname)}"` : ''}>
      <span class="mt-ew-kreis" aria-hidden="true"></span><span class="mt-ew-avatar" aria-hidden="true">${avatar}</span>
      <span class="mt-ew-text"><span class="mt-ew-name">${e(titel)}</span>${pills ? `<span class="mt-ew-pills">${pills}</span>` : ''}</span></div>`;
  const personZeile = p => zeile(p.id, e(p.initialen || kuerzel(p.name)), p.name,
    p.rollen.map(r => `<span class="mt-ew-pill ${PILL_DUNKEL.has(r.art) ? 'dunkel' : ''}">${e(r.text)}</span>`).join(''), p.anzeigename !== p.name ? p.anzeigename : '');
  const gruppeZeile = g => zeile(g.id, '<i class="bi bi-people"></i>', g.name, `<span class="mt-ew-pill">${g.art === 'klasse' ? 'Klasse' : 'Gruppe'}</span>`);

  const box = dialog({
    titel: 'Empfänger*innen', breit: true,
    inhalt: `<div class="mt-ew">
        <div class="mt-ew-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-ew-suche placeholder="${kollegium ? 'Kolleg*innen suchen' : 'Lehrkräfte suchen'}" aria-label="${kollegium ? 'Kolleg*innen suchen' : 'Lehrkräfte suchen'}" autocomplete="off"></div>
        <div class="mt-ew-filter" data-ew-filter></div>
        <div class="mt-ew-liste" data-ew-liste role="group" aria-label="Empfänger*innen"></div>
      </div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Übernehmen', art: 'haupt', aktion: () => fertig(neu) }],
  });
  box.closest('.modal')?.classList.add('mt-dialog', 'mt-ew-dialog');
  const suche = box.querySelector('[data-ew-suche]');
  const filterLeiste = box.querySelector('[data-ew-filter]');
  const liste = box.querySelector('[data-ew-liste]');
  let menue = null;

  const pillsZeichnen = () => {
    filterLeiste.innerHTML = `<button type="button" class="mt-ew-fpill erste ${filter.ausgewaehlt ? 'an' : ''}" data-ew-aus aria-pressed="${filter.ausgewaehlt}">Ausgewählt${neu.size ? ` (${neu.size})` : ''}</button>`
      + EW_FILTER.map(([k, titel, optionen]) => {
        // Kollegium: alle Filter wie im Original sichtbar (leere mit Hinweis im Menü); Familie nur Filter mit Einträgen
        if (!optionen(d).length && (!kollegium || (k === 'liste' && !d.filter.listen))) return '';
        const wert = filter[k] ? optionen(d).find(o => o.id === filter[k])?.name ?? filter[k] : '';
        return `<button type="button" class="mt-ew-fpill ${wert ? 'an' : ''}" data-ew-menue="${k}" aria-haspopup="listbox" aria-expanded="false">${e(wert ? `${titel.replace(/ \(.*\)/, '')}: ${wert}` : titel)}<i class="bi bi-chevron-down" aria-hidden="true"></i></button>`;
      }).join('');
    filterLeiste.querySelector('[data-ew-aus]').onclick = () => { filter.ausgewaehlt = !filter.ausgewaehlt; pillsZeichnen(); listeZeichnen(); };
    filterLeiste.querySelectorAll('[data-ew-menue]').forEach(b => b.onclick = () => menueOeffnen(b));
  };
  const menueZu = (fokus = false) => {
    if (!menue) return;
    const knopf = menue.knopf;
    menue.remove(); menue = null;
    knopf.setAttribute('aria-expanded', 'false');
    if (fokus) knopf.focus();
  };
  const menueOeffnen = knopf => {
    const k = knopf.dataset.ewMenue;
    if (menue?.knopf === knopf) { menueZu(); return; }
    menueZu();
    const [, titel, optionen] = EW_FILTER.find(f => f[0] === k);
    const opts = [{ id: '', name: `Alle (${titel.replace(/ \(.*\)/, '')})` }, ...optionen(d)];
    const leerText = { liste: 'Keine eigenen Listen angelegt', abteilung: 'Keine Abteilungen hinterlegt' }[k] ?? 'Keine Einträge';
    menue = document.createElement('div');
    menue.className = 'mt-ew-menue';
    menue.setAttribute('role', 'listbox');
    menue.innerHTML = (opts.length > 12 ? '<input type="search" class="mt-ew-menue-suche" placeholder="Suchen" aria-label="Optionen durchsuchen">' : '')
      + `<div class="mt-ew-menue-liste">${opts.map(o => `<button type="button" role="option" aria-selected="${(filter[k] ?? '') === o.id}" data-wert="${e(o.id)}" data-text="${e(o.name.toLowerCase())}">${e(o.name)}${(filter[k] ?? '') === o.id ? '<i class="bi bi-check2" aria-hidden="true"></i>' : ''}</button>`).join('')}</div>`
      + (opts.length === 1 ? `<div class="mt-ew-menue-leer">${leerText}</div>` : '');
    menue.knopf = knopf;
    knopf.setAttribute('aria-expanded', 'true');
    // Menü im Dialoginhalt (nicht in der Pill-Leiste, die mobil waagerecht scrollt) unter der Pill ausrichten
    const wurzel = box.querySelector('.mt-ew');
    wurzel.append(menue);
    const r = knopf.getBoundingClientRect(), w = wurzel.getBoundingClientRect();
    menue.style.left = `${Math.max(8, Math.min(r.left - w.left, w.width - menue.offsetWidth - 8))}px`;
    menue.style.top = `${r.bottom - w.top + 4}px`;
    const knoepfe = () => [...menue.querySelectorAll('[role=option]:not([hidden])')];
    menue.querySelectorAll('[role=option]').forEach(o => o.onclick = () => {
      filter[k] = o.dataset.wert || undefined;
      menueZu(); pillsZeichnen(); listeZeichnen();
      filterLeiste.querySelector(`[data-ew-menue="${k}"]`)?.focus();
    });
    const ms = menue.querySelector('.mt-ew-menue-suche');
    if (ms) ms.oninput = () => { const f = ms.value.trim().toLowerCase(); menue.querySelectorAll('[role=option]').forEach(o => { o.hidden = !!f && !o.dataset.text.includes(f); }); };
    menue.addEventListener('keydown', ev => {
      const l = knoepfe(), i = l.indexOf(document.activeElement);
      if (ev.key === 'ArrowDown') { ev.preventDefault(); (l[i + 1] ?? l[0])?.focus(); }
      else if (ev.key === 'ArrowUp') { ev.preventDefault(); (i <= 0 ? (ms ?? l[l.length - 1]) : l[i - 1])?.focus(); }
      else if (ev.key === 'Escape') { ev.preventDefault(); ev.stopPropagation(); menueZu(true); }
      else if (ev.key === 'Tab') menueZu();
    });
    (ms ?? menue.querySelector('[aria-selected=true]') ?? knoepfe()[0])?.focus();
  };

  const zeilen = () => [...liste.querySelectorAll('.mt-ew-zeile')];
  const listeZeichnen = () => {
    const f = suche.value.trim().toLowerCase();
    const personen = menge.filter(p => passt(p, f));
    const grp = gruppen.filter(g => gruppePasst(g, f));
    const alle = personen.length > 0 && personen.every(p => neu.has(p.id));
    liste.innerHTML = (personen.length ? `<div class="mt-ew-zeile mt-ew-alle" role="checkbox" tabindex="-1" aria-checked="${alle}" data-alle><span class="mt-ew-kreis" aria-hidden="true"></span><span class="mt-ew-name">Alle auswählen</span></div>` : '')
      + personen.map(personZeile).join('')
      + (grp.length ? `<div class="mt-ew-abschnitt">Gruppen und Klassen</div>${grp.map(gruppeZeile).join('')}` : '')
      || `<div class="mt-ew-leer">${f || filterAktiv() || filter.ausgewaehlt ? 'Keine Treffer.' : 'Keine Empfänger*innen verfügbar.'}</div>`;
  };
  const umschalten = z => {
    if (!z) return;
    if (z.hasAttribute('data-alle')) {
      const f = suche.value.trim().toLowerCase();
      const sichtbar = menge.filter(p => passt(p, f));
      const an = z.getAttribute('aria-checked') !== 'true';
      sichtbar.forEach(p => { if (an) neu.set(p.id, ewChipName(p)); else neu.delete(p.id); });
    } else {
      const id = z.dataset.id;
      if (neu.has(id)) neu.delete(id);
      else { const p = menge.find(x => x.id === id); const g = gruppen.find(x => x.id === id); neu.set(id, p ? ewChipName(p) : g?.name ?? id); }
    }
    // Zustand ohne Neuaufbau aktualisieren (Fokus und Bildlauf bleiben stehen)
    zeilen().forEach(x => { if (x.dataset.id) x.setAttribute('aria-checked', String(neu.has(x.dataset.id))); });
    const alleZ = liste.querySelector('[data-alle]');
    if (alleZ) { const ps = zeilen().filter(x => x.dataset.id && menge.some(p => p.id === x.dataset.id)); alleZ.setAttribute('aria-checked', String(ps.length > 0 && ps.every(x => neu.has(x.dataset.id)))); }
    const aus = filterLeiste.querySelector('[data-ew-aus]');
    if (aus) aus.textContent = `Ausgewählt${neu.size ? ` (${neu.size})` : ''}`;
  };
  liste.addEventListener('click', ev => umschalten(ev.target.closest('.mt-ew-zeile')));
  liste.addEventListener('keydown', ev => {
    const l = zeilen(), i = l.indexOf(document.activeElement);
    if (ev.key === ' ' || ev.key === 'Enter') { ev.preventDefault(); umschalten(l[i]); }
    else if (ev.key === 'ArrowDown') { ev.preventDefault(); l[Math.min(i + 1, l.length - 1)]?.focus(); }
    else if (ev.key === 'ArrowUp') { ev.preventDefault(); if (i <= 0) suche.focus(); else l[i - 1].focus(); }
    else if (ev.key === 'Home') { ev.preventDefault(); l[0]?.focus(); }
    else if (ev.key === 'End') { ev.preventDefault(); l[l.length - 1]?.focus(); }
  });
  // Rovierender Fokus: nur die zuletzt fokussierte Zeile ist per Tab erreichbar
  liste.addEventListener('focusin', ev => { const z = ev.target.closest('.mt-ew-zeile'); if (z) zeilen().forEach(x => { x.tabIndex = x === z ? 0 : -1; }); });
  suche.addEventListener('input', listeZeichnen);
  suche.addEventListener('keydown', ev => {
    if (ev.key === 'ArrowDown') { ev.preventDefault(); (liste.querySelector('.mt-ew-zeile:not([data-alle])') ?? zeilen()[0])?.focus(); }
    else if (ev.key === 'Enter') { ev.preventDefault(); const erste = liste.querySelector('.mt-ew-zeile:not([data-alle])'); if (erste) { umschalten(erste); erste.focus(); } }
  });
  // Klick außerhalb schließt das Filtermenü; Esc im Menü schließt nur das Menü (nicht den Dialog)
  box.addEventListener('mousedown', ev => { if (menue && !menue.contains(ev.target) && ev.target.closest('[data-ew-menue]') !== menue.knopf) menueZu(); });
  pillsZeichnen();
  listeZeichnen();
}

// ------------------------------------------------------------------ Schreiben

/**
 * Mitteilung schreiben. vorgabe: { empfaenger: [{id, name}], betreff, text, antwortAuf } (Antworten); entwurf: gespeicherter Entwurf (auch Weiterleiten);
 * an: Empfängerkennung (z. B. „liste:3“).
 */
async function schreiben(kontext, { an, vorgabe, entwurf, titel } = {}) {
  const [empfaenger, optionen] = await Promise.all([holen(`${BASIS}/empfaenger`), holen(`${BASIS}/optionen`)]);
  // ohne Senderecht (Schüler*innen/Erziehungsberechtigte laut Einstellungen) nur Antworten an den Absender
  const nurAntwort = !optionen.darfSenden;
  if (nurAntwort && !(optionen.darfAntworten && (vorgabe?.antwortAuf || entwurf?.antwortAuf))) { toast('Ihr Konto darf keine Mitteilungen senden.', true); return; }
  if (!empfaenger.length && !vorgabe?.empfaenger?.length && !entwurf?.antwortAuf) { toast('Für Ihr Konto sind keine Empfänger verfügbar.', true); return; }
  const namen = new Map(empfaenger.map(x => [x.id, x.name]));
  const gewaehlt = new Map();
  for (const x of vorgabe?.empfaenger ?? []) gewaehlt.set(x.id, x.name);
  for (const id of entwurf?.entwurfEmpfaenger ?? (an ? [an] : [])) gewaehlt.set(id, namen.get(id) ?? 'Empfänger*in');
  const vorhandeneAnhaenge = [...(entwurf?.anhaenge ?? [])];
  const neueDateien = [];
  let entwurfId = entwurf?.id ?? null;
  const antwortAuf = vorgabe?.antwortAuf ?? entwurf?.antwortAuf ?? null;
  let lesebestaetigung = !!entwurf?.lesebestaetigung;
  let wichtig = !!entwurf?.wichtig;
  const maxBytes = optionen.maxAnhangMb * 1024 * 1024;

  const werte = box => ({
    empfaenger: [...gewaehlt.keys()], betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value,
    wichtig, lesebestaetigung, antwortAuf,
  });
  const entwurfSpeichern = async box => {
    entwurfId = (await senden(`${BASIS}/entwurf`, { id: entwurfId, ...werte(box) })).id;
    for (const d of neueDateien.splice(0)) {
      const fd = new FormData();
      fd.append('datei', d);
      vorhandeneAnhaenge.push(await api(`${BASIS}/${entwurfId}/anhaenge`, { methode: 'POST', formular: fd }));
    }
    return entwurfId;
  };
  const nachher = ziel => { if (location.hash.startsWith('#/mitteilungen')) kontext.navigieren(ziel); };
  const box = dialog({
    titel: titel ?? (entwurf ? 'Entwurf bearbeiten' : vorgabe ? 'Antworten' : 'Neue Mitteilung'), breit: true,
    inhalt: `<div class="mt-schreiben" data-schreiben>
        <div class="mt-an-zeile"><span class="mt-an-label">An</span><div class="mt-chips" data-chips></div>
          ${nurAntwort ? '' : '<button type="button" class="mt-plus" data-plus title="Empfänger*innen auswählen" aria-label="Empfänger*innen auswählen" aria-expanded="false"><i class="bi bi-plus-lg"></i></button>'}
          <div class="mt-wahl mt-wahl-pop d-none" data-wahl></div></div>
        <div class="mt-feld-zeile"><label class="mt-an-label" for="mt-betreff">Betreff</label><input class="form-control mt-betreff" id="mt-betreff" name="betreff" maxlength="200" value="${e(entwurf?.betreff ?? vorgabe?.betreff ?? '')}" required></div>
        <div class="mt-editor">
          <textarea class="form-control" name="text" rows="14" placeholder="Text hier eingeben" aria-label="Nachricht">${e(entwurf?.text ?? vorgabe?.text ?? '')}</textarea>
          <div class="mt-text mt-vorschau d-none" data-vorschau></div>
        </div>
        <div class="mt-anhaenge" data-anhangsliste></div>
      </div>
      <div class="mt-fuss-werkzeuge d-none" data-werkzeuge>
        <button type="button" class="mt-aktion ${lesebestaetigung ? 'an' : ''}" data-lb aria-pressed="${lesebestaetigung}" title="Lesebestätigung anfordern" aria-label="Lesebestätigung anfordern"><i class="bi bi-envelope-check"></i></button>
        ${optionen.wichtig ? `<button type="button" class="mt-aktion ${wichtig ? 'an' : ''}" data-wichtig aria-pressed="${wichtig}" title="Als wichtig markieren" aria-label="Als wichtig markieren"><i class="bi bi-exclamation-circle"></i></button>` : ''}
        <label class="mt-aktion mb-0" title="Anhang hinzufügen (höchstens ${optionen.maxAnhaenge} Dateien, je ${optionen.maxAnhangMb} MB)" aria-label="Anhang hinzufügen"><i class="bi bi-paperclip"></i><input type="file" multiple hidden data-dateien></label>
        <button type="button" class="mt-aktion" data-vorschau-knopf aria-pressed="false" title="Vorschau (Markdown: **fett**, *kursiv*, ~~durchgestrichen~~, # Überschrift, - Liste, [Link](https://…))" aria-label="Vorschau"><i class="bi bi-eye"></i></button>
      </div>
      <div class="mt-wahl d-none" data-wahl></div>`,
    beimOeffnen: b => {
      const chips = () => {
        b.querySelector('[data-chips]').innerHTML = [...gewaehlt].map(([id, name]) => `<span class="u-chip-dunkel mt-chip" title="${e(name)}">${e(name.replace(/\s*\(.*?\)\s*/, ' ').replace(/\s+–.*/, '').trim())}<button type="button" data-weg="${e(id)}" aria-label="${e(name)} entfernen">${icon('x')}</button></span>`).join('')
          || '<span class="mt-an-leer">Empfänger*innen über + auswählen</span>';
        b.querySelectorAll('[data-weg]').forEach(x => x.onclick = () => { gewaehlt.delete(x.dataset.weg); chips(); });
      };
      chips();
      const wahl = b.querySelector('[data-wahl]');
      const plus = b.querySelector('[data-plus]');
      // Fußleiste wie im Original: Speichern | Lesebestätigung, Wichtig, Anhang, Vorschau … Abbrechen, Senden
      const werkzeuge = b.querySelector('[data-werkzeuge]');
      b.querySelector('.modal-footer [data-knopf="0"]')?.after(werkzeuge);
      werkzeuge.classList.remove('d-none');
      const wahlZu = () => { wahl.classList.add('d-none'); plus?.setAttribute('aria-expanded', 'false'); };
      // „+“ öffnet den Dialog „Empfänger*innen“ (Personensuche mit Filtern) über dem Schreibfenster
      if (plus) plus.onclick = async () => {
        plus.setAttribute('aria-expanded', 'true');
        try {
          await empfaengerDialog(!!optionen.listen, gewaehlt, neu => { gewaehlt.clear(); neu.forEach((v, k) => gewaehlt.set(k, v)); chips(); });
          document.querySelector('.mt-ew-dialog')?.addEventListener('hidden.bs.modal', () => { wahlZu(); plus.focus(); }, { once: true });
        } catch (err) { wahlZu(); throw err; }
      };
      b.querySelector('[data-vorschau-knopf]').onclick = ev => {
        const vorschau = ev.currentTarget.getAttribute('aria-pressed') !== 'true';
        ev.currentTarget.setAttribute('aria-pressed', String(vorschau)); ev.currentTarget.classList.toggle('an', vorschau);
        b.querySelector('[name=text]').classList.toggle('d-none', vorschau);
        const v = b.querySelector('[data-vorschau]');
        v.classList.toggle('d-none', !vorschau);
        if (vorschau) { v.innerHTML = markdown(b.querySelector('[name=text]').value) || '<span class="text-body-secondary">Kein Text.</span>'; linksBinden(v); }
      };
      const schalter = (sel, setzen) => b.querySelector(sel)?.addEventListener('click', ev => {
        const an = !ev.currentTarget.classList.contains('an');
        ev.currentTarget.classList.toggle('an', an); ev.currentTarget.setAttribute('aria-pressed', an); setzen(an);
      });
      schalter('[data-lb]', an => { lesebestaetigung = an; });
      schalter('[data-wichtig]', an => { wichtig = an; });
      const zeigeAnhaenge = () => {
        const l = b.querySelector('[data-anhangsliste]');
        l.innerHTML = vorhandeneAnhaenge.map(a => `<span class="mt-anhang"><i class="bi bi-file-earmark"></i>${e(a.name)} <span class="mt-groesse">${groesse(a.groesse)}</span><button type="button" class="btn-close" data-anhang-weg="${a.id}" aria-label="Anhang entfernen"></button></span>`).join('')
          + neueDateien.map((d, i) => `<span class="mt-anhang"><i class="bi bi-file-earmark"></i>${e(d.name)} <span class="mt-groesse">${groesse(d.size)}</span><button type="button" class="btn-close" data-datei-weg="${i}" aria-label="Anhang entfernen"></button></span>`).join('');
        l.querySelectorAll('[data-datei-weg]').forEach(x => x.onclick = () => { neueDateien.splice(Number(x.dataset.dateiWeg), 1); zeigeAnhaenge(); });
        l.querySelectorAll('[data-anhang-weg]').forEach(x => x.onclick = async () => {
          await api(`${BASIS}/anhaenge/${x.dataset.anhangWeg}`, { methode: 'DELETE' });
          vorhandeneAnhaenge.splice(vorhandeneAnhaenge.findIndex(a => String(a.id) === x.dataset.anhangWeg), 1);
          zeigeAnhaenge();
        });
      };
      zeigeAnhaenge();
      b.querySelector('[data-dateien]').onchange = ev => {
        for (const d of ev.target.files) {
          if (d.size > maxBytes) { toast(`${d.name} ist größer als ${optionen.maxAnhangMb} MB.`, true); continue; }
          if (neueDateien.length + vorhandeneAnhaenge.length >= optionen.maxAnhaenge) { toast(`Höchstens ${optionen.maxAnhaenge} Anhänge.`, true); break; }
          neueDateien.push(d);
        }
        ev.target.value = '';
        zeigeAnhaenge();
      };
    },
    knoepfe: [
      { text: 'Speichern', aktion: async b => { await entwurfSpeichern(b); toast('Entwurf gespeichert.'); nachher('#/mitteilungen/entwuerfe'); } },
      { text: 'Abbrechen' },
      {
        text: 'Senden', art: 'haupt', aktion: async b => {
          const w = werte(b);
          if (!w.empfaenger.length) { toast('Bitte mindestens einen Empfänger auswählen.', true); return false; }
          if (!w.betreff.trim()) { toast('Bitte einen Betreff eingeben.', true); return false; }
          if (entwurfId || neueDateien.length) {
            const id = await entwurfSpeichern(b);
            await senden(`${BASIS}/${id}/senden`, {});
          } else await senden(BASIS, w);
          toast('Mitteilung gesendet.');
          nachher('#/mitteilungen/gesendet');
        },
      },
    ],
  });
  box.closest('.modal')?.classList.add('mt-dialog', 'mt-schreibdialog');
  box.querySelector('[data-knopf="0"]')?.classList.add('mt-speichern');
  box.querySelector('[data-knopf="1"]')?.classList.add('ms-auto');
}

// ------------------------------------------------------------------ Listen (eigene Verteiler)

async function listen(seite, kontext) {
  kontext.titel('Listen', `<button class="btn btn-primary" data-neu>${icon('plus')}Erstellen</button>`);
  const liste = await holen(`${BASIS}/listen`);
  seite.innerHTML = `<p class="small text-body-secondary">Eigene Listen fassen Empfänger zusammen (Personen, Klassen, Gruppen). Beim Schreiben erscheinen sie im Reiter „Listen“.</p>
    <div class="u-tabelle-karte">${liste.length ? `<table class="u-tabelle"><thead><tr><th>Name</th><th>Mitglieder</th><th>erstellt</th><th></th></tr></thead><tbody>
      ${liste.map((l, i) => `<tr><td class="fw-semibold" style="width:auto">${e(l.name)}</td><td class="small">${e(l.namen.join(', '))}</td><td class="small text-body-secondary">${e(zeit(l.erstellt))}</td>
        <td class="text-end text-nowrap"><button class="btn btn-sm btn-outline-secondary" data-schreiben="${i}">${icon('senden')}Schreiben</button>
          <button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${i}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${i}" title="Löschen">${icon('loeschen')}</button></td></tr>`).join('')}</tbody></table>`
      : leer('Noch keine Listen angelegt.', 'people')}</div>`;
  const neu = () => kontext.navigieren(location.hash);
  const formular = async l => {
    const empfaenger = (await holen(`${BASIS}/empfaenger`)).filter(x => x.art !== 'liste');
    const gewaehlt = new Map((l?.mitglieder ?? []).map((id, i) => [id, l.namen[i] ?? id]));
    dialog({
      titel: l ? `Liste ${l.name}` : 'Neue Liste', breit: true,
      inhalt: `<div class="mb-3"><label class="form-label">Name</label><input class="form-control" name="name" value="${e(l?.name ?? '')}" required></div><div class="mt-wahl eingebettet" data-wahl></div>`,
      beimOeffnen: b => {
        const zeichnen = () => auswahl(b.querySelector('[data-wahl]'), empfaenger, gewaehlt, neu => { gewaehlt.clear(); neu.forEach((v, k) => gewaehlt.set(k, v)); toast(`${gewaehlt.size} Mitglieder übernommen.`); zeichnen(); }, () => b.schliessen());
        zeichnen();
      },
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async b => {
        b.querySelectorAll('.mt-wahl-zeile input').forEach(c => { const x = empfaenger.find(y => y.id === c.value); if (c.checked) gewaehlt.set(x.id, x.name); else gewaehlt.delete(x.id); });
        await senden(l ? `${BASIS}/listen/${l.id}` : `${BASIS}/listen`, { name: b.querySelector('[name=name]').value, mitglieder: [...gewaehlt.keys()] }, l ? 'PUT' : 'POST');
        toast('Liste gespeichert.'); neu();
      } }],
    });
  };
  document.querySelector('[data-neu]').onclick = () => formular(null);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(liste[Number(b.dataset.bearbeiten)]));
  seite.querySelectorAll('[data-schreiben]').forEach(b => b.onclick = () => schreiben(kontext, { an: 'liste:' + liste[Number(b.dataset.schreiben)].id }));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const l = liste[Number(b.dataset.loeschen)];
    if (!await bestaetigen('Liste löschen', `Liste „${l.name}“ löschen?`)) return;
    await api(`${BASIS}/listen/${l.id}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Einstellungen (Administration)

async function einstellungen(seite, kontext) {
  kontext.titel(TITEL.einstellungen);
  const { einstellungen: x, benutzer } = await holen(`${BASIS}/einstellungen`);
  const gesperrt = new Set(x.gesperrt ?? []);
  const schalter = (name, text, hinweis = '') => `<label class="mt-einst-zeile"><input type="checkbox" class="form-check-input" name="${name}" ${x[name] ? 'checked' : ''}>
    <span><b>${e(text)}</b>${hinweis ? `<small>${e(hinweis)}</small>` : ''}</span></label>`;
  seite.innerHTML = `<div class="mt-einst">
    <section class="u-panel"><header><h2>Wer darf an wen schreiben?</h2></header><div class="mt-einst-rumpf">
      <div class="mt-einst-gruppe">Lehrkräfte</div>
      ${schalter('lehrerAnFamilie', 'Lehrkräfte schreiben Schüler*innen und Erziehungsberechtigten', 'Einzelne Personen sowie Schüler*innen bzw. Erziehungsberechtigte einer Klasse. Schulleitung und Konten mit dem Recht „Nachrichten“ dürfen immer.')}
      ${schalter('lehrerNurEigeneKlassen', 'nur in den eigenen Klassen', 'Klassen mit eigenem Unterricht bzw. eigener Klassenleitung.')}
      <div class="mt-einst-gruppe">Schüler*innen und Erziehungsberechtigte</div>
      ${schalter('elternSenden', 'Erziehungsberechtigte dürfen Mitteilungen schreiben')}
      ${schalter('schuelerSenden', 'Schüler*innen dürfen Mitteilungen schreiben')}
      ${schalter('familieAnKlassenleitung', 'an die Klassenleitung')}
      ${schalter('familieAnLehrkraefte', 'an alle Lehrkräfte der Klasse')}
      ${schalter('familieAnLeitung', 'an die Schulleitung')}
      ${schalter('familieAntworten', 'auf erhaltene Mitteilungen antworten', 'Auch wenn sie dem Absender sonst nicht schreiben dürften.')}
    </div></section>
    <section class="u-panel"><header><h2>Anhänge und Aufbewahrung</h2></header><div class="mt-einst-rumpf mt-einst-zahlen">
      <label class="u-feld"><span>Größe je Anhang (MB)</span><input type="number" class="form-control" name="maxAnhangMb" min="1" max="25" value="${x.maxAnhangMb}"></label>
      <label class="u-feld"><span>Anhänge je Mitteilung</span><input type="number" class="form-control" name="maxAnhaenge" min="0" max="10" value="${x.maxAnhaenge}"></label>
      <label class="u-feld"><span>Automatisch löschen nach (Tagen)</span><input type="number" class="form-control" name="aufbewahrungTage" min="30" max="3650" value="${x.aufbewahrungTage}"></label>
    </div></section>
    <section class="u-panel"><header><h2>Sperren</h2><span class="ms-auto small text-body-secondary" data-gesperrt-zahl>${gesperrt.size} gesperrt</span></header><div class="mt-einst-rumpf">
      <p class="small text-body-secondary mb-2">Gesperrte Konten können Mitteilungen lesen, aber keine senden.</p>
      <div class="mt-suche mb-2"><i class="bi bi-search" aria-hidden="true"></i><input type="search" class="form-control form-control-sm" data-bsuche placeholder="Konto suchen" aria-label="Konto suchen"></div>
      <div class="mt-wahl-liste mt-einst-konten">${benutzer.map(b => `<label class="mt-wahl-zeile" data-name="${e((b.name + ' ' + b.rolle).toLowerCase())}">
        <input type="checkbox" class="form-check-input" value="${b.id}" ${gesperrt.has(b.id) ? 'checked' : ''}><span class="mt-wahl-name">${e(b.name)}</span><span class="mt-wahl-art">${e(b.rolle)}</span></label>`).join('')}</div>
    </div></section>
    <div class="d-flex justify-content-end"><button class="btn btn-primary" data-speichern>Speichern</button></div></div>`;
  seite.querySelector('[data-bsuche]').oninput = ev => {
    const f = ev.target.value.trim().toLowerCase();
    seite.querySelectorAll('.mt-einst-konten .mt-wahl-zeile').forEach(z => z.classList.toggle('d-none', !!f && !z.dataset.name.includes(f)));
  };
  seite.querySelectorAll('.mt-einst-konten input').forEach(c => c.onchange = () => {
    if (c.checked) gesperrt.add(Number(c.value)); else gesperrt.delete(Number(c.value));
    seite.querySelector('[data-gesperrt-zahl]').textContent = `${gesperrt.size} gesperrt`;
  });
  seite.querySelector('[data-speichern]').onclick = async ev => {
    const d = { gesperrt: [...gesperrt] };
    seite.querySelectorAll('.mt-einst [name]').forEach(f => { d[f.name] = f.type === 'checkbox' ? f.checked : Number(f.value); });
    ev.currentTarget.disabled = true;
    try { await senden(`${BASIS}/einstellungen`, d, 'PUT'); toast('Einstellungen gespeichert.'); }
    catch (err) { toast(err.message, true); }
    finally { ev.currentTarget.disabled = false; }
  };
}
