// HTTP-Client für das UntPlan Web-Portal (läuft im Hauptprozess, damit keine CORS-Beschränkungen gelten).

import type { PortalMeldung, SyncAntwort, SyncPayload, SyncRequest } from '../core/sync';

function url(req: SyncRequest, p: string): string {
  const base = req.portalUrl.replace(/\/+$/, '');
  return `${base}/api/v1${p}`;
}

async function call<T>(req: SyncRequest, method: string, p: string, body?: unknown): Promise<T> {
  if (!req.portalUrl) throw new Error('Keine Portal-Adresse konfiguriert');
  const res = await fetch(url(req, p), {
    method,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${req.apiKey}`,
      'User-Agent': 'UntPlan-Desktop',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(30_000),
  });
  const text = await res.text();
  let json: any = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    /* kein JSON */
  }
  if (!res.ok) throw new Error(json?.meldung ?? `Portal antwortet mit HTTP ${res.status}`);
  return json as T;
}

export async function portalStatus(req: SyncRequest): Promise<{ name: string; version: string }> {
  return call(req, 'GET', '/status');
}

export async function portalPush(req: SyncRequest, payload: SyncPayload): Promise<SyncAntwort> {
  return call(req, 'PUT', `/schulen/${encodeURIComponent(req.schulId)}/snapshot`, payload);
}

export async function portalPull(req: SyncRequest, since: number): Promise<{ meldungen: PortalMeldung[] }> {
  return call(req, 'GET', `/schulen/${encodeURIComponent(req.schulId)}/meldungen?seit=${since}`);
}

export async function portalAck(req: SyncRequest, ids: string[]): Promise<{ ok: boolean }> {
  return call(req, 'POST', `/schulen/${encodeURIComponent(req.schulId)}/meldungen/quittieren`, { ids });
}
