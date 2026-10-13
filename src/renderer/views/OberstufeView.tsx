import { useMemo, useState } from 'react';
import { newKlasse, newUnterricht } from '../../core/factory';
import { loescheUnterrichte } from '../../core/loeschen';
import type { Kursart, Unterricht } from '../../core/model';
import { KURSARTEN, OBERSTUFE } from '../../core/nrw';
import { Field, Modal, MultiSelect, NumberInput, TextInput } from '../components/ui';
import { useStore } from '../store';

export function OberstufeView() {
  const { data, idx, update } = useStore();
  const [stufe, setStufe] = useState('EF');
  const [neu, setNeu] = useState(false);
  const klasse = data.klassen.find((k) => k.jahrgang === stufe);
  const kurse = data.unterricht.filter((u) => klasse && u.klassenIds.includes(klasse.id));
  const schienen = useMemo(() => [...new Set(kurse.map((k) => k.schiene || '—'))].sort((a, b) => a.localeCompare(b, 'de', { numeric: true })), [kurse]);
  const lehrerOpts = data.lehrer.map((l) => ({ value: l.id, label: l.kuerzel }));
  const set = (id: string, f: (u: Unterricht) => void) => update('Kurs bearbeitet', (d) => { const u = d.unterricht.find((x) => x.id === id); if (u) f(u); });

  const kollisionen = (schiene: string) => {
    const ks = kurse.filter((k) => (k.schiene || '—') === schiene);
    const count = new Map<string, number>();
    for (const k of ks) for (const l of k.lehrerIds) count.set(l, (count.get(l) ?? 0) + 1);
    return [...count].filter(([, n]) => n > 1).map(([l]) => idx.lehrer.get(l)?.kuerzel ?? '?');
  };
  const raumBedarf = (schiene: string) => kurse.filter((k) => (k.schiene || '—') === schiene).length;

  if (!klasse)
    return (
      <div className="card" style={{ maxWidth: 700 }}>
        <div className="tabs">{OBERSTUFE.map((s) => <button key={s} className={`tab ${stufe === s ? 'active' : ''}`} onClick={() => setStufe(s)}>{s}</button>)}</div>
        <p>Für die Jahrgangsstufe <b>{stufe}</b> ist noch keine Stufe angelegt.</p>
        <button className="primary" onClick={() => update(`Stufe ${stufe} angelegt`, (d) => { d.klassen.push(newKlasse({ name: stufe, jahrgang: stufe, keineHohlstunden: false, schuelerzahl: 90 })); })}>Stufe {stufe} anlegen</button>
      </div>
    );

  return (
    <>
      <div className="tabs">{OBERSTUFE.map((s) => <button key={s} className={`tab ${stufe === s ? 'active' : ''}`} onClick={() => setStufe(s)}>{s}</button>)}</div>
      <div className="card">
        <div className="row" style={{ marginBottom: 10 }}>
          <button className="primary" onClick={() => setNeu(true)}>+ Kurs</button>
          <span className="muted small">{kurse.length} Kurse · {schienen.length} Schienen · APO-GOSt: GK 3 Std., LK 5 Std., VTF/PJK 2 Std.</span>
        </div>
        <h3>Schienenübersicht (Bänder)</h3>
        <div className="row" style={{ alignItems: 'flex-start', overflowX: 'auto', flexWrap: 'nowrap' }}>
          {schienen.map((s) => {
            const k = kollisionen(s);
            return (
              <div key={s} className="card" style={{ minWidth: 170, padding: 10, marginBottom: 0 }}>
                <div className="row"><b>Schiene {s}</b><span className="spacer" /><span className="pill">{raumBedarf(s)} Räume</span></div>
                {k.length > 0 && <div className="pill err" style={{ margin: '4px 0' }}>Kollision: {k.join(', ')}</div>}
                {kurse.filter((x) => (x.schiene || '—') === s).map((x) => (
                  <div key={x.id} className="lesson" style={{ background: idx.faecher.get(x.fachId)?.farbe ?? '#eee', cursor: 'default' }}>
                    <span className="f">{x.kursname || idx.faecher.get(x.fachId)?.kuerzel}</span> <span className="x">{x.lehrerIds.map((l) => idx.lehrer.get(l)?.kuerzel).join(', ')} · {x.wochenstunden}h</span>
                  </div>
                ))}
              </div>
            );
          })}
        </div>
        <h3>Kursliste</h3>
        <div className="tbl-wrap">
          <table className="tbl">
            <thead><tr><th>Kurs</th><th>Fach</th><th>Kursart</th><th>Lehrkraft</th><th>Std.</th><th>Schiene</th><th>HJ</th><th>verplant</th><th /></tr></thead>
            <tbody>
              {[...kurse].sort((a, b) => a.kursname.localeCompare(b.kursname)).map((u) => (
                <tr key={u.id}>
                  <td><TextInput value={u.kursname} onChange={(v) => set(u.id, (x) => { x.kursname = v; x.gruppe = v; })} /></td>
                  <td>{idx.faecher.get(u.fachId)?.kuerzel}</td>
                  <td>
                    <select value={u.kursart ?? ''} onChange={(e) => set(u.id, (x) => { x.kursart = (e.target.value || null) as Kursart | null; })}>
                      <option value="">–</option>
                      {Object.entries(KURSARTEN).map(([k, v]) => <option key={k} value={k}>{k} – {v.name}</option>)}
                    </select>
                  </td>
                  <td><MultiSelect options={lehrerOpts} value={u.lehrerIds} onChange={(v) => set(u.id, (x) => { x.lehrerIds = v; })} /></td>
                  <td style={{ width: 70 }}><NumberInput value={u.wochenstunden} min={0} onChange={(n) => set(u.id, (x) => { x.wochenstunden = n; })} /></td>
                  <td style={{ width: 90 }}><TextInput value={u.schiene} onChange={(v) => set(u.id, (x) => { x.schiene = v; })} /></td>
                  <td style={{ width: 90 }}>
                    <select value={u.halbjahr} onChange={(e) => set(u.id, (x) => { x.halbjahr = Number(e.target.value) as 0 | 1 | 2; })}>
                      <option value={0}>ganz</option><option value={1}>1. HJ</option><option value={2}>2. HJ</option>
                    </select>
                  </td>
                  <td>{idx.plaeneProUnterricht.get(u.id)?.length ?? 0}/{u.wochenstunden}</td>
                  <td><button className="sm danger" onClick={() => update('Kurs gelöscht', (d) => loescheUnterrichte(d, new Set([u.id])))}>✕</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      {neu && <NeuerKurs stufeId={klasse.id} stufe={stufe} onClose={() => setNeu(false)} />}
    </>
  );
}

function NeuerKurs({ stufeId, stufe, onClose }: { stufeId: string; stufe: string; onClose: () => void }) {
  const { data, update } = useStore();
  const [fachId, setFachId] = useState(data.faecher[0]?.id ?? '');
  const [art, setArt] = useState<Kursart>('GK');
  const [anzahl, setAnzahl] = useState(1);
  const [schiene, setSchiene] = useState('1');
  const [lehrer, setLehrer] = useState<string[]>([]);
  const fach = data.faecher.find((f) => f.id === fachId);
  const create = () => {
    update('Kurse angelegt', (d) => {
      const vorhanden = d.unterricht.filter((u) => u.klassenIds.includes(stufeId) && u.fachId === fachId && u.kursart === art).length;
      for (let i = 0; i < anzahl; i++) {
        const name = `${fach?.kuerzel ?? '?'}-${art}${vorhanden + i + 1}`;
        const raumIds = fach?.raumTyp ? d.raeume.filter((r) => r.typ === fach.raumTyp).map((r) => r.id) : [];
        d.unterricht.push(newUnterricht({
          fachId, klassenIds: [stufeId], lehrerIds: i === 0 ? lehrer : [], wochenstunden: KURSARTEN[art].stunden,
          doppelstunden: art === 'LK' ? 2 : art === 'GK' ? 1 : 1, kursart: art, kursname: name, gruppe: name,
          schiene, raumIds,
        }));
      }
    });
    onClose();
  };
  return (
    <Modal title={`Neuer Kurs (${stufe})`} onClose={onClose} footer={<><button onClick={onClose}>Abbrechen</button><button className="primary" onClick={create}>Anlegen</button></>}>
      <div className="grid2">
        <Field label="Fach">
          <select value={fachId} onChange={(e) => setFachId(e.target.value)}>
            {[...data.faecher].sort((a, b) => a.kuerzel.localeCompare(b.kuerzel)).map((f) => <option key={f.id} value={f.id}>{f.kuerzel} – {f.name}</option>)}
          </select>
        </Field>
        <Field label="Kursart">
          <select value={art} onChange={(e) => setArt(e.target.value as Kursart)}>
            {Object.entries(KURSARTEN).map(([k, v]) => <option key={k} value={k}>{k} – {v.name} ({v.stunden} Std.)</option>)}
          </select>
        </Field>
        <Field label="Anzahl paralleler Kurse"><NumberInput value={anzahl} min={1} max={10} onChange={setAnzahl} /></Field>
        <Field label="Schiene"><input value={schiene} onChange={(e) => setSchiene(e.target.value)} /></Field>
        <Field label="Lehrkraft (1. Kurs)"><MultiSelect options={data.lehrer.map((l) => ({ value: l.id, label: l.kuerzel }))} value={lehrer} onChange={setLehrer} /></Field>
      </div>
      <p className="muted small">Kurse derselben Schiene werden im Stundenplan parallel gelegt. Mehrere Kurse eines Fachs (z. B. M-GK1, M-GK2) landen zunächst in derselben Schiene und können in der Kursliste anderen Schienen zugeordnet werden.</p>
    </Modal>
  );
}
