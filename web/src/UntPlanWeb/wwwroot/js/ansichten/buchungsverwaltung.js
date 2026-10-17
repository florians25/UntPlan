// Buchen → Verwaltung (nur Administration): Ressourcenarten mit Symbol und Ressourcen (Name, Art, fest an einem Raum oder mobil mit Anzahl
// und Standort; Beispiel-Ressourcen zum Start) sowie
// Raum-Stammdaten für die Auswahlkriterien (Kapazität, Gebäude, Abteilung, Raumgruppe; Raumart und „buchbar“ kommen aus UntPlan).
import { e, holen, senden, api, toast, dialog, bestaetigen, alarm, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { ressourcenSymbol } from './buchen.js';

export async function zeigen(seite, kontext, bereich) {
  if (!kontext.ich.admin) { seite.innerHTML = alarm('warning', 'Nur für die Administration.'); return; }
  return bereich === 'raumdaten' ? raumdaten(seite, kontext) : ressourcen(seite, kontext);
}

const optionen = (liste, wert, leerText) => `${leerText != null ? `<option value="">${e(leerText)}</option>` : ''}${liste.map(([w, t, z]) =>
  `<option value="${e(w)}" ${String(w) === String(wert ?? '') ? 'selected' : ''} ${z ? `data-zweit="${e(z)}"` : ''}>${e(t)}</option>`).join('')}`;

async function ressourcen(seite, kontext) {
  const neu = () => kontext.navigieren('#/buchen/verwaltung', true);
  const [opt, arten, liste, symbole] = await Promise.all([holen('/api/app/buchen/optionen'), holen('/api/app/buchen/ressourcenarten'), holen('/api/app/buchen/ressourcen'),
    holen('/api/app/buchen/symbole')]);
  kontext.titel('Ressourcen verwalten', '<a class="nl-knopf" href="#/buchen/ressourcen"><i class="bi bi-grid-3x2-gap"></i>Ressourcenübersicht</a>', `${liste.length}`);
  const standorte = [...new Set(liste.map(r => r.standort).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de'));
  const symbolWahl = (wert, vorschlag) => `<div class="bu-symbolwahl" role="radiogroup" aria-label="Symbol">${['', ...symbole].map(x => `<label title="${x ? e(x) : 'automatisch'}">
      <input type="radio" name="symbol" value="${e(x)}" ${x === (wert ?? '') ? 'checked' : ''}><span><i class="bi bi-${e(x || vorschlag)}"></i>${x ? '' : '<small>auto</small>'}</span></label>`).join('')}</div>`;
  seite.innerHTML = `
    <div class="bu-verwaltung">
      <section class="bu-karte">
        <header><i class="bi bi-tags"></i><h2>Ressourcenarten</h2></header>
        <p class="nl-unter">Nach diesen Arten filtern Lehrkräfte bei der Raum- und Ressourcensuche (z. B. Beamer, Internet, Laptop tragbar).</p>
        <ul class="bu-artenpflege">${arten.map(a => `<li><i class="bi bi-${e(ressourcenSymbol({ symbol: a.symbol, art: a.name }))} bu-artsymbol" aria-hidden="true"></i><span class="nl-tag">${e(a.name)}</span>
            <span class="nl-unter">${liste.filter(r => r.artId === a.id).length} Ressourcen</span>
            <button type="button" class="nl-icon sichtbar" data-art-bearbeiten="${a.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>
            <button type="button" class="nl-icon sichtbar rot" data-art-weg="${a.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button></li>`).join('') || `<li class="nl-unter">Noch keine Ressourcenarten.</li>`}</ul>
        <form class="bu-artneu" data-art-neu><input class="form-control" name="name" maxlength="100" placeholder="Neue Ressourcenart" required aria-label="Neue Ressourcenart">
          <button class="nl-hinzu bu-klein" type="submit"><i class="bi bi-plus-lg"></i>Hinzufügen</button></form>
      </section>
      <section>
        <div class="u-sf-leiste">${suchfeldMitFilter({ platzhalter: 'Ressource, Raum oder Standort suchen', label: 'Ressourcen suchen', filter: [
            { name: 'art', label: 'Art', alle: 'Alle Arten', wert: '', optionen: arten.map(a => [String(a.id), a.name]) },
            { name: 'zuordnung', label: 'Zuordnung', alle: 'Mobil und fest', wert: '', optionen: [['mobil', 'Mobil'], ['fest', 'Fest in einem Raum']] }] })}
          <div class="u-sf-rechts"><button type="button" class="nl-hinzu" data-neu><i class="bi bi-plus-lg"></i>Ressource hinzufügen</button></div></div>
        <div class="nl-tabelle bu-tabelle">${liste.length ? `<table><thead><tr><th>Ressource</th><th>Art</th><th>Zuordnung</th><th>Standort</th><th class="text-end">Anzahl</th><th>Status</th><th></th></tr></thead><tbody>
          ${liste.map(r => `<tr data-name="${e(`${r.name} ${r.art} ${r.raum} ${r.standort}`.toLowerCase())}" data-art="${r.artId ?? ''}" data-zuordnung="${r.raum ? 'fest' : 'mobil'}"><td><div class="bu-vw-name"><span class="bu-vw-symbol"><i class="bi bi-${e(ressourcenSymbol(r))}" aria-hidden="true"></i></span><div><div class="nl-name">${e(r.name)}</div>${r.bemerkung ? `<div class="nl-unter">${e(r.bemerkung)}</div>` : ''}</div></div></td>
            <td data-label="Art">${r.art ? `<span class="nl-tag">${e(r.art)}</span>` : ''}</td>
            <td data-label="Zuordnung">${r.raum ? `<span class="nl-chip grau"><i class="bi bi-door-closed"></i>fest in ${e(r.raum)}</span>` : '<span class="nl-chip lila"><i class="bi bi-arrows-move"></i>mobil</span>'}</td>
            <td data-label="Standort">${r.mobil && r.standort ? `<i class="bi bi-geo-alt text-body-secondary me-1"></i>${e(r.standort)}` : ''}</td>
            <td class="text-end" data-label="Anzahl">${r.anzahl}</td><td data-label="Status">${r.aktiv ? '<span class="nl-chip gruen">aktiv</span>' : '<span class="nl-chip grau">inaktiv</span>'}</td>
            <td class="text-end text-nowrap"><button type="button" class="nl-icon" data-bearbeiten="${r.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>
              <button type="button" class="nl-icon rot" data-weg="${r.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button></td></tr>`).join('')}
          </tbody></table>` : `<div class="bu-leer border-0"><i class="bi bi-box-seam" aria-hidden="true"></i><b>Noch keine Ressourcen angelegt.</b>
            <span>Mobile Geräte (Beamer, Laptopwagen, Tablet-Koffer …) mit Anzahl und Standort oder feste Ausstattung eines Raums.</span>
            <div class="d-flex flex-wrap justify-content-center gap-2 mt-2"><button type="button" class="nl-knopf" data-beispiele><i class="bi bi-magic"></i>Beispiel-Ressourcen anlegen</button>
              <button type="button" class="nl-hinzu" data-neu2><i class="bi bi-plus-lg"></i>Ressource hinzufügen</button></div></div>`}</div>
      </section>
    </div>`;
  seite.querySelector('[data-art-neu]').onsubmit = async ev => {
    ev.preventDefault();
    try { await senden('/api/app/buchen/ressourcenarten', { name: ev.target.name.value, reihenfolge: arten.length * 10 + 10 }); toast('Ressourcenart angelegt.'); neu(); } catch (err) { kontext.fehler(err); }
  };
  seite.querySelectorAll('[data-art-bearbeiten]').forEach(b => b.onclick = () => {
    const a = arten.find(x => String(x.id) === b.dataset.artBearbeiten);
    dialog({ titel: 'Ressourcenart bearbeiten', breit: true, inhalt: `<label class="form-label">Name</label><input class="form-control mb-3" name="name" maxlength="100" value="${e(a.name)}">
        <label class="form-label">Symbol</label>${symbolWahl(a.symbol, ressourcenSymbol({ art: a.name }))}`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        await senden(`/api/app/buchen/ressourcenarten/${a.id}`, { name: box.querySelector('[name=name]').value, reihenfolge: a.reihenfolge, symbol: box.querySelector('[name=symbol]:checked')?.value ?? '' }, 'PUT'); neu();
      } }] });
  });
  seite.querySelectorAll('[data-art-weg]').forEach(b => b.onclick = async () => {
    const a = arten.find(x => String(x.id) === b.dataset.artWeg);
    if (!await bestaetigen('Ressourcenart löschen', `„${a.name}“ löschen? Die Ressourcen bleiben erhalten (ohne Art).`)) return;
    try { await api(`/api/app/buchen/ressourcenarten/${a.id}`, { methode: 'DELETE' }); neu(); } catch (err) { kontext.fehler(err); }
  });
  const formular = (r = {}) => dialog({
    titel: r.id ? 'Ressource bearbeiten' : 'Ressource hinzufügen', breit: true,
    inhalt: `<div class="row g-3">
      <div class="col-sm-7"><label class="form-label">Name</label><input class="form-control" name="name" maxlength="100" value="${e(r.name ?? '')}" placeholder="z. B. Beamer mobil 1"></div>
      <div class="col-sm-5"><label class="form-label">Ressourcenart</label><select class="form-select" name="artId">${optionen(arten.map(a => [a.id, a.name]), r.artId ?? '', 'keine')}</select></div>
      <div class="col-12"><label class="form-label">Zuordnung</label><div class="bu-segment" role="radiogroup">
        <button type="button" class="${r.raum ? '' : 'an'}" data-zuordnung="mobil">Mobil (unabhängig von Räumen)</button><button type="button" class="${r.raum ? 'an' : ''}" data-zuordnung="fest">Fest in einem Raum</button></div></div>
      <div class="col-sm-7 ${r.raum ? '' : 'd-none'}" data-raumfeld><label class="form-label">Raum</label><select class="form-select" name="raum">${optionen(opt.raeume.map(x => [x.kurz, x.kurz, x.name]), r.raum ?? '', 'bitte wählen')}</select></div>
      <div class="col-sm-4 ${r.raum ? 'd-none' : ''}" data-anzahlfeld><label class="form-label">Anzahl (Stück)</label><input type="number" class="form-control" name="anzahl" min="1" max="999" value="${r.anzahl ?? 1}"></div>
      <div class="col-sm-8 ${r.raum ? 'd-none' : ''}" data-standortfeld><label class="form-label">Standort (Ausgabe bzw. Lagerort)</label><input class="form-control" name="standort" maxlength="100" list="bu-standorte" value="${e(r.standort ?? '')}" placeholder="z. B. Medienraum">
        <datalist id="bu-standorte">${standorte.map(x => `<option value="${e(x)}">`).join('')}</datalist></div>
      <div class="col-12"><label class="form-label">Bemerkung</label><input class="form-control" name="bemerkung" maxlength="500" value="${e(r.bemerkung ?? '')}"></div>
      <div class="col-12"><label class="bu-schalter"><input type="checkbox" class="form-check-input" name="aktiv" ${r.aktiv === false ? '' : 'checked'}><span>aktiv (buchbar bzw. als Kriterium wählbar)</span></label></div></div>`,
    beimOeffnen: box => box.querySelectorAll('[data-zuordnung]').forEach(b => b.onclick = () => {
      box.querySelectorAll('[data-zuordnung]').forEach(x => x.classList.toggle('an', x === b));
      const fest = b.dataset.zuordnung === 'fest';
      box.querySelector('[data-raumfeld]').classList.toggle('d-none', !fest);
      box.querySelector('[data-anzahlfeld]').classList.toggle('d-none', fest);
      box.querySelector('[data-standortfeld]').classList.toggle('d-none', fest);
    }),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const w = n => box.querySelector(`[name=${n}]`);
      const fest = box.querySelector('[data-zuordnung="fest"]').classList.contains('an');
      if (fest && !w('raum').value) throw new Error('Bitte einen Raum wählen.');
      const daten = { name: w('name').value, artId: w('artId').value ? Number(w('artId').value) : null, raum: fest ? w('raum').value : '', anzahl: fest ? 1 : Number(w('anzahl').value) || 1,
        bemerkung: w('bemerkung').value, aktiv: w('aktiv').checked, standort: fest ? '' : w('standort').value };
      await senden(r.id ? `/api/app/buchen/ressourcen/${r.id}` : '/api/app/buchen/ressourcen', daten, r.id ? 'PUT' : 'POST');
      toast('Ressource gespeichert.');
      neu();
    } }],
  });
  seite.querySelector('[data-neu]').onclick = () => formular();
  seite.querySelector('[data-neu2]')?.addEventListener('click', () => formular());
  seite.querySelector('[data-beispiele]')?.addEventListener('click', async ev => {
    ev.currentTarget.disabled = true;
    try { const r = await senden('/api/app/buchen/ressourcen/beispiele', {}); toast(`${r.angelegt} Beispiel-Ressourcen angelegt.`); neu(); } catch (err) { kontext.fehler(err); ev.currentTarget.disabled = false; }
  });
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(liste.find(r => String(r.id) === b.dataset.bearbeiten)));
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = async () => {
    const r = liste.find(x => String(x.id) === b.dataset.weg);
    if (!await bestaetigen('Ressource löschen', `„${r.name}“ löschen? Bestehende Buchungen behalten ihre Termine.`)) return;
    try { await api(`/api/app/buchen/ressourcen/${r.id}`, { methode: 'DELETE' }); neu(); } catch (err) { kontext.fehler(err); }
  });
  zeilenfilter(seite, ['art', 'zuordnung']);
}

