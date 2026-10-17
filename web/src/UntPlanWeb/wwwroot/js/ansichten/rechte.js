// Administration → Rechte und Rollen: Rollen als Rechte-Profile, Rollen anlegen/kopieren/bearbeiten/löschen, Rechte einzelner Benutzer
// (Profil, Überschreibungen, Vorschau der effektiven Rechte) und Passwortregeln.
// Rollen & Rechte: Rechte in Abschnitten je Gruppe (Überschrift mit Symbol und Anzahl), Suche und Filter, fixierte Spaltenköpfe
// (Rollen mit Farbe und Symbol), Stufen „Kein Zugriff / Lesen / Schreiben“ als Segment-Pillen. Auf kleinen Bildschirmen (und auf Wunsch)
// Rolle für Rolle statt der breiten Matrix. Alle Rechte kommen aus dem Katalog des Servers – neue Rechte und Gruppen erscheinen automatisch.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, formularWerte, suchfeld, suchfeldMitFilter, suchfeldMitFilterBinden, segment, segmentSetzen, infoHinweis } from '../ui.js';

const STUFE = { 0: 'Nein', 1: 'Lesen', 2: 'Ja' };
const STUFE2 = { 0: 'Nein', 1: 'Lesen', 2: 'Schreiben' };
const stufen = r => r.zweistufig ? STUFE2 : { 0: 'Nein', 2: 'Ja' };
/** Stufen als Segment-Pillen: [Wert, Text, Symbol]. Einstufige Rechte kennen nur „Kein Zugriff“ und „Zugriff“. */
const PILLEN = r => r.zweistufig
  ? [[0, 'Kein Zugriff', 'slash-circle'], [1, 'Lesen', 'eye'], [2, 'Schreiben', 'pencil']]
  : [[0, 'Kein Zugriff', 'slash-circle'], [2, 'Zugriff', 'check-lg']];

/** Farbe und Symbol je Basisrolle (eigene Rollen übernehmen die Farbe ihrer Basis). */
const ROLLEN = {
  admin: ['#414d55', 'shield-lock'], leitung: ['#7c5cc4', 'award'], lehrer: ['#2f7fc1', 'person-workspace'],
  schueler: ['#2f9e6b', 'mortarboard'], eltern: ['#d9822b', 'people'],
};
const rollenStil = p => ROLLEN[p.basis] ?? ['#67747e', 'person-gear'];
/** Symbole der Rechte-Gruppen; unbekannte (neue) Gruppen erhalten ein neutrales Symbol. */
const GRUPPEN = {
  'Stundenplan-Zeitraum': 'calendar-range', Stundenplan: 'calendar3', Vertretungsplan: 'arrow-left-right', Klassenbuch: 'journal-text', Abwesenheiten: 'person-x',
  Hausaufgaben: 'pencil-square', 'Prüfungen': 'file-earmark-check', 'Raumbuchung & Ressourcen': 'door-open', Kommunikation: 'chat-square-text',
  Sprechtag: 'people', Berichte: 'file-earmark-bar-graph', 'Schüler*innen': 'mortarboard', 'Offene Stunden': 'hourglass-split', Administration: 'gear',
};
const norm = t => String(t ?? '').toLocaleLowerCase('de-DE');

