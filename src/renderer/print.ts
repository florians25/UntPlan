// HTML-Erzeugung für Ausdrucke / PDF (Stundenpläne, Vertretungsplan, Aufsichtsplan).

import { formatDE, weekdayIndex } from '../core/dates';
import { buildIndex, eintraegeFuer, type Sicht } from '../core/index';
import { TAGE_LANG, TAGE_KURZ, lehrerName, type SchoolData } from '../core/model';
import { abwesenheitenAm, vertretungsplanZeilen } from '../core/vertretung';

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!);

function page(title: string, body: string, landscape = true): string {
  return `<!doctype html><html lang="de"><head><meta charset="utf-8"><title>${esc(title)}</title><style>
  @page { size: A4 ${landscape ? 'landscape' : 'portrait'}; margin: 12mm; }
  body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; color: #111; margin: 0; }
  h1 { font-size: 18px; margin: 0 0 2px; } .sub { color: #555; margin-bottom: 8px; }
  .pg { page-break-after: always; } .pg:last-child { page-break-after: auto; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid #999; padding: 3px 4px; vertical-align: top; }
  th { background: #eee; }
  .tt td { height: 46px; width: 18%; } .tt th.p { width: 60px; font-weight: normal; font-size: 10px; }
  .l { border-radius: 3px; padding: 1px 3px; margin-bottom: 1px; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .f { font-weight: bold; } .x { font-size: 9.5px; }
  .vp td, .vp th { font-size: 11.5px; } .entfall { color: #b00; font-weight: bold; }
  .msg { border: 1px solid #999; padding: 6px; margin: 6px 0; background: #fffbe6; }
  .foot { margin-top: 6px; color: #777; font-size: 9px; }
  </style></head><body>${body}</body></html>`;
}

export function stundenplanHTML(d: SchoolData, sicht: Sicht, ids: string[]): string {
  const idx = buildIndex(d);
  const S = d.zeitraster.stunden;
  const pages = ids.map((id) => {
    const eintraege = eintraegeFuer(d, idx, sicht, id);
    const titel =
      sicht === 'klasse' ? `Klasse ${idx.klassen.get(id)?.name}` : sicht === 'lehrer' ? `${lehrerName(idx.lehrer.get(id))} (${idx.lehrer.get(id)?.kuerzel})` : `Raum ${idx.raeume.get(id)?.kuerzel} – ${idx.raeume.get(id)?.name}`;
    let rows = '';
    for (const p of S) {
      rows += `<tr><th class="p">${p.nr}.<br>${p.beginn}–${p.ende}</th>`;
      for (let t = 0; t < d.zeitraster.tage; t++) {
        const cell = eintraege
          .filter((e) => e.tag === t && e.stunde === p.nr)
          .map((e) => {
            const u = idx.unterricht.get(e.unterrichtId)!;
            const f = idx.faecher.get(u.fachId);
            const info =
              sicht === 'klasse'
                ? `${u.lehrerIds.map((l) => idx.lehrer.get(l)?.kuerzel).join(', ')} · ${e.raumId ? idx.raeume.get(e.raumId)?.kuerzel : ''}`
                : sicht === 'lehrer'
                  ? `${u.klassenIds.map((k) => idx.klassen.get(k)?.name).join(', ')} · ${e.raumId ? idx.raeume.get(e.raumId)?.kuerzel : ''}`
                  : `${u.klassenIds.map((k) => idx.klassen.get(k)?.name).join(', ')} · ${u.lehrerIds.map((l) => idx.lehrer.get(l)?.kuerzel).join(', ')}`;
            return `<div class="l" style="background:${f?.farbe ?? '#eee'}"><span class="f">${esc(f?.kuerzel ?? '?')}${u.kursname ? ' ' + esc(u.kursname) : u.gruppe ? ' ' + esc(u.gruppe) : ''}</span><div class="x">${esc(info)}</div></div>`;
          })
          .join('');
        rows += `<td>${cell}</td>`;
      }
      rows += '</tr>';
    }
    const head = Array.from({ length: d.zeitraster.tage }, (_, t) => `<th>${TAGE_LANG[t]}</th>`).join('');
    return `<div class="pg"><h1>${esc(titel)}</h1><div class="sub">${esc(d.meta.schulname)} · Schuljahr ${esc(d.meta.schuljahr)}</div>
      <table class="tt"><thead><tr><th class="p"></th>${head}</tr></thead><tbody>${rows}</tbody></table>
      <div class="foot">Erstellt mit UntPlan am ${new Date().toLocaleDateString('de-DE')}</div></div>`;
  });
  return page('Stundenpläne', pages.join(''));
}

