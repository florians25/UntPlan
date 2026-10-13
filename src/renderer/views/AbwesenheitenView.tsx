import { useState } from 'react';
import { formatDE, todayISO } from '../../core/dates';
import { lehrerName, newId, type Abwesenheit, type AbwesenheitsGrund, type AbwesenheitsTyp } from '../../core/model';
import { Field, Modal, MultiSelect, NumberInput, useConfirm } from '../components/ui';
import { useStore } from '../store';

export const GRUENDE: AbwesenheitsGrund[] = ['Krankheit', 'Fortbildung', 'Dienstlich', 'Klassenfahrt', 'Exkursion', 'Praktikum', 'Prüfung', 'Sonderurlaub', 'Mutterschutz/Elternzeit', 'Sperrung', 'Sonstiges'];

export function AbwesenheitenView() {
  const { data, idx, update } = useStore();
  const [edit, setEdit] = useState<Abwesenheit | null>(null);
  const [ab, setAb] = useState(todayISO());
  const [typ, setTyp] = useState<AbwesenheitsTyp | ''>('');
  const [dialog, confirm] = useConfirm();
  const S = data.zeitraster.stunden.length;
  const rows = data.abwesenheiten.filter((a) => a.bis >= ab && (!typ || a.typ === typ)).sort((a, b) => a.von.localeCompare(b.von));
  const name = (a: Abwesenheit) => a.typ === 'lehrer' ? `${idx.lehrer.get(a.refId)?.kuerzel ?? '?'} – ${lehrerName(idx.lehrer.get(a.refId))}` : a.typ === 'klasse' ? `Klasse ${idx.klassen.get(a.refId)?.name ?? '?'}` : `Raum ${idx.raeume.get(a.refId)?.kuerzel ?? '?'}`;
  const neu = (t: AbwesenheitsTyp): Abwesenheit => ({
    id: newId('aw'), typ: t, refId: t === 'lehrer' ? data.lehrer[0]?.id ?? '' : t === 'klasse' ? data.klassen[0]?.id ?? '' : data.raeume[0]?.id ?? '',
    von: todayISO(), bis: todayISO(), stundeVon: 1, stundeBis: S, grund: t === 'klasse' ? 'Klassenfahrt' : t === 'raum' ? 'Sperrung' : 'Krankheit', text: '', begleitLehrerIds: [],
  });
  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={() => setEdit(neu('lehrer'))}>+ Lehrkraft abwesend</button>
        <button onClick={() => setEdit(neu('klasse'))}>+ Klasse abwesend</button>
        <button onClick={() => setEdit(neu('raum'))}>+ Raum gesperrt</button>
        <span className="spacer" />
        <label className="row small">ab <input type="date" value={ab} onChange={(e) => setAb(e.target.value)} /></label>
        <select value={typ} onChange={(e) => setTyp(e.target.value as AbwesenheitsTyp | '')}>
          <option value="">alle</option><option value="lehrer">Lehrkräfte</option><option value="klasse">Klassen</option><option value="raum">Räume</option>
        </select>
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead><tr><th>Wer</th><th>von</th><th>bis</th><th>Stunden</th><th>Grund</th><th>Bemerkung</th><th /></tr></thead>
          <tbody>
            {rows.map((a) => (
              <tr key={a.id}>
                <td>{name(a)}{a.begleitLehrerIds.length > 0 && <div className="small muted">Begleitung: {a.begleitLehrerIds.map((l) => idx.lehrer.get(l)?.kuerzel).join(', ')}</div>}</td>
                <td>{formatDE(a.von)}</td>
                <td>{formatDE(a.bis)}</td>
                <td>{a.stundeVon === 1 && a.stundeBis >= S ? 'ganztägig' : `${a.stundeVon}.–${a.stundeBis}.`}</td>
                <td><span className="pill">{a.grund}</span></td>
                <td className="small">{a.text}</td>
                <td className="nowrap">
                  <button className="sm" onClick={() => setEdit(a)}>Bearbeiten</button>{' '}
                  <button className="sm danger" onClick={() => confirm('Abwesenheit löschen?', () => update('Abwesenheit gelöscht', (d) => { d.abwesenheiten = d.abwesenheiten.filter((x) => x.id !== a.id); }), 'Löschen')}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!rows.length && <div className="empty">Keine Abwesenheiten ab {formatDE(ab)}.</div>}
      </div>
      {edit && <AbwesenheitDialog a={edit} onClose={() => setEdit(null)} />}
    </div>
  );
}

export function AbwesenheitDialog({ a, onClose }: { a: Abwesenheit; onClose: () => void }) {
  const { data, update } = useStore();
  const [x, setX] = useState<Abwesenheit>(a);
  const S = data.zeitraster.stunden.length;
  const optionen = x.typ === 'lehrer' ? data.lehrer.map((l) => ({ value: l.id, label: `${l.kuerzel} – ${l.nachname}` }))
    : x.typ === 'klasse' ? data.klassen.map((k) => ({ value: k.id, label: k.name })) : data.raeume.map((r) => ({ value: r.id, label: r.kuerzel }));
  const speichern = () => {
    update('Abwesenheit gespeichert', (d) => {
      const i = d.abwesenheiten.findIndex((y) => y.id === x.id);
      const v = { ...x, bis: x.bis < x.von ? x.von : x.bis };
      if (i >= 0) d.abwesenheiten[i] = v;
      else d.abwesenheiten.push(v);
    });
    onClose();
  };
  return (
    <Modal title={x.typ === 'lehrer' ? 'Abwesenheit Lehrkraft' : x.typ === 'klasse' ? 'Abwesenheit Klasse' : 'Raumsperrung'} onClose={onClose}
      footer={<><button onClick={onClose}>Abbrechen</button><button className="primary" disabled={!x.refId} onClick={speichern}>Speichern</button></>}>
      <div className="grid2">
        <Field label={x.typ === 'lehrer' ? 'Lehrkraft' : x.typ === 'klasse' ? 'Klasse' : 'Raum'}>
          <MultiSelect single options={optionen} value={[x.refId]} onChange={(v) => setX({ ...x, refId: v[0] })} />
        </Field>
        <Field label="Grund">
          <select value={x.grund} onChange={(e) => setX({ ...x, grund: e.target.value as AbwesenheitsGrund })}>{GRUENDE.map((g) => <option key={g}>{g}</option>)}</select>
        </Field>
        <Field label="von"><input type="date" value={x.von} onChange={(e) => setX({ ...x, von: e.target.value, bis: x.bis < e.target.value ? e.target.value : x.bis })} /></Field>
        <Field label="bis (einschließlich)"><input type="date" value={x.bis} onChange={(e) => setX({ ...x, bis: e.target.value })} /></Field>
        <Field label="ab Stunde (erster Tag)"><NumberInput value={x.stundeVon} min={1} max={S} onChange={(n) => setX({ ...x, stundeVon: n })} /></Field>
        <Field label="bis Stunde (letzter Tag)"><NumberInput value={x.stundeBis} min={1} max={S} onChange={(n) => setX({ ...x, stundeBis: n })} /></Field>
        {x.typ === 'klasse' && (
          <Field label="Begleitende Lehrkräfte (gelten als abwesend)">
            <MultiSelect options={data.lehrer.map((l) => ({ value: l.id, label: l.kuerzel }))} value={x.begleitLehrerIds} onChange={(v) => setX({ ...x, begleitLehrerIds: v })} />
          </Field>
        )}
        <Field label="Bemerkung"><input value={x.text} onChange={(e) => setX({ ...x, text: e.target.value })} /></Field>
      </div>
      <p className="muted small">Gesundheitsdaten sind besonders schützenswert (DSGVO Art. 9). Der Grund „Krankheit“ wird im öffentlichen Vertretungsplan nicht angezeigt.</p>
    </Modal>
  );
}
