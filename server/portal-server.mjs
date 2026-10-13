// UntPlan Web-Portal – Referenzserver
//
// Minimaler, abhängigkeitsfreier Server (Node.js ≥ 18), der die Sync-API (docs/SYNC_API.md)
// implementiert und eine einfache Web-Ansicht des Vertretungsplans bereitstellt.
// Gedacht für Entwicklung, Tests und als Vorlage für das spätere Produktiv-Portal.
//
// Start:   npm run portal            (Port 8787, Daten in server/data/)
// Umgebung: PORT=8787  UNTPLAN_DATA=./server/data  UNTPLAN_KEYS="199999:geheim,123456:anderer"
//           Ohne UNTPLAN_KEYS wird für jede Schule der Schlüssel "demo" akzeptiert.

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const VERSION = '0.1.0';
const here = path.dirname(fileURLToPath(import.meta.url));

export function createPortalServer({ dataDir = path.join(here, 'data'), keys = parseKeys(process.env.UNTPLAN_KEYS) } = {}) {
  fs.mkdirSync(dataDir, { recursive: true });
  const file = (id) => path.join(dataDir, `${id.replace(/[^A-Za-z0-9_-]/g, '_')}.json`);
  const load = (id) => {
    try {
      return JSON.parse(fs.readFileSync(file(id), 'utf8'));
    } catch {
      return { revision: 0, snapshot: null, meldungen: [], meldungSeq: 0 };
    }
  };
  const save = (id, st) => {
    const f = file(id);
    fs.writeFileSync(f + '.tmp', JSON.stringify(st));
    fs.renameSync(f + '.tmp', f);
  };
  const authorized = (req, id) => {
    const token = (req.headers.authorization ?? '').replace(/^Bearer\s+/i, '');
    const expected = keys ? keys.get(id) : 'demo';
    if (!expected || !token) return false;
    const a = Buffer.from(token);
    const b = Buffer.from(expected);
    return a.length === b.length && crypto.timingSafeEqual(a, b);
  };

  return http.createServer(async (req, res) => {
    const url = new URL(req.url ?? '/', 'http://localhost');
    const parts = url.pathname.split('/').filter(Boolean);
    res.setHeader('X-Content-Type-Options', 'nosniff');
    try {
      // ---------------- API ----------------
      if (parts[0] === 'api' && parts[1] === 'v1') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type');
        res.setHeader('Access-Control-Allow-Methods', 'GET, PUT, POST, OPTIONS');
        if (req.method === 'OPTIONS') return send(res, 204, '');
        if (parts[2] === 'status' && req.method === 'GET') return json(res, 200, { name: 'UntPlan Portal (Referenzserver)', version: VERSION });
        if (parts[2] !== 'schulen' || !parts[3]) return json(res, 404, { meldung: 'Unbekannter Endpunkt' });
        const id = decodeURIComponent(parts[3]);
        const sub = parts.slice(4).join('/');

        // Öffentliche Lesezugriffe (Vertretungsplan, Stundenplan)
        if (req.method === 'GET' && sub === 'vertretungen') {
          const st = load(id);
          if (!st.snapshot) return json(res, 404, { meldung: 'Keine Daten' });
          const datum = url.searchParams.get('datum');
          const klasse = url.searchParams.get('klasse');
          const v = st.snapshot.vertretungen.filter((x) => (!datum || x.datum === datum) && (!klasse || x.klassen.includes(klasse)));
          return json(res, 200, { revision: st.revision, vertretungen: v, nachrichten: st.snapshot.nachrichten.filter((n) => !n.intern) });
        }
        if (req.method === 'GET' && sub === 'stundenplan') {
          const st = load(id);
          if (!st.snapshot) return json(res, 404, { meldung: 'Keine Daten' });
          const klasse = url.searchParams.get('klasse');
          const lehrer = url.searchParams.get('lehrer');
          const plan = st.snapshot.stundenplan.filter((x) => (!klasse || x.klassen.includes(klasse)) && (!lehrer || x.lehrer.includes(lehrer)));
          return json(res, 200, { revision: st.revision, zeitraster: st.snapshot.zeitraster, stundenplan: plan });
        }
        // Meldung aus dem Portal einreichen (z. B. Krankmeldung einer Lehrkraft).
        // Im Produktivsystem nur nach Anmeldung der Lehrkraft (SSO/LOGINEO NRW o. ä.).
        if (req.method === 'POST' && sub === 'meldungen') {
          const body = await readJson(req);
          const st = load(id);
          if (!st.snapshot) return json(res, 404, { meldung: 'Schule unbekannt' });
          const kuerzel = String(body.lehrerKuerzel ?? '').trim();
          if (!st.snapshot.lehrer.some((l) => l.kuerzel.toLowerCase() === kuerzel.toLowerCase())) return json(res, 400, { meldung: 'Lehrkraft unbekannt' });
          if (!/^\d{4}-\d{2}-\d{2}$/.test(body.von ?? '')) return json(res, 400, { meldung: 'Datum fehlt' });
          const m = {
            id: `m${++st.meldungSeq}`,
            seq: st.meldungSeq,
            typ: body.typ === 'nachricht' ? 'nachricht' : 'abwesenheit',
            erstellt: new Date().toISOString(),
            absender: kuerzel,
            lehrerKuerzel: kuerzel,
            von: body.von,
            bis: /^\d{4}-\d{2}-\d{2}$/.test(body.bis ?? '') ? body.bis : body.von,
            stundeVon: Number(body.stundeVon) || undefined,
            stundeBis: Number(body.stundeBis) || undefined,
            grund: String(body.grund ?? 'Krankheit').slice(0, 40),
            text: String(body.text ?? '').slice(0, 500),
            quittiert: false,
          };
          st.meldungen.push(m);
          save(id, st);
          return json(res, 201, { ok: true, id: m.id });
        }

        // Ab hier: nur mit API-Schlüssel der Desktop-Anwendung
        if (!authorized(req, id)) return json(res, 401, { meldung: 'API-Schlüssel ungültig' });
        if (req.method === 'PUT' && sub === 'snapshot') {
          const payload = await readJson(req);
          if (payload?.schema !== 'untplan-sync/1') return json(res, 400, { meldung: 'Unbekanntes Schema' });
          const st = load(id);
          st.revision += 1;
          st.snapshot = payload;
          st.empfangen = new Date().toISOString();
          save(id, st);
          return json(res, 200, { ok: true, revision: st.revision });
        }
        if (req.method === 'GET' && sub === 'meldungen') {
          const st = load(id);
          const seit = Number(url.searchParams.get('seit') ?? 0);
          return json(res, 200, { meldungen: st.meldungen.filter((m) => !m.quittiert && m.seq > seit) });
        }
        if (req.method === 'POST' && sub === 'meldungen/quittieren') {
          const body = await readJson(req);
          const ids = new Set(Array.isArray(body.ids) ? body.ids : []);
          const st = load(id);
          for (const m of st.meldungen) if (ids.has(m.id)) m.quittiert = true;
          save(id, st);
          return json(res, 200, { ok: true });
        }
        return json(res, 404, { meldung: 'Unbekannter Endpunkt' });
      }

      // ---------------- Web-Ansicht ----------------
      if (req.method === 'GET' && parts.length === 0) {
        const schulen = fs.readdirSync(dataDir).filter((f) => f.endsWith('.json')).map((f) => {
          const st = JSON.parse(fs.readFileSync(path.join(dataDir, f), 'utf8'));
          return { id: f.replace(/\.json$/, ''), name: st.snapshot?.schule?.name ?? '?' };
        });
        return html(res, page('UntPlan Portal', `<h1>UntPlan Portal</h1><p>Referenzserver · Version ${VERSION}</p><ul>${schulen.map((s) => `<li><a href="/s/${enc(s.id)}">${esc(s.name)}</a></li>`).join('') || '<li>Noch keine Schule synchronisiert.</li>'}</ul>`));
      }
      if (req.method === 'GET' && parts[0] === 's' && parts[1]) {
        const id = decodeURIComponent(parts[1]);
        const st = load(id);
        if (!st.snapshot) return html(res, page('Nicht gefunden', '<h1>Schule nicht gefunden</h1>'), 404);
        const klasse = url.searchParams.get('klasse') ?? '';
        const s = st.snapshot;
        const tage = [...new Set(s.vertretungen.map((v) => v.datum))].sort();
        const body = `<h1>${esc(s.schule.name)}</h1><p class="sub">Vertretungsplan · Stand ${new Date(s.erzeugt).toLocaleString('de-DE')}</p>
          <form><select name="klasse" onchange="this.form.submit()"><option value="">Alle Klassen</option>${s.klassen.map((k) => `<option ${k.name === klasse ? 'selected' : ''}>${esc(k.name)}</option>`).join('')}</select></form>
          ${s.nachrichten.filter((n) => !n.intern).map((n) => `<div class="msg">${fmt(n.datum)}: ${esc(n.text)}</div>`).join('')}
          ${tage.map((t) => {
            const rows = s.vertretungen.filter((v) => v.datum === t && (!klasse || v.klassen.includes(klasse)));
            if (!rows.length) return '';
            return `<h2>${fmt(t)}</h2><table><tr><th>Klasse</th><th>Std.</th><th>Fach</th><th>Vertretung</th><th>Raum</th><th>Art</th><th>Hinweis</th></tr>${rows.map((v) => `<tr class="${v.art === 'Entfall' ? 'e' : ''}"><td>${esc(v.klassen.join(', '))}</td><td>${v.stunde}</td><td>${esc(v.fach)}</td><td>${esc(v.vertreter.join(', '))}</td><td>${esc(v.raum)}</td><td>${esc(v.art)}</td><td>${esc(v.text)}</td></tr>`).join('')}</table>`;
          }).join('') || '<p>Keine Vertretungen.</p>'}
          <h2>Krankmeldung (Lehrkräfte)</h2>
          <form id="km"><input name="lehrerKuerzel" placeholder="Kürzel" required> <input type="date" name="von" required> bis <input type="date" name="bis"> <input name="text" placeholder="Bemerkung (optional)"> <button>Melden</button></form>
          <p id="kmr" class="sub">Demo: Im Produktivbetrieb nur nach Anmeldung.</p>
          <script>document.getElementById('km').onsubmit=async(e)=>{e.preventDefault();const f=Object.fromEntries(new FormData(e.target));const r=await fetch(${JSON.stringify(`/api/v1/schulen/${enc(id)}/meldungen`).replace(/</g, '\\u003c').replace(/'/g, '%27')},{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...f,typ:'abwesenheit',grund:'Krankheit'})});const j=await r.json();document.getElementById('kmr').textContent=r.ok?'Meldung übermittelt – wird bei der nächsten Synchronisation in UntPlan angezeigt.':j.meldung;};</script>`;
        return html(res, page(s.schule.name, body));
      }
      send(res, 404, 'Nicht gefunden');
    } catch (e) {
      json(res, e.status ?? 500, { meldung: e.message ?? 'Serverfehler' });
    }
  });
}