function stil() {
  if (document.getElementById('rechte-stil')) return;
  const s = document.createElement('style');
  s.id = 'rechte-stil';
  s.textContent = `
    .rm-leiste { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 12px; margin-bottom: 12px; }
    .rm-leiste .u-suchfeld { flex: 1 1 280px; max-width: 420px; }
    .rm-leiste .rm-gruppenwahl { width: 220px; flex: 0 1 220px; }
    .rm-legende .rm-ansicht { margin-left: auto; }
    .rm-legende { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 14px; margin: 0 0 14px; font-size: .8rem; color: #67747e; }
    .rm-legende .rm-l { display: inline-flex; align-items: center; gap: 6px; }
    .rm-legende .rm-l i { display: inline-grid; place-content: center; width: 22px; height: 22px; border-radius: 50%; font-size: .72rem; }
    .rm-l.s0 i, .rm-stufen > button.an.s0 { background: #fff; color: #57626b; }
    .rm-l.s0 i { background: #eef0f2; }
    .rm-l.s1 i, .rm-stufen > button.an.s1 { background: #fff3cf; color: #7a5600; }
    .rm-l.s2 i, .rm-stufen > button.an.s2 { background: #dff3e7; color: #0f6b40; }
    .rm-legende .rm-trenn { width: 1px; height: 14px; background: #d5dae0; }

    .rm-matrix { --rm-recht: minmax(250px, 1.35fr); --rm-min: 150px; position: relative; overflow: auto; max-height: max(440px, calc(100dvh - 250px));
      background: #fff; border: 1px solid #dfe3e7; border-radius: 12px; overscroll-behavior: contain; }
    .rm-zeile { display: grid; grid-template-columns: var(--rm-recht) repeat(var(--rm-n), minmax(var(--rm-min), 1fr)); min-width: calc(250px + var(--rm-n) * var(--rm-min)); }
    .rm-zeile > * { min-width: 0; }
    .rm-kopfzeile { position: sticky; top: 0; z-index: 4; background: #fff; border-bottom: 1px solid #dfe3e7; box-shadow: 0 4px 10px -8px rgba(0, 5, 10, .25); }
    .rm-kopfzeile > .rm-recht { display: flex; flex-direction: column; justify-content: flex-end; padding: 14px 16px; font-size: .78rem; font-weight: 500; color: #8a96a1; }
    .rm-recht { position: sticky; left: 0; z-index: 2; background: inherit; padding: 10px 16px; border-right: 1px solid #edf0f2; }
    .rm-kopfzeile > .rm-recht { z-index: 5; background: #fff; }
    .rm-rolle { position: relative; display: flex; flex-direction: column; gap: 6px; padding: 14px 12px 10px; border-top: 3px solid var(--rolle); }
    .rm-rolle-name { display: flex; align-items: center; gap: 8px; min-width: 0; }
    .rm-rolle-symbol { flex: none; display: inline-grid; place-content: center; width: 30px; height: 30px; border-radius: 50%; background: color-mix(in srgb, var(--rolle) 14%, #fff);
      color: var(--rolle); font-size: .95rem; }
    .rm-rolle-name b { font-weight: 500; color: #24323d; font-size: .9rem; line-height: 1.2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .rm-rolle-meta { font-size: .74rem; color: #8a96a1; line-height: 1.3; }
    .rm-rolle-meta a { color: var(--u-akzent-dunkel, #dd5718); text-decoration: none; font-weight: 500; }
    .rm-rolle-aktionen { display: flex; gap: 2px; }
    .rm-rolle-aktionen button { width: 28px; height: 28px; display: inline-grid; place-content: center; border: 0; border-radius: 50%; background: none; color: #67747e; font-size: .85rem; }
    .rm-rolle-aktionen button:hover { background: #f1f3f5; color: #24323d; }
    .rm-rolle-aktionen button.rot:hover { background: #fde8e8; color: #b42318; }
    .rm-gruppe-titel { position: sticky; left: 0; display: flex; align-items: center; gap: 10px; margin: 0; padding: 16px 16px 8px; background: #f8f9fa; border-top: 1px solid #edf0f2;
      font-size: .82rem; font-weight: 500; color: #414d55; letter-spacing: .01em; }
    .rm-gruppe:first-of-type .rm-gruppe-titel { border-top: 0; }
    .rm-gruppe-titel .bi { color: #8a96a1; font-size: 1rem; }
    .rm-gruppe-titel small { font-weight: 400; color: #8a96a1; }
    .rm-gruppe { background: #fff; }
    .rm-matrix .rm-gruppe-titel { width: 100%; min-width: calc(250px + var(--rm-n) * var(--rm-min)); }
    .rm-zeile.rm-recht-zeile { background: #fff; border-top: 1px solid #f0f2f4; }
    .rm-zeile.rm-recht-zeile:hover { background: #fafbfc; }
    .rm-recht b { display: block; font-weight: 500; color: #24323d; font-size: .9rem; line-height: 1.3; }
    .rm-recht small { display: block; margin-top: 2px; font-size: .76rem; color: #8a96a1; line-height: 1.35; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
    .rm-zelle { display: flex; align-items: center; padding: 8px 12px; container-type: inline-size; }
    .rm-stufen { max-width: 100%; }
    .rm-stufen > button { padding: 0 10px; }
    @container (max-width: 232px) { .rm-stufen > button:not(.an) span { display: none; } .rm-stufen > button:not(.an) { width: 30px; padding: 0; } }
    @container (max-width: 150px) { .rm-stufen > button span { display: none; } .rm-stufen > button { width: 30px; padding: 0; } }
    .rm-zelle.geaendert .rm-stufen, .rm-zelle.geaendert .rm-wochen { box-shadow: 0 0 0 2px var(--u-akzent, #f56b2a); }
    .rm-fest { display: inline-flex; align-items: center; gap: 6px; height: 28px; padding: 0 10px; border-radius: 999px; background: #dff3e7; color: #0f6b40; font-size: .78rem; font-weight: 500; white-space: nowrap; }
    .rm-na { font-size: .76rem; color: #b7c0c7; }
    .rm-wochen { width: 100%; max-width: 9rem; height: 32px; padding: 0 10px; border: 1px solid #d5dae0; border-radius: 8px; font: inherit; font-size: .85rem; }
    .rm-wochen:focus { outline: 0; border-color: var(--u-akzent); box-shadow: 0 0 0 3px rgba(var(--u-akzent-rgb), .12); }
    .rm-wochen:placeholder-shown { color: #8a96a1; }
    .rm-leer { padding: 40px 16px; text-align: center; color: #8a96a1; }
    .rm-fuss { margin-top: 12px; font-size: .78rem; color: #8a96a1; line-height: 1.5; max-width: 980px; }
    .rm-speichern { position: sticky; bottom: 16px; z-index: 30; display: flex; align-items: center; gap: 10px; width: fit-content; max-width: 100%; margin: 14px 0 0 auto; padding: 8px 8px 8px 18px;
      background: #24323d; color: #fff; border-radius: 999px; box-shadow: 0 10px 28px rgba(0, 5, 10, .25); font-size: .88rem; }
    .rm-speichern[hidden] { display: none; }
    .rm-speichern .btn-link { color: #fff; opacity: .8; text-decoration: none; font-weight: 500; }
    .rm-speichern .btn-link:hover { opacity: 1; }

    /* Rolle für Rolle */
    .rm-rollenwahl { display: flex; gap: 8px; overflow-x: auto; padding: 2px 2px 10px; margin-bottom: 4px; scrollbar-width: thin; }
    .rm-rollenwahl button { flex: none; display: inline-flex; align-items: center; gap: 8px; height: 40px; padding: 0 14px 0 6px; border: 1px solid #dfe3e7; border-radius: 999px;
      background: #fff; color: #414d55; font-weight: 500; font-size: .875rem; white-space: nowrap; }
    .rm-rollenwahl button .rm-rolle-symbol { width: 28px; height: 28px; font-size: .85rem; }
    .rm-rollenwahl button.an { border-color: var(--rolle); box-shadow: 0 0 0 1px var(--rolle); color: #24323d; }
    .rm-rollenkarte { background: #fff; border: 1px solid #dfe3e7; border-radius: 12px; overflow: hidden; }
    .rm-rollenkarte > .rm-rolle { flex-direction: row; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 14px 16px; border-bottom: 1px solid #edf0f2; }
    .rm-rollenkarte > .rm-rolle .rm-rolle-aktionen { margin-left: auto; }
    .rm-rollenkarte .rm-gruppe-titel { position: static; }
    .rm-eintrag { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 16px; padding: 12px 16px; border-top: 1px solid #f0f2f4; }
    .rm-eintrag .rm-recht { position: static; padding: 0; border: 0; flex: 1 1 260px; }
    .rm-eintrag .rm-zelle { padding: 0; container-type: normal; }
    .rm-eintrag .rm-wochen { width: 9rem; }
    @media (max-width: 991.98px) {
      .rm-legende .rm-ansicht { display: none; }
      .rm-leiste .u-suchfeld { max-width: none; flex-basis: 100%; }
      .rm-leiste .rm-gruppenwahl { flex: 1 1 100%; width: auto; }
    }
    @media (max-width: 575.98px) {
      .rm-eintrag .rm-zelle { width: 100%; }
      .rm-eintrag .rm-stufen { width: 100%; }
      .rm-eintrag .rm-stufen > button { flex: 1 1 0; }
      .rm-speichern { left: 0; right: 0; width: auto; margin-left: 0; border-radius: 14px; }
    }

    /* Rechte einzelner Benutzer */
    .rm-tabelle { font-size: .85rem; }
    .rm-tabelle thead th { position: sticky; top: 0; background: #fff; z-index: 2; font-weight: 500; color: #8a96a1; font-size: .78rem; }
    .rm-tabelle .rm-gruppe-zeile td { background: #f8f9fa; font-weight: 500; font-size: .8rem; color: #414d55; }
    .rm-tabelle .rm-gruppe-zeile .bi { color: #8a96a1; margin-right: 8px; }
    .rm-wert { display: inline-block; min-width: 4.5rem; text-align: center; border-radius: 999px; padding: .1rem .55rem; font-size: .76rem; font-weight: 500; }
    .rm-wert.s0 { background: #eef0f2; color: #6c757d; } .rm-wert.s1 { background: #fff3cf; color: #7a5600; } .rm-wert.s2 { background: #dff3e7; color: #0f6b40; }
    .rm-quelle { font-size: .75rem; color: #8a96a1; }
    .rm-quelle.gewaehrt { color: #0f6b40; font-weight: 500; } .rm-quelle.entzogen { color: #a1262b; font-weight: 500; }
    .rm-beschreibung { font-size: .76rem; color: #8a96a1; }
    @media (max-width: 767.98px) { .rm-tabelle.u-karten-tabelle > tbody > tr.rm-gruppe-zeile { padding: 10px 16px; background: #f8f9fa; } .rm-tabelle.u-karten-tabelle > tbody > tr.rm-gruppe-zeile > td { position: static; } }
  `;
  document.head.append(s);
}

