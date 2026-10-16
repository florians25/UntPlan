// Mein Profil wie im Original: Reiter „Allgemein“ (E-Mail, Sprache, Benachrichtigungen, Passwort ändern) und „Freigaben“
// (Kalenderabo, gemerkte Konten auf diesem Gerät).
import { e, icon, holen, senden, api, toast, bestaetigen, dialog } from '../ui.js';

export async function zeigen(seite, kontext, [reiterParam]) {
  const reiter = reiterParam === 'freigaben' ? 'freigaben' : 'allgemein';
  kontext.titel('Mein Profil');
  const p = await holen('/api/app/profil');
  const datum = s => s ? new Date(s.replace(' ', 'T')).toLocaleString('de-DE') : '–';
  seite.innerHTML = `
    <ul class="nav nav-tabs mb-3"><li class="nav-item"><a class="nav-link ${reiter === 'allgemein' ? 'active' : ''}" href="#/profil">Allgemein</a></li>
      <li class="nav-item"><a class="nav-link ${reiter === 'freigaben' ? 'active' : ''}" href="#/profil/freigaben">Freigaben</a></li></ul>
    <div data-inhalt></div>`;
  const inhalt = seite.querySelector('[data-inhalt]');

  if (reiter === 'allgemein') {
    inhalt.innerHTML = `<div class="card" style="max-width:860px"><div class="card-body p-4">
      <div class="small fw-bold text-body-secondary text-uppercase">${e(p.rolle)}</div>
      <div class="fs-4 fw-semibold text-body-emphasis mb-3">${e(p.anzeigename || p.login)}</div><hr>
      <form data-profil><div class="row g-3">
        <div class="col-md-6"><label class="form-label">Benutzername</label><input class="form-control" value="${e(p.login)}" readonly></div>
        <div class="col-md-6"><label class="form-label">${p.kollegium ? 'Lehrerkürzel' : 'Klasse'}</label><input class="form-control" value="${e(p.element || '–')}" readonly></div>
        <div class="col-md-6"><label class="form-label">E-Mail-Adresse</label><input class="form-control" type="email" name="email" value="${e(p.email)}"></div>
        <div class="col-md-6"><label class="form-label">Sprache</label><select class="form-select" name="sprache"><option ${p.sprache !== 'English' ? 'selected' : ''}>Deutsch</option><option ${p.sprache === 'English' ? 'selected' : ''}>English</option></select></div>
        <div class="col-md-6"><label class="form-label">Benutzergruppe</label><input class="form-control" value="${e(p.rolle)}" readonly></div>
        <div class="col-md-6"><label class="form-label">Letzte Anmeldung</label><input class="form-control" value="${e(datum(p.vorherigeAnmeldung))}" readonly></div>
      </div>
      <div class="form-check mt-3"><input class="form-check-input" type="checkbox" name="benachrichtigung" id="pf-b" ${p.benachrichtigung ? 'checked' : ''}>
        <label class="form-check-label" for="pf-b">Benachrichtigungen über neue Mitteilungen erhalten (Hinweis im Browser bzw. auf dem Gerät)</label></div>
      <div class="d-flex flex-wrap gap-2 mt-4"><button class="btn btn-primary" type="submit">Speichern</button><button class="btn btn-outline-secondary" type="button" data-passwort>Passwort ändern</button></div>
      </form></div></div>`;
    const form = inhalt.querySelector('[data-profil]');
    form.onsubmit = async ev => {
      ev.preventDefault();
      try {
        await senden('/api/app/profil', { email: form.email.value, sprache: form.sprache.value, benachrichtigung: form.benachrichtigung.checked }, 'PUT');
        // Systemmeldungen des Browsers für neue Mitteilungen erlauben
        if (form.benachrichtigung.checked && 'Notification' in window && Notification.permission === 'default') { try { await Notification.requestPermission(); } catch { /* nicht unterstützt */ } }
        await kontext.sitzungNeu?.();
        toast('Gespeichert.');
      }
      catch (err) { kontext.fehler(err); }
    };
    inhalt.querySelector('[data-passwort]').onclick = () => dialog({
      titel: 'Passwort ändern',
      inhalt: `<div class="mb-3"><label class="form-label">Bisheriges Passwort</label><input class="form-control" name="alt" type="password" required autocomplete="current-password"></div>
        <div class="mb-3"><label class="form-label">Neues Passwort</label><input class="form-control" name="neu" type="password" minlength="8" required autocomplete="new-password"><div class="form-text">Mindestens 8 Zeichen (bzw. nach der Sicherheitsrichtlinie der Schule).</div></div>
        <div><label class="form-label">Neues Passwort wiederholen</label><input class="form-control" name="neu2" type="password" minlength="8" required autocomplete="new-password"></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Passwort ändern', art: 'haupt', aktion: async box => {
        const w = n => box.querySelector(`[name=${n}]`).value;
        if (w('neu') !== w('neu2')) { toast('Die neuen Passwörter stimmen nicht überein.', true); return false; }
        await senden('/api/app/passwort', { alt: w('alt'), neu: w('neu') });
        toast('Passwort geändert.');
      } }],
    });
    return;
  }

  const konten = await holen('/api/app/konten').catch(() => []);
  inhalt.innerHTML = `<div class="row g-3" style="max-width:1100px">
    ${p.element ? `<div class="col-lg-7"><div class="card h-100"><div class="card-header">Stundenplan im Kalender abonnieren</div><div class="card-body">
      <p class="small text-body-secondary">Persönlicher Link für Outlook, Google Kalender oder das Smartphone (Stunden und Vertretungen der nächsten vier Wochen). Bitte nicht weitergeben.</p>
      <div class="input-group mb-2"><input class="form-control font-monospace small bg-body-tertiary" data-ical value="${e(p.ical)}" readonly>
        <button class="btn btn-outline-secondary" type="button" data-kopie title="Kopieren">${icon('kopieren')}</button></div>
      <button class="btn btn-sm btn-outline-secondary" data-neu>Neuen Link erzeugen</button></div></div></div>` : ''}
    <div class="col-lg-5"><div class="card h-100"><div class="card-header">Konten auf diesem Gerät</div>
      ${konten.length ? `<ul class="u-liste">${konten.map(k => `<li><span class="u-avatar">${e(k.login.slice(0, 1).toUpperCase())}</span><div class="u-zeilentext"><div class="titel">${e(k.login)}${k.aktuell ? ' <span class="u-chip gruen ms-1">angemeldet</span>' : ''}</div>
        <div class="unter">${e(k.rolle)}</div></div>${k.aktuell ? '' : `<button class="btn btn-sm btn-outline-secondary" data-vergessen="${k.id}">Entfernen</button>`}</li>`).join('')}</ul>`
        : '<div class="card-body small text-body-secondary">Keine weiteren Konten gemerkt.</div>'}
      <div class="card-footer small text-body-secondary">Über das Konto-Menü unten links wechseln Sie zwischen den hier gemerkten Konten, ohne sich neu anzumelden (30 Tage).</div></div></div>
  </div>`;
  inhalt.querySelector('[data-kopie]')?.addEventListener('click', () => {
    const t = inhalt.querySelector('[data-ical]').value;
    navigator.clipboard?.writeText(t).then(() => toast('Link kopiert.'), () => toast('Bitte den Link manuell kopieren.', true));
  });
  inhalt.querySelector('[data-neu]')?.addEventListener('click', async () => {
    if (!await bestaetigen('Neuen Kalenderlink erzeugen', 'Der bisherige Link funktioniert danach nicht mehr.', 'Neu erzeugen')) return;
    const r = await senden('/api/app/profil/ical', {});
    inhalt.querySelector('[data-ical]').value = r.ical;
    toast('Neuer Link erzeugt.');
  });
  inhalt.querySelectorAll('[data-vergessen]').forEach(b => b.onclick = async () => { await api(`/api/app/konten/${b.dataset.vergessen}`, { methode: 'DELETE' }); kontext.navigieren(location.hash); });
}
