import { useEffect, useMemo, useRef, useState } from 'react';
import { eintraegeFuer, kurzKlassen, kurzLehrer, type Sicht } from '../../core/index';
import { newId, TAGE_LANG, hasSlot, type Planeintrag, type Unterricht } from '../../core/model';
import type { PlanerErgebnis } from '../../core/planer';
import { pruefeStundenplan, slotFrei, standardRaum } from '../../core/pruefung';
import { api } from '../api';
import { Field, Modal, textColor, useConfirm } from '../components/ui';
import { stundenplanHTML } from '../print';
import { useStore } from '../store';

type Drag = { typ: 'plan'; planId: string; unterrichtId: string } | { typ: 'neu'; unterrichtId: string };

export function StundenplanView() {
  const { data, idx, update, viewParam, toast } = useStore();
  const [sicht, setSicht] = useState<Sicht>('klasse');
  const [refId, setRefId] = useState<string>('');
  const [drag, setDrag] = useState<Drag | null>(null);
  const [sel, setSel] = useState<string | null>(null);
  const [autoOffen, setAutoOffen] = useState(false);
  const [dialog, confirm] = useConfirm();

  useEffect(() => {
    if (viewParam) {
      const [s, id] = viewParam.split(':');
      setSicht(s as Sicht);
      setRefId(id);
    }
  }, [viewParam]);

  const liste = sicht === 'klasse' ? data.klassen.map((k) => ({ id: k.id, label: k.name }))
    : sicht === 'lehrer' ? data.lehrer.map((l) => ({ id: l.id, label: `${l.kuerzel} – ${l.nachname}` }))
      : data.raeume.map((r) => ({ id: r.id, label: `${r.kuerzel} – ${r.name}` }));
  const aktiv = liste.find((x) => x.id === refId) ? refId : liste[0]?.id ?? '';

  const eintraege = useMemo(() => (aktiv ? eintraegeFuer(data, idx, sicht, aktiv) : []), [data, idx, sicht, aktiv]);
  const konflikte = useMemo(() => {
    const s = new Set<string>();
    for (const b of pruefeStundenplan(data, idx)) if (b.schwere === 'fehler') b.planeintragIds.forEach((id) => s.add(id));
    return s;
  }, [data, idx]);

  const offen = useMemo(() => {
    if (!aktiv || sicht === 'raum') return [];
    return data.unterricht
      .filter((u) => (sicht === 'klasse' ? u.klassenIds.includes(aktiv) : u.lehrerIds.includes(aktiv)))
      .map((u) => ({ u, rest: u.wochenstunden - (idx.plaeneProUnterricht.get(u.id)?.length ?? 0) }))
      .filter((x) => x.rest > 0);
  }, [data.unterricht, idx, aktiv, sicht]);

  const dragU = drag ? idx.unterricht.get(drag.unterrichtId) : undefined;
  const cellState = (tag: number, stunde: number): '' | 'drop-ok' | 'drop-bad' => {
    if (!dragU) return '';
    const ignore = drag?.typ === 'plan' ? [drag.planId] : [];
    return slotFrei(data, idx, dragU, tag, stunde, ignore).length ? 'drop-bad' : 'drop-ok';
  };

  const blocked = (tag: number, stunde: number) => {
    if (sicht === 'klasse') return hasSlot(idx.klassen.get(aktiv)?.sperrzeiten ?? [], tag, stunde);
    if (sicht === 'lehrer') return hasSlot(idx.lehrer.get(aktiv)?.sperrzeiten ?? [], tag, stunde);
    return hasSlot(idx.raeume.get(aktiv)?.sperrzeiten ?? [], tag, stunde);
  };

  const drop = (tag: number, stunde: number) => {
    if (!drag || !dragU) return;
    const gruende = slotFrei(data, idx, dragU, tag, stunde, drag.typ === 'plan' ? [drag.planId] : []);
    if (gruende.length) toast(`Achtung: ${gruende.join(', ')}`, 'fehler');
    if (drag.typ === 'plan') {
      update('Stunde verschoben', (d) => {
        const p = d.stundenplan.find((x) => x.id === drag.planId);
        if (!p) return;
        p.tag = tag;
        p.stunde = stunde;
        p.raumId = sicht === 'raum' ? aktiv : standardRaum(data, idx, dragU, tag, stunde, [p.id]);
      });
    } else {
      const raumId = sicht === 'raum' ? aktiv : standardRaum(data, idx, dragU, tag, stunde);
      update('Stunde gesetzt', (d) => { d.stundenplan.push({ id: newId('pl'), unterrichtId: dragU.id, tag, stunde, raumId, fixiert: false }); });
    }
    setDrag(null);
  };

  const removeToPool = () => {
    if (drag?.typ === 'plan') update('Stunde entfernt', (d) => { d.stundenplan = d.stundenplan.filter((p) => p.id !== drag.planId); });
    setDrag(null);
  };

  const print = async (alle: boolean) => {
    const ids = alle ? liste.map((x) => x.id) : [aktiv];
    await api.printPdf(stundenplanHTML(data, sicht, ids), `Stundenplan_${sicht}.pdf`, true);
  };
  useEffect(() => {
    const h = () => print(false);
    window.addEventListener('untplan-print', h);
    return () => window.removeEventListener('untplan-print', h);
  });

  const selected = sel ? idx.planeintraege.get(sel) : undefined;

  return (
    <div className="grid2" style={{ gridTemplateColumns: '1fr 300px', alignItems: 'start' }}>
      {dialog}
      <div>
        <div className="card" style={{ padding: 12 }}>
          <div className="row">
            <div className="tabs" style={{ marginBottom: 0, borderBottom: 0 }}>
              {(['klasse', 'lehrer', 'raum'] as Sicht[]).map((s) => (
                <button key={s} className={`tab ${sicht === s ? 'active' : ''}`} onClick={() => { setSicht(s); setRefId(''); setSel(null); }}>
                  {s === 'klasse' ? 'Klassen' : s === 'lehrer' ? 'Lehrkräfte' : 'Räume'}
                </button>
              ))}
            </div>
            <select value={aktiv} onChange={(e) => { setRefId(e.target.value); setSel(null); }} style={{ minWidth: 200 }}>
              {liste.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
            </select>
            <button className="ghost" onClick={() => { const i = liste.findIndex((x) => x.id === aktiv); setRefId(liste[Math.max(0, i - 1)]?.id ?? ''); }}>◀</button>
            <button className="ghost" onClick={() => { const i = liste.findIndex((x) => x.id === aktiv); setRefId(liste[Math.min(liste.length - 1, i + 1)]?.id ?? ''); }}>▶</button>
            <span className="spacer" />
            <button className="primary" onClick={() => setAutoOffen(true)}>⚙️ Automatisch planen…</button>
            <button onClick={() => print(false)}>🖨️ PDF</button>
            <button onClick={() => print(true)}>Alle drucken</button>
            <button className="danger" onClick={() => confirm('Alle nicht fixierten Stunden aus dem Stundenplan entfernen?', () => update('Plan geleert', (d) => { d.stundenplan = d.stundenplan.filter((p) => p.fixiert); }), 'Leeren')}>Leeren</button>
          </div>
        </div>
        <div className="card" style={{ padding: 8 }}>
          <table className="tt">
            <thead>
              <tr>
                <th className="per" />
                {Array.from({ length: data.zeitraster.tage }, (_, t) => <th key={t}>{TAGE_LANG[t]}</th>)}
              </tr>
            </thead>
            <tbody>
              {data.zeitraster.stunden.map((p) => (
                <tr key={p.nr}>
                  <th className="per">{p.nr}.<small>{p.beginn}–{p.ende}</small></th>
                  {Array.from({ length: data.zeitraster.tage }, (_, t) => (
                    <td
                      key={t}
                      className={`${blocked(t, p.nr) ? 'blocked' : ''} ${cellState(t, p.nr)}`}
                      onDragOver={(e) => e.preventDefault()}
                      onDrop={(e) => { e.preventDefault(); drop(t, p.nr); }}
                    >
                      {eintraege.filter((e) => e.tag === t && e.stunde === p.nr).map((e) => (
                        <LessonCard key={e.id} p={e} sicht={sicht} conflict={konflikte.has(e.id)} selected={sel === e.id}
                          onClick={() => setSel(e.id)} onDragStart={() => setDrag({ typ: 'plan', planId: e.id, unterrichtId: e.unterrichtId })} onDragEnd={() => setDrag(null)} />
                      ))}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <div>
        <div className="card" onDragOver={(e) => e.preventDefault()} onDrop={removeToPool} style={{ minHeight: 120 }}>
          <h2>Nicht verplant</h2>
          {sicht === 'raum' && <p className="muted small">In der Raumsicht Unterricht über Klassen- oder Lehrersicht setzen.</p>}
          <div className="pool">
            {offen.map(({ u, rest }) => {
              const f = idx.faecher.get(u.fachId);
              return (
                <div key={u.id} className="lesson" draggable style={{ background: f?.farbe, color: textColor(f?.farbe ?? '#ccc') }}
                  onDragStart={() => setDrag({ typ: 'neu', unterrichtId: u.id })} onDragEnd={() => setDrag(null)}>
                  <span className="f">{f?.kuerzel}{u.gruppe ? ` ${u.gruppe}` : ''}</span> <span className="x">×{rest}</span>
                  <div className="x">{sicht === 'klasse' ? kurzLehrer(idx, u.lehrerIds) : kurzKlassen(idx, u.klassenIds)}</div>
                </div>
              );
            })}
            {sicht !== 'raum' && !offen.length && <span className="muted small">Alles verplant ✔</span>}
          </div>
          <p className="muted small" style={{ marginTop: 10 }}>Ziehen Sie Stunden in das Raster. Zum Entfernen eine Stunde hierher ziehen. Grün = konfliktfrei.</p>
        </div>
        {selected && <Details p={selected} onClose={() => setSel(null)} />}
      </div>
      {autoOffen && <AutoPlan onClose={() => setAutoOffen(false)} klasseId={sicht === 'klasse' ? aktiv : undefined} />}
    </div>
  );
}

function LessonCard({ p, sicht, conflict, selected, onClick, onDragStart, onDragEnd }: { p: Planeintrag; sicht: Sicht; conflict: boolean; selected: boolean; onClick: () => void; onDragStart: () => void; onDragEnd: () => void }) {
  const { idx } = useStore();
  const u = idx.unterricht.get(p.unterrichtId)!;
  const f = idx.faecher.get(u.fachId);
  const raum = p.raumId ? idx.raeume.get(p.raumId)?.kuerzel : '';
  const info = sicht === 'klasse' ? `${kurzLehrer(idx, u.lehrerIds)} · ${raum ?? ''}` : sicht === 'lehrer' ? `${kurzKlassen(idx, u.klassenIds)} · ${raum ?? ''}` : `${kurzKlassen(idx, u.klassenIds)} · ${kurzLehrer(idx, u.lehrerIds)}`;
  return (
    <div className={`lesson ${p.fixiert ? 'fixed' : ''} ${conflict ? 'conflict' : ''} ${selected ? 'selected' : ''}`} draggable={!p.fixiert}
      style={{ background: f?.farbe ?? '#ddd', color: textColor(f?.farbe ?? '#ddd') }} onClick={onClick} onDragStart={onDragStart} onDragEnd={onDragEnd}
      title={`${f?.name ?? ''}${u.kursname ? ' – ' + u.kursname : ''}\n${kurzKlassen(idx, u.klassenIds)} · ${kurzLehrer(idx, u.lehrerIds)} · ${raum ?? ''}`}>
      <span className="f">{f?.kuerzel}</span>{u.kursname ? <span className="x"> {u.kursname}</span> : u.gruppe ? <span className="x"> {u.gruppe}</span> : null}
      <div className="x">{info}</div>
    </div>
  );
}

function Details({ p, onClose }: { p: Planeintrag; onClose: () => void }) {
  const { data, idx, update } = useStore();
  const u = idx.unterricht.get(p.unterrichtId) as Unterricht;
  const f = idx.faecher.get(u.fachId);
  const belegt = new Set(data.stundenplan.filter((x) => x.tag === p.tag && x.stunde === p.stunde && x.id !== p.id && x.raumId).map((x) => x.raumId));
  const gruende = slotFrei(data, idx, u, p.tag, p.stunde, [p.id]);
  return (
    <div className="card">
      <div className="row"><h2 style={{ margin: 0 }}>{f?.kuerzel} {u.kursname || u.gruppe}</h2><span className="spacer" /><button className="ghost sm" onClick={onClose}>✕</button></div>
      <p className="small">{TAGE_LANG[p.tag]}, {p.stunde}. Stunde<br />Klasse(n): {kurzKlassen(idx, u.klassenIds)}<br />Lehrkraft: {kurzLehrer(idx, u.lehrerIds) || '–'}</p>
      {gruende.length > 0 && <p className="pill err">{gruende.join(', ')}</p>}
      <Field label="Raum">
        <select value={p.raumId ?? ''} onChange={(e) => update('Raum geändert', (d) => { const x = d.stundenplan.find((y) => y.id === p.id); if (x) x.raumId = e.target.value || null; })}>
          <option value="">– kein Raum –</option>
          {data.raeume.map((r) => <option key={r.id} value={r.id}>{r.kuerzel} {belegt.has(r.id) ? '(belegt)' : ''} {r.typ !== 'Klassenraum' ? `· ${r.typ}` : ''}</option>)}
        </select>
      </Field>
      <div className="row" style={{ marginTop: 10 }}>
        <label className="check"><input type="checkbox" checked={p.fixiert} onChange={(e) => update(e.target.checked ? 'Fixiert' : 'Fixierung gelöst', (d) => { const x = d.stundenplan.find((y) => y.id === p.id); if (x) x.fixiert = e.target.checked; })} /> fixiert (bleibt bei Optimierung)</label>
      </div>
      <div className="row" style={{ marginTop: 10 }}>
        <button className="danger" onClick={() => { update('Stunde entfernt', (d) => { d.stundenplan = d.stundenplan.filter((x) => x.id !== p.id); }); onClose(); }}>Aus Plan entfernen</button>
      </div>
    </div>
  );
}

function AutoPlan({ onClose, klasseId }: { onClose: () => void; klasseId?: string }) {
  const { data, update, toast, idx } = useStore();
  const [stufe, setStufe] = useState(150_000);
  const [nurKlasse, setNurKlasse] = useState(false);
  const [behalten, setBehalten] = useState(false);
  const [laeuft, setLaeuft] = useState(false);
  const [fortschritt, setFortschritt] = useState(0);
  const [kosten, setKosten] = useState<number | null>(null);
  const [ergebnis, setErgebnis] = useState<PlanerErgebnis | null>(null);
  const worker = useRef<Worker | null>(null);
  useEffect(() => () => worker.current?.terminate(), []);

  const start = () => {
    setLaeuft(true);
    setErgebnis(null);
    const w = new Worker(new URL('../planerWorker.ts', import.meta.url), { type: 'module' });
    worker.current = w;
    w.onmessage = (e) => {
      const m = e.data;
      if (m.typ === 'fortschritt') {
        setFortschritt(m.anteil);
        setKosten(m.kosten);
      } else if (m.typ === 'fertig') {
        setErgebnis(m.res);
        setLaeuft(false);
        w.terminate();
      } else if (m.typ === 'fehler') {
        toast(`Planung fehlgeschlagen: ${m.meldung}`, 'fehler');
        setLaeuft(false);
      }
    };
    w.postMessage({ data, opt: { iterationen: stufe, seed: Math.floor(Math.random() * 1e9), bestehendeBehalten: behalten, nurKlassenIds: nurKlasse && klasseId ? [klasseId] : [] } });
  };
  const uebernehmen = () => {
    if (!ergebnis) return;
    update('Automatische Planung', (d) => { d.stundenplan = ergebnis.stundenplan; });
    toast(`Stundenplan übernommen (${ergebnis.harteKonflikte} Konflikte)`, ergebnis.harteKonflikte ? 'fehler' : 'ok');
    onClose();
  };
  const ohneLehrer = data.unterricht.filter((u) => !u.lehrerIds.length).length;
  return (
    <Modal title="Automatische Stundenplanerstellung" onClose={onClose} footer={
      <>
        <button onClick={onClose}>Schließen</button>
        {!ergebnis && <button className="primary" disabled={laeuft} onClick={start}>{laeuft ? 'Optimiere…' : 'Starten'}</button>}
        {ergebnis && <><button onClick={start}>Erneut versuchen</button><button className="primary" onClick={uebernehmen}>Ergebnis übernehmen</button></>}
      </>
    }>
      <div className="col" style={{ gap: 10 }}>
        <Field label="Gründlichkeit">
          <select value={stufe} onChange={(e) => setStufe(Number(e.target.value))} disabled={laeuft}>
            <option value={30_000}>Schnell (Entwurf)</option>
            <option value={150_000}>Normal</option>
            <option value={600_000}>Gründlich</option>
            <option value={2_000_000}>Sehr gründlich (große Schulen)</option>
          </select>
        </Field>
        {klasseId && <label className="check"><input type="checkbox" checked={nurKlasse} onChange={(e) => setNurKlasse(e.target.checked)} disabled={laeuft} /> nur Klasse {idx.klassen.get(klasseId)?.name} neu planen (Rest bleibt fest)</label>}
        <label className="check"><input type="checkbox" checked={behalten} onChange={(e) => setBehalten(e.target.checked)} disabled={laeuft} /> bestehenden Plan als Ausgangspunkt verwenden</label>
        <p className="muted small">Fixierte Stunden (📌) bleiben unverändert. Berücksichtigt werden Sperrzeiten, Kopplungen, Schienen/Bänder, Doppelstunden, Fachräume, Hohlstunden (Klassen/Lehrkräfte), Fachverteilung, Tageshöchstbelastung und die Mittagspause.</p>
        {ohneLehrer > 0 && <p className="pill warn">{ohneLehrer} Unterrichte haben noch keine Lehrkraft.</p>}
        {laeuft && (
          <>
            <div className="progress"><div style={{ width: `${fortschritt * 100}%` }} /></div>
            <span className="small muted">{Math.round(fortschritt * 100)} % · Bewertung {kosten?.toFixed(0)}</span>
          </>
        )}
        {ergebnis && (
          <div className="card" style={{ marginBottom: 0 }}>
            <b>Ergebnis</b>
            <div className="small">
              Harte Konflikte: <span className={`pill ${ergebnis.harteKonflikte ? 'err' : 'ok'}`}>{ergebnis.harteKonflikte}</span> ·
              Bewertung (weich): {ergebnis.kosten - ergebnis.harteKonflikte * 1000} · Planungsblöcke: {ergebnis.bloecke} ·
              ohne Raum: {ergebnis.ohneRaum} · Dauer: {(ergebnis.dauerMs / 1000).toFixed(1)} s
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
