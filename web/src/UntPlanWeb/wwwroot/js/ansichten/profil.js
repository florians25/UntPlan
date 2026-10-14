// Eigenes Konto: Angaben, Passwort ändern, Kalenderabo.
import { e, icon, holen, senden, toast, bestaetigen } from '../ui.js';

export async function zeigen(seite, kontext) {
  kontext.titel('Mein Konto');
  const p = await holen('/api/app/profil');
  seite.innerHTML = `<div class="row g-3">
    <div class="col-lg-6">
      <div class="card mb-3"><div class="card-header">Angaben</div>
        <table class="table table-sm table-card mb-0"><tbody>
          <tr><th class="fw-normal text-body-secondary w-50">Benutzername</th><td>${e(p.login)}</td></tr>
          <tr><th class="fw-normal text-body-secondary">Name</th><td>${e(p.anzeigename || '–')}</td></tr>
          <tr><th class="fw-normal text-body-secondary">Rolle</th><td>${e(p.rolle)}</td></tr>
          ${p.element ? `<tr><th class="fw-normal text-body-secondary">Kürzel</th><td>${e(p.element)}</td></tr>` : ''}
          ${p.email ? `<tr><th class="fw-normal text-body-secondary">E-Mail</th><td>${e(p.email)}</td></tr>` : ''}
          <tr><th class="fw-normal text-body-secondary">Letzte Anmeldung</th><td>${p.letzteAnmeldung ? e(new Date(p.letzteAnmeldung.replace(' ', 'T')).toLocaleString('de-DE')) : '–'}</td></tr>
        </tbody></table></div>
      ${p.element ? `<div class="card"><div class="card-header">Stundenplan im Kalender abonnieren</div><div class="card-body">
        <p class="small text-body-secondary">Persönlicher Link für Outlook, Google Kalender oder das Smartphone (Stunden und Vertretungen der nächsten vier Wochen). Bitte nicht weitergeben.</p>
        <div class="input-group mb-2"><input class="form-control font-monospace small bg-body-tertiary" data-ical value="${e(p.ical)}" readonly>
          <button class="btn btn-outline-secondary" type="button" data-kopie title="Kopieren">${icon('kopieren')}</button></div>
        <button class="btn btn-sm btn-outline-secondary" data-neu>Neuen Link erzeugen</button>
      </div></div>` : ''}
    </div>
    <div class="col-lg-6">
      <div class="card"><div class="card-header">Passwort ändern</div><div class="card-body">
        <form>
          <div class="mb-3"><label class="form-label">Bisheriges Passwort</label><input class="form-control" name="alt" type="password" required autocomplete="current-password"></div>
          <div class="mb-3"><label class="form-label">Neues Passwort</label><input class="form-control" name="neu" type="password" minlength="8" required autocomplete="new-password"><div class="form-text">Mindestens 8 Zeichen.</div></div>
          <div class="mb-3"><label class="form-label">Neues Passwort wiederholen</label><input class="form-control" name="neu2" type="password" minlength="8" required autocomplete="new-password"></div>
          <button class="btn btn-primary" type="submit">Passwort ändern</button>
        </form></div></div>
    </div>
  </div>`;
  const form = seite.querySelector('form');
  form.onsubmit = async ev => {
    ev.preventDefault();
    if (form.neu.value !== form.neu2.value) { toast('Die neuen Passwörter stimmen nicht überein.', true); return; }
    try { await senden('/api/app/passwort', { alt: form.alt.value, neu: form.neu.value }); form.reset(); toast('Passwort geändert.'); }
    catch (err) { kontext.fehler(err); }
  };
  seite.querySelector('[data-kopie]')?.addEventListener('click', () => {
    const t = seite.querySelector('[data-ical]').value;
    navigator.clipboard?.writeText(t).then(() => toast('Link kopiert.'), () => toast('Bitte den Link manuell kopieren.', true));
  });
  seite.querySelector('[data-neu]')?.addEventListener('click', async () => {
    if (!await bestaetigen('Neuen Kalenderlink erzeugen', 'Der bisherige Link funktioniert danach nicht mehr.', 'Neu erzeugen')) return;
    const r = await senden('/api/app/profil/ical', {});
    seite.querySelector('[data-ical]').value = r.ical;
    toast('Neuer Link erzeugt.');
  });
}
