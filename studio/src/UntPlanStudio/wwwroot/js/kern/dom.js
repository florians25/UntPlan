// Kleine DOM-Hilfen (kein Framework): h('div', { class: 'x', onclick: f }, kind1, 'Text', …)

export function h(tag, attrs, ...kinder) {
  const el = document.createElement(tag);
  if (attrs) {
    for (const [k, v] of Object.entries(attrs)) {
      if (v === undefined || v === null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k === 'html') el.innerHTML = v; // nur für eigene, feste Inhalte (z. B. SVG-Symbole)
      else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
      else if (k === 'dataset') Object.assign(el.dataset, v);
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
      else if (v === true) el.setAttribute(k, '');
      else el.setAttribute(k, v);
    }
  }
  anhaengen(el, kinder);
  return el;
}

function anhaengen(el, kinder) {
  for (const k of kinder) {
    if (k === null || k === undefined || k === false) continue;
    if (Array.isArray(k)) anhaengen(el, k);
    else el.append(k instanceof Node ? k : document.createTextNode(String(k)));
  }
}

export function leeren(el) {
  while (el.firstChild) el.firstChild.remove();
  return el;
}

/** Führt f höchstens einmal je `ms` Millisekunden aus (letzter Aufruf gewinnt). */
export function entprellen(f, ms = 200) {
  let t = 0;
  return (...a) => {
    clearTimeout(t);
    t = setTimeout(() => f(...a), ms);
  };
}

/** Sortiervergleich wie im Desktop: deutsch, Zahlen numerisch, Groß-/Kleinschreibung egal. */
export const vergleich = new Intl.Collator('de', { numeric: true, sensitivity: 'base' }).compare;

export function textbreite(text, schrift = '12.5px system-ui') {
  const c = textbreite.c || (textbreite.c = document.createElement('canvas').getContext('2d'));
  c.font = schrift;
  return c.measureText(text).width;
}

export function datumDe(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function groesse(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
}
