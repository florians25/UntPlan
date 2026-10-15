// Klassenbuch → Sitzplan einer Klasse: Raster mit Tafel vorne, Schüler*innen per Klick auf Plätze setzen (Klassenleitung), Druck.
import { e, icon, holen, senden, toast, filterFeld, leer } from '../ui.js';

export async function zeigen(seite, kontext, [klasseParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : eigene ?? el.klassen[0]?.kurz ?? '';
  if (!klasse) { kontext.titel('Sitzplan'); seite.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  const d = await holen(`/api/app/sitzplan/${encodeURIComponent(klasse)}`);
  let zeilen = d.plan.zeilen, spalten = d.plan.spalten;
  const belegung = new Map(Object.entries(d.plan.belegung ?? {}));
  const name = id => { const s = d.schueler.find(x => x.id === id); return s ? `${s.vorname} <small>${e(s.name)}</small>` : ''; };
  let gewaehlt = null; // Schüler-ID aus der Liste oder Platz „z-s“
  let geaendert = false;

  kontext.titel(`Sitzplan ${klasse}`, `${d.verwalten ? '<button class="btn btn-primary" data-speichern disabled>Speichern</button>' : ''}
    <button class="btn btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>`);
  const zeichnen = () => {
    const vergeben = new Set(belegung.values());
    const frei = d.schueler.filter(s => !vergeben.has(s.id));
    let platz = '';
    for (let z = 1; z <= zeilen; z++) for (let s = 1; s <= spalten; s++) {
      const k = `${z}-${s}`, id = belegung.get(k);
      platz += `<div class="sp-platz ${id ? 'belegt' : ''} ${gewaehlt === k ? 'gewaehlt' : ''}" data-platz="${k}" role="button" tabindex="0" title="Reihe ${z}, Platz ${s}">${id ? `<span>${name(id)}</span>` : ''}</div>`;
    }
    seite.innerHTML = `
      <div class="u-filter nicht-drucken">
        ${filterFeld('Klasse', `<select class="form-select" data-klasse>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
        ${d.verwalten ? `${filterFeld('Reihen', `<input type="number" class="form-control" data-zeilen min="1" max="15" value="${zeilen}" style="width:90px;min-width:0">`)}
          ${filterFeld('Plätze je Reihe', `<input type="number" class="form-control" data-spalten min="1" max="15" value="${spalten}" style="width:90px;min-width:0">`)}
          <div class="u-rechts"><button class="btn btn-outline-secondary" data-leeren>Alle Plätze leeren</button><button class="btn btn-outline-secondary" data-alphabetisch>Alphabetisch setzen</button></div>` : ''}
      </div>
      ${d.schueler.length ? `<div class="sp-raum" style="grid-template-columns:repeat(${spalten}, minmax(100px, 1fr))"><div class="sp-tafel">Tafel</div>${platz}</div>
        ${d.verwalten ? `<div class="mt-3 nicht-drucken"><div class="small fw-bold mb-2">Ohne Platz (${frei.length}) – Schüler*in wählen und auf einen Platz klicken; Klick auf einen belegten Platz und dann auf einen anderen tauscht.</div>
          <div class="sp-frei">${frei.map(s => `<button type="button" data-schueler="${s.id}" class="${gewaehlt === s.id ? 'gewaehlt' : ''}">${e(s.vorname)} ${e(s.name)}</button>`).join('') || '<span class="small text-body-secondary">Alle Schüler*innen haben einen Platz.</span>'}</div></div>` : ''}`
        : leer('Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.', 'people')}`;
    seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(`#/sitzplan/${encodeURIComponent(ev.target.value)}`);
    document.querySelector('[data-drucken]').onclick = () => window.print();
    const speichern = document.querySelector('[data-speichern]');
    if (speichern) {
      speichern.disabled = !geaendert;
      speichern.onclick = async () => {
        try { await senden(`/api/app/sitzplan/${encodeURIComponent(klasse)}`, { zeilen, spalten, belegung: Object.fromEntries(belegung) }, 'PUT'); geaendert = false; toast('Sitzplan gespeichert.'); zeichnen(); }
        catch (err) { kontext.fehler(err); }
      };
    }
    if (!d.verwalten) return;
    const aendern = () => { geaendert = true; zeichnen(); };
    seite.querySelector('[data-zeilen]').onchange = ev => { zeilen = Math.max(1, Math.min(15, Number(ev.target.value) || 1)); [...belegung.keys()].forEach(k => { if (Number(k.split('-')[0]) > zeilen) belegung.delete(k); }); aendern(); };
    seite.querySelector('[data-spalten]').onchange = ev => { spalten = Math.max(1, Math.min(15, Number(ev.target.value) || 1)); [...belegung.keys()].forEach(k => { if (Number(k.split('-')[1]) > spalten) belegung.delete(k); }); aendern(); };
    seite.querySelector('[data-leeren]').onclick = () => { belegung.clear(); aendern(); };
    seite.querySelector('[data-alphabetisch]').onclick = () => {
      belegung.clear();
      d.schueler.slice(0, zeilen * spalten).forEach((s, i) => belegung.set(`${Math.floor(i / spalten) + 1}-${(i % spalten) + 1}`, s.id));
      aendern();
    };
    seite.querySelectorAll('[data-schueler]').forEach(b => b.onclick = () => { const id = Number(b.dataset.schueler); gewaehlt = gewaehlt === id ? null : id; zeichnen(); });
    seite.querySelectorAll('[data-platz]').forEach(p => p.onclick = () => {
      const k = p.dataset.platz;
      if (typeof gewaehlt === 'number') { belegung.set(k, gewaehlt); gewaehlt = null; aendern(); return; }
      if (typeof gewaehlt === 'string') {
        if (gewaehlt !== k) { const a = belegung.get(gewaehlt), b = belegung.get(k); if (a) belegung.set(k, a); else belegung.delete(k); if (b) belegung.set(gewaehlt, b); else belegung.delete(gewaehlt); gewaehlt = null; aendern(); return; }
        // zweiter Klick auf denselben Platz: frei machen
        belegung.delete(k); gewaehlt = null; aendern(); return;
      }
      if (belegung.has(k)) { gewaehlt = k; zeichnen(); }
    });
  };
  zeichnen();
}
