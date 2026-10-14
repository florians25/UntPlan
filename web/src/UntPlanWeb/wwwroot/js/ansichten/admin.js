// Administration: Synchronisation mit UntPlan, Benutzer, Rückmeldungen, Nachrichten zum Tag, Monitore, Protokoll, Schule.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, formularWerte, heuteIso, datumKurz, meldungText, statusMarker } from '../ui.js';

const BEREICHE = [
  { id: 'sync', text: 'Übersicht & Synchronisation', admin: true },
  { id: 'benutzer', text: 'Benutzer', admin: true },
  { id: 'rueckmeldungen', text: 'Rückmeldungen', admin: true },
  { id: 'nachrichten', text: 'Nachrichten zum Tag', admin: false },
  { id: 'monitore', text: 'Monitore', admin: true },
  { id: 'protokoll', text: 'Protokoll', admin: true },
];
const ROLLEN = { admin: 'Administration', leitung: 'Schulleitung', lehrer: 'Lehrkraft', schueler: 'Klasse / Schüler' };

function kopieren(text) {
  navigator.clipboard?.writeText(text).then(() => toast('In die Zwischenablage kopiert.'), () => toast('Kopieren nicht möglich – bitte manuell markieren.', true));
}

function herunterladen(name, inhalt, typ = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob(['﻿' + inhalt], { type: typ }));
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

export async function zeigen(seite, kontext, [bereichParam]) {
  const ich = kontext.ich;
  const erlaubt = BEREICHE.filter(b => ich.admin || !b.admin);
  const bereich = erlaubt.find(b => b.id === bereichParam) ?? erlaubt[0];
  kontext.titel(ich.admin ? 'Administration' : 'Nachrichten zum Tag');
  seite.innerHTML = `${erlaubt.length > 1 ? `<nav class="reiter">${erlaubt.map(b => `<a href="#/admin/${b.id}" class="${b === bereich ? 'aktiv' : ''}">${b.text}</a>`).join('')}</nav>` : ''}<div data-bereich></div>`;
  const ziel = seite.querySelector('[data-bereich]');
  const neu = () => kontext.navigieren(location.hash);
  await ({ sync, benutzer, rueckmeldungen, nachrichten, monitore, protokoll })[bereich.id](ziel, kontext, neu);
}

// ------------------------------------------------------------------ Übersicht und Synchronisation