function parseKeys(s) {
  if (!s) return null;
  return new Map(s.split(',').map((p) => p.split(':').map((x) => x.trim())).filter((p) => p.length === 2));
}
function send(res, status, body, type = 'text/plain; charset=utf-8') {
  res.writeHead(status, { 'Content-Type': type });
  res.end(body);
}
function json(res, status, obj) {
  send(res, status, JSON.stringify(obj), 'application/json; charset=utf-8');
}
function html(res, body, status = 200) {
  send(res, status, body, 'text/html; charset=utf-8');
}
function readJson(req, limit = 20 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (c) => {
      size += c.length;
      if (size > limit) {
        reject(Object.assign(new Error('Anfrage zu groß'), { status: 413 }));
        req.destroy();
      } else chunks.push(c);
    });
    req.on('end', () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}'));
      } catch {
        reject(Object.assign(new Error('Ungültiges JSON'), { status: 400 }));
      }
    });
    req.on('error', reject);
  });
}
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const enc = (s) => encodeURIComponent(s);
const fmt = (iso) => iso.split('-').reverse().join('.');
function page(title, body) {
  return `<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${esc(title)}</title>
  <style>body{font-family:system-ui,Segoe UI,Arial;margin:0 auto;max-width:960px;padding:16px;color:#1d2433}table{border-collapse:collapse;width:100%;margin-bottom:16px}th,td{border-bottom:1px solid #e3e7ee;padding:6px;text-align:left}th{background:#f8fafc}.e td{color:#b00}.msg{background:#fff4e0;padding:8px;border-radius:6px;margin:6px 0}.sub{color:#667085}input,select,button{font:inherit;padding:4px 8px}</style></head><body>${body}</body></html>`;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT ?? 8787);
  const dataDir = process.env.UNTPLAN_DATA ? path.resolve(process.env.UNTPLAN_DATA) : undefined;
  createPortalServer({ dataDir }).listen(port, () => {
    console.log(`UntPlan Portal (Referenzserver) läuft auf http://localhost:${port}`);
    if (!process.env.UNTPLAN_KEYS) console.log('Hinweis: UNTPLAN_KEYS nicht gesetzt – API-Schlüssel "demo" wird für alle Schulen akzeptiert (nur für Tests!).');
  });
}
