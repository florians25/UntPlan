// Eigenes Profil: Passwort ändern, Kalenderabo.
import { e, icon, holen, senden, toast, bestaetigen } from '../ui.js';

export async function zeigen(seite, kontext) {
  kontext.titel('Mein Profil');
  const p = await holen('/api/app/profil');
  seite.innerHTML = `<div class="raster raster-2">
    <div class="karte"><div class="karte-kopf">${icon('profil')}<h2>${e(p.anzeigename || p.login)}</h2></div><div class="karte-inhalt">
      <table class="tabelle"><tbody>
        <tr><th>Benutzername</th><td>${e(p.login)}</td></tr>
        <tr><th>Rolle</th><td>${e(p.rolle)}</td></tr>
        ${p.element ? `<tr><th>Kürzel</th><td>${e(p.element)}</td></tr>` : ''}
        ${p.email ? `<tr><th>E-Mail</th><td>${e(p.email)}</td></tr>` : ''}
        <tr><th>Letzte Anmeldung</th><td>${p.letzteAnmeldung ? e(new Date(p.letzteAnmeldung.replace(' ', 'T')).toLocaleString('de-DE')) : '–'}</td></tr>
      </tbody></table></div></div>
    <div class="karte"><div class="karte-kopf">${icon('schluessel')}<h2>Passwort ändern</h2></div><div class="karte-inhalt">
      <form class="formular">
        <label class="feld">Bisheriges Passwort<input name="alt" type="password" required autocomplete="current-password"></label>
        <div class="zeile"><label class="feld">Neues Passwort<input name="neu" type="password" minlength="8" required autocomplete="new-password"></label>
          <label class="feld">Wiederholen<input name="neu2" type="password" minlength="8" required autocomplete="new-password"></label></div>
        <div><button class="knopf haupt" type="submit">Passwort ändern</button></div>
      </form></div></div>
    ${p.element ? `<div class="karte"><div class="karte-kopf">${icon('kalender')}<h2>Stundenplan im Kalender</h2></div><div class="karte-inhalt formular">
      <p class="gedimmt" style="margin:0">Abonnieren Sie Ihren Stundenplan (inkl. Vertretungen der nächsten vier Wochen) in Outlook, Google Kalender oder auf dem Smartphone.
        Der Link ist persönlich – bitte nicht weitergeben.</p>
      <div class="schluessel-zeile"><div class="code klein" data-ical>${e(p.ical)}</div><button class="knopf rund" data-kopie title="Kopieren">${icon('kopieren', 'icon-klein')}</button></div>
      <div><button class="knopf klein" data-neu>${icon('aktualisieren', 'icon-klein')} Neuen Link erzeugen</button></div>
    </div></div>` : ''}
  </div>`;
  const form = seite.querySelector('form');
  form.onsubmit = async ev => {
    ev.preventDefault();
    if (form.neu.value !== form.neu2.value) { toast('Die neuen Passwörter stimmen nicht überein.', true); return; }
    try { await senden('/api/app/passwort', { alt: form.alt.value, neu: form.neu.value }); form.reset(); toast('Passwort geändert.'); }
    catch (err) { kontext.fehler(err); }
  };
  seite.querySelector('[data-kopie]')?.addEventListener('click', () => {
    const t = seite.querySelector('[data-ical]').textContent;
    navigator.clipboard?.writeText(t).then(() => toast('Link kopiert.'), () => toast('Bitte den Link manuell kopieren.', true));
  });
  seite.querySelector('[data-neu]')?.addEventListener('click', async () => {
    if (!await bestaetigen('Neuen Kalenderlink erzeugen', 'Der bisherige Link funktioniert danach nicht mehr.', 'Neu erzeugen')) return;
    const r = await senden('/api/app/profil/ical', {});
    seite.querySelector('[data-ical]').textContent = r.ical;
    toast('Neuer Link erzeugt.');
  });
}
