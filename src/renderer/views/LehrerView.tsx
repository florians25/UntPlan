import { useMemo, useState } from 'react';
import { newLehrer } from '../../core/factory';
import { istStundenLehrer } from '../../core/index';
import { loescheLehrer, verwendung } from '../../core/loeschen';
import { sollStunden, type Lehrer } from '../../core/model';
import { PFLICHTSTUNDEN } from '../../core/nrw';
import { Field, Modal, MultiSelect, NumberInput, Search, SlotGrid, TextInput, useConfirm, useSort } from '../components/ui';
import { useStore } from '../store';

export function LehrerView() {
  const { data, update, setView } = useStore();
  const [q, setQ] = useState('');
  const [edit, setEdit] = useState<string | null>(null);
  const [dialog, confirm] = useConfirm();
  const fachOpts = useMemo(() => data.faecher.map((f) => ({ value: f.id, label: f.kuerzel })), [data.faecher]);
  const rows = data.lehrer.filter((l) => `${l.kuerzel} ${l.nachname} ${l.vorname}`.toLowerCase().includes(q.toLowerCase()));
  const { sorted, th } = useSort(rows, {
    kuerzel: (l) => l.kuerzel,
    name: (l) => l.nachname,
    soll: (l) => sollStunden(l),
    ist: (l) => istStundenLehrer(data, l.id),
  }, 'kuerzel');
  const set = (id: string, f: (l: Lehrer) => void, label = 'Lehrkraft bearbeitet') =>
    update(label, (d) => { const l = d.lehrer.find((x) => x.id === id); if (l) f(l); });

  const add = () => {
    const l = newLehrer({ kuerzel: 'NEU', nachname: 'Neue Lehrkraft', pflichtstunden: PFLICHTSTUNDEN[data.meta.schulform] });
    update('Lehrkraft angelegt', (d) => { d.lehrer.push(l); });
    setEdit(l.id);
  };
  const editing = data.lehrer.find((l) => l.id === edit);

  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={add}>+ Lehrkraft</button>
        <Search value={q} onChange={setQ} />
        <span className="spacer" />
        <span className="muted small">{data.lehrer.length} Lehrkräfte · Soll gesamt {data.lehrer.reduce((s, l) => s + sollStunden(l), 0)} Std.</span>
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead>
            <tr>
              {th('kuerzel', 'Kürzel', { width: 80 })}
              {th('name', 'Nachname')}
              <th>Vorname</th>
              <th style={{ width: 160 }}>Fächer</th>
              <th style={{ width: 80 }}>Pflicht</th>
              <th style={{ width: 70 }}>Anrechn.</th>
              {th('soll', 'Soll', { width: 60 })}
              {th('ist', 'Ist', { width: 60 })}
              <th style={{ width: 50 }}>Aktiv</th>
              <th style={{ width: 150 }} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((l) => {
              const ist = istStundenLehrer(data, l.id);
              const soll = sollStunden(l);
              const anr = l.anrechnungen.reduce((s, a) => s + a.stunden, 0);
              return (
                <tr key={l.id}>
                  <td><TextInput value={l.kuerzel} onChange={(v) => set(l.id, (x) => { x.kuerzel = v.toUpperCase(); })} /></td>
                  <td><TextInput value={l.nachname} onChange={(v) => set(l.id, (x) => { x.nachname = v; })} /></td>
                  <td><TextInput value={l.vorname} onChange={(v) => set(l.id, (x) => { x.vorname = v; })} /></td>
                  <td><MultiSelect options={fachOpts} value={l.faecher} onChange={(v) => set(l.id, (x) => { x.faecher = v; })} /></td>
                  <td><NumberInput value={l.pflichtstunden} step={0.5} min={0} onChange={(n) => set(l.id, (x) => { x.pflichtstunden = n; })} /></td>
                  <td className="muted">{anr || ''}</td>
                  <td>{soll}</td>
                  <td><span className={`pill ${ist === soll ? 'ok' : ist > soll ? 'warn' : 'err'}`}>{ist}</span></td>
                  <td><input type="checkbox" checked={l.aktiv} onChange={(e) => set(l.id, (x) => { x.aktiv = e.target.checked; })} /></td>
                  <td className="nowrap">
                    <button className="sm" onClick={() => setEdit(l.id)}>Details</button>{' '}
                    <button className="sm" onClick={() => setView('stundenplan', `lehrer:${l.id}`)}>Plan</button>{' '}
                    <button className="sm danger" onClick={() => {
                      const n = verwendung(data, 'lehrer', l.id);
                      confirm(<>Lehrkraft <b>{l.kuerzel}</b> löschen?{n > 0 && <> Sie ist in {n} Unterricht(en) eingesetzt; diese verlieren die Lehrkraft.</>}</>, () => update('Lehrkraft gelöscht', (d) => loescheLehrer(d, l.id)), 'Löschen');
                    }}>✕</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {editing && <LehrerDetails l={editing} onClose={() => setEdit(null)} />}
    </div>
  );
}

function LehrerDetails({ l, onClose }: { l: Lehrer; onClose: () => void }) {
  const { data, update } = useStore();
  const set = (f: (x: Lehrer) => void) => update('Lehrkraft bearbeitet', (d) => { const x = d.lehrer.find((y) => y.id === l.id); if (x) f(x); });
  const S = data.zeitraster.stunden.length;
  return (
    <Modal wide title={`${l.kuerzel} – ${l.vorname} ${l.nachname}`} onClose={onClose} footer={<button className="primary" onClick={onClose}>Fertig</button>}>
      <div className="grid3">
        <Field label="Kürzel"><TextInput value={l.kuerzel} onChange={(v) => set((x) => { x.kuerzel = v.toUpperCase(); })} /></Field>
        <Field label="Nachname"><TextInput value={l.nachname} onChange={(v) => set((x) => { x.nachname = v; })} /></Field>
        <Field label="Vorname"><TextInput value={l.vorname} onChange={(v) => set((x) => { x.vorname = v; })} /></Field>
        <Field label="E-Mail (dienstlich)"><TextInput value={l.email} onChange={(v) => set((x) => { x.email = v; })} /></Field>
        <Field label="Pflichtstunden (Deputat)"><NumberInput value={l.pflichtstunden} step={0.5} onChange={(n) => set((x) => { x.pflichtstunden = n; })} /></Field>
        <Field label="Max. Stunden pro Tag"><NumberInput value={l.maxStundenProTag} min={1} max={S} onChange={(n) => set((x) => { x.maxStundenProTag = n; })} /></Field>
      </div>
      <h3>Anrechnungen / Ermäßigungen</h3>
      <table className="tbl" style={{ maxWidth: 520 }}>
        <thead><tr><th>Grund</th><th style={{ width: 90 }}>Stunden</th><th style={{ width: 40 }} /></tr></thead>
        <tbody>
          {l.anrechnungen.map((a, i) => (
            <tr key={i}>
              <td>
                <TextInput value={a.grund} onChange={(v) => set((x) => { x.anrechnungen[i].grund = v; })} />
              </td>
              <td><NumberInput value={a.stunden} step={0.5} onChange={(n) => set((x) => { x.anrechnungen[i].stunden = n; })} /></td>
              <td><button className="sm danger" onClick={() => set((x) => { x.anrechnungen.splice(i, 1); })}>✕</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="row" style={{ marginTop: 6 }}>
        {['Schulleitung', 'Altersermäßigung', 'Schwerbehinderung', 'Fachleitung', 'Ausbildungskoordination', 'Sonstige Anrechnung'].map((g) => (
          <button key={g} className="sm" onClick={() => set((x) => { x.anrechnungen.push({ grund: g, stunden: 1 }); })}>+ {g}</button>
        ))}
      </div>
      <div className="grid2" style={{ marginTop: 14 }}>
        <div>
          <h3>Sperrzeiten (nicht einsetzbar)</h3>
          <SlotGrid tage={data.zeitraster.tage} stunden={S} value={l.sperrzeiten} onChange={(v) => set((x) => { x.sperrzeiten = v; })} />
          <p className="muted small">Klick auf Tag/Stunde markiert ganze Spalte/Zeile.</p>
        </div>
        <div>
          <h3>Bereitschaft / Präsenzstunden (Vertretungsreserve)</h3>
          <SlotGrid green tage={data.zeitraster.tage} stunden={S} value={l.bereitschaft} onChange={(v) => set((x) => { x.bereitschaft = v; })} />
        </div>
      </div>
      <Field label="Notiz"><textarea rows={2} value={l.notiz} onChange={(e) => set((x) => { x.notiz = e.target.value; })} /></Field>
    </Modal>
  );
}
