import { useState } from 'react';
import { addDays } from '../../core/dates';
import { newId, type Schulform } from '../../core/model';
import { JAHRGAENGE, PFLICHTSTUNDEN, SCHULFORMEN, pruefeSchulnummer } from '../../core/nrw';
import { Field, NumberInput, TextInput, useConfirm } from '../components/ui';
import { useStore } from '../store';

export function SchuleView() {
  const [tab, setTab] = useState('schule');
  return (
    <>
      <div className="tabs">
        {[['schule', 'Schuldaten'], ['zeitraster', 'Zeitraster'], ['pausen', 'Pausen & Aufsichtsbereiche'], ['einst', 'Einstellungen']].map(([k, l]) => (
          <button key={k} className={`tab ${tab === k ? 'active' : ''}`} onClick={() => setTab(k)}>{l}</button>
        ))}
      </div>
      {tab === 'schule' && <Schuldaten />}
      {tab === 'zeitraster' && <ZeitrasterEditor />}
      {tab === 'pausen' && <Pausen />}
      {tab === 'einst' && <Einstellungen />}
    </>
  );
}

function Schuldaten() {
  const { data, update } = useStore();
  const m = data.meta;
  const set = (k: keyof typeof m, v: string) => update(`Schuldaten: ${k}`, (d) => { (d.meta as any)[k] = v; });
  const [dialog, confirm] = useConfirm();
  return (
    <div className="card" style={{ maxWidth: 900 }}>
      {dialog}
      <div className="grid2">
        <Field label="Schulname"><TextInput value={m.schulname} onChange={(v) => set('schulname', v)} /></Field>
        <Field label="Schulnummer (6-stellig, NRW)">
          <TextInput value={m.schulnummer} onChange={(v) => set('schulnummer', v)} />
          {m.schulnummer && !pruefeSchulnummer(m.schulnummer) && <span style={{ color: 'var(--err)' }}>Die Schulnummer muss 6 Ziffern haben.</span>}
        </Field>
        <Field label="Schulform">
          <select value={m.schulform} onChange={(e) => {
            const sf = e.target.value as Schulform;
            set('schulform', sf);
            if (data.lehrer.length)
              confirm(<>Pflichtstunden aller Vollzeit-Lehrkräfte auf <b>{PFLICHTSTUNDEN[sf]}</b> Std. ({SCHULFORMEN[sf]}) setzen?</>, () =>
                update('Pflichtstunden angepasst', (d) => {
                  const alt = PFLICHTSTUNDEN[m.schulform];
                  d.lehrer.forEach((l) => { if (l.pflichtstunden === alt) l.pflichtstunden = PFLICHTSTUNDEN[sf]; });
                }), 'Anpassen');
          }}>
            {Object.entries(SCHULFORMEN).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <Field label="Ort"><TextInput value={m.ort} onChange={(v) => set('ort', v)} /></Field>
        <Field label="Schuljahr"><TextInput value={m.schuljahr} onChange={(v) => set('schuljahr', v)} /></Field>
        <div />
        <Field label="Erster Schultag"><input type="date" value={m.schuljahrBeginn} onChange={(e) => set('schuljahrBeginn', e.target.value)} /></Field>
        <Field label="Letzter Schultag"><input type="date" value={m.schuljahrEnde} onChange={(e) => set('schuljahrEnde', e.target.value)} /></Field>
        <Field label="Beginn 2. Halbjahr"><input type="date" value={m.halbjahrWechsel} onChange={(e) => set('halbjahrWechsel', e.target.value)} /></Field>
      </div>
      <p className="muted small" style={{ marginTop: 16 }}>
        Bundesland: Nordrhein-Westfalen · Pflichtstunden Vollzeit {SCHULFORMEN[m.schulform]}: {PFLICHTSTUNDEN[m.schulform]} Std. ·
        Jahrgänge: {JAHRGAENGE[m.schulform].join(', ')}
      </p>
    </div>
  );
}

function ZeitrasterEditor() {
  const { data, update } = useStore();
  const z = data.zeitraster;
  return (
    <div className="card" style={{ maxWidth: 760 }}>
      <div className="row" style={{ marginBottom: 12 }}>
        <Field label="Unterrichtstage">
          <select value={z.tage} onChange={(e) => update('Unterrichtstage', (d) => { d.zeitraster.tage = Number(e.target.value); })}>
            <option value={5}>Montag – Freitag</option>
            <option value={6}>Montag – Samstag</option>
          </select>
        </Field>
        <Field label="Mittagspause in Stunde (0 = keine)">
          <NumberInput value={z.mittagspauseStunde} min={0} max={z.stunden.length} onChange={(n) => update('Mittagspause', (d) => { d.zeitraster.mittagspauseStunde = n; })} />
        </Field>
      </div>
      <table className="tbl">
        <thead><tr><th>Stunde</th><th>Beginn</th><th>Ende</th><th>Dauer</th><th /></tr></thead>
        <tbody>
          {z.stunden.map((p, i) => {
            const [bh, bm] = p.beginn.split(':').map(Number);
            const [eh, em] = p.ende.split(':').map(Number);
            return (
              <tr key={i}>
                <td>{p.nr}.</td>
                <td><input type="time" value={p.beginn} onChange={(e) => update('Zeitraster', (d) => { d.zeitraster.stunden[i].beginn = e.target.value; })} /></td>
                <td><input type="time" value={p.ende} onChange={(e) => update('Zeitraster', (d) => { d.zeitraster.stunden[i].ende = e.target.value; })} /></td>
                <td className="muted">{eh * 60 + em - (bh * 60 + bm)} min</td>
                <td>{i === z.stunden.length - 1 && <button className="sm danger" onClick={() => update('Stunde entfernt', (d) => { d.zeitraster.stunden.pop(); })}>Entfernen</button>}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="row" style={{ marginTop: 10 }}>
        <button onClick={() => update('Stunde hinzugefügt', (d) => {
          const last = d.zeitraster.stunden[d.zeitraster.stunden.length - 1];
          const [h, mm] = (last?.ende ?? '07:15').split(':').map(Number);
          const b = h * 60 + mm + 5;
          const f = (x: number) => `${String(Math.floor(x / 60)).padStart(2, '0')}:${String(x % 60).padStart(2, '0')}`;
          d.zeitraster.stunden.push({ nr: (last?.nr ?? 0) + 1, beginn: f(b), ende: f(b + 45) });
        })}>+ Stunde</button>
        <span className="muted small">Für 60- oder 67,5-Minuten-Raster einfach Beginn/Ende anpassen.</span>
      </div>
    </div>
  );
}

function Pausen() {
  const { data, update } = useStore();
  return (
    <div className="grid2">
      <div className="card">
        <h2>Pausen</h2>
        <table className="tbl">
          <thead><tr><th>Bezeichnung</th><th>nach Stunde</th><th>Dauer (min)</th><th /></tr></thead>
          <tbody>
            {data.pausen.map((p, i) => (
              <tr key={p.id}>
                <td><TextInput value={p.name} onChange={(v) => update('Pause', (d) => { d.pausen[i].name = v; })} /></td>
                <td><NumberInput value={p.nachStunde} min={1} onChange={(n) => update('Pause', (d) => { d.pausen[i].nachStunde = n; })} /></td>
                <td><NumberInput value={p.dauer} min={0} onChange={(n) => update('Pause', (d) => { d.pausen[i].dauer = n; })} /></td>
                <td><button className="sm danger" onClick={() => update('Pause gelöscht', (d) => { d.pausen.splice(i, 1); d.aufsichten = d.aufsichten.filter((a) => a.pauseId !== p.id); })}>✕</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => update('Pause angelegt', (d) => { d.pausen.push({ id: newId('pa'), name: 'Pause', nachStunde: 2, dauer: 15 }); })}>+ Pause</button>
      </div>
      <div className="card">
        <h2>Aufsichtsbereiche</h2>
        <table className="tbl">
          <thead><tr><th>Bereich</th><th>Aufsichten je Pause</th><th /></tr></thead>
          <tbody>
            {data.aufsichtsbereiche.map((b, i) => (
              <tr key={b.id}>
                <td><TextInput value={b.name} onChange={(v) => update('Aufsichtsbereich', (d) => { d.aufsichtsbereiche[i].name = v; })} /></td>
                <td><NumberInput value={b.bedarf} min={0} onChange={(n) => update('Aufsichtsbereich', (d) => { d.aufsichtsbereiche[i].bedarf = n; })} /></td>
                <td><button className="sm danger" onClick={() => update('Bereich gelöscht', (d) => { d.aufsichtsbereiche.splice(i, 1); d.aufsichten = d.aufsichten.filter((a) => a.bereichId !== b.id); })}>✕</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => update('Bereich angelegt', (d) => { d.aufsichtsbereiche.push({ id: newId('ab'), name: 'Neuer Bereich', bedarf: 1 }); })}>+ Bereich</button>
      </div>
    </div>
  );
}

function Einstellungen() {
  const { data, update } = useStore();
  const e = data.einstellungen;
  const jg = [...JAHRGAENGE[data.meta.schulform]];
  return (
    <div className="card" style={{ maxWidth: 760 }}>
      <div className="grid2">
        <Field label="Mehrarbeit vergütungsfähig ab mehr als … Std./Monat">
          <NumberInput value={e.mehrarbeitsGrenze} min={0} onChange={(n) => update('Einstellung', (d) => { d.einstellungen.mehrarbeitsGrenze = n; })} />
        </Field>
        <Field label="Max. Vertretungen je Lehrkraft und Woche (Richtwert)">
          <NumberInput value={e.maxVertretungenProWoche} min={0} onChange={(n) => update('Einstellung', (d) => { d.einstellungen.maxVertretungenProWoche = n; })} />
        </Field>
        <Field label="Entfall in Randstunden erlaubt ab Jahrgang">
          <select value={e.entfallErlaubtAbJahrgang} onChange={(ev) => update('Einstellung', (d) => { d.einstellungen.entfallErlaubtAbJahrgang = ev.target.value; })}>
            {jg.map((j) => <option key={j}>{j}</option>)}
            <option value="99">nie</option>
          </select>
        </Field>
        <Field label="Titel des Vertretungsplans">
          <TextInput value={e.vertretungsplanTitel} onChange={(v) => update('Einstellung', (d) => { d.einstellungen.vertretungsplanTitel = v; })} />
        </Field>
      </div>
      <p className="muted small">In der Sekundarstufe I ist verlässlicher Unterricht sicherzustellen – Entfall wird daher standardmäßig erst ab der Oberstufe vorgeschlagen. Nächster Halbjahreswechsel: {data.meta.halbjahrWechsel ? addDays(data.meta.halbjahrWechsel, 0).split('-').reverse().join('.') : '–'}</p>
    </div>
  );
}