// Planfenster: Wochen zurück bzw. voraus (leer = unbegrenzt, 0 = nur die aktuelle Woche)
const FENSTER = [
  ['planWochenZurueck', 'Wochen zurück', 'Wie viele Wochen vor der aktuellen Woche der Stundenplan angezeigt wird. Leer = unbegrenzt, 0 = keine früheren Wochen.'],
  ['planWochenVor', 'Wochen voraus', 'Wie viele Wochen nach der aktuellen Woche der Stundenplan angezeigt wird. Leer = unbegrenzt, 0 = keine künftigen Wochen.'],
];
const wochenZahl = v => (v === '' || v == null ? null : Math.max(0, Math.round(Number(v))));
const fensterText = v => v == null ? 'unbegrenzt' : v === 1 ? '1 Woche' : `${v} Wochen`;
/** Felder eines Profils für PUT (Planfenster immer mitsenden, sonst gilt „unbegrenzt“). */
const fensterWerte = (p, a = {}) => Object.fromEntries(FENSTER.map(([f]) => [f, f in a ? a[f] : p[f] ?? null]));

const wert = (s, r) => `<span class="rm-wert s${s}">${(r?.zweistufig ? STUFE2 : STUFE)[s] ?? s}</span>`;
const ansichtMerken = { lesen: () => { try { return localStorage.getItem('untplan:rechte-ansicht') || 'matrix'; } catch { return 'matrix'; } },
  schreiben: v => { try { localStorage.setItem('untplan:rechte-ansicht', v); } catch { /* privat */ } } };

export async function zeigen(ziel, kontext, reiter = 'rollen') {
  stil();
  reiter = String(reiter).split('?')[0];
  const tabs = [['rollen', 'Rollen & Rechte'], ['benutzer', 'Rechte einzelner Benutzer'], ['passwort', 'Passwortregeln']];
  ziel.innerHTML = `<ul class="nav nav-tabs mb-3">${tabs.map(([id, t]) => `<li class="nav-item"><a class="nav-link ${reiter === id ? 'active' : ''}" href="#/admin/rechte/${id}">${t}</a></li>`).join('')}</ul><div data-reiter></div>`;
  const box = ziel.querySelector('[data-reiter]');
  if (reiter === 'benutzer') await benutzerrechte(box, kontext);
  else if (reiter === 'passwort') await passwort(box);
  else await rollen(box, kontext);
}

// ------------------------------------------------------------------ Rollen × Rechte

/** Gruppen in Katalog-Reihenfolge, ergänzt um Gruppen neuer Rechte, die (noch) nicht in der Gruppenliste stehen. */
const gruppenAus = k => [...new Set([...(k.gruppen ?? []), ...k.rechte.map(r => r.gruppe || 'Weitere Rechte')])];