export function vertretungsplanHTML(d: SchoolData, daten: string[], intern: boolean): string {
  const idx = buildIndex(d);
  const pages = daten.map((iso) => {
    const zeilen = vertretungsplanZeilen(d, iso, idx);
    const msgs = d.tagesnachrichten.filter((n) => n.datum === iso && (intern || !n.intern));
    const abw = intern ? abwesenheitenAm(d, iso).filter((a) => a.typ === 'lehrer').map((a) => idx.lehrer.get(a.refId)?.kuerzel).filter(Boolean) : [];
    const abwKl = abwesenheitenAm(d, iso).filter((a) => a.typ === 'klasse').map((a) => `${idx.klassen.get(a.refId)?.name} (${a.grund})`);
    const rows = zeilen
      .map((z) => `<tr><td>${esc(z.klassen)}</td><td>${z.stunde}</td><td>${esc(z.fach)}</td><td>${esc(z.fachNeu !== z.fach ? z.fachNeu : '')}</td>${intern ? `<td>${esc(z.lehrer)}</td>` : ''}<td>${esc(z.vertreter)}</td><td>${esc(z.raumNeu)}</td><td class="${['Entfall', 'Freisetzung'].includes(z.art) ? 'entfall' : ''}">${esc(z.art)}</td><td>${esc(z.text)}</td></tr>`)
      .join('');
    return `<div class="pg"><h1>${esc(d.einstellungen.vertretungsplanTitel)} – ${TAGE_LANG[weekdayIndex(iso)] ?? ''}, ${formatDE(iso)}</h1>
      <div class="sub">${esc(d.meta.schulname)} · Stand: ${new Date().toLocaleString('de-DE')}</div>
      ${msgs.map((m) => `<div class="msg">${esc(m.text)}</div>`).join('')}
      ${abw.length ? `<div class="sub">Abwesende Lehrkräfte: ${esc(abw.join(', '))}</div>` : ''}
      ${abwKl.length ? `<div class="sub">Abwesende Klassen: ${esc(abwKl.join(', '))}</div>` : ''}
      <table class="vp"><thead><tr><th>Klasse(n)</th><th>Std.</th><th>Fach</th><th>statt</th>${intern ? '<th>Lehrkraft</th>' : ''}<th>Vertretung</th><th>Raum</th><th>Art</th><th>Hinweis</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="9">Keine Vertretungen.</td></tr>`}</tbody></table></div>`;
  });
  return page('Vertretungsplan', pages.join(''), false);
}

export function aufsichtsplanHTML(d: SchoolData): string {
  const idx = buildIndex(d);
  let rows = '';
  for (const p of [...d.pausen].sort((a, b) => a.nachStunde - b.nachStunde)) {
    for (const b of d.aufsichtsbereiche) {
      rows += `<tr><th>${esc(p.name)}</th><th>${esc(b.name)}</th>`;
      for (let t = 0; t < d.zeitraster.tage; t++)
        rows += `<td>${d.aufsichten.filter((a) => a.pauseId === p.id && a.bereichId === b.id && a.tag === t).map((a) => esc(idx.lehrer.get(a.lehrerId)?.kuerzel ?? '?')).join(', ')}</td>`;
      rows += '</tr>';
    }
  }
  const head = Array.from({ length: d.zeitraster.tage }, (_, t) => `<th>${TAGE_KURZ[t]}</th>`).join('');
  return page('Aufsichtsplan', `<h1>Pausenaufsichtsplan</h1><div class="sub">${esc(d.meta.schulname)} · ${esc(d.meta.schuljahr)}</div><table><thead><tr><th>Pause</th><th>Bereich</th>${head}</tr></thead><tbody>${rows}</tbody></table>`);
}
