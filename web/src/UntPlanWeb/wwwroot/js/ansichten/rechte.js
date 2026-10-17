// Administration → Rechte und Rollen: Rollen als Rechte-Profile (Matrix Rolle × Recht, gruppiert, mit Suche), Rollen anlegen/kopieren/
// bearbeiten/löschen, Rechte einzelner Benutzer (Profil, Überschreibungen, Vorschau der effektiven Rechte) und Passwortregeln.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, formularWerte } from '../ui.js';

const STUFE = { 0: 'Nein', 1: 'Lesen', 2: 'Ja' };
const STUFE2 = { 0: 'Nein', 1: 'Lesen', 2: 'Schreiben' };
const stufen = r => r.zweistufig ? STUFE2 : { 0: 'Nein', 2: 'Ja' };

function stil() {
  if (document.getElementById('rechte-stil')) return;
  const s = document.createElement('style');
  s.id = 'rechte-stil';
  s.textContent = `
    .rm-matrix { font-size: .85rem; }
    .rm-matrix thead th { position: sticky; top: 0; background: var(--bs-body-bg); z-index: 2; vertical-align: bottom; min-width: 7.5rem; }
    .rm-matrix th:first-child, .rm-matrix td:first-child { position: sticky; left: 0; background: var(--bs-body-bg); z-index: 1; min-width: 15rem; max-width: 22rem; }
    .rm-matrix thead th:first-child { z-index: 3; }
    .rm-matrix .rm-gruppe td { background: var(--u-grund, #f3f5f7); font-weight: 700; text-transform: uppercase; font-size: .72rem; letter-spacing: .04em; }
    .rm-matrix select { min-width: 6.5rem; font-size: .8rem; padding: .15rem 1.6rem .15rem .4rem; }
    .rm-matrix select.s0 { color: var(--u-grau-2, #6c757d); }
    .rm-matrix select.s1 { background-color: #fff6db; }
    .rm-matrix select.s2 { background-color: #e3f5ea; }
    .rm-matrix select.geaendert { outline: 2px solid var(--u-akzent, #f26b38); }
    .rm-matrix .rm-kopf { display: flex; flex-direction: column; gap: .2rem; }
    .rm-matrix .rm-kopf small { font-weight: 400; color: var(--u-grau-2, #6c757d); }
    .rm-wert { display: inline-block; min-width: 4.5rem; text-align: center; border-radius: 999px; padding: .05rem .5rem; font-size: .78rem; font-weight: 600; }
    .rm-wert.s0 { background: #eef0f2; color: #6c757d; } .rm-wert.s1 { background: #fff6db; color: #6b4e00; } .rm-wert.s2 { background: #e3f5ea; color: #0f6b40; }
    .rm-quelle { font-size: .75rem; color: var(--u-grau-2, #6c757d); }
    .rm-quelle.gewaehrt { color: #0f6b40; font-weight: 600; } .rm-quelle.entzogen { color: #a1262b; font-weight: 600; }
    .rm-beschreibung { font-size: .75rem; color: var(--u-grau-2, #6c757d); }
    .rm-matrix input.rm-wochen { width: 6.5rem; font-size: .8rem; padding: .15rem .4rem; }
    .rm-matrix input.rm-wochen.geaendert { outline: 2px solid var(--u-akzent, #f26b38); }
    .rm-matrix input.rm-wochen:placeholder-shown { color: var(--u-grau-2, #6c757d); }
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

async function rollen(box, kontext) {
  const k = await holen('/api/app/admin/rechte/katalog');
  const profile = k.profile;
  const aenderungen = {}; // profilId → { recht: stufe }
  const fenster = {}; // profilId → { planWochenZurueck, planWochenVor }
  let suche = '';
  kontext.titel('Rechte und Rollen', `<button class="btn btn-primary" data-neu>${icon('plus')}Neue Rolle</button>`);
  const zeichnen = () => {
    const q = suche.trim().toLowerCase();
    const treffer = r => !q || `${r.name} ${r.beschreibung} ${r.gruppe} ${r.id}`.toLowerCase().includes(q);
    const kopf = p => `<th><div class="rm-kopf"><span>${e(p.name)}</span><small>${p.system ? 'Systemrolle' : `eigene Rolle · Basis ${e(p.basisName)}`} · <a href="#/admin/benutzer/${e(p.basis)}">${p.benutzer} Konten</a></small>
      <div class="btn-group btn-group-sm">${p.aenderbar ? `<button class="btn btn-outline-secondary" data-bearbeiten="${e(p.id)}" title="Name, Beschreibung und Stundenplan-Zeitraum">${icon('stift')}</button>` : ''}
        ${p.aenderbar ? `<button class="btn btn-outline-secondary" data-kopieren="${e(p.id)}" title="Kopieren">${icon('kopieren')}</button>` : ''}
        ${p.system && p.aenderbar ? `<button class="btn btn-outline-secondary" data-zuruecksetzen="${e(p.id)}" title="Auf Standard zurücksetzen"><i class="bi bi-arrow-counterclockwise"></i></button>` : ''}
        ${!p.system ? `<button class="btn btn-outline-danger" data-loeschen="${e(p.id)}" title="Löschen">${icon('loeschen')}</button>` : ''}</div></div></th>`;
    const zelle = (p, r) => {
      const s = aenderungen[p.id]?.[r.id] ?? p.rechte[r.id] ?? 0;
      if (!p.aenderbar) return `<td>${wert(2, r)}</td>`;
      if (r.nurKollegium && (p.basis === 'schueler' || p.basis === 'eltern')) return `<td title="Nur für Konten des Kollegiums">${wert(0, r)}</td>`;
      return `<td><select class="form-select form-select-sm s${s} ${aenderungen[p.id]?.[r.id] !== undefined ? 'geaendert' : ''}" data-p="${e(p.id)}" data-r="${e(r.id)}" aria-label="${e(r.name)} – ${e(p.name)}">
        ${Object.entries(stufen(r)).map(([v, t]) => `<option value="${v}" ${Number(v) === s ? 'selected' : ''}>${t}</option>`).join('')}</select></td>`;
    };
    const fensterZelle = (p, f, titel) => {
      if (!p.aenderbar) return `<td><span class="rm-beschreibung">unbegrenzt</span></td>`;
      const geaendert = fenster[p.id] && f in fenster[p.id];
      const v = geaendert ? fenster[p.id][f] : p[f];
      return `<td><input type="number" min="0" max="${k.planWochenHoechstens ?? 520}" step="1" inputmode="numeric" class="form-control form-control-sm rm-wochen ${geaendert ? 'geaendert' : ''}"
        placeholder="unbegrenzt" value="${v ?? ''}" data-fp="${e(p.id)}" data-f="${f}" aria-label="${e(titel)} – ${e(p.name)}"></td>`;
    };
    const fensterZeilen = FENSTER.some(([, t, b]) => treffer({ name: t, beschreibung: b, gruppe: 'Stundenplan Zeitraum', id: 'plan.fenster' }))
      ? `<tr class="rm-gruppe"><td colspan="${profile.length + 1}">Stundenplan-Zeitraum</td></tr>` + FENSTER.map(([f, t, b]) =>
        `<tr><td><div class="fw-semibold">${t}</div><div class="rm-beschreibung">${b}</div></td>${profile.map(p => fensterZelle(p, f, t)).join('')}</tr>`).join('') : '';
    const anzahl = Object.values(aenderungen).reduce((n, a) => n + Object.keys(a).length, 0) + Object.values(fenster).reduce((n, a) => n + Object.keys(a).length, 0);
    box.innerHTML = `<div class="d-flex flex-wrap gap-2 align-items-center mb-2">
        <input type="search" class="form-control" style="max-width:22rem" placeholder="Recht suchen …" value="${e(suche)}" data-suche aria-label="Recht suchen">
        <span class="small text-body-secondary">${k.rechte.length} Rechte in ${k.gruppen.length} Gruppen · Administration hat immer alle Rechte</span>
        <button class="btn btn-primary ms-auto" data-speichern ${anzahl ? '' : 'disabled'}>${anzahl ? `${anzahl} Änderung(en) speichern` : 'Keine Änderungen'}</button></div>
      <div class="card"><div class="table-responsive" style="max-height:70vh"><table class="table table-sm align-middle mb-0 rm-matrix"><thead><tr><th>Recht</th>${profile.map(kopf).join('')}</tr></thead><tbody>
      ${fensterZeilen}
      ${k.gruppen.map(g => { const rs = k.rechte.filter(r => r.gruppe === g && treffer(r)); return rs.length ? `<tr class="rm-gruppe"><td colspan="${profile.length + 1}">${e(g)}</td></tr>` + rs.map(r => `<tr><td><div class="fw-semibold">${e(r.name)}</div><div class="rm-beschreibung">${e(r.beschreibung)}</div></td>${profile.map(p => zelle(p, r)).join('')}</tr>`).join('') : ''; }).join('')}
      </tbody></table></div><div class="card-footer small text-body-secondary">Eigene Rollen bauen auf einer Basisrolle auf (Kollegium, Schüler*in, Erziehungsberechtigte). Rechte des Kollegiums wirken bei Schüler- und Elternkonten nicht.
        „Rückwirkend bearbeiten“: Abwesenheiten, die länger als ${k.rueckwirkendTage} Tage zurückliegen.
        Stundenplan-Zeitraum: Wochen vor bzw. nach der aktuellen Woche, die der Stundenplan zeigt (leer = unbegrenzt, 0 = nur die aktuelle Woche) – im Web und in den Apps. Einzelnen Konten lassen sich unter „Rechte einzelner Benutzer“ Rechte zusätzlich geben oder entziehen.</div></div>`;
    const s = box.querySelector('[data-suche]');
    s.oninput = () => { suche = s.value; const pos = s.selectionStart; zeichnen(); const n = box.querySelector('[data-suche]'); n.focus(); n.setSelectionRange(pos, pos); };
    box.querySelectorAll('select[data-p]').forEach(sel => sel.onchange = () => {
      const p = profile.find(x => x.id === sel.dataset.p);
      const v = Number(sel.value);
      aenderungen[p.id] ??= {};
      if (v === p.rechte[sel.dataset.r]) delete aenderungen[p.id][sel.dataset.r]; else aenderungen[p.id][sel.dataset.r] = v;
      zeichnen();
    });
    box.querySelectorAll('input[data-fp]').forEach(inp => inp.onchange = () => {
      const p = profile.find(x => x.id === inp.dataset.fp), f = inp.dataset.f;
      const v = wochenZahl(inp.value);
      fenster[p.id] ??= {};
      if (v === (p[f] ?? null)) delete fenster[p.id][f]; else fenster[p.id][f] = v;
      zeichnen();
    });
    box.querySelector('[data-speichern]').onclick = async () => {
      try {
        for (const id of new Set([...Object.keys(aenderungen), ...Object.keys(fenster)])) {
          const a = aenderungen[id] ?? {}, f = fenster[id] ?? {};
          if (!Object.keys(a).length && !Object.keys(f).length) continue;
          const p = profile.find(x => x.id === id);
          await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(id)}`, { name: p.name, beschreibung: p.beschreibung, rechte: { ...p.rechte, ...a }, ...fensterWerte(p, f) }, 'PUT');
        }
        toast('Rechte gespeichert.');
        kontext.navigieren(location.hash);
      } catch (err) { toast(err.message, true); }
    };
    box.querySelectorAll('[data-kopieren]').forEach(b => b.onclick = () => neueRolle(b.dataset.kopieren));
    box.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(profile.find(p => p.id === b.dataset.bearbeiten)));
    box.querySelectorAll('[data-zuruecksetzen]').forEach(b => b.onclick = async () => {
      if (!await bestaetigen('Rolle zurücksetzen', 'Die Rechte dieser Systemrolle auf die Standardwerte zurücksetzen?', 'Zurücksetzen')) return;
      await senden(`/api/app/admin/rechte/profile/${encodeURIComponent(b.dataset.zuruecksetzen)}/zuruecksetzen`, {});
      toast('Zurückgesetzt.'); kontext.navigieren(location.hash);
    });
    box.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
      const p = profile.find(x => x.id === b.dataset.loeschen);
      if (!await bestaetigen('Rolle löschen', `Die Rolle „${p.name}“ löschen? ${p.benutzer} Konten erhalten wieder das Standardprofil ihrer Basisrolle.`)) return;
      await api(`/api/app/admin/rechte/profile/${encodeURIComponent(p.id)}`, { methode: 'DELETE' });
      toast('Rolle gelöscht.'); kontext.navigieren(location.hash);
    });
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
      <input type="search" class="form-control mb-2" placeholder="Benutzer suchen …" data-suche aria-label="Benutzer suchen"></div>
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
          <div class="col-sm-6"><input type="search" class="form-control" placeholder="Recht suchen …" value="${e(filter)}" data-filter aria-label="Recht suchen"></div></div>
          ${v.benutzer.rolle === 'admin' ? '<p class="small text-body-secondary">Die Administration hat immer alle Rechte.</p>' : '<p class="small text-body-secondary mb-2">Überschreibung „—“ = wie die Rolle. Geänderte Werte zeigen die Vorschau sofort; gespeichert wird erst mit „Speichern“.</p>'}</div>
        <div class="table-responsive" style="max-height:60vh"><table class="table table-sm align-middle mb-0 rm-matrix"><thead><tr><th>Recht</th><th>Rolle</th><th>Überschreibung</th><th>Effektiv</th></tr></thead><tbody>
        ${k.gruppen.map(g => { const rs = v.rechte.filter(x => katalog[x.recht].gruppe === g && (!q || `${katalog[x.recht].name} ${katalog[x.recht].beschreibung}`.toLowerCase().includes(q)));
          return rs.length ? `<tr class="rm-gruppe"><td colspan="4">${e(g)}</td></tr>` + rs.map(x => { const r = katalog[x.recht]; return `<tr><td><div class="fw-semibold">${e(r.name)}</div><div class="rm-beschreibung">${e(r.beschreibung)}</div></td>
            <td>${wert(Math.max(x.profil, x.zusatz), r)}</td>
            <td><select class="form-select form-select-sm" data-r="${e(x.recht)}" ${v.benutzer.rolle === 'admin' ? 'disabled' : ''} aria-label="Überschreibung ${e(r.name)}"><option value="">—</option>${Object.entries(stufen(r)).map(([s, t]) => `<option value="${s}" ${ueber[x.recht] === Number(s) ? 'selected' : ''}>${t}</option>`).join('')}</select></td>
            <td>${wert(x.effektiv, r)} <div class="rm-quelle ${x.quelle === 'gewährt' ? 'gewaehrt' : x.quelle === 'entzogen' ? 'entzogen' : ''}">${e(x.quelle)}</div></td></tr>`; }).join('') : ''; }).join('')}
        </tbody></table></div></div>`;
      const vorschau = async () => zeichnen(await senden(`/api/app/admin/rechte/benutzer/${gewaehlt}/vorschau`, { profil, ueberschreibungen: ueber }));
      ziel.querySelector('[data-profil]').onchange = ev => { profil = ev.target.value; vorschau(); };
      ziel.querySelectorAll('select[data-r]').forEach(s => s.onchange = () => { if (s.value === '') delete ueber[s.dataset.r]; else ueber[s.dataset.r] = Number(s.value); vorschau(); });
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
