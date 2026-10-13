// Sichere Brücke zwischen Oberfläche und Hauptprozess.
import { contextBridge, ipcRenderer } from 'electron';

const api = {
  openDialog: () => ipcRenderer.invoke('file:open-dialog'),
  openPath: (p: string) => ipcRenderer.invoke('file:open-path', p),
  openLast: () => ipcRenderer.invoke('file:last'),
  save: (content: string, saveAs: boolean) => ipcRenderer.invoke('file:save', content, saveAs),
  newFile: () => ipcRenderer.invoke('file:new'),
  setDirty: (v: boolean) => ipcRenderer.send('state:dirty', v),
  quitAfterSave: () => ipcRenderer.invoke('app:quit-after-save'),
  exportFile: (name: string, content: string, filterName: string, ext: string) => ipcRenderer.invoke('export:save', name, content, filterName, ext),
  exportDir: (files: { name: string; inhalt: string }[]) => ipcRenderer.invoke('export:dir', files),
  importFiles: (title: string, exts: string[]) => ipcRenderer.invoke('import:files', title, exts),
  printPdf: (html: string, name: string, landscape: boolean) => ipcRenderer.invoke('print:pdf', html, name, landscape),
  printHtml: (html: string) => ipcRenderer.invoke('print:html', html),
  portalStatus: (req: unknown) => ipcRenderer.invoke('portal:status', req),
  portalPush: (req: unknown, payload: unknown) => ipcRenderer.invoke('portal:push', req, payload),
  portalPull: (req: unknown, since: number) => ipcRenderer.invoke('portal:pull', req, since),
  portalAck: (req: unknown, ids: string[]) => ipcRenderer.invoke('portal:ack', req, ids),
  appInfo: () => ipcRenderer.invoke('app:info'),
  onMenu: (cb: (id: string, arg?: string) => void) => {
    const h = (_e: unknown, id: string, arg?: string) => cb(id, arg);
    ipcRenderer.on('menu', h);
    return () => ipcRenderer.removeListener('menu', h);
  },
};

contextBridge.exposeInMainWorld('untplan', api);
export type UntPlanApi = typeof api;