async function sync(ziel, kontext, neu) {
  const u = await holen('/api/app/admin/uebersicht');
  let frisch = null;
  try { frisch = JSON.parse(sessionStorage.getItem('untplan:neuerSchluessel') ?? 'null'); sessionStorage.removeItem('untplan:neuerSchluessel'); } catch { /* egal */ }
  const p = u.paket;
  const anzahl = r => u.benutzer[r] ?? 0;
  ziel.innerHTML = `
    ${frisch ? `<div class="hinweis" style="margin-bottom:18px">${icon('schluessel')}<div><b>UntPlanWeb ist eingerichtet.</b> Tragen Sie die folgenden Verbindungsdaten in UntPlan ein
      (<i>UntPlanWeb → Einstellungen</i>). Der API-Schlüssel wird nur jetzt angezeigt.</div></div>` : ''}
    <div class="raster raster-2">
      <div class="karte"><div class="karte-kopf">${icon('aktualisieren')}<h2>Verbindung mit UntPlan</h2></div><div class="karte-inhalt formular">
        <label class="feld">Adresse<div class="schluessel-zeile"><div class="code">${e(u.adresse)}</div><button class="knopf rund" data-kopie="${e(u.adresse)}" title="Kopieren">${icon('kopieren', 'icon-klein')}</button></div></label>
        <label class="feld">Schul-ID<div class="schluessel-zeile"><div class="code">${e(u.schulId || '–')}</div><button class="knopf rund" data-kopie="${e(u.schulId)}" title="Kopieren">${icon('kopieren', 'icon-klein')}</button></div></label>
        <label class="feld">API-Schlüssel<div class="schluessel-zeile" data-schluessel>
          ${frisch?.apiSchluessel ? `<div class="code">${e(frisch.apiSchluessel)}</div><button class="knopf rund" data-kopie="${e(frisch.apiSchluessel)}" title="Kopieren">${icon('kopieren', 'icon-klein')}</button>`
            : `<div class="code gedimmt">${u.apiSchluesselGesetzt ? '•••••••• (gespeichert, aus Sicherheitsgründen nicht anzeigbar)' : 'noch nicht erzeugt'}</div>`}</div></label>
        <div><button class="knopf" data-neuer-schluessel>${icon('schluessel', 'icon-klein')} ${u.apiSchluesselGesetzt ? 'Neuen Schlüssel erzeugen' : 'Schlüssel erzeugen'}</button></div>
        <p class="gedimmt klein" style="margin:0">In UntPlan unter <b>UntPlanWeb → Einstellungen</b> Adresse, Schul-ID und API-Schlüssel eintragen, dann
          <b>Exportieren</b> (oder den Knopf in der Schnellzugriffsleiste). Abwesenheiten, Raumbuchungen und Klausuren holt UntPlan beim Datenaustausch ab.</p>
      </div></div>
      <div class="karte"><div class="karte-kopf">${icon('info')}<h2>Datenstand</h2>
        <div class="rechts"><label class="knopf klein" style="cursor:pointer">${icon('hochladen', 'icon-klein')} Paket hochladen<input type="file" accept=".json,application/json" data-paket hidden></label></div></div>
        <div class="karte-inhalt">${p ? `<table class="tabelle"><tbody>
          <tr><th>Empfangen</th><td>${e(new Date(p.empfangen).toLocaleString('de-DE'))}</td></tr>
          <tr><th>Revision</th><td>${p.revision}${p.programmversion ? ` <span class="gedimmt">· UntPlan ${e(p.programmversion)}</span>` : ''}</td></tr>
          <tr><th>Schuljahr</th><td>${e(p.schuljahr)}</td></tr>
          <tr><th>Zeitraum</th><td>${p.von ? `${e(datumKurz(p.von))}${p.von.slice(0, 4)} – ${e(datumKurz(p.bis))}${p.bis.slice(0, 4)}` : '–'}</td></tr>
          <tr><th>Stammdaten</th><td>${p.klassen} Klassen · ${p.lehrer} Lehrkräfte · ${p.raeume} Räume</td></tr>
          <tr><th>Pläne</th><td>${p.perioden} Perioden · ${p.tage} tagesaktuelle Tage · ${p.vertretungen} Vertretungen</td></tr>
        </tbody></table>` : `<div class="leer">${icon('hochladen')}<p>Noch keine Daten empfangen.</p></div>`}</div></div>
    </div>
    <div class="kennzahlen" style="margin-top:18px">
      <a class="karte kennzahl" href="#/admin/benutzer" style="color:inherit">${icon('benutzer')}<div><b>${anzahl('lehrer') + anzahl('leitung') + anzahl('admin')}</b><small>Konten Kollegium</small></div></a>
      <a class="karte kennzahl" href="#/admin/benutzer" style="color:inherit">${icon('benutzer')}<div><b>${anzahl('schueler')}</b><small>Klassenkonten</small></div></a>
      <a class="karte kennzahl ${u.offeneRueckmeldungen ? 'rot' : 'gruen'}" href="#/admin/rueckmeldungen" style="color:inherit">${icon('melden')}<div><b>${u.offeneRueckmeldungen}</b><small>Rückmeldungen warten auf UntPlan</small></div></a>
    </div>
    <div class="karte" style="margin-top:18px;max-width:720px"><div class="karte-kopf">${icon('admin')}<h2>Schule</h2></div><div class="karte-inhalt">
      <form class="formular" data-schule><div class="zeile">
        <label class="feld">Name der Schule<input name="schulname" value="${e(u.schulname)}" required></label>
        <label class="feld">Schul-ID<input name="schulId" value="${e(u.schulId)}" pattern="[a-z0-9-]+" title="Kleinbuchstaben, Ziffern und Bindestriche"></label></div>
        <div><button class="knopf haupt" type="submit">Speichern</button> <span class="gedimmt klein">UntPlanWeb ${e(u.version ?? '')}</span></div>
      </form></div></div>`;

  ziel.querySelectorAll('[data-kopie]').forEach(b => b.onclick = () => kopieren(b.dataset.kopie));
  ziel.querySelector('[data-neuer-schluessel]').onclick = async () => {
    if (u.apiSchluesselGesetzt && !await bestaetigen('Neuen API-Schlüssel erzeugen', 'Der bisherige Schlüssel wird ungültig. UntPlan muss danach mit dem neuen Schlüssel eingerichtet werden.', 'Neu erzeugen')) return;
    const r = await senden('/api/app/admin/apischluessel', {});
    const box = ziel.querySelector('[data-schluessel]');
    box.innerHTML = `<div class="code">${e(r.apiSchluessel)}</div><button class="knopf rund" title="Kopieren">${icon('kopieren', 'icon-klein')}</button>`;
    box.querySelector('button').onclick = () => kopieren(r.apiSchluessel);
    toast('Neuer Schlüssel erzeugt – bitte jetzt in UntPlan eintragen.');
  };
  ziel.querySelector('[data-paket]').onchange = async ev => {
    const datei = ev.target.files[0];
    if (!datei) return;
    const fd = new FormData();
    fd.append('datei', datei);
    try {
      const r = await api('/api/app/admin/paket', { methode: 'POST', formular: fd });
      toast(`Paket übernommen (Revision ${r.revision}).`);
      await kontext.elemente(true);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelector('[data-schule]').onsubmit = async ev => {
    ev.preventDefault();
    try { await senden('/api/app/admin/schule', formularWerte(ev.target), 'PUT'); toast('Gespeichert.'); await kontext.sitzungNeu(); neu(); }
    catch (err) { kontext.fehler(err); }
  };
}

// ------------------------------------------------------------------ Benutzer

function passwortHinweis(login, pw) {
  dialog({
    titel: 'Startpasswort', inhalt: `<p>Das Startpasswort für <b>${e(login)}</b> lautet:</p>
      <div class="schluessel-zeile"><div class="code" style="font-size:18px">${e(pw)}</div><button class="knopf rund" data-k>${icon('kopieren', 'icon-klein')}</button></div>
      <p class="gedimmt klein" style="margin-top:10px">Beim ersten Anmelden muss ein eigenes Passwort vergeben werden.</p>`,
    beimOeffnen: box => { box.querySelector('[data-k]').onclick = () => kopieren(pw); },
    knoepfe: [{ text: 'Fertig', art: 'haupt' }],
  });
}

async function benutzer(ziel, kontext, neu) {
  const [liste, el] = await Promise.all([holen('/api/app/admin/benutzer'), kontext.elemente()]);
  ziel.innerHTML = `<div class="leiste">
      <input type="search" data-filter placeholder="Suchen …" style="width:220px">
      <div class="segment" data-rollen><button class="aktiv" data-rolle="">Alle</button>${Object.entries(ROLLEN).map(([r, n]) => `<button data-rolle="${r}">${n}</button>`).join('')}</div>
      <span class="abstand"></span>
      <button class="knopf" data-stammdaten>${icon('benutzer', 'icon-klein')} Konten aus Stammdaten</button>
      <button class="knopf haupt" data-neu>${icon('plus', 'icon-klein')} Benutzer</button>
    </div>
    <div class="karte"><div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Benutzername</th><th>Name</th><th>Rolle</th><th>Kürzel</th><th>Letzte Anmeldung</th><th></th></tr></thead><tbody>
      ${liste.map(b => `<tr data-zeile data-rolle="${e(b.rolle)}" data-text="${e((b.login + ' ' + b.anzeigename + ' ' + b.element).toLowerCase())}">
        <td><b>${e(b.login)}</b>${b.gesperrt ? ' <span class="marker rot">gesperrt</span>' : ''}${b.passwortAendern ? ' <span class="marker gelb">Startpasswort</span>' : ''}</td>
        <td>${e(b.anzeigename)}${b.email ? `<br><small class="gedimmt">${e(b.email)}</small>` : ''}</td>
        <td>${e(b.rollenname)}</td><td>${e(b.element)}</td>
        <td class="gedimmt">${b.letzteAnmeldung ? e(new Date(b.letzteAnmeldung.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })) : 'nie'}</td>
        <td style="text-align:right;white-space:nowrap">
          <button class="knopf klein flach" data-bearbeiten="${b.id}" title="Bearbeiten">${icon('stift', 'icon-klein')}</button>
          <button class="knopf klein flach" data-passwort="${b.id}" title="Passwort zurücksetzen">${icon('schluessel', 'icon-klein')}</button>
          ${b.id !== kontext.ich.id ? `<button class="knopf klein flach gefahr" data-loeschen="${b.id}" title="Löschen">${icon('loeschen', 'icon-klein')}</button>` : ''}
        </td></tr>`).join('')}
    </tbody></table></div></div>
    <datalist id="elementliste">${[...el.lehrer, ...el.klassen].map(x => `<option value="${e(x.kurz)}">${e(x.name ?? '')}</option>`).join('')}</datalist>`;

  let rolle = '';
  const filtern = () => {
    const f = ziel.querySelector('[data-filter]').value.trim().toLowerCase();
    ziel.querySelectorAll('[data-zeile]').forEach(z => z.classList.toggle('versteckt', (rolle && z.dataset.rolle !== rolle) || (f && !z.dataset.text.includes(f))));
  };
  ziel.querySelector('[data-filter]').oninput = filtern;
  ziel.querySelectorAll('[data-rolle]').forEach(b => { if (b.tagName === 'BUTTON') b.onclick = () => { rolle = b.dataset.rolle; ziel.querySelectorAll('[data-rollen] button').forEach(x => x.classList.toggle('aktiv', x === b)); filtern(); }; });

  const formular = (b = {}) => `<div class="formular">
      <div class="zeile"><label class="feld">Benutzername<input name="login" value="${e(b.login ?? '')}" ${b.id ? 'disabled' : 'required'} autocomplete="off"></label>
        <label class="feld">Anzeigename<input name="anzeigename" value="${e(b.anzeigename ?? '')}"></label></div>
      <div class="zeile"><label class="feld">Rolle<select name="rolle">${Object.entries(ROLLEN).map(([r, n]) => `<option value="${r}" ${r === (b.rolle ?? 'lehrer') ? 'selected' : ''}>${n}</option>`).join('')}</select></label>
        <label class="feld">Kürzel (Lehrkraft bzw. Klasse)<input name="element" value="${e(b.element ?? '')}" list="elementliste" autocomplete="off"></label></div>
      <label class="feld">E-Mail (optional)<input name="email" type="email" value="${e(b.email ?? '')}"></label>
      ${b.id ? `<label class="check"><input type="checkbox" name="gesperrt" ${b.gesperrt ? 'checked' : ''}> Konto gesperrt</label>`
        : '<label class="feld">Passwort (leer = Startpasswort erzeugen)<input name="passwort" type="password" minlength="8" autocomplete="new-password"></label>'}
    </div>`;

  ziel.querySelector('[data-neu]').onclick = () => dialog({
    titel: 'Neuer Benutzer', inhalt: formular(), knoepfe: [{ text: 'Abbrechen' }, {
      text: 'Anlegen', art: 'haupt', aktion: async box => {
        const r = await senden('/api/app/admin/benutzer', { ...formularWerte(box), gesperrt: false });
        toast('Benutzer angelegt.');
        neu();
        if (r.passwort) passwortHinweis(r.benutzer.login, r.passwort);
      },
    }],
  });
  ziel.querySelectorAll('[data-bearbeiten]').forEach(k => k.onclick = () => {
    const b = liste.find(x => x.id === Number(k.dataset.bearbeiten));
    dialog({
      titel: `Benutzer ${b.login}`, inhalt: formular(b), knoepfe: [{ text: 'Abbrechen' }, {
        text: 'Speichern', art: 'haupt', aktion: async box => {
          await senden(`/api/app/admin/benutzer/${b.id}`, formularWerte(box), 'PUT');
          toast('Gespeichert.');
          neu();
        },
      }],
    });
  });
  ziel.querySelectorAll('[data-passwort]').forEach(k => k.onclick = async () => {
    const b = liste.find(x => x.id === Number(k.dataset.passwort));
    if (!await bestaetigen('Passwort zurücksetzen', `Für ${b.login} ein neues Startpasswort erzeugen?`, 'Zurücksetzen')) return;
    const r = await senden(`/api/app/admin/benutzer/${b.id}/passwort`, {});
    passwortHinweis(b.login, r.passwort);
    neu();
  });
  ziel.querySelectorAll('[data-loeschen]').forEach(k => k.onclick = async () => {
    const b = liste.find(x => x.id === Number(k.dataset.loeschen));
    if (!await bestaetigen('Benutzer löschen', `${b.login} wirklich löschen?`)) return;
    await api(`/api/app/admin/benutzer/${b.id}`, { methode: 'DELETE' });
    toast('Benutzer gelöscht.');
    neu();
  });
  ziel.querySelector('[data-stammdaten]').onclick = () => dialog({
    titel: 'Konten aus Stammdaten anlegen',
    inhalt: `<p>Für alle Lehrkräfte bzw. Klassen aus UntPlan, die noch kein Konto haben, wird ein Konto mit Startpasswort angelegt
      (Benutzername: Lehrerkürzel bzw. <code>klasse-7a</code>).</p>
      <label class="check"><input type="checkbox" name="lehrer" checked> Lehrkräfte (${el.lehrer.length})</label><br>
      <label class="check"><input type="checkbox" name="klassen"> Klassenkonten (${el.klassen.length})</label>`,
    knoepfe: [{ text: 'Abbrechen' }, {
      text: 'Konten anlegen', art: 'haupt', aktion: async box => {
        const r = await senden('/api/app/admin/benutzer/stammdaten', formularWerte(box));
        neu();
        dialog({
          titel: `${r.anzahl} Konten angelegt`, breit: true,
          inhalt: r.anzahl ? `<div class="hinweis warnung" style="margin-bottom:12px">${icon('warnung')}<div>Die Startpasswörter werden nur jetzt angezeigt. Bitte die Liste herunterladen und sicher verteilen.</div></div>
            <div class="tabelle-huelle" style="max-height:340px"><table class="tabelle"><thead><tr><th>Benutzername</th><th>Startpasswort</th><th>Rolle</th><th>Name</th></tr></thead><tbody>
            ${r.konten.map(k => `<tr><td><b>${e(k.login)}</b></td><td class="code" style="border:0">${e(k.passwort)}</td><td>${e(k.rolle)}</td><td>${e(k.name)}</td></tr>`).join('')}</tbody></table></div>`
            : '<p>Alle Lehrkräfte bzw. Klassen haben bereits ein Konto.</p>',
          knoepfe: r.anzahl ? [{ text: `${icon('herunterladen', 'icon-klein')} Liste herunterladen (CSV)`, aktion: () => { herunterladen(`untplanweb-konten-${heuteIso()}.csv`, r.csv); return false; } }, { text: 'Fertig', art: 'haupt' }] : [{ text: 'OK', art: 'haupt' }],
        });
      },
    }],
  });
}

// ------------------------------------------------------------------ Rückmeldungen

async function rueckmeldungen(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/rueckmeldungen');
  ziel.innerHTML = `<div class="hinweis" style="margin-bottom:16px">${icon('info')}<div>Rückmeldungen werden von UntPlan beim Datenaustausch abgeholt und nach der Übernahme automatisch quittiert.
      Hier können Sie den Stand einsehen und bei Bedarf manuell ändern.</div></div>
    <div class="karte">${liste.length ? `<div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Meldung</th><th>Von</th><th>Erstellt</th><th>Stand</th><th></th></tr></thead><tbody>
      ${liste.map(m => `<tr><td>${e(meldungText(m))}${m.text && m.typ !== 'nachricht' ? `<br><small class="gedimmt">${e(m.text)}</small>` : ''}</td>
        <td>${e(m.absender)}</td><td class="gedimmt" style="white-space:nowrap">${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }))}</td>
        <td>${statusMarker(m.status)}</td>
        <td style="text-align:right;white-space:nowrap"><select data-status="${e(m.id)}" style="width:auto;height:30px;font-size:13px">
          ${['offen', 'quittiert', 'abgelehnt'].map(s => `<option value="${s}" ${s === m.status ? 'selected' : ''}>${s}</option>`).join('')}</select>
          <button class="knopf klein flach gefahr" data-loeschen="${e(m.id)}" title="Löschen">${icon('loeschen', 'icon-klein')}</button></td></tr>`).join('')}
    </tbody></table></div>` : '<div class="leer">Keine Rückmeldungen.</div>'}</div>`;
  ziel.querySelectorAll('[data-status]').forEach(s => s.onchange = async () => {
    try { await senden(`/api/app/admin/rueckmeldungen/${encodeURIComponent(s.dataset.status)}`, { status: s.value }, 'PUT'); toast('Gespeichert.'); neu(); }
    catch (err) { kontext.fehler(err); }
  });
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Rückmeldung löschen', 'Diese Rückmeldung endgültig löschen?')) return;
    await api(`/api/app/admin/rueckmeldungen/${encodeURIComponent(b.dataset.loeschen)}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Nachrichten zum Tag

const ZIELGRUPPEN = { alle: 'Alle', lehrer: 'Nur Kollegium', schueler: 'Nur Schüler' };

async function nachrichten(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/nachrichten');
  ziel.innerHTML = `<div class="leiste"><p class="gedimmt" style="margin:0">Nachrichten erscheinen im Vertretungsplan, auf der Startseite und – falls gewünscht – auf den Monitoren. Tagestexte aus UntPlan werden zusätzlich angezeigt.</p>
      <span class="abstand"></span><button class="knopf haupt" data-neu>${icon('plus', 'icon-klein')} Nachricht</button></div>
    <div class="karte">${liste.length ? `<div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Zeitraum</th><th>Text</th><th>Für</th><th>Anzeige</th><th></th></tr></thead><tbody>
      ${liste.map(n => `<tr class="${n.bis < heuteIso() ? 'gedimmt' : ''}"><td style="white-space:nowrap">${e(datumKurz(n.von))}${n.bis !== n.von ? ' – ' + e(datumKurz(n.bis)) : ''}</td>
        <td>${e(n.text)}</td><td>${e(ZIELGRUPPEN[n.zielgruppe] ?? n.zielgruppe)}</td>
        <td>${['Vertretungsplan', n.startseite ? 'Startseite' : '', n.monitor ? 'Monitor' : ''].filter(Boolean).join(', ')}</td>
        <td style="text-align:right;white-space:nowrap"><button class="knopf klein flach" data-bearbeiten="${n.id}">${icon('stift', 'icon-klein')}</button>
          <button class="knopf klein flach gefahr" data-loeschen="${n.id}">${icon('loeschen', 'icon-klein')}</button></td></tr>`).join('')}
    </tbody></table></div>` : '<div class="leer">Keine Nachrichten.</div>'}</div>`;
  const formular = (n = {}) => `<div class="formular">
      <div class="zeile"><label class="feld">von<input type="date" name="von" value="${e(n.von ?? heuteIso())}" required></label>
        <label class="feld">bis<input type="date" name="bis" value="${e(n.bis ?? heuteIso())}" required></label>
        <label class="feld">Für<select name="zielgruppe">${Object.entries(ZIELGRUPPEN).map(([z, t]) => `<option value="${z}" ${z === (n.zielgruppe ?? 'alle') ? 'selected' : ''}>${t}</option>`).join('')}</select></label></div>
      <label class="feld">Text<textarea name="text" rows="4" required>${e(n.text ?? '')}</textarea></label>
      <label class="check"><input type="checkbox" name="startseite" ${n.startseite ?? true ? 'checked' : ''}> auf der Startseite anzeigen</label>
      <label class="check"><input type="checkbox" name="monitor" ${n.monitor ?? true ? 'checked' : ''}> auf den Monitoren anzeigen</label>
    </div>`;
  const bearbeiten = n => dialog({
    titel: n ? 'Nachricht bearbeiten' : 'Neue Nachricht', inhalt: formular(n),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      await senden(n ? `/api/app/admin/nachrichten/${n.id}` : '/api/app/admin/nachrichten', formularWerte(box), n ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  ziel.querySelector('[data-neu]').onclick = () => bearbeiten(null);
  ziel.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(liste.find(n => n.id === Number(b.dataset.bearbeiten))));
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Nachricht löschen', 'Diese Nachricht löschen?')) return;
    await api(`/api/app/admin/nachrichten/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Monitore

async function monitore(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/monitore');
  ziel.innerHTML = `<div class="leiste"><p class="gedimmt" style="margin:0">Ein Monitor zeigt den Vertretungsplan ohne Anmeldung über einen geheimen Link an (z. B. im Eingangsbereich, Browser im Vollbild).</p>
      <span class="abstand"></span><button class="knopf haupt" data-neu>${icon('plus', 'icon-klein')} Monitor</button></div>
    <div class="raster raster-2">${liste.map(m => `<div class="karte"><div class="karte-kopf">${icon('monitor')}<h2>${e(m.name)}</h2>
        <div class="rechts"><span class="marker ${m.version === 'lehrer' ? 'violett' : 'petrol'}">${m.version === 'lehrer' ? 'Lehrerversion' : 'Schülerversion'}</span></div></div>
      <div class="karte-inhalt formular">
        <div class="schluessel-zeile"><div class="code klein">${e(m.link)}</div><button class="knopf rund" data-kopie="${e(m.link)}" title="Kopieren">${icon('kopieren', 'icon-klein')}</button></div>
        <div class="leiste" style="margin:0"><span class="gedimmt">${m.tage} ${m.tage === 1 ? 'Tag' : 'Tage'}</span><span class="abstand"></span>
          <a class="knopf klein" href="${e(m.link)}" target="_blank" rel="noopener">${icon('augen', 'icon-klein')} Öffnen</a>
          <button class="knopf klein" data-bearbeiten="${m.id}">${icon('stift', 'icon-klein')}</button>
          <button class="knopf klein gefahr" data-loeschen="${m.id}">${icon('loeschen', 'icon-klein')}</button></div>
      </div></div>`).join('') || '<div class="karte"><div class="leer">Noch keine Monitore.</div></div>'}</div>`;
  const formular = (m = {}) => `<div class="formular">
      <label class="feld">Name<input name="name" value="${e(m.name ?? '')}" placeholder="z. B. Foyer" required></label>
      <div class="zeile"><label class="feld">Version<select name="version"><option value="schueler">Schülerversion</option><option value="lehrer" ${m.version === 'lehrer' ? 'selected' : ''}>Lehrerversion (Lehrerzimmer)</option></select></label>
        <label class="feld">Anzahl Tage<input type="number" name="tage" min="1" max="5" value="${m.tage ?? 2}"></label></div></div>`;
  const bearbeiten = m => dialog({
    titel: m ? 'Monitor bearbeiten' : 'Neuer Monitor', inhalt: formular(m),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      await senden(m ? `/api/app/admin/monitore/${m.id}` : '/api/app/admin/monitore', formularWerte(box), m ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  ziel.querySelector('[data-neu]').onclick = () => bearbeiten(null);
  ziel.querySelectorAll('[data-kopie]').forEach(b => b.onclick = () => kopieren(b.dataset.kopie));
  ziel.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(liste.find(m => m.id === Number(b.dataset.bearbeiten))));
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Monitor löschen', 'Der Link wird ungültig. Fortfahren?')) return;
    await api(`/api/app/admin/monitore/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Protokoll

async function protokoll(ziel) {
  const liste = await holen('/api/app/admin/protokoll');
  ziel.innerHTML = `<div class="karte">${liste.length ? `<div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Zeit</th><th>Benutzer</th><th>Aktion</th><th>Details</th></tr></thead><tbody>
    ${liste.map(p => `<tr><td class="gedimmt" style="white-space:nowrap">${e(p.zeit)}</td><td>${e(p.benutzer)}</td><td>${e(p.aktion)}</td><td class="gedimmt">${e(p.details)}</td></tr>`).join('')}
    </tbody></table></div>` : '<div class="leer">Noch keine Einträge.</div>'}</div>`;
}

