import { useState } from 'react';
import { addDays, todayISO } from '../../core/dates';
import { exportGPU, exportICal, exportUnterrichtsverteilung, importGPU, importKlassenCSV, importLehrerCSV, type ImportBericht } from '../../core/austausch';
import { api } from '../api';
import { stundenplanHTML } from '../print';
import { useStore } from '../store';

export function AustauschView() {
  const { data, update, toast } = useStore();
  const [bericht, setBericht] = useState<string[]>([]);
  const [lehrerIcal, setLehrerIcal] = useState('');

  const zeige = (titel: string, b: ImportBericht & { details?: string[] }) => {
    setBericht([`${titel}: ${b.neu} neu, ${b.aktualisiert} aktualisiert`, ...(b.details ?? []), ...b.fehler.map((f) => '⚠ ' + f)]);
    toast(`${titel} abgeschlossen`, b.fehler.length ? 'fehler' : 'ok');
  };

  const csvImport = async (art: 'lehrer' | 'klassen') => {
    const files = await api.importFiles(art === 'lehrer' ? 'Lehrkräfte-CSV (Schild-NRW) wählen' : 'Klassen-CSV (Schild-NRW) wählen', ['csv', 'txt']);
    if (!files.length) return;
    let res: ImportBericht = { neu: 0, aktualisiert: 0, fehler: [] };
    update(art === 'lehrer' ? 'Import Lehrkräfte' : 'Import Klassen', (d) => {
      res = art === 'lehrer' ? importLehrerCSV(d as any, files[0].inhalt) : importKlassenCSV(d as any, files[0].inhalt);
    });
    zeige(art === 'lehrer' ? 'Import Lehrkräfte' : 'Import Klassen', res);
  };

  const gpuImport = async () => {
    const files = await api.importFiles('GPU-Dateien wählen (GPU001–GPU006, Mehrfachauswahl)', ['txt', 'csv', 'TXT']);
    if (!files.length) return;
    let res: ReturnType<typeof importGPU> = { neu: 0, aktualisiert: 0, fehler: [], details: [] };
    update('Import GPU', (d) => { res = importGPU(d as any, files); });
    zeige('GPU-Import', res);
  };

  return (
    <div className="grid2">
      <div className="card">
        <h2>Import</h2>
        <h3>Schild-NRW</h3>
        <p className="muted small">Exportieren Sie in Schild-NRW die Lehrkräfte bzw. Klassen als CSV/Text (Semikolon-getrennt mit Kopfzeile). Erkannte Spalten: Kürzel, Nachname, Vorname, E-Mail, Pflichtstunden, Fächer bzw. Klasse, Jahrgang, Klassenlehrer, Schülerzahl.</p>
        <div className="row">
          <button onClick={() => csvImport('lehrer')}>Lehrkräfte importieren…</button>
          <button onClick={() => csvImport('klassen')}>Klassen importieren…</button>
        </div>
        <h3>Umstieg von Untis (GPU-Austauschformat)</h3>
        <p className="muted small">In Untis über den Import/Export-Dialog die Austauschdateien (DIF/GPU-Format) exportieren und GPU001 (Stundenplan), GPU002 (Unterricht), GPU003 (Klassen), GPU004 (Lehrer), GPU005 (Räume), GPU006 (Fächer) gemeinsam auswählen.</p>
        <button onClick={gpuImport}>GPU-Dateien importieren…</button>
        <h3>Komplette Schuldatei</h3>
        <p className="muted small">Über „Datei › Öffnen“ lassen sich UntPlan-Dateien (*.untplan) laden.</p>
        {bericht.length > 0 && (
          <div className="card" style={{ marginTop: 12, background: '#f8fafc' }}>
            {bericht.map((b, i) => <div key={i} className="small">{b}</div>)}
          </div>
        )}
      </div>
      <div className="card">
        <h2>Export</h2>
        <div className="col" style={{ gap: 10 }}>
          <div className="row">
            <button onClick={() => api.exportFile('Unterrichtsverteilung.csv', exportUnterrichtsverteilung(data), 'CSV', 'csv')}>Unterrichtsverteilung (CSV, Excel)</button>
          </div>
          <div className="row">
            <button onClick={() => api.exportDir(exportGPU(data))}>GPU-Austauschdateien (GPU001–006)</button>
          </div>
          <div className="row">
            <button onClick={() => api.printPdf(stundenplanHTML(data, 'klasse', data.klassen.map((k) => k.id)), 'Klassenplaene.pdf', true)}>Alle Klassenpläne (PDF)</button>
            <button onClick={() => api.printPdf(stundenplanHTML(data, 'lehrer', data.lehrer.filter((l) => l.aktiv).map((l) => l.id)), 'Lehrerplaene.pdf', true)}>Alle Lehrerpläne (PDF)</button>
            <button onClick={() => api.printPdf(stundenplanHTML(data, 'raum', data.raeume.map((r) => r.id)), 'Raumplaene.pdf', true)}>Alle Raumpläne (PDF)</button>
          </div>
          <div className="row">
            <select value={lehrerIcal} onChange={(e) => setLehrerIcal(e.target.value)}>
              <option value="">Lehrkraft wählen…</option>
              {data.lehrer.map((l) => <option key={l.id} value={l.id}>{l.kuerzel}</option>)}
            </select>
            <button disabled={!lehrerIcal} onClick={() => api.exportFile(`Stundenplan_${data.lehrer.find((l) => l.id === lehrerIcal)?.kuerzel}.ics`, exportICal(data, lehrerIcal, todayISO(), addDays(todayISO(), 27)), 'iCalendar', 'ics')}>
              Persönlicher Kalender (iCal, 4 Wochen)
            </button>
          </div>
          <div className="row">
            <button onClick={() => api.exportFile(`${data.meta.schulname || 'Schule'}.untplan`, JSON.stringify(data, null, 1), 'UntPlan Schuldatei', 'untplan')}>Sicherungskopie der Schuldatei</button>
          </div>
        </div>
      </div>
    </div>
  );
}
