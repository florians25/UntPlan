import { useMemo } from 'react';
import { formatDE, todayISO, weekdayIndex } from '../../core/dates';
import { pruefeStundenplan } from '../../core/pruefung';
import { abwesenheitenAm, betroffeneStunden, freierTagAm, istUnterrichtstag, naechsterUnterrichtstag } from '../../core/vertretung';
import { lehrerName, sollStunden, TAGE_LANG } from '../../core/model';
import { useStore } from '../store';

export function Dashboard({ onNeu }: { onNeu: () => void }) {
  const { data, idx, setView, open } = useStore();
  const heute = todayISO();
  const tag = istUnterrichtstag(data, heute) ? heute : naechsterUnterrichtstag(data, heute);
  const befunde = useMemo(() => pruefeStundenplan(data, idx), [data, idx]);
  const offen = useMemo(() => betroffeneStunden(data, tag, idx).filter((b) => !b.vertretung), [data, tag, idx]);
  const abw = abwesenheitenAm(data, tag);
  const verplant = data.stundenplan.length;
  const soll = data.unterricht.reduce((s, u) => s + u.wochenstunden, 0);
  const deputat = data.lehrer.reduce((s, l) => s + sollStunden(l), 0);
  const leer = data.lehrer.length === 0 && data.klassen.length === 0;
  const ft = freierTagAm(data, heute);
  const fehler = befunde.filter((b) => b.schwere === 'fehler').length;
  const warn = befunde.filter((b) => b.schwere === 'warnung').length;

  if (leer)
    return (
      <div className="card" style={{ maxWidth: 760 }}>
        <h2>Willkommen bei UntPlan</h2>
        <p>UntPlan unterstützt Schulen in Nordrhein-Westfalen bei Unterrichtsverteilung, Stundenplanerstellung, Vertretungsplanung, Pausenaufsichten und Statistik.</p>
        <ol>
          <li>Schuldaten, Schulform und Zeitraster festlegen</li>
          <li>Lehrkräfte, Klassen, Räume und Fächer erfassen – oder aus Schild-NRW bzw. Untis (GPU) importieren</li>
          <li>Stundentafel und Unterrichtsverteilung pflegen</li>
          <li>Stundenplan automatisch erstellen und per Drag & Drop nachbearbeiten</li>
          <li>Täglich Abwesenheiten erfassen und Vertretungen planen</li>
        </ol>
        <div className="row">
          <button className="primary" onClick={onNeu}>Neue Schule / Beispielschule</button>
          <button onClick={() => open()}>Schuldatei öffnen</button>
          <button onClick={() => setView('austausch')}>Daten importieren</button>
        </div>
      </div>
    );

  return (
    <>
      <div className="grid4">
        <div className="card stat"><div className="v">{data.lehrer.filter((l) => l.aktiv).length}</div><div className="l">Lehrkräfte (Soll gesamt {deputat} Std.)</div></div>
        <div className="card stat"><div className="v">{data.klassen.length}</div><div className="l">Klassen / Kurse</div></div>
        <div className="card stat"><div className="v">{verplant} / {soll}</div><div className="l">verplante Wochenstunden</div>
          <div className="progress" style={{ marginTop: 6 }}><div style={{ width: `${soll ? Math.min(100, (verplant / soll) * 100) : 0}%` }} /></div>
        </div>
        <div className="card stat" style={{ cursor: 'pointer' }} onClick={() => setView('diagnose')}>
          <div className="v" style={{ color: fehler ? 'var(--err)' : 'var(--ok)' }}>{fehler}</div>
          <div className="l">Konflikte · {warn} Warnungen</div>
        </div>
      </div>
      <div className="grid2">
        <div className="card">
          <h2>{TAGE_LANG[weekdayIndex(tag)]}, {formatDE(tag)} {tag !== heute && <span className="pill info">nächster Schultag</span>}</h2>
          {ft && <p className="pill warn">Heute: {ft.name}</p>}
          <div className="row" style={{ marginBottom: 10 }}>
            <span className={`pill ${offen.length ? 'err' : 'ok'}`}>{offen.length} offene Vertretungsstunden</span>
            <span className="pill">{abw.length} Abwesenheiten</span>
          </div>
          {abw.slice(0, 8).map((a) => (
            <div key={a.id} className="small">
              • {a.typ === 'lehrer' ? lehrerName(idx.lehrer.get(a.refId)) : a.typ === 'klasse' ? `Klasse ${idx.klassen.get(a.refId)?.name}` : `Raum ${idx.raeume.get(a.refId)?.kuerzel}`} – {a.grund}
              {a.stundeVon > 1 || a.stundeBis < data.zeitraster.stunden.length ? ` (${a.stundeVon}.–${a.stundeBis}. Std.)` : ''}
            </div>
          ))}
          <div className="row" style={{ marginTop: 12 }}>
            <button className="primary" onClick={() => setView('vertretung', tag)}>Zur Vertretungsplanung</button>
            <button onClick={() => setView('abwesenheiten')}>Abwesenheit erfassen</button>
          </div>
        </div>
        <div className="card">
          <h2>Schnellzugriff</h2>
          <div className="row">
            <button onClick={() => setView('stundenplan')}>🗓️ Stundenplan bearbeiten</button>
            <button onClick={() => setView('unterricht')}>🧩 Unterrichtsverteilung</button>
            <button onClick={() => setView('aushang')}>📺 Vertretungsplan anzeigen</button>
            <button onClick={() => setView('statistik')}>📊 Mehrarbeit</button>
            <button onClick={() => setView('portal')}>🌐 Mit Portal synchronisieren</button>
          </div>
          <h3>Diagnose</h3>
          {befunde.slice(0, 6).map((b, i) => (
            <div key={i} className="small">
              <span className={`pill ${b.schwere === 'fehler' ? 'err' : b.schwere === 'warnung' ? 'warn' : ''}`}>{b.kategorie}</span> {b.text}
            </div>
          ))}
          {!befunde.length && <p className="muted">Keine Auffälligkeiten.</p>}
        </div>
      </div>
    </>
  );
}
