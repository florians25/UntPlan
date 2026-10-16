// Administration → Übersicht: Version, Datenbank und Updates (UW-02). Wird von admin.js in die Übersicht eingebunden.
// Der Hinweis „Update verfügbar“ erscheint je Benutzer und Version nur einmal als schwebende Meldung (keine Layoutverschiebung)
// und lässt sich schließen; die Karte „Version & Updates“ wird an ihrem Platz aktualisiert statt neu angehängt.
import { e, icon, holen, senden, toast, bestaetigen } from '../ui.js';

const zeit = s => s ? new Date(s).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '';
const HINWEIS = 'untplan:update-hinweis';
/** laufende Abfrage je Ziel – ältere (langsamere) Antworten werden verworfen */
const laeufe = new WeakMap();

function hinweisGeschlossen(login, version) {
  try { return localStorage.getItem(`${HINWEIS}:${login || '-'}`) === version; } catch { return false; }
}

/** Schwebender, schließbarer Hinweis (einmal je Benutzer und Version). */
function hinweisZeigen(u, login) {
  if (!u.updateVerfuegbar || !u.neueste || hinweisGeschlossen(login, u.neueste) || document.querySelector('[data-update-hinweis]')) return;
  const h = document.createElement('div');
  h.className = 'up-hinweis';
  h.dataset.updateHinweis = '';
  h.setAttribute('role', 'status');
  h.innerHTML = `<i class="bi bi-arrow-up-circle" aria-hidden="true"></i>
    <div><strong>Update verfügbar</strong><span>UntPlanWeb ${e(u.neueste)} (installiert: ${e(u.version)})${u.hinweisurl ? ` · <a href="${e(u.hinweisurl)}" target="_blank" rel="noopener">Änderungen</a>` : ''}</span></div>
    <button type="button" class="btn-close" aria-label="Hinweis schließen"></button>`;
  const schliessen = () => {
    try { localStorage.setItem(`${HINWEIS}:${login || '-'}`, u.neueste); } catch { /* privat */ }
    h.remove();
  };
  h.querySelector('.btn-close').onclick = schliessen;
  // beim Verlassen der Übersicht verschwindet der Hinweis (er gehört zur Übersicht)
  const weg = () => { h.remove(); window.removeEventListener('hashchange', weg); };
  window.addEventListener('hashchange', weg);
  document.body.append(h);
}

/** Karte „Version & Updates“ am Ende der Übersicht anfügen bzw. an ihrem Platz aktualisieren. */
export async function einbinden(ziel, neu = false, login = '') {
  const lauf = (laeufe.get(ziel) ?? 0) + 1;
  laeufe.set(ziel, lauf);
  const alt = ziel.querySelector('[data-update-karte]');
  alt?.querySelector('[data-update-pruefen]')?.setAttribute('disabled', '');
  let u;
  try { u = await holen('/api/app/admin/update' + (neu ? '?neu=1' : '')); }
  catch { alt?.querySelector('[data-update-pruefen]')?.removeAttribute('disabled'); return; }
  // inzwischen neu geprüft, Seite gewechselt oder neu gezeichnet: nichts mehr anfügen
  if (laeufe.get(ziel) !== lauf || !ziel.isConnected) return;

  const status = !u.pruefungAktiv
    ? '<span class="text-body-secondary">Prüfung abgeschaltet</span>'
    : u.fehler ? `<span class="text-danger">${e(u.fehler)}</span>`
    : u.updateVerfuegbar ? `<span class="badge text-bg-warning">Update verfügbar</span> ${e(u.neueste)}${u.hinweisurl ? ` · <a href="${e(u.hinweisurl)}" target="_blank" rel="noopener">Änderungen</a>` : ''}`
    : `<span class="badge text-bg-success">aktuell</span> <span class="text-body-secondary">neueste: ${e(u.neueste ?? '–')}</span>`;
  const aktion = u.installierbar
    ? `<button class="btn btn-${u.updateVerfuegbar ? 'primary' : 'outline-secondary'} btn-sm" data-update-installieren>${icon('herunterladen', 'me-1')}Update installieren</button>`
    : `<div class="small text-body-secondary mb-1">Update auf dem Server (Sicherung vorher, Migrationen laufen beim Start automatisch):</div>
       <code class="d-block p-2 bg-body-tertiary rounded small user-select-all">${e(u.befehl)}</code>`;

  const karte = document.createElement('div');
  karte.className = 'col-xl-6';
  karte.dataset.updateKarte = '';
  karte.innerHTML = `<div class="card h-100"><div class="card-header d-flex align-items-center">Version &amp; Updates
      <button class="btn btn-outline-secondary btn-sm ms-auto" data-update-pruefen title="Erneut prüfen">${icon('aktualisieren')}</button></div>
    <table class="table table-sm table-card mb-0"><tbody>
      <tr><th class="fw-normal text-body-secondary w-25">UntPlanWeb</th><td>Version ${e(u.version)}</td></tr>
      <tr><th class="fw-normal text-body-secondary">Neueste</th><td>${status}${u.geprueft ? `<div class="small text-body-secondary">geprüft ${e(zeit(u.geprueft))}</div>` : ''}</td></tr>
      ${u.mobil ? `<tr><th class="fw-normal text-body-secondary">UntPlanMobile</th><td>Version ${e(u.mobil.version)}${u.mobil.updateVerfuegbar ? ' <span class="badge text-bg-warning">Update verfügbar</span>' : ''}</td></tr>` : ''}
      <tr><th class="fw-normal text-body-secondary">Datenbank</th><td>${e(u.datenbank)} <span class="text-body-secondary">· Schemastand ${e(u.schemastand)}</span></td></tr>
      ${u.angefordert ? `<tr><th class="fw-normal text-body-secondary">Angefordert</th><td>${e(u.angefordert)} – wartet auf das Update-Skript</td></tr>` : ''}
    </tbody></table>
    <div class="card-body">${aktion}</div></div>`;
  const vorhanden = ziel.querySelector('[data-update-karte]');
  if (vorhanden) vorhanden.replaceWith(karte);
  else (ziel.querySelector('.row') ?? ziel).append(karte);
  hinweisZeigen(u, login);

  karte.querySelector('[data-update-pruefen]').onclick = () => einbinden(ziel, true, login);
  const knopf = karte.querySelector('[data-update-installieren]');
  if (knopf) knopf.onclick = async () => {
    if (!await bestaetigen('Update installieren', 'Die Datenbank wird gesichert (SQLite) und das neueste Image geladen. UntPlanWeb startet danach neu und ist kurz nicht erreichbar.', 'Installieren')) return;
    knopf.disabled = true;
    try { const r = await senden('/api/app/admin/update/installieren', {}); toast(r.meldung); await einbinden(ziel, false, login); }
    catch (err) { toast(err.message, true); knopf.disabled = false; }
  };
}
