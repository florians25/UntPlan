// Administration → Wartung: Aktive Benutzer (angemeldete Sitzungen mit letzter Aktivität), Zugriff ent-/sperren (Wartungsmodus: nur
// die Administration kann sich anmelden, angemeldete Benutzer bleiben angemeldet) und „Über UntPlan“ (Version, letzte Übertragungen
// aus dem Desktop-Programm, Module). Aufgerufen aus admin.js.
import { e, icon, holen, senden, toast, leer, initialen } from '../ui.js';

const zeit = s => s ? new Date(s.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '–';
const vor = min => min < 1 ? 'gerade eben' : min < 60 ? `vor ${min} min` : min < 1440 ? `vor ${Math.floor(min / 60)} h` : `vor ${Math.floor(min / 1440)} d`;

// ------------------------------------------------------------------ Aktive Benutzer

export async function aktive(ziel, kontext, neu, [zeitraumParam] = []) {
  const stunden = ['1', '24', '168'].includes(zeitraumParam) ? zeitraumParam : '24';
  const d = await holen(`/api/app/admin/sitzungen?stunden=${stunden}`);
  const aktiv = d.sitzungen.filter(s => s.aktiv);
  const konten = new Set(d.sitzungen.map(s => s.benutzerId));
  ziel.classList.add('wt-seite');
  ziel.innerHTML = `
    ${d.gesperrt ? `<div class="lg-wartung"><i class="bi bi-lock" aria-hidden="true"></i><span>Der Zugriff ist gesperrt: Nur die Administration kann sich anmelden. <a href="#/admin/zugriff">Zugriff freigeben</a></span></div>` : ''}
    <div class="nl-zahlen">
      <span class="nl-zahl"><span class="wt-online"></span><b>${aktiv.length}</b> gerade aktiv (letzte ${d.aktivMinuten} min)</span>
      <span class="nl-zahl"><b>${d.sitzungen.length}</b> Sitzungen</span>
      <span class="nl-zahl"><b>${konten.size}</b> Benutzer</span>
    </div>
    <div class="nl-leiste">
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Benutzer suchen" aria-label="Benutzer suchen"></label>
      <label class="nl-pille"><span class="me-1 text-body-secondary">Aktivität</span><select data-zeitraum aria-label="Zeitraum">
        ${[['1', 'letzte Stunde'], ['24', 'letzte 24 Stunden'], ['168', 'letzte 7 Tage']].map(([w, t]) => `<option value="${w}" ${w === stunden ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
      <span class="ms-auto d-flex gap-2"><button type="button" class="nl-knopf" data-neu><i class="bi bi-arrow-repeat"></i>Aktualisieren</button><a class="nl-knopf" href="#/admin/zugriff"><i class="bi bi-lock"></i>Zugriff ent-/sperren</a></span>
    </div>
    <div class="nl-tabelle">${d.sitzungen.length ? `<table><thead><tr><th></th><th>Benutzer</th><th>Rolle</th><th>Gerät</th><th>Adresse</th><th>Angemeldet seit</th><th>Letzte Aktivität</th></tr></thead><tbody>
      ${d.sitzungen.map(s => `<tr data-name="${e(`${s.login} ${s.anzeigename} ${s.rollenname}`.toLowerCase())}">
        <td class="nl-avatar-zelle"><span class="nl-avatar ${s.aktiv ? 'voll' : ''}">${e(initialen(s.anzeigename || s.login))}</span></td>
        <td><div class="nl-name">${e(s.anzeigename || s.login)}</div><div class="nl-unter">${e(s.login)}</div></td>
        <td>${e(s.rollenname)}</td><td class="nl-unter">${e(s.geraet || '–')}</td><td class="nl-unter">${e(s.adresse || '–')}</td>
        <td class="nl-unter text-nowrap">${e(zeit(s.beginn))}</td>
        <td class="text-nowrap"><span class="wt-online ${s.aktiv ? '' : 'aus'}"></span>${e(vor(s.minuten))}<div class="nl-unter">${e(zeit(s.letzte))}</div></td></tr>`).join('')}
      </tbody></table>` : `<div class="p-3">${leer('In diesem Zeitraum war niemand angemeldet.', 'people')}</div>`}</div>
    <p class="ug-hinweis">Angezeigt werden Sitzungen seit dem letzten Start von UntPlanWeb. „Gerade aktiv“: Aktivität in den letzten ${d.aktivMinuten} Minuten.
      Vor Wartungsarbeiten (z. B. einem Import aus dem Desktop-Programm) prüfen Sie hier, ob noch jemand arbeitet.</p>`;
  ziel.querySelector('[data-zeitraum]').onchange = ev => kontext.navigieren(`#/admin/aktive/${ev.target.value}`);
  ziel.querySelector('[data-neu]').onclick = () => neu();
  ziel.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    ziel.querySelectorAll('tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  };
}

// ------------------------------------------------------------------ Zugriff ent-/sperren

export async function zugriff(ziel, kontext, neu) {
  const d = await holen('/api/app/admin/zugriff');
  ziel.classList.add('wt-seite');
  ziel.innerHTML = `
    <div class="wt-status ${d.gesperrt ? 'gesperrt' : ''}">
      <span class="wt-symbol"><i class="bi bi-${d.gesperrt ? 'lock' : 'unlock'}" aria-hidden="true"></i></span>
      <div><h3>${d.gesperrt ? 'Zugriff gesperrt' : 'Zugriff freigegeben'}</h3>
        <p>${d.gesperrt ? `Seit ${e(zeit(d.seit))}${d.von ? ` (${e(d.von)})` : ''} können sich nur Konten der Administration anmelden.` : 'Alle Benutzer können sich anmelden.'}</p></div>
    </div>
    <div class="wt-karte"><form>
      <label class="wt-schalter"><input type="checkbox" name="gesperrt" ${d.gesperrt ? 'checked' : ''}>Zugriff sperren (Wartungsmodus)</label>
      <label class="form-label" for="wt-hinweis">Hinweis auf der Anmeldeseite</label>
      <textarea id="wt-hinweis" name="hinweis" maxlength="500" placeholder="${e(d.standardHinweis)}">${e(d.hinweis)}</textarea>
      <div class="lg-wartung mt-3"><i class="bi bi-info-circle" aria-hidden="true"></i><span>Bereits angemeldete Benutzer werden durch das Sperren nicht abgemeldet.
        Derzeit ${d.aktiv === 1 ? 'ist <b>1 Sitzung</b>' : `sind <b>${d.aktiv} Sitzungen</b>`} aktiv (letzte ${d.aktivMinuten} min)${d.aktivOhneAdmin ? `, davon ${d.aktivOhneAdmin} ohne Administration` : ''}.
        Prüfen Sie vor dem Sperren unter <a href="#/admin/aktive">Aktive Benutzer</a>, dass niemand mehr arbeitet.</span></div>
      <button class="btn btn-primary mt-3" type="submit">Speichern</button>
    </form></div>`;
  const form = ziel.querySelector('form');
  form.onsubmit = async ev => {
    ev.preventDefault();
    try {
      const r = await senden('/api/app/admin/zugriff', { gesperrt: form.gesperrt.checked, hinweis: form.hinweis.value }, 'PUT');
      toast(r.gesperrt ? 'Zugriff gesperrt – nur die Administration kann sich anmelden.' : 'Zugriff freigegeben.');
      neu();
    } catch (err) { kontext.fehler(err); }
  };
}

// ------------------------------------------------------------------ Über UntPlan

export async function ueber(ziel) {
  const d = await holen('/api/app/admin/ueber');
  const wert = (titel, inhalt) => `<div class="wt-wert"><small>${e(titel)}</small><b>${inhalt}</b></div>`;
  ziel.classList.add('wt-seite');
  ziel.innerHTML = `
    <div class="wt-karte"><div class="d-flex align-items-center gap-3 mb-3">${icon('dashboard')}<h4 class="m-0">${e(d.produkt)} ${e(d.version)}</h4></div>
      <div class="wt-raster">
        ${wert('Version', e(d.version))}${wert('Datenbank', `${e(d.datenbank)} · Schema ${d.schemastand}`)}${wert('Laufzeit', e(d.laufzeit))}
        ${wert('Schule', e(d.schule || '–'))}${d.paket ? wert('Schuljahr', `${e(d.paket.schuljahrVon.split('-').reverse().join('.'))} – ${e(d.paket.schuljahrBis.split('-').reverse().join('.'))}`) : ''}
        ${d.paket ? wert('Daten', `${d.paket.klassen} Klassen · ${d.paket.lehrer} Lehrkräfte · ${d.paket.schueler} Schüler*innen`) : ''}
      </div></div>
    <div class="wt-karte"><h4>Letzte Übertragungen aus dem Desktop-Programm</h4>
      <div class="nl-tabelle"><table><thead><tr><th>Daten</th><th>Übertragen am</th><th>Revision</th><th>Desktop-Version</th></tr></thead><tbody>
      ${d.importe.map(i => `<tr><td class="ug-name">${e(i.name)}</td><td>${i.empfangen ? e(zeit(i.empfangen)) : '<span class="nl-chip grau">noch nie</span>'}</td>
        <td>${i.revision ?? '–'}</td><td class="nl-unter">${e(i.programmversion || '–')}</td></tr>`).join('')}</tbody></table></div></div>
    <div class="wt-karte"><h4>Module</h4><div class="wt-module">
      ${d.module.map(m => `<div class="wt-modul ${m.aktiv ? '' : 'aus'}"><i class="bi bi-${m.aktiv ? 'check-circle' : 'dash-circle'}" aria-hidden="true"></i>
        <span>${e(m.name)}<small>${m.aktiv ? 'freigeschaltet' : 'nicht eingerichtet'}${m.hinweis ? ` · ${e(m.hinweis)}` : ''}</small></span></div>`).join('')}</div></div>`;
}
