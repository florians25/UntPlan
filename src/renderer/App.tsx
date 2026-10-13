import { useEffect, useMemo, useState } from 'react';
import { pruefeStundenplan } from '../core/pruefung';
import { demoSchool, emptySchool } from '../core/factory';
import { SCHULFORMEN } from '../core/nrw';
import type { Schulform } from '../core/model';
import { api, isElectron } from './api';
import { useStore } from './store';
import { Field, Modal } from './components/ui';
import { Dashboard } from './views/Dashboard';
import { SchuleView } from './views/SchuleView';
import { LehrerView } from './views/LehrerView';
import { KlassenView } from './views/KlassenView';
import { FaecherView } from './views/FaecherView';
import { RaeumeView } from './views/RaeumeView';
import { UnterrichtView } from './views/UnterrichtView';
import { OberstufeView } from './views/OberstufeView';
import { StundentafelView } from './views/StundentafelView';
import { StundenplanView } from './views/StundenplanView';
import { DiagnoseView } from './views/DiagnoseView';
import { AufsichtenView } from './views/AufsichtenView';
import { KalenderView } from './views/KalenderView';
import { AbwesenheitenView } from './views/AbwesenheitenView';
import { VertretungView } from './views/VertretungView';
import { AushangView } from './views/AushangView';
import { StatistikView } from './views/StatistikView';
import { AustauschView } from './views/AustauschView';
import { PortalView } from './views/PortalView';

interface NavEntry {
  id: string;
  label: string;
  ico: string;
  group: string;
}

const NAV: NavEntry[] = [
  { id: 'dashboard', label: 'Übersicht', ico: '🏠', group: '' },
  { id: 'schule', label: 'Schule & Zeitraster', ico: '🏫', group: 'Stammdaten' },
  { id: 'lehrer', label: 'Lehrkräfte', ico: '👩‍🏫', group: 'Stammdaten' },
  { id: 'klassen', label: 'Klassen', ico: '👥', group: 'Stammdaten' },
  { id: 'faecher', label: 'Fächer', ico: '📚', group: 'Stammdaten' },
  { id: 'raeume', label: 'Räume', ico: '🚪', group: 'Stammdaten' },
  { id: 'kalender', label: 'Ferien & Kalender', ico: '📅', group: 'Stammdaten' },
  { id: 'stundentafel', label: 'Stundentafel', ico: '📋', group: 'Planung' },
  { id: 'unterricht', label: 'Unterrichtsverteilung', ico: '🧩', group: 'Planung' },
  { id: 'oberstufe', label: 'Oberstufe / Kurse', ico: '🎓', group: 'Planung' },
  { id: 'stundenplan', label: 'Stundenplan', ico: '🗓️', group: 'Planung' },
  { id: 'diagnose', label: 'Diagnose', ico: '🩺', group: 'Planung' },
  { id: 'aufsichten', label: 'Pausenaufsichten', ico: '🛡️', group: 'Planung' },
  { id: 'abwesenheiten', label: 'Abwesenheiten', ico: '🤒', group: 'Tagesgeschäft' },
  { id: 'vertretung', label: 'Vertretungsplanung', ico: '🔁', group: 'Tagesgeschäft' },
  { id: 'aushang', label: 'Vertretungsplan / Monitor', ico: '📺', group: 'Tagesgeschäft' },
  { id: 'statistik', label: 'Statistik & Mehrarbeit', ico: '📊', group: 'Auswertung' },
  { id: 'austausch', label: 'Import / Export', ico: '🔄', group: 'Auswertung' },
  { id: 'portal', label: 'Web-Portal', ico: '🌐', group: 'Auswertung' },
];

