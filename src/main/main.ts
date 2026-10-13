// UntPlan – Electron-Hauptprozess: Fenster, Menü, Dateizugriff, Druck, Portal-Synchronisation.

import { app, BrowserWindow, dialog, ipcMain, Menu, shell, type MenuItemConstructorOptions } from 'electron';
import * as fs from 'fs';
import * as path from 'path';
import { portalPull, portalPush, portalAck, portalStatus } from './portal';

const isDev = !!process.env.VITE_DEV_SERVER_URL;
let win: BrowserWindow | null = null;
let currentFile: string | null = null;
let dirty = false;

interface AppSettings {
  lastFile: string | null;
  recent: string[];
}

const settingsPath = () => path.join(app.getPath('userData'), 'einstellungen.json');

function loadSettings(): AppSettings {
  try {
    return { lastFile: null, recent: [], ...JSON.parse(fs.readFileSync(settingsPath(), 'utf8')) };
  } catch {
    return { lastFile: null, recent: [] };
  }
}

function saveSettings(s: AppSettings) {
  try {
    fs.mkdirSync(path.dirname(settingsPath()), { recursive: true });
    fs.writeFileSync(settingsPath(), JSON.stringify(s, null, 2), 'utf8');
  } catch (e) {
    console.error('Einstellungen konnten nicht gespeichert werden', e);
  }
}

function rememberFile(file: string) {
  const s = loadSettings();
  s.lastFile = file;
  s.recent = [file, ...s.recent.filter((f) => f !== file)].slice(0, 8);
  saveSettings(s);
  app.addRecentDocument(file);
  buildMenu();
}

function setTitle() {
  if (!win) return;
  const name = currentFile ? path.basename(currentFile) : 'Unbenannt';
  win.setTitle(`${dirty ? '● ' : ''}${name} – UntPlan`);
}

/** Atomisches Schreiben mit Sicherungskopie (*.bak) */
function writeAtomic(file: string, content: string) {
  const tmp = `${file}.tmp`;
  fs.writeFileSync(tmp, content, 'utf8');
  if (fs.existsSync(file)) {
    try {
      fs.copyFileSync(file, `${file}.bak`);
    } catch {
      /* Sicherung optional */
    }
  }
  fs.renameSync(tmp, file);
}

const FILTERS = [{ name: 'UntPlan Schuldatei', extensions: ['untplan'] }, { name: 'JSON', extensions: ['json'] }];

function send(channel: string, ...args: unknown[]) {
  win?.webContents.send(channel, ...args);
}

function createWindow() {
  win = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1024,
    minHeight: 640,
    title: 'UntPlan',
    backgroundColor: '#f4f6f9',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  win.once('ready-to-show', () => win?.show());
  // Fenstertitel wird vom Hauptprozess verwaltet (Dateiname + Änderungsmarker)
  win.on('page-title-updated', (e) => {
    e.preventDefault();
    setTitle();
  });
  if (isDev) win.loadURL(process.env.VITE_DEV_SERVER_URL!);
  else win.loadFile(path.join(__dirname, '..', '..', 'renderer', 'index.html'));

  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });

  win.on('close', (e) => {
    if (!dirty) return;
    const choice = dialog.showMessageBoxSync(win!, {
      type: 'question',
      buttons: ['Speichern', 'Nicht speichern', 'Abbrechen'],
      defaultId: 0,
      cancelId: 2,
      title: 'UntPlan',
      message: 'Es gibt ungespeicherte Änderungen. Jetzt speichern?',
    });
    if (choice === 2) e.preventDefault();
    else if (choice === 0) {
      e.preventDefault();
      send('menu', 'save-and-quit');
    }
  });
}

