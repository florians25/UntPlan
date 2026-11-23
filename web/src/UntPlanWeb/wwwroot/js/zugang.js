// Zugang ohne Anmeldung (Leitfaden Klassenleitung, Kapitel 6): „Kennwort vergessen“ (Benutzername und hinterlegte E-Mail-Adresse →
// E-Mail mit Link → nach dem Anklicken E-Mail mit neuem Kennwort) und die Selbstregistrierung der Erziehungsberechtigten und
// Ausbildungsbetriebe über die bei den Schüler*innen hinterlegte E-Mail-Adresse. Die Formulare stehen in der Karte der Anmeldeseite
// (rahmen = anmeldeRahmen aus app.js). Adressen der Links aus den E-Mails: #/kennwort/<token>, #/registrieren/<token>.
import { e, holen, senden, alarm } from './ui.js';

const meldung = (form, art, text) => { form.querySelector('[data-meldung]').innerHTML = alarm(art, text); };
const zurAnmeldung = '<button type="button" class="lg-vergessen" data-zurueck>Zurück zur Anmeldung</button>';
const zurueckBinden = (form, weiter) => form.querySelectorAll('[data-zurueck]').forEach(b => b.onclick = () => { history.replaceState(null, '', location.pathname + '#/heute'); weiter(); });

/** Link aus einer E-Mail? Liefert true, wenn die Seite übernommen wurde. */
export function zugangsLink(rahmen, weiter) {
  const m = location.hash.match(/^#\/(kennwort|registrieren)\/([A-Za-z0-9_-]{10,})/);
  if (!m) return false;
  if (m[1] === 'kennwort') kennwortLink(rahmen, m[2], weiter);
  else registrierungLink(rahmen, m[2], weiter);
  return true;
}

/** „Kennwort vergessen“: Benutzername (z. B. nachnamevorJJ) und E-Mail-Adresse wie in UntPlanWeb gespeichert. */
export function kennwortVergessen(rahmen, weiter) {
  document.title = 'UntPlanWeb';
  const form = rahmen('Kennwort vergessen', `<form autocomplete="on">
      <label class="lg-feld"><span>Benutzername</span><input name="login" autocomplete="username" required autofocus></label>
      <label class="lg-feld"><span>E-Mail-Adresse</span><input name="email" type="email" autocomplete="email" required></label>
      <div data-meldung></div>
      <button class="btn btn-primary lg-login" type="submit">Senden</button>
      ${zurAnmeldung}
    </form>`, 'Neues Kennwort per E-Mail anfordern.');
  zurueckBinden(form, weiter);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      await senden('/api/app/kennwort-vergessen', { login: form.login.value.trim(), email: form.email.value.trim() });
      meldung(form, 'success', 'Wenn Benutzername und E-Mail-Adresse zusammenpassen, erhalten Sie in Kürze eine E-Mail mit einem Link zum Zurücksetzen des Kennworts.');
      form.querySelectorAll('input').forEach(i => { i.disabled = true; });
      btn.hidden = true;
    } catch (err) { meldung(form, 'danger', err.message); btn.disabled = false; }
  };
}

/** Selbstregistrierung: E-Mail-Adresse eingeben, die bei der Schülerin/dem Schüler hinterlegt ist. */
export function registrieren(rahmen, weiter) {
  document.title = 'UntPlanWeb';
  const form = rahmen('Registrieren', `<form autocomplete="on">
      <label class="lg-feld"><span>E-Mail-Adresse</span><input name="email" type="email" autocomplete="email" required autofocus></label>
      <div data-meldung></div>
      <button class="btn btn-primary lg-login" type="submit">Senden</button>
      ${zurAnmeldung}
    </form>`, 'Selbstregistrierung für Eltern und Betriebe.');
  zurueckBinden(form, weiter);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      await senden('/api/app/registrieren', { email: form.email.value.trim() });
      meldung(form, 'success', 'Ist die E-Mail-Adresse bei einer Schülerin bzw. einem Schüler hinterlegt, erhalten Sie in Kürze eine E-Mail mit dem Link zur Registrierung. Andernfalls wenden Sie sich bitte an die Klassenleitung.');
      form.email.disabled = true;
      btn.hidden = true;
    } catch (err) { meldung(form, 'danger', err.message); btn.disabled = false; }
  };
}

async function kennwortLink(rahmen, token, weiter) {
  document.title = 'UntPlanWeb';
  const form = rahmen('Kennwort zurücksetzen', `<form>
      <div data-meldung></div>
      <button class="btn btn-primary lg-login" type="submit">Kennwort zurücksetzen</button>
      ${zurAnmeldung}
    </form>`);
  zurueckBinden(form, weiter);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      await senden('/api/app/kennwort-zuruecksetzen', { token });
      meldung(form, 'success', 'Ihr Kennwort wurde zurückgesetzt. Sie erhalten jetzt eine E-Mail mit dem neuen Kennwort und können sich damit anmelden.');
      btn.hidden = true;
    } catch (err) { meldung(form, 'danger', err.message); btn.disabled = false; }
  };
}

async function registrierungLink(rahmen, token, weiter) {
  document.title = 'UntPlanWeb';
  let d;
  try { d = await holen(`/api/app/registrieren/${encodeURIComponent(token)}`); }
  catch (err) {
    const f = rahmen('Registrieren', `<form><div data-meldung>${alarm('danger', err.message)}</div>${zurAnmeldung}</form>`);
    zurueckBinden(f, weiter);
    return;
  }
  const form = rahmen('Registrieren', `<form autocomplete="on">
      <p class="lg-text">${e(d.rolle)} von: <b>${e(d.schueler.join(', ') || '–')}</b><br>E-Mail-Adresse: ${e(d.email)}</p>
      <label class="lg-feld"><span>Benutzername</span><input name="login" autocomplete="username" required autofocus minlength="2" maxlength="60"></label>
      <label class="lg-feld"><span>Kennwort</span><input name="passwort" type="password" autocomplete="new-password" required minlength="8"></label>
      <label class="lg-feld"><span>Kennwort wiederholen</span><input name="passwort2" type="password" autocomplete="new-password" required minlength="8"></label>
      <div data-meldung></div>
      <button class="btn btn-primary lg-login" type="submit">Registrieren</button>
      ${zurAnmeldung}
    </form>`, 'Benutzername und Kennwort festlegen.');
  zurueckBinden(form, weiter);
  form.onsubmit = async ev => {
    ev.preventDefault();
    if (form.passwort.value !== form.passwort2.value) return meldung(form, 'danger', 'Die Kennwörter stimmen nicht überein.');
    const btn = form.querySelector('button[type=submit]');
    btn.disabled = true;
    try {
      await senden(`/api/app/registrieren/${encodeURIComponent(token)}`, { login: form.login.value.trim(), passwort: form.passwort.value });
      await senden('/api/app/anmelden', { login: form.login.value.trim(), passwort: form.passwort.value });
      history.replaceState(null, '', location.pathname + '#/heute');
      weiter();
    } catch (err) { meldung(form, 'danger', err.message); btn.disabled = false; }
  };
}