async function raumdaten(seite, kontext) {
  const opt = await holen('/api/app/buchen/optionen');
  kontext.titel('Raumdaten', '', `${opt.raeume.length}`);
  const liste = (id, werte) => `<datalist id="${id}">${werte.map(w => `<option value="${e(w)}">`).join('')}</datalist>`;
  seite.innerHTML = `
    <p class="nl-hinweis"><i class="bi bi-info-circle"></i>Raumart, Kapazität und „buchbar“ kommen aus den Stammdaten von UntPlan. Hier ergänzen Sie Gebäude, Abteilung und Raumgruppe
      (und bei Bedarf eine abweichende Kapazität) für die Auswahlkriterien beim Buchen. Feste Ressourcen ordnen Sie unter <a href="#/buchen/verwaltung">Ressourcen verwalten</a> zu.</p>
    <div class="u-sf-leiste">${suchfeldMitFilter({ platzhalter: 'Raum suchen', label: 'Raum suchen', filter: [
      ...(opt.gebaeude.length ? [{ name: 'gebaeude', label: 'Gebäude', alle: 'Alle Gebäude', wert: '', optionen: opt.gebaeude.map(x => [x, x]) }] : []),
      { name: 'buchbar', label: 'Buchbar', alle: 'Alle Räume', wert: '', optionen: [['ja', 'Nur buchbare'], ['nein', 'Nicht buchbare']] }] })}</div>
    ${liste('bu-geb', opt.gebaeude)}${liste('bu-abt', opt.abteilungen)}${liste('bu-grp', opt.raumgruppen)}
    <div class="nl-tabelle bu-tabelle"><table class="bu-raumdaten"><thead><tr><th>Raum</th><th>Raumart</th><th>Buchbar</th><th>Kapazität</th><th>Gebäude</th><th>Abteilung</th><th>Raumgruppe</th><th>Feste Ressourcen</th></tr></thead><tbody>
      ${opt.raeume.map(r => `<tr data-raum="${e(r.kurz)}" data-gebaeude="${e(r.gebaeude)}" data-buchbar="${r.buchbar ? 'ja' : 'nein'}" data-name="${e(`${r.kurz} ${r.name} ${r.raumart} ${r.gebaeude} ${r.abteilung} ${r.raumgruppe}`.toLowerCase())}">
        <td><div class="nl-name">${e(r.kurz)}</div><div class="nl-unter">${e(r.name !== r.kurz ? r.name : '')}</div></td>
        <td>${r.raumart ? `<span class="nl-tag">${e(r.raumart)}</span>` : ''}</td>
        <td>${r.buchbar ? '<span class="nl-chip gruen">ja</span>' : '<span class="nl-chip grau">nein</span>'}</td>
        <td><input type="number" class="form-control bu-menge" min="0" max="9999" name="kapazitaet" value="${r.kapazitaet !== r.kapazitaetUntPlan ? r.kapazitaet : ''}" placeholder="${r.kapazitaetUntPlan || ''}" aria-label="Kapazität ${e(r.kurz)}"></td>
        <td><input class="form-control" name="gebaeude" list="bu-geb" maxlength="100" value="${e(r.gebaeude)}" aria-label="Gebäude ${e(r.kurz)}"></td>
        <td><input class="form-control" name="abteilung" list="bu-abt" maxlength="100" value="${e(r.abteilung)}" aria-label="Abteilung ${e(r.kurz)}"></td>
        <td><input class="form-control" name="raumgruppe" list="bu-grp" maxlength="100" value="${e(r.raumgruppe)}" aria-label="Raumgruppe ${e(r.kurz)}"></td>
        <td><div class="nl-chips">${r.ressourcen.map(x => `<span class="nl-chip grau">${e(x)}</span>`).join('')}</div></td></tr>`).join('')}
    </tbody></table></div>`;
  seite.querySelectorAll('tr[data-raum] input').forEach(f => f.addEventListener('change', async () => {
    const tr = f.closest('tr');
    const w = n => tr.querySelector(`[name=${n}]`).value;
    try {
      await senden(`/api/app/buchen/raumdaten/${encodeURIComponent(tr.dataset.raum)}`, { kapazitaet: Number(w('kapazitaet')) || 0, gebaeude: w('gebaeude'), abteilung: w('abteilung'), raumgruppe: w('raumgruppe') }, 'PUT');
      toast(`Raum ${tr.dataset.raum} gespeichert.`);
    } catch (err) { kontext.fehler(err); }
  }));
  zeilenfilter(seite, ['gebaeude', 'buchbar']);
}

/** Suchfeld mit Filter über einer Tabelle: Freitext (data-name) und Dropdowns (data-<filter> der Zeilen) blenden Zeilen aus. */
function zeilenfilter(seite, namen) {
  const wahl = { suche: '' };
  const anwenden = () => seite.querySelectorAll('tbody tr[data-name]').forEach(tr => {
    tr.hidden = (wahl.suche.length > 0 && !tr.dataset.name.includes(wahl.suche)) || namen.some(n => wahl[n] && tr.dataset[n] !== wahl[n]);
  });
  suchfeldMitFilterBinden(seite.querySelector('.u-sf-leiste'), {
    beiSuche: t => { wahl.suche = t.toLowerCase(); anwenden(); },
    beiFilter: (n, w) => { wahl[n] = w; anwenden(); },
  });
}
