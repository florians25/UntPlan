// Meine Unterrichte: Unterrichte der gültigen Periode (Lehrkraft oder Klasse) mit Wochenstunden, Terminen und Räumen.
import { e, holen, leer, filterFeld, herunterladen, heuteIso, icon } from '../ui.js';
import { elementWahl } from '../elementwahl.js';

export async function zeigen(seite, kontext, [artParam, elementParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const art = artParam === 'klasse' ? 'klasse' : 'lehrer';
  const element = elementParam || (art === 'lehrer' ? ich.element : '') || '';
  kontext.titel(art === 'lehrer' && element && element === ich.element ? 'Meine Unterrichte' : 'Unterrichte',
    `<button class="btn btn-outline-secondary" data-csv>${icon('csv')}CSV</button><button class="btn btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>`);
  seite.innerHTML = `
    <div class="u-filter nicht-drucken">
      ${filterFeld('Anzeigen für', `<select class="form-select" data-art><option value="lehrer" ${art === 'lehrer' ? 'selected' : ''}>Lehrkraft</option><option value="klasse" ${art === 'klasse' ? 'selected' : ''}>Klasse</option></select>`)}
      <label class="u-feld"><span>${art === 'lehrer' ? 'Lehrkraft' : 'Klasse'}</span><span data-wahl></span></label>
      <div class="u-rechts"><input type="search" class="form-control" data-suche placeholder="Fach, Klasse …" aria-label="Filtern"></div>
    </div>
    <div class="card" data-inhalt></div>`;
  const ziel = (a, x) => `#/unterricht/${a}/${encodeURIComponent(x ?? '')}`;
  seite.querySelector('[data-art]').onchange = ev => kontext.navigieren(ziel(ev.target.value, ev.target.value === 'lehrer' ? ich.element : el.klassen[0]?.kurz));
  seite.querySelector('[data-wahl]').replaceWith(elementWahl({
    eintraege: art === 'lehrer' ? el.lehrer : el.klassen, wert: element, platzhalter: art === 'lehrer' ? 'Lehrkraft wählen' : 'Klasse wählen',
    beiWahl: x => kontext.navigieren(ziel(art, x)),
  }));
  document.querySelector('[data-drucken]').onclick = () => window.print();
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!element) { inhalt.innerHTML = leer('Bitte eine Lehrkraft oder Klasse wählen.', 'meineunterrichte'); return; }
  const liste = await holen(`/api/app/unterricht?${art}=${encodeURIComponent(element)}`);
  const summe = liste.reduce((s, u) => s + (u.wochenstunden || 0), 0);
  inhalt.innerHTML = liste.length ? `<div class="card-header d-flex align-items-center">${liste.length} Unterrichte<span class="ms-auto u-chip">${summe} Wochenstunden</span></div>
    <div class="table-responsive"><table class="table table-hover table-card align-middle"><thead><tr><th>Klasse(n)</th><th>Fach</th><th>Lehrkraft</th><th class="text-end">Std.</th><th>Termine</th><th>Räume</th><th>Bemerkung</th></tr></thead><tbody>
    ${liste.map(u => `<tr data-zeile data-text="${e([u.fach, u.fachName, ...u.klassen, ...u.lehrer, u.text].join(' ').toLowerCase())}" role="button" data-klasse="${e(u.klassen[0] ?? '')}">
      <td class="fw-semibold">${e(u.klassen.join(', ') || '—')}</td>
      <td><span class="fachpunkt" style="background:${/^#[0-9a-f]{6}$/i.test(u.farbe) ? u.farbe : '#c3cbd2'}"></span>${e(u.fach)}${u.fachName !== u.fach ? ` <span class="text-body-secondary small">${e(u.fachName)}</span>` : ''}</td>
      <td>${e(u.lehrer.join(', '))}</td><td class="text-end">${u.wochenstunden}</td>
      <td class="small">${u.termine.map(t => `<span class="u-chip me-1 mb-1">${e(t)}</span>`).join('')}</td>
      <td class="small">${e(u.raeume.join(', '))}</td>
      <td class="small text-body-secondary">${e([u.kurs, u.wochen ? `${u.wochen}-Woche` : '', u.text].filter(Boolean).join(' · '))}</td></tr>`).join('')}
    </tbody></table></div>` : leer('Keine Unterrichte gefunden.', 'meineunterrichte');
  inhalt.querySelectorAll('[data-klasse]').forEach(tr => tr.onclick = () => { if (tr.dataset.klasse) kontext.navigieren(`#/klassenbuch/${encodeURIComponent(tr.dataset.klasse)}/woche`); });
  seite.querySelector('[data-suche]').oninput = ev => {
    const f = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('[data-zeile]').forEach(z => z.classList.toggle('d-none', !!f && !z.dataset.text.includes(f)));
  };
  document.querySelector('[data-csv]').onclick = () => herunterladen(`unterrichte-${element}-${heuteIso()}.csv`,
    ['Klassen;Fach;Lehrkraft;Wochenstunden;Termine;Räume;Bemerkung', ...liste.map(u => [u.klassen.join(', '), u.fach, u.lehrer.join(', '), u.wochenstunden, u.termine.join(', '), u.raeume.join(', '), u.text]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