async function rollen(box, kontext) {
  const k = await holen('/api/app/admin/rechte/katalog');
  const profile = k.profile;
  const gruppen = gruppenAus(k);
  const aenderungen = {}; // profilId → { recht: stufe }
  const fenster = {}; // profilId → { planWochenZurueck, planWochenVor }
  const zustand = { suche: '', gruppe: '', filter: 'alle', ansicht: ansichtMerken.lesen(), rolle: profile.find(p => p.aenderbar)?.id ?? profile[0]?.id };
  const schmal = matchMedia('(max-width: 991.98px)');
  const ansicht = () => schmal.matches ? 'rolle' : zustand.ansicht;
  kontext.titel('Rechte und Rollen', `<button class="btn btn-primary" data-neu>${icon('plus')}Neue Rolle</button>`);

  const istWert = (p, r) => aenderungen[p.id]?.[r.id] ?? p.rechte[r.id] ?? r.standard?.[p.basis] ?? 0;
  const gesperrt = (p, r) => r.nurKollegium && (p.basis === 'schueler' || p.basis === 'eltern');
  const abweichend = r => profile.some(p => p.aenderbar && !gesperrt(p, r) && istWert(p, r) !== (r.standard?.[p.basis] ?? istWert(p, r)));
  const ungespeichert = r => profile.some(p => aenderungen[p.id]?.[r.id] !== undefined);
  const passt = r => {
    const q = norm(zustand.suche).trim();
    if (q && !q.split(/\s+/).every(t => norm(`${r.name} ${r.beschreibung} ${r.gruppe} ${r.id}`).includes(t))) return false;
    if (zustand.gruppe && (r.gruppe || 'Weitere Rechte') !== zustand.gruppe) return false;
    if (zustand.filter === 'abweichend') return abweichend(r);
    if (zustand.filter === 'ungespeichert') return ungespeichert(r);
    return true;
  };
  const fensterPasst = () => {
    const q = norm(zustand.suche).trim();
    if (zustand.gruppe && zustand.gruppe !== 'Stundenplan-Zeitraum' && zustand.gruppe !== 'Stundenplan') return false;
    if (zustand.filter === 'abweichend') return profile.some(p => FENSTER.some(([f]) => (p[f] ?? null) !== null));
    if (zustand.filter === 'ungespeichert') return Object.values(fenster).some(a => Object.keys(a).length);
    return !q || FENSTER.some(([, t, b]) => q.split(/\s+/).every(x => norm(`${t} ${b} stundenplan zeitraum plan.fenster`).includes(x)));
  };

  // ---- Bausteine
  const rolleKopf = (p, kurz = false) => {
    const [farbe, sym] = rollenStil(p);
    return `<div class="rm-rolle" style="--rolle:${farbe}">
      <div class="rm-rolle-name"><span class="rm-rolle-symbol"><i class="bi bi-${sym}" aria-hidden="true"></i></span><b title="${e(p.name)}">${e(p.name)}</b></div>
      <div class="rm-rolle-meta">${p.system ? 'Systemrolle' : `Eigene Rolle · Basis ${e(p.basisName)}`} · <a href="#/admin/benutzer/${e(p.basis)}">${p.benutzer} ${p.benutzer === 1 ? 'Konto' : 'Konten'}</a></div>
      ${kurz ? '' : `<div class="rm-rolle-aktionen">${p.aenderbar ? `<button type="button" data-bearbeiten="${e(p.id)}" title="Name, Beschreibung und Stundenplan-Zeitraum" aria-label="${e(p.name)} bearbeiten">${icon('stift')}</button>
          <button type="button" data-kopieren="${e(p.id)}" title="Rolle kopieren" aria-label="${e(p.name)} kopieren">${icon('kopieren')}</button>` : ''}
        ${p.system && p.aenderbar ? `<button type="button" data-zuruecksetzen="${e(p.id)}" title="Auf Standard zurücksetzen" aria-label="${e(p.name)} auf Standard zurücksetzen"><i class="bi bi-arrow-counterclockwise"></i></button>` : ''}
        ${!p.system ? `<button type="button" class="rot" data-loeschen="${e(p.id)}" title="Rolle löschen" aria-label="${e(p.name)} löschen">${icon('loeschen')}</button>` : ''}
        ${!p.aenderbar ? '<span class="rm-rolle-meta">hat immer alle Rechte</span>' : ''}</div>`}
    </div>`;
  };
  const zelle = (p, r) => {
    if (!p.aenderbar) return `<div class="rm-zelle"><span class="rm-fest"><i class="bi bi-check-all" aria-hidden="true"></i>Vollzugriff</span></div>`;
    if (gesperrt(p, r)) return `<div class="rm-zelle"><span class="rm-na" title="Nur für Konten des Kollegiums">nicht verfügbar</span></div>`;
    const s = istWert(p, r);
    return `<div class="rm-zelle ${aenderungen[p.id]?.[r.id] !== undefined ? 'geaendert' : ''}" data-zelle="${e(p.id)}|${e(r.id)}">
      <div class="u-segment klein rm-stufen" role="group" aria-label="${e(r.name)} – ${e(p.name)}">${PILLEN(r).map(([w, t, sym]) =>
        `<button type="button" class="s${w} ${w === s ? 'an' : ''}" data-wert="${w}" aria-pressed="${w === s}" title="${e(t)}"><i class="bi bi-${sym}" aria-hidden="true"></i><span>${e(t)}</span></button>`).join('')}</div></div>`;
  };
  const fensterZelle = (p, f, titel) => {
    if (!p.aenderbar) return `<div class="rm-zelle"><span class="rm-na">unbegrenzt</span></div>`;
    const geaendert = fenster[p.id] && f in fenster[p.id];
    const v = geaendert ? fenster[p.id][f] : p[f];
    return `<div class="rm-zelle ${geaendert ? 'geaendert' : ''}"><input type="number" min="0" max="${k.planWochenHoechstens ?? 520}" step="1" inputmode="numeric" class="rm-wochen"
      placeholder="unbegrenzt" value="${v ?? ''}" data-fp="${e(p.id)}" data-f="${f}" aria-label="${e(titel)} – ${e(p.name)}"></div>`;
  };
  const gruppeTitel = (g, n) => `<h3 class="rm-gruppe-titel"><i class="bi bi-${GRUPPEN[g] ?? 'shield-check'}" aria-hidden="true"></i>${e(g)} <small>${n}</small></h3>`;
  const rechtText = (name, beschreibung) => `<div class="rm-recht"><b>${e(name)}</b>${beschreibung ? `<small title="${e(beschreibung)}">${e(beschreibung)}</small>` : ''}</div>`;

  // ---- Matrix (breite Bildschirme)
  const matrix = () => {
    const abschnitte = gruppen.map(g => {
      const rs = k.rechte.filter(r => (r.gruppe || 'Weitere Rechte') === g && passt(r));
      return rs.length ? `<section class="rm-gruppe" data-gruppe="${e(g)}">${gruppeTitel(g, rs.length)}
        ${rs.map(r => `<div class="rm-zeile rm-recht-zeile" data-recht="${e(r.id)}">${rechtText(r.name, r.beschreibung)}${profile.map(p => zelle(p, r)).join('')}</div>`).join('')}</section>` : '';
    });
    const fz = fensterPasst() ? `<section class="rm-gruppe" data-gruppe="Stundenplan-Zeitraum">${gruppeTitel('Stundenplan-Zeitraum', FENSTER.length)}
      ${FENSTER.map(([f, t, b]) => `<div class="rm-zeile rm-recht-zeile">${rechtText(t, b)}${profile.map(p => fensterZelle(p, f, t)).join('')}</div>`).join('')}</section>` : '';
    const inhalt = fz + abschnitte.join('');
    return `<div class="rm-matrix" style="--rm-n:${profile.length}" data-kopf-schatten>
      <div class="rm-zeile rm-kopfzeile"><div class="rm-recht">Recht</div>${profile.map(p => rolleKopf(p)).join('')}</div>
      ${inhalt || '<div class="rm-leer">Kein Recht passt zur Suche bzw. zum Filter.</div>'}</div>`;
  };

  // ---- Rolle für Rolle (schmale Bildschirme bzw. auf Wunsch)
  const rolleAnsicht = () => {
    const p = profile.find(x => x.id === zustand.rolle) ?? profile[0];
    const abschnitte = gruppen.map(g => {
      const rs = k.rechte.filter(r => (r.gruppe || 'Weitere Rechte') === g && passt(r));
      return rs.length ? `<section class="rm-gruppe" data-gruppe="${e(g)}">${gruppeTitel(g, rs.length)}
        ${rs.map(r => `<div class="rm-eintrag" data-recht="${e(r.id)}">${rechtText(r.name, r.beschreibung)}${zelle(p, r)}</div>`).join('')}</section>` : '';
    });
    const fz = fensterPasst() ? `<section class="rm-gruppe">${gruppeTitel('Stundenplan-Zeitraum', FENSTER.length)}
      ${FENSTER.map(([f, t, b]) => `<div class="rm-eintrag">${rechtText(t, b)}${fensterZelle(p, f, t)}</div>`).join('')}</section>` : '';
    return `<div class="rm-rollenwahl" role="tablist" aria-label="Rolle wählen">${profile.map(x => { const [farbe, sym] = rollenStil(x); return `<button type="button" role="tab" class="${x.id === p.id ? 'an' : ''}" style="--rolle:${farbe}" data-rolle="${e(x.id)}" aria-selected="${x.id === p.id}">
        <span class="rm-rolle-symbol"><i class="bi bi-${sym}" aria-hidden="true"></i></span>${e(x.name)}</button>`; }).join('')}</div>
      <div class="rm-rollenkarte">${rolleKopf(p)}${(fz + abschnitte.join('')) || '<div class="rm-leer">Kein Recht passt zur Suche bzw. zum Filter.</div>'}</div>`;
  };

  const anzahl = () => Object.values(aenderungen).reduce((n, a) => n + Object.keys(a).length, 0) + Object.values(fenster).reduce((n, a) => n + Object.keys(a).length, 0);
  const leisteAktualisieren = () => {
    const n = anzahl();
    const l = box.querySelector('[data-speicherleiste]');
    l.hidden = !n;
    l.querySelector('[data-anzahl]').textContent = n === 1 ? '1 ungespeicherte Änderung' : `${n} ungespeicherte Änderungen`;
  };

  box.innerHTML = `<div class="rm-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Recht suchen …', label: 'Recht suchen', filter: [
        { name: 'gruppe', label: 'Gruppe', alle: 'Alle Gruppen', optionen: ['Stundenplan-Zeitraum', ...gruppen].map(g => [g, g]) },
        { name: 'filter', label: 'Filter', alle: null, standard: 'alle', wert: zustand.filter, optionen: [['alle', 'Alle Rechte'], ['abweichend', 'Abweichend vom Standard'], ['ungespeichert', 'Ungespeichert']] },
      ] })}
    </div>
    <div class="rm-legende"><span class="rm-l s0"><i class="bi bi-slash-circle"></i>Kein Zugriff</span><span class="rm-l s1"><i class="bi bi-eye"></i>Lesen</span><span class="rm-l s2"><i class="bi bi-pencil"></i>Schreiben bzw. Zugriff</span>
      <span class="rm-trenn"></span><span>${k.rechte.length} Rechte in ${gruppen.length} Gruppen · Administration hat immer alle Rechte</span>
      ${infoHinweis(`Eigene Rollen bauen auf einer Basisrolle auf (Kollegium, Schüler*in, Erziehungsberechtigte). Rechte des Kollegiums wirken bei Schüler- und Elternkonten nicht.
        „Rückwirkend bearbeiten“: Abwesenheiten, die länger als ${Number(k.rueckwirkendTage) || 0} Tage zurückliegen. Stundenplan-Zeitraum: Wochen vor bzw. nach der aktuellen Woche, die der Stundenplan zeigt
        (leer = unbegrenzt, 0 = nur die aktuelle Woche) – im Web und in den Apps. Einzelnen Konten lassen sich unter „Rechte einzelner Benutzer“ Rechte zusätzlich geben oder entziehen.`, 'Hinweise zu Rollen')}
      <div class="rm-ansicht">${segment([['matrix', 'Matrix', { symbol: 'grid-3x3' }], ['rolle', 'Rolle für Rolle', { symbol: 'person-lines-fill' }]], zustand.ansicht, { attr: 'data-ansicht', label: 'Ansicht', klasse: 'klein' })}</div></div>
    <div data-rm-inhalt></div>
    <div class="rm-speichern" data-speicherleiste hidden role="status"><span data-anzahl></span>
      <button type="button" class="btn btn-link btn-sm" data-verwerfen>Verwerfen</button><button type="button" class="btn btn-primary btn-sm" data-speichern>Speichern</button></div>`;
  const inhalt = box.querySelector('[data-rm-inhalt]');

  const zeichnen = () => {
    const scroll = inhalt.querySelector('.rm-matrix')?.scrollLeft ?? 0;
    inhalt.innerHTML = ansicht() === 'rolle' ? rolleAnsicht() : matrix();
    const m = inhalt.querySelector('.rm-matrix');
    if (m) m.scrollLeft = scroll;
    binden();
    leisteAktualisieren();
  };

  const binden = () => {
    inhalt.querySelectorAll('[data-zelle]').forEach(z => z.addEventListener('click', ev => {
      const b = ev.target.closest('button[data-wert]');
      if (!b) return;
      const [pid, rid] = z.dataset.zelle.split('|');
      const p = profile.find(x => x.id === pid), r = k.rechte.find(x => x.id === rid);
      const v = Number(b.dataset.wert);
      aenderungen[pid] ??= {};
      if (v === (p.rechte[rid] ?? r.standard?.[p.basis] ?? 0)) delete aenderungen[pid][rid]; else aenderungen[pid][rid] = v;
      segmentSetzen(z, v);
      z.classList.toggle('geaendert', aenderungen[pid][rid] !== undefined);
      leisteAktualisieren();
    }));
    inhalt.querySelectorAll('input[data-fp]').forEach(inp => inp.onchange = () => {
      const p = profile.find(x => x.id === inp.dataset.fp), f = inp.dataset.f;
      const v = wochenZahl(inp.value);
      fenster[p.id] ??= {};
      if (v === (p[f] ?? null)) delete fenster[p.id][f]; else fenster[p.id][f] = v;
      inp.closest('.rm-zelle').classList.toggle('geaendert', f in fenster[p.id]);
      leisteAktualisieren();
    });
    inhalt.querySelectorAll('[data-rolle]').forEach(b => b.onclick = () => { zustand.rolle = b.dataset.rolle; zeichnen(); });
    inhalt.querySelectorAll('[data-kopieren]').forEach(b => b.onclick = () => neueRolle(b.dataset.kopieren));
    inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(profile.find(p => p.id === b.dataset.bearbeiten)));
    inhalt.querySelectorAll('[data-zuruecksetzen]').forEach(b => b.onclick = async () => {
      if (!await bestaetigen('Rolle zurücksetzen', 'Die Rechte dieser Systemrolle auf die Standardwerte zurücksetzen?', 'Zurücksetzen')) return;
      await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(b.dataset.zuruecksetzen)}/zuruecksetzen`, {});
      toast('Zurückgesetzt.'); kontext.navigieren(location.hash);
    });
    inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
      const p = profile.find(x => x.id === b.dataset.loeschen);
      if (!await bestaetigen('Rolle löschen', `Die Rolle „${p.name}“ löschen? ${p.benutzer} Konten erhalten wieder das Standardprofil ihrer Basisrolle.`)) return;
      await api(`/api/app/admin/rechte/profile/${encodeURIComponent(p.id)}`, { methode: 'DELETE' });
      toast('Rolle gelöscht.'); kontext.navigieren(location.hash);
    });
  };

  // Suche und Filter zeichnen nur den Inhalt neu – das Suchfeld behält den Fokus
  // gemeinsames Suchfeld mit Filter: Gruppe und „Abweichend/Ungespeichert“ als Dropdowns im Suchfeld
  suchfeldMitFilterBinden(box.querySelector('.rm-leiste'), {
    beiSuche: t => { zustand.suche = t; zeichnen(); },
    beiFilter: (name, wert) => { zustand[name] = wert; zeichnen(); },
  });
  box.querySelectorAll('[data-ansicht]').forEach(b => b.onclick = () => { zustand.ansicht = b.dataset.wert; ansichtMerken.schreiben(zustand.ansicht); segmentSetzen(b.parentElement, zustand.ansicht); zeichnen(); });
  const wechsel = () => { if (box.isConnected) zeichnen(); else schmal.removeEventListener('change', wechsel); };
  schmal.addEventListener('change', wechsel);
  box.querySelector('[data-verwerfen]').onclick = () => { Object.keys(aenderungen).forEach(x => delete aenderungen[x]); Object.keys(fenster).forEach(x => delete fenster[x]); zeichnen(); };
  box.querySelector('[data-speichern]').onclick = async ev => {
    ev.currentTarget.disabled = true;
    try {
      for (const id of new Set([...Object.keys(aenderungen), ...Object.keys(fenster)])) {
        const a = aenderungen[id] ?? {}, f = fenster[id] ?? {};
        if (!Object.keys(a).length && !Object.keys(f).length) continue;
        const p = profile.find(x => x.id === id);
        await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(id)}`, { name: p.name, beschreibung: p.beschreibung, rechte: { ...p.rechte, ...a }, ...fensterWerte(p, f) }, 'PUT');
      }
      toast('Rechte gespeichert.');
      kontext.navigieren(location.hash);
    } catch (err) { toast(err.message, true); ev.currentTarget.disabled = false; }
  };

  const neueRolle = vorlage => dialog({
    titel: vorlage ? 'Rolle kopieren' : 'Neue Rolle',
    inhalt: `<div class="mb-3"><label class="form-label">Name</label><input class="form-control" name="name" required maxlength="100" placeholder="z. B. Referendar*in, Sekretariat, Oberstufenkoordination"></div>
      ${vorlage ? `<p class="small text-body-secondary mb-0">Kopie von „${e(profile.find(p => p.id === vorlage)?.name)}“ mit denselben Rechten und derselben Basisrolle.</p>` : `
      <div class="mb-3"><label class="form-label">Basisrolle</label><select class="form-select" name="basis">${k.basisrollen.filter(b => b.id !== 'admin').map(b => `<option value="${b.id}" ${b.id === 'lehrer' ? 'selected' : ''}>${e(b.name)}</option>`).join('')}</select>
        <div class="form-text">Die Basisrolle bestimmt die Art des Kontos (Kollegium, Schüler*in, Erziehungsberechtigte) und die Ausgangsrechte.</div></div>
      <div><label class="form-label">Beschreibung</label><input class="form-control" name="beschreibung" maxlength="500"></div>`}`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Anlegen', art: 'haupt', aktion: async b => {
      const w = formularWerte(b);
      if (vorlage) await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(vorlage)}/kopieren`, { name: w.name });
      else await senden('/api/app/admin/rechte/profile', { name: w.name, basis: w.basis, beschreibung: w.beschreibung });
      toast('Rolle angelegt.'); kontext.navigieren(location.hash);
    } }],
  });
  const bearbeiten = p => dialog({
    titel: `Rolle „${p.name}“`,
    inhalt: `${p.system ? '' : `<div class="mb-3"><label class="form-label">Name</label><input class="form-control" name="name" value="${e(p.name)}" maxlength="100"></div>`}
      <div class="mb-3"><label class="form-label">Beschreibung</label><textarea class="form-control" name="beschreibung" rows="3" maxlength="500">${e(p.beschreibung)}</textarea></div>
      <div class="form-label mb-1">Stundenplan-Zeitraum</div>
      <div class="d-flex flex-wrap gap-3">${FENSTER.map(([f, t]) => {
        const v = fenster[p.id] && f in fenster[p.id] ? fenster[p.id][f] : p[f];
        return `<label class="small">${t}<input type="number" class="form-control mt-1" style="width:9rem" name="${f}" min="0" max="${k.planWochenHoechstens ?? 520}" step="1" inputmode="numeric" placeholder="unbegrenzt" value="${v ?? ''}"></label>`;
      }).join('')}</div>
      <div class="form-text">Wochen vor bzw. nach der aktuellen Woche, die Konten dieser Rolle im Stundenplan sehen (Web und Apps). Leer = unbegrenzt, 0 = nur die aktuelle Woche.
        Aktuell: ${fensterText(p.planWochenZurueck)} zurück, ${fensterText(p.planWochenVor)} voraus.</div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async b => {
      const w = formularWerte(b);
      await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(p.id)}`, { name: w.name ?? p.name, beschreibung: w.beschreibung, rechte: { ...p.rechte, ...(aenderungen[p.id] ?? {}) },
        planWochenZurueck: wochenZahl(w.planWochenZurueck), planWochenVor: wochenZahl(w.planWochenVor) }, 'PUT');
      toast('Gespeichert.'); kontext.navigieren(location.hash);
    } }],
  });
  document.querySelector('[data-neu]').onclick = () => neueRolle(null);
  zeichnen();
}

// ------------------------------------------------------------------ Rechte einzelner Benutzer

async function benutzerrechte(box, kontext) {
  const [liste, k] = await Promise.all([holen('/api/app/admin/benutzer'), holen('/api/app/admin/rechte/katalog')]);
  const params = new URLSearchParams(location.hash.split('?')[1] ?? '');
  let gewaehlt = Number(params.get('id')) || null;
  box.innerHTML = `<div class="row g-3"><div class="col-lg-4"><div class="card"><div class="card-body pb-2">
      ${suchfeld({ platzhalter: 'Benutzer suchen …', label: 'Benutzer suchen', klasse: 'voll mb-2' })}</div>
      <div class="list-group list-group-flush" style="max-height:65vh;overflow:auto" data-liste></div></div></div>
    <div class="col-lg-8" data-detail><div class="card"><div class="card-body text-body-secondary">Bitte links ein Konto wählen.</div></div></div></div>`;
  const listeBox = box.querySelector('[data-liste]');
  const zeichneListe = q => {
    q = (q ?? '').toLowerCase();
    listeBox.innerHTML = liste.filter(u => !q || `${u.login} ${u.anzeigename} ${u.rollenname}`.toLowerCase().includes(q)).slice(0, 300)
      .map(u => `<button type="button" class="list-group-item list-group-item-action ${u.id === gewaehlt ? 'active' : ''}" data-id="${u.id}"><div class="fw-semibold">${e(u.anzeigename || u.login)}</div><div class="small">${e(u.login)} · ${e(u.rollenname)}</div></button>`).join('');
    listeBox.querySelectorAll('[data-id]').forEach(b => b.onclick = () => { gewaehlt = Number(b.dataset.id); zeichneListe(box.querySelector('[data-suche]').value); detail(); });
  };
  box.querySelector('[data-suche]').oninput = ev => zeichneListe(ev.target.value);
  const katalog = Object.fromEntries(k.rechte.map(r => [r.id, r]));
  const detail = async () => {
    const d = await holen(`/api/app/admin/rechte/benutzer/${gewaehlt}`);
    let profil = d.profil;
    const ueber = { ...d.ueberschreibungen };
    let filter = '';
    const ziel = box.querySelector('[data-detail]');
    const zeichnen = v => {
      const q = filter.toLowerCase();
      ziel.innerHTML = `<div class="card"><div class="card-header d-flex flex-wrap gap-2 align-items-center">
          <span class="fw-semibold">${e(v.benutzer.anzeigename || v.benutzer.login)}</span><span class="u-chip">${e(v.benutzer.rollenname)}</span>
          <span class="u-chip gruen">${v.anzahl} Rechte effektiv</span>
          ${v.benutzer.zusatzrechte.length ? `<span class="u-chip lila" title="Bisherige Zusatzrechte (wirken weiter)">Zusatzrechte: ${e(v.benutzer.zusatzrechte.join(', '))}</span>` : ''}
          <button class="btn btn-primary btn-sm ms-auto" data-speichern ${v.benutzer.rolle === 'admin' ? 'disabled' : ''}>Speichern</button></div>
        <div class="card-body pb-0"><div class="row g-2 align-items-end mb-2">
          <div class="col-sm-6"><label class="form-label">Rolle (Rechte-Profil)</label><select class="form-select" data-profil ${v.benutzer.rolle === 'admin' ? 'disabled' : ''}>
            ${v.profile.map(p => `<option value="${e(p.id)}" ${p.id === profil ? 'selected' : ''}>${e(p.name)}${p.system ? ' (Standard)' : ''}</option>`).join('')}</select></div>
          <div class="col-sm-6">${suchfeld({ platzhalter: 'Recht suchen …', label: 'Recht suchen', wert: filter, attr: 'data-filter', klasse: 'voll' })}</div></div>
          ${v.benutzer.rolle === 'admin' ? '<p class="small text-body-secondary">Die Administration hat immer alle Rechte.</p>' : '<p class="small text-body-secondary mb-2">Überschreibung „Wie Rolle“ = Wert der Rolle. Geänderte Werte zeigen die Vorschau sofort; gespeichert wird erst mit „Speichern“.</p>'}</div>
        <div class="table-responsive" style="max-height:60vh"><table class="table table-sm align-middle mb-0 rm-tabelle u-karten-tabelle"><thead><tr><th>Recht</th><th>Rolle</th><th>Überschreibung</th><th>Effektiv</th></tr></thead><tbody>
        ${gruppenAus(k).map(g => { const rs = v.rechte.filter(x => katalog[x.recht] && (katalog[x.recht].gruppe || 'Weitere Rechte') === g && (!q || `${katalog[x.recht].name} ${katalog[x.recht].beschreibung}`.toLowerCase().includes(q)));
          return rs.length ? `<tr class="rm-gruppe-zeile"><td colspan="4"><i class="bi bi-${GRUPPEN[g] ?? 'shield-check'}" aria-hidden="true"></i>${e(g)}</td></tr>` + rs.map(x => { const r = katalog[x.recht]; return `<tr><td><div class="fw-medium">${e(r.name)}</div><div class="rm-beschreibung">${e(r.beschreibung)}</div></td>
            <td data-label="Rolle">${wert(Math.max(x.profil, x.zusatz), r)}</td>
            <td data-label="Überschreibung">${segment([['', 'Wie Rolle'], ...Object.entries(stufen(r))], ueber[x.recht] ?? '', { attr: `data-r="${e(x.recht)}"`, label: `Überschreibung ${r.name}`, klasse: 'klein', deaktiviert: v.benutzer.rolle === 'admin' })}</td>
            <td data-label="Effektiv">${wert(x.effektiv, r)} <div class="rm-quelle ${x.quelle === 'gewährt' ? 'gewaehrt' : x.quelle === 'entzogen' ? 'entzogen' : ''}">${e(x.quelle)}</div></td></tr>`; }).join('') : ''; }).join('')}
        </tbody></table></div></div>`;
      const vorschau = async () => zeichnen(await senden(`/api/app/admin/rechte/benutzer/${gewaehlt}/vorschau`, { profil, ueberschreibungen: ueber }));
      ziel.querySelector('[data-profil]').onchange = ev => { profil = ev.target.value; vorschau(); };
      ziel.querySelectorAll('button[data-r]').forEach(b => b.onclick = () => { const r = b.getAttribute('data-r'); if (b.dataset.wert === '') delete ueber[r]; else ueber[r] = Number(b.dataset.wert); vorschau(); });
      const f = ziel.querySelector('[data-filter]');
      f.oninput = () => { filter = f.value; const pos = f.selectionStart; zeichnen(v); const n = ziel.querySelector('[data-filter]'); n.focus(); n.setSelectionRange(pos, pos); };
      ziel.querySelector('[data-speichern]').onclick = async () => {
        try { zeichnen(await senden(`/api/app/admin/rechte/benutzer/${gewaehlt}`, { profil, ueberschreibungen: ueber }, 'PUT')); toast('Rechte gespeichert.'); }
        catch (err) { toast(err.message, true); }
      };
    };
    zeichnen(d);
  };
  zeichneListe('');
  if (gewaehlt) await detail();
}

// ------------------------------------------------------------------ Passwortregeln

async function passwort(box) {
  const regel = await holen('/api/app/admin/einstellungen/passwort');
  const schalter = (name, text) => `<div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" name="${name}" id="pw-${name}" ${regel[name] ? 'checked' : ''}><label class="form-check-label" for="pw-${name}">${e(text)}</label></div>`;
  box.innerHTML = `<div class="card u-einstellungen"><div class="card-header">Sicherheitsrichtlinie (Passwortregeln)</div><div class="card-body"><form>
    <div class="row g-3 mb-2"><div class="col-sm-5"><label class="form-label">Mindestlänge</label><input type="number" class="form-control" name="minLaenge" min="8" max="64" value="${regel.minLaenge}"></div></div>
    ${schalter('grossbuchstaben', 'Großbuchstaben erforderlich')}${schalter('ziffern', 'Ziffern erforderlich')}${schalter('sonderzeichen', 'Sonderzeichen erforderlich')}
    <p class="small text-body-secondary mt-2 mb-3">Gilt für selbst gewählte Passwörter (Passwort ändern). Startpasswörter erzeugt UntPlanWeb zufällig.</p>
    <button class="btn btn-primary" type="submit">Speichern</button></form></div></div>`;
  const form = box.querySelector('form');
  form.onsubmit = async ev => {
    ev.preventDefault();
    try { await senden('/api/app/admin/einstellungen/passwort', formularWerte(form), 'PUT'); toast('Einstellungen gespeichert.'); }
    catch (err) { toast(err.message, true); }
  };
}