function buildMenu() {
  const recent = loadSettings().recent;
  const m = (id: string) => () => send('menu', id);
  const template: MenuItemConstructorOptions[] = [
    {
      label: '&Datei',
      submenu: [
        { label: 'Neu…', accelerator: 'CmdOrCtrl+N', click: m('new') },
        { label: 'Öffnen…', accelerator: 'CmdOrCtrl+O', click: m('open') },
        {
          label: 'Zuletzt geöffnet',
          submenu: recent.length
            ? recent.map((f) => ({ label: f, click: () => send('menu', 'open-path', f) }))
            : [{ label: '(leer)', enabled: false }],
        },
        { type: 'separator' },
        { label: 'Speichern', accelerator: 'CmdOrCtrl+S', click: m('save') },
        { label: 'Speichern unter…', accelerator: 'CmdOrCtrl+Shift+S', click: m('save-as') },
        { type: 'separator' },
        { label: 'Drucken…', accelerator: 'CmdOrCtrl+P', click: m('print') },
        { type: 'separator' },
        { label: 'Beenden', role: 'quit' },
      ],
    },
    {
      label: '&Bearbeiten',
      submenu: [
        { label: 'Rückgängig', accelerator: 'CmdOrCtrl+Z', click: m('undo') },
        { label: 'Wiederholen', accelerator: 'CmdOrCtrl+Y', click: m('redo') },
        { type: 'separator' },
        { label: 'Ausschneiden', role: 'cut' },
        { label: 'Kopieren', role: 'copy' },
        { label: 'Einfügen', role: 'paste' },
        { label: 'Alles auswählen', role: 'selectAll' },
      ],
    },
    {
      label: '&Ansicht',
      submenu: [
        { label: 'Neu laden', role: 'reload', visible: isDev },
        { label: 'Entwicklerwerkzeuge', role: 'toggleDevTools' },
        { type: 'separator' },
        { label: 'Vergrößern', role: 'zoomIn' },
        { label: 'Verkleinern', role: 'zoomOut' },
        { label: 'Originalgröße', role: 'resetZoom' },
        { type: 'separator' },
        { label: 'Vollbild', role: 'togglefullscreen' },
      ],
    },
    {
      label: '&Hilfe',
      submenu: [
        { label: 'Über UntPlan', click: m('about') },
      ],
    },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

// ---------------------------------------------------------------------------
// IPC
// ---------------------------------------------------------------------------

ipcMain.handle('file:open-dialog', async () => {
  const r = await dialog.showOpenDialog(win!, { title: 'Schuldatei öffnen', filters: FILTERS, properties: ['openFile'] });
  if (r.canceled || !r.filePaths[0]) return null;
  return readSchoolFile(r.filePaths[0]);
});

ipcMain.handle('file:open-path', async (_e, file: string) => readSchoolFile(file));

function readSchoolFile(file: string) {
  const content = fs.readFileSync(file, 'utf8');
  currentFile = file;
  dirty = false;
  rememberFile(file);
  setTitle();
  return { path: file, content };
}

ipcMain.handle('file:last', async () => {
  const s = loadSettings();
  const fromArgs = process.argv.find((a) => a.endsWith('.untplan') && fs.existsSync(a));
  const file = fromArgs ?? s.lastFile;
  if (file && fs.existsSync(file)) return readSchoolFile(file);
  return null;
});

ipcMain.handle('file:save', async (_e, content: string, saveAs: boolean) => {
  let file = currentFile;
  if (!file || saveAs) {
    const r = await dialog.showSaveDialog(win!, { title: 'Schuldatei speichern', filters: FILTERS, defaultPath: file ?? 'Schule.untplan' });
    if (r.canceled || !r.filePath) return null;
    file = r.filePath;
  }
  writeAtomic(file, content);
  currentFile = file;
  dirty = false;
  rememberFile(file);
  setTitle();
  return file;
});

ipcMain.handle('file:new', async () => {
  currentFile = null;
  dirty = false;
  setTitle();
});

ipcMain.on('state:dirty', (_e, value: boolean) => {
  dirty = value;
  setTitle();
});

ipcMain.handle('app:quit-after-save', () => {
  dirty = false;
  app.quit();
});

ipcMain.handle('export:save', async (_e, name: string, content: string, filterName: string, ext: string) => {
  const r = await dialog.showSaveDialog(win!, { title: 'Exportieren', defaultPath: name, filters: [{ name: filterName, extensions: [ext] }] });
  if (r.canceled || !r.filePath) return null;
  fs.writeFileSync(r.filePath, content, 'utf8');
  return r.filePath;
});

ipcMain.handle('export:dir', async (_e, files: { name: string; inhalt: string }[]) => {
  const r = await dialog.showOpenDialog(win!, { title: 'Zielordner wählen', properties: ['openDirectory', 'createDirectory'] });
  if (r.canceled || !r.filePaths[0]) return null;
  for (const f of files) fs.writeFileSync(path.join(r.filePaths[0], f.name), f.inhalt, 'utf8');
  return r.filePaths[0];
});

ipcMain.handle('import:files', async (_e, title: string, exts: string[]) => {
  const r = await dialog.showOpenDialog(win!, { title, properties: ['openFile', 'multiSelections'], filters: [{ name: 'Dateien', extensions: exts }] });
  if (r.canceled) return [];
  return r.filePaths.map((p) => {
    const buf = fs.readFileSync(p);
    // Schild-/Untis-Exporte sind häufig Windows-1252 kodiert
    let inhalt = buf.toString('utf8');
    if (inhalt.includes('�')) inhalt = new TextDecoder('windows-1252').decode(buf);
    return { name: path.basename(p), inhalt };
  });
});

ipcMain.handle('print:pdf', async (_e, html: string, name: string, landscape: boolean) => {
  const r = await dialog.showSaveDialog(win!, { title: 'Als PDF speichern', defaultPath: name, filters: [{ name: 'PDF', extensions: ['pdf'] }] });
  if (r.canceled || !r.filePath) return null;
  const pw = new BrowserWindow({ show: false, webPreferences: { sandbox: true } });
  await pw.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
  const pdf = await pw.webContents.printToPDF({ landscape, printBackground: true, pageSize: 'A4' });
  fs.writeFileSync(r.filePath, pdf);
  pw.destroy();
  shell.openPath(r.filePath);
  return r.filePath;
});

ipcMain.handle('print:html', async (_e, html: string) => {
  const pw = new BrowserWindow({ show: false, webPreferences: { sandbox: true } });
  await pw.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
  return new Promise<boolean>((resolve) => {
    pw.webContents.print({ printBackground: true }, (ok) => {
      pw.destroy();
      resolve(ok);
    });
  });
});

ipcMain.handle('portal:status', (_e, req) => portalStatus(req));
ipcMain.handle('portal:push', (_e, req, payload) => portalPush(req, payload));
ipcMain.handle('portal:pull', (_e, req, since: number) => portalPull(req, since));
ipcMain.handle('portal:ack', (_e, req, ids: string[]) => portalAck(req, ids));

ipcMain.handle('app:info', () => ({ version: app.getVersion(), platform: process.platform, userData: app.getPath('userData') }));

// ---------------------------------------------------------------------------

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) app.quit();
else {
  app.on('second-instance', (_e, argv) => {
    const file = argv.find((a) => a.endsWith('.untplan'));
    if (win) {
      if (win.isMinimized()) win.restore();
      win.focus();
      if (file) send('menu', 'open-path', file);
    }
  });
  app.whenReady().then(() => {
    app.setAppUserModelId('de.untplan.desktop');
    buildMenu();
    createWindow();
  });
  app.on('window-all-closed', () => app.quit());
}
