// UntPlan Studio (Beta) – Start der Oberfläche: Anmeldung, Shell, Module, zuletzt geöffnetes Dokument.
import { api } from './kern/api.js';
import { an } from './kern/ereignisse.js';
import { zustand } from './kern/zustand.js';
import { anmelden, shellAufbauen, shell } from './kern/shell.js';
import { moduleLaden, befehl } from './kern/modulsystem.js';
import { dokumentOeffnen } from './kern/dokument.js';
import { fehlerMelden } from './kern/dialog.js';
import { module } from './module/register.js';

async function starten() {
  zustand.system = await api.get('/api/system').catch(() => ({ anwendung: 'UntPlan Studio (Beta)', version: '', beta: true }));
  zustand.benutzer = (await api.get('/api/sitzung').catch(() => ({}))).benutzer || await anmelden();
  an('abgemeldet', async () => {
    if (document.querySelector('.anmeldung')) return;
    zustand.benutzer = await anmelden();
    location.reload();
  });

  shellAufbauen(document.getElementById('app'));
  await moduleLaden(module);
  shell.ribbon.zeichnen(); // Menüband mit den Einträgen der Module

  // zuletzt bearbeitetes Dokument wieder öffnen
  let id = null;
  try { id = sessionStorage.getItem('studio.dokument'); } catch { /* privat */ }
  try {
    const liste = await api.get('/api/dokumente');
    if (!id || !liste.dokumente.some((d) => d.id === id)) id = liste.zuletzt[0] || null;
    if (id) await dokumentOeffnen(id);
    else befehl('datei.backstage', { seite: liste.dokumente.length ? 'oeffnen' : 'neu' });
  } catch (e) {
    fehlerMelden(e);
  }
  document.body.dataset.bereit = '1';
}

starten().catch((e) => {
  console.error(e);
  document.getElementById('app').textContent = `UntPlan Studio konnte nicht gestartet werden: ${e.message || e}`;
});
