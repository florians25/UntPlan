// Zugriff auf den Electron-Hauptprozess. Läuft die Oberfläche ohne Electron (z. B. im Browser
// zu Test-/Vorschauzwecken), werden Datei-Operationen über Downloads/LocalStorage nachgebildet.

import type { PortalMeldung, SyncAntwort, SyncPayload, SyncRequest } from '../core/sync';

export interface FileResult {
  path: string;
  content: string;
}

export interface Api {
  openDialog(): Promise<FileResult | null>;
  openPath(p: string): Promise<FileResult | null>;
  openLast(): Promise<FileResult | null>;
  save(content: string, saveAs: boolean): Promise<string | null>;
  newFile(): Promise<void>;
  setDirty(v: boolean): void;
  quitAfterSave(): Promise<void>;
  exportFile(name: string, content: string, filterName: string, ext: string): Promise<string | null>;
  exportDir(files: { name: string; inhalt: string }[]): Promise<string | null>;
  importFiles(title: string, exts: string[]): Promise<{ name: string; inhalt: string }[]>;
  printPdf(html: string, name: string, landscape: boolean): Promise<string | null>;
  printHtml(html: string): Promise<boolean>;
  portalStatus(req: SyncRequest): Promise<{ name: string; version: string }>;
  portalPush(req: SyncRequest, payload: SyncPayload): Promise<SyncAntwort>;
  portalPull(req: SyncRequest, since: number): Promise<{ meldungen: PortalMeldung[] }>;
  portalAck(req: SyncRequest, ids: string[]): Promise<{ ok: boolean }>;
  appInfo(): Promise<{ version: string; platform: string; userData: string }>;
  onMenu(cb: (id: string, arg?: string) => void): () => void;
}

declare global {
  interface Window {
    untplan?: Api;
  }
}

const LS_KEY = 'untplan.browser.datei';

function download(name: string, content: string, type = 'text/plain') {
  const blob = new Blob([content], { type });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

function pickFiles(accept: string, multiple: boolean): Promise<{ name: string; inhalt: string }[]> {
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = accept;
    input.multiple = multiple;
    input.onchange = async () => {
      const files = Array.from(input.files ?? []);
      resolve(await Promise.all(files.map(async (f) => ({ name: f.name, inhalt: await f.text() }))));
    };
    input.click();
  });
}

async function browserFetch<T>(req: SyncRequest, method: string, p: string, body?: unknown): Promise<T> {
  const res = await fetch(`${req.portalUrl.replace(/\/+$/, '')}/api/v1${p}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${req.apiKey}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const json = await res.json().catch(() => null);
  if (!res.ok) throw new Error(json?.meldung ?? `HTTP ${res.status}`);
  return json as T;
}

const browserApi: Api = {
  async openDialog() {
    const [f] = await pickFiles('.untplan,.json', false);
    return f ? { path: f.name, content: f.inhalt } : null;
  },
  async openPath() {
    return null;
  },
  async openLast() {
    try {
      const content = localStorage.getItem(LS_KEY);
      return content ? { path: 'Browser-Speicher', content } : null;
    } catch {
      return null;
    }
  },
  async save(content, saveAs) {
    try {
      localStorage.setItem(LS_KEY, content);
    } catch {
      /* Speicher voll */
    }
    if (saveAs) download('Schule.untplan', content, 'application/json');
    return 'Browser-Speicher';
  },
  async newFile() {},
  setDirty() {},
  async quitAfterSave() {},
  async exportFile(name, content) {
    download(name, content);
    return name;
  },
  async exportDir(files) {
    for (const f of files) download(f.name, f.inhalt);
    return 'Downloads';
  },
  async importFiles(_title, exts) {
    return pickFiles(exts.map((e) => '.' + e).join(','), true);
  },
  async printPdf(html) {
    return (await browserApi.printHtml(html)) ? 'Druckdialog' : null;
  },
  async printHtml(html) {
    const w = window.open('', '_blank');
    if (!w) return false;
    w.document.write(html);
    w.document.close();
    w.focus();
    w.print();
    return true;
  },
  portalStatus: (req) => browserFetch(req, 'GET', '/status'),
  portalPush: (req, payload) => browserFetch(req, 'PUT', `/schulen/${encodeURIComponent(req.schulId)}/snapshot`, payload),
  portalPull: (req, since) => browserFetch(req, 'GET', `/schulen/${encodeURIComponent(req.schulId)}/meldungen?seit=${since}`),
  portalAck: (req, ids) => browserFetch(req, 'POST', `/schulen/${encodeURIComponent(req.schulId)}/meldungen/quittieren`, { ids }),
  async appInfo() {
    return { version: '0.1.0', platform: 'browser', userData: '' };
  },
  onMenu() {
    return () => {};
  },
};

export const api: Api = window.untplan ?? browserApi;
export const isElectron = !!window.untplan;