export function App() {
  const s = useStore();
  const [neu, setNeu] = useState(false);
  const [about, setAbout] = useState(false);
  const fehler = useMemo(() => pruefeStundenplan(s.data, s.idx).filter((b) => b.schwere === 'fehler').length, [s.data, s.idx]);

  // Menü des Hauptprozesses
  useEffect(
    () =>
      api.onMenu(async (id, arg) => {
        if (id === 'new') setNeu(true);
        else if (id === 'open') s.open();
        else if (id === 'open-path' && arg) s.openPath(arg);
        else if (id === 'save') s.save();
        else if (id === 'save-as') s.save(true);
        else if (id === 'undo') s.undo();
        else if (id === 'redo') s.redo();
        else if (id === 'print') window.dispatchEvent(new CustomEvent('untplan-print'));
        else if (id === 'about') setAbout(true);
        else if (id === 'save-and-quit') {
          if (await s.save()) api.quitAfterSave();
        }
      }),
    [s],
  );

  // Tastenkürzel im Browser-Modus (in Electron übernimmt das Menü)
  useEffect(() => {
    if (isElectron) return;
    const h = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      const t = e.target as HTMLElement;
      const inField = ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName);
      if (e.key === 's') {
        e.preventDefault();
        s.save();
      } else if (e.key === 'z' && !inField) {
        e.preventDefault();
        s.undo();
      } else if (e.key === 'y' && !inField) {
        e.preventDefault();
        s.redo();
      }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [s]);

  const current = NAV.find((n) => n.id === s.view) ?? NAV[0];
  let group = '';

  return (
    <div className="app">
      <aside className="side">
        <div className="brand">
          <div className="brand-logo">UP</div>
          <div>
            UntPlan
            <small>Stunden- & Vertretungsplan NRW</small>
          </div>
        </div>
        {NAV.map((n) => {
          const header = n.group !== group ? ((group = n.group), <div className="nav-group" key={'g' + n.group}>{n.group}</div>) : null;
          return (
            <div key={n.id}>
              {header}
              <button className={`nav-item ${s.view === n.id ? 'active' : ''}`} onClick={() => s.setView(n.id)}>
                <span className="ico">{n.ico}</span>
                {n.label}
                {n.id === 'diagnose' && fehler > 0 && <span className="nav-badge">{fehler}</span>}
              </button>
            </div>
          );
        })}
        <div className="spacer" />
        <div className="small" style={{ padding: 12, color: '#7d8aa6' }}>
          {s.data.meta.schuljahr} · {SCHULFORMEN[s.data.meta.schulform]}
        </div>
      </aside>
      <div className="main">
        <div className="topbar">
          <h1>{current.label}</h1>
          <span className="school">
            {s.data.meta.schulname}
            {s.data.meta.schulnummer ? ` (${s.data.meta.schulnummer})` : ''}
            {s.dirty ? ' · ungespeichert' : ''}
          </span>
          <span className="spacer" />
          {!isElectron && (
            <>
              <button onClick={() => setNeu(true)}>Neu</button>
              <button onClick={() => s.open()}>Öffnen</button>
            </>
          )}
          <button className="ghost" disabled={!s.canUndo} title={s.undoLabel ? `Rückgängig: ${s.undoLabel}` : 'Rückgängig'} onClick={s.undo}>↶</button>
          <button className="ghost" disabled={!s.canRedo} title="Wiederholen" onClick={s.redo}>↷</button>
          <button className="primary" onClick={() => s.save()}>💾 Speichern</button>
        </div>
        <div className="content">
          <ViewSwitch id={current.id} onNeu={() => setNeu(true)} />
        </div>
      </div>
      {neu && <NeueSchule onClose={() => setNeu(false)} />}
      {about && (
        <Modal title="Über UntPlan" onClose={() => setAbout(false)}>
          <p><b>UntPlan</b> – Stundenplan- und Vertretungsplanung für Schulen in Nordrhein-Westfalen.</p>
          <p className="muted">Version 0.1.0. Alle Daten werden lokal in einer Schuldatei (*.untplan) gespeichert. Eine Synchronisation mit dem UntPlan Web-Portal ist optional.</p>
        </Modal>
      )}
      <div className="toasts">
        {s.toasts.map((t) => (
          <div key={t.id} className={`toast ${t.typ}`}>{t.text}</div>
        ))}
      </div>
    </div>
  );
}

function ViewSwitch({ id, onNeu }: { id: string; onNeu: () => void }) {
  switch (id) {
    case 'schule': return <SchuleView />;
    case 'lehrer': return <LehrerView />;
    case 'klassen': return <KlassenView />;
    case 'faecher': return <FaecherView />;
    case 'raeume': return <RaeumeView />;
    case 'kalender': return <KalenderView />;
    case 'stundentafel': return <StundentafelView />;
    case 'unterricht': return <UnterrichtView />;
    case 'oberstufe': return <OberstufeView />;
    case 'stundenplan': return <StundenplanView />;
    case 'diagnose': return <DiagnoseView />;
    case 'aufsichten': return <AufsichtenView />;
    case 'abwesenheiten': return <AbwesenheitenView />;
    case 'vertretung': return <VertretungView />;
    case 'aushang': return <AushangView />;
    case 'statistik': return <StatistikView />;
    case 'austausch': return <AustauschView />;
    case 'portal': return <PortalView />;
    default: return <Dashboard onNeu={onNeu} />;
  }
}

function NeueSchule({ onClose }: { onClose: () => void }) {
  const s = useStore();
  const [name, setName] = useState('');
  const [form, setForm] = useState<Schulform>('GY');
  const [demo, setDemo] = useState(false);
  const create = async () => {
    if (s.dirty && !confirm('Ungespeicherte Änderungen gehen verloren. Fortfahren?')) return;
    const d = demo ? demoSchool() : emptySchool(form, name || 'Neue Schule');
    await s.newSchool(d);
    s.setView(demo ? 'dashboard' : 'schule');
    onClose();
  };
  return (
    <Modal title="Neue Schuldatei" onClose={onClose} footer={<><button onClick={onClose}>Abbrechen</button><button className="primary" onClick={create}>Anlegen</button></>}>
      <div className="col" style={{ gap: 12 }}>
        <label className="check"><input type="checkbox" checked={demo} onChange={(e) => setDemo(e.target.checked)} /> Beispielschule (Gymnasium mit Lehrkräften, Klassen und Unterricht) laden</label>
        {!demo && (
          <>
            <Field label="Schulname"><input autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder="z. B. Städtisches Gymnasium Musterstadt" /></Field>
            <Field label="Schulform (NRW)">
              <select value={form} onChange={(e) => setForm(e.target.value as Schulform)}>
                {Object.entries(SCHULFORMEN).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
              </select>
            </Field>
            <p className="muted small">Ferien NRW, gesetzliche Feiertage, der Fächerkatalog und Pflichtstunden der Schulform werden automatisch vorbelegt.</p>
          </>
        )}
      </div>
    </Modal>
  );
}
