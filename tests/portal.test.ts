import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { AddressInfo } from 'node:net';
// @ts-expect-error – JavaScript-Modul ohne Typen
import { createPortalServer } from '../server/portal-server.mjs';
import { demoSchool } from '../src/core/factory';
import { erstelleStundenplan } from '../src/core/planer';
import { automatischeVertretung } from '../src/core/vertretung';
import { buildSyncPayload, uebernehmeMeldungen } from '../src/core/syncPayload';

describe('Web-Portal Referenzserver', () => {
  let base = '';
  let server: any;
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'untplan-portal-'));
  beforeAll(async () => {
    server = createPortalServer({ dataDir: dir, keys: new Map([['199999', 'geheim']]) });
    await new Promise<void>((r) => server.listen(0, r));
    base = `http://127.0.0.1:${(server.address() as AddressInfo).port}/api/v1`;
  });
  afterAll(() => server.close());

  it('synchronisiert Plan, Vertretungen und Krankmeldungen', async () => {
    const d = demoSchool();
    d.stundenplan = erstelleStundenplan(d, { iterationen: 5000 }).stundenplan;
    d.abwesenheiten.push({ id: 'a', typ: 'lehrer', refId: d.lehrer[2].id, von: '2026-10-13', bis: '2026-10-13', stundeVon: 1, stundeBis: 8, grund: 'Krankheit', text: '', begleitLehrerIds: [] });
    d.vertretungen.push(...automatischeVertretung(d, '2026-10-13'));
    const payload = buildSyncPayload(d, '2026-10-12');

    const denied = await fetch(`${base}/schulen/199999/snapshot`, { method: 'PUT', headers: { Authorization: 'Bearer falsch' }, body: JSON.stringify(payload) });
    expect(denied.status).toBe(401);

    const put = await fetch(`${base}/schulen/199999/snapshot`, { method: 'PUT', headers: { Authorization: 'Bearer geheim', 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    expect(put.status).toBe(200);
    expect((await put.json()).revision).toBe(1);

    const v = await (await fetch(`${base}/schulen/199999/vertretungen?datum=2026-10-13`)).json();
    expect(v.vertretungen.length).toBe(d.vertretungen.length);
    expect(JSON.stringify(v)).not.toContain('Krankheit');

    const k = await fetch(`${base}/schulen/199999/meldungen`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ lehrerKuerzel: d.lehrer[4].kuerzel, von: '2026-10-14' }) });
    expect(k.status).toBe(201);
    const m = await (await fetch(`${base}/schulen/199999/meldungen?seit=0`, { headers: { Authorization: 'Bearer geheim' } })).json();
    expect(m.meldungen).toHaveLength(1);
    const res = uebernehmeMeldungen(d, m.meldungen);
    expect(res.uebernommen).toHaveLength(1);
    await fetch(`${base}/schulen/199999/meldungen/quittieren`, { method: 'POST', headers: { Authorization: 'Bearer geheim' }, body: JSON.stringify({ ids: res.uebernommen }) });
    const m2 = await (await fetch(`${base}/schulen/199999/meldungen?seit=0`, { headers: { Authorization: 'Bearer geheim' } })).json();
    expect(m2.meldungen).toHaveLength(0);

    const web = await (await fetch(base.replace('/api/v1', '/s/199999'))).text();
    expect(web).toContain('Beispiel-Gymnasium');
  });
});
