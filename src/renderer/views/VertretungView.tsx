import { useEffect, useMemo, useState } from 'react';
import { offeneAufsichten } from '../../core/aufsicht';
import { addDays, formatDE, todayISO, weekdayIndex } from '../../core/dates';
import { kurzKlassen, kurzLehrer } from '../../core/index';
import { lehrerName, newId, TAGE_LANG, type Vertretung, type VertretungsArt } from '../../core/model';
import {
  abwesenheitenAm, automatischeVertretung, betroffeneStunden, freieRaeume, freierTagAm, istUnterrichtstag,
  lehrerBelegt, neueVertretung, tagesplan, vertretungsVorschlaege, vertretungenAm, type BetroffeneStunde,
} from '../../core/vertretung';
import { api } from '../api';
import { Field, MultiSelect, NumberInput, useConfirm } from '../components/ui';
import { vertretungsplanHTML } from '../print';
import { useStore } from '../store';
import { AbwesenheitDialog } from './AbwesenheitenView';

export function VertretungView() {
  const { data, idx, update, viewParam, toast } = useStore();
  const [datum, setDatum] = useState(viewParam && /^\d{4}-/.test(viewParam) ? viewParam : todayISO());
  const [sel, setSel] = useState<string | null>(null);
  const [abwNeu, setAbwNeu] = useState(false);
  const [dialog, confirm] = useConfirm();
  useEffect(() => { if (viewParam && /^\d{4}-/.test(viewParam)) setDatum(viewParam); }, [viewParam]);

  const betroffen = useMemo(() => betroffeneStunden(data, datum, idx), [data, datum, idx]);
  const sonst = useMemo(() => vertretungenAm(data, datum).filter((v) => !v.planeintragId), [data, datum]);
  const abw = abwesenheitenAm(data, datum);
  const aufsichten = useMemo(() => offeneAufsichten(data, datum), [data, datum]);
  const offen = betroffen.filter((b) => !b.vertretung).length;
  const ft = freierTagAm(data, datum);
  const schultag = istUnterrichtstag(data, datum);
  const auswahl = betroffen.find((b) => b.planeintrag.id === sel) ?? betroffen.find((b) => !b.vertretung) ?? betroffen[0];

  const schritt = (n: number) => {
    let x = addDays(datum, n);
    for (let i = 0; i < 30 && !istUnterrichtstag(data, x); i++) x = addDays(x, n);
    setDatum(x);
    setSel(null);
  };

  const auto = () => {
    const neu = automatischeVertretung(data, datum);
    if (!neu.length) return toast('Keine offenen Stunden.', 'info');
    update(`Automatische Vertretung ${formatDE(datum)}`, (d) => { d.vertretungen.push(...neu); });
    toast(`${neu.length} Vertretungen vorgeschlagen – bitte prüfen.`, 'ok');
  };

  return (
    <>
      {dialog}
      <div className="card" style={{ padding: 12 }}>
        <div className="row">
          <button onClick={() => schritt(-1)}>◀</button>
          <input type="date" value={datum} onChange={(e) => { setDatum(e.target.value); setSel(null); }} />
          <button onClick={() => schritt(1)}>▶</button>
          <button className="ghost" onClick={() => setDatum(todayISO())}>Heute</button>
          <b>{TAGE_LANG[weekdayIndex(datum)] ?? 'Sonntag'}, {formatDE(datum)}</b>
          {ft && <span className="pill warn">{ft.name}</span>}
          <span className={`pill ${offen ? 'err' : 'ok'}`}>{offen} offen</span>
          <span className="spacer" />
          <button onClick={() => setAbwNeu(true)}>+ Abwesenheit</button>
          <button className="primary" disabled={!offen} onClick={auto}>⚙️ Automatisch vertreten</button>
          <button onClick={() => api.printPdf(vertretungsplanHTML(data, [datum], true), `Vertretungsplan_${datum}.pdf`, false)}>🖨️ PDF</button>
          <button className="danger" disabled={!vertretungenAm(data, datum).length} onClick={() => confirm(`Alle Vertretungen am ${formatDE(datum)} löschen?`, () => update('Vertretungen gelöscht', (d) => { d.vertretungen = d.vertretungen.filter((v) => v.datum !== datum); }), 'Löschen')}>Tag zurücksetzen</button>
        </div>
      </div>
      {!schultag ? <div className="card empty">An diesem Tag findet kein Unterricht statt.</div> : (
        <div className="grid2" style={{ gridTemplateColumns: '1.2fr 1fr', alignItems: 'start' }}>
          <div>
            <div className="card">
              <h2>Abwesend</h2>
              <div className="row">
                {abw.map((a) => (
                  <span key={a.id} className="chip">
                    {a.typ === 'lehrer' ? idx.lehrer.get(a.refId)?.kuerzel : a.typ === 'klasse' ? idx.klassen.get(a.refId)?.name : idx.raeume.get(a.refId)?.kuerzel}
                    <span className="muted"> {a.grund}{a.stundeVon > 1 || a.stundeBis < data.zeitraster.stunden.length ? ` ${a.stundeVon}.–${a.stundeBis}.` : ''}</span>
                  </span>
                ))}
                {!abw.length && <span className="muted">Keine Abwesenheiten erfasst.</span>}
              </div>
            </div>
            <div className="card">
              <h2>Betroffene Stunden</h2>
              <table className="tbl">
                <thead><tr><th>Std.</th><th>Klasse</th><th>Fach</th><th>Lehrkraft</th><th>Grund</th><th>Regelung</th></tr></thead>
                <tbody>
                  {betroffen.map((b) => (
                    <tr key={b.planeintrag.id} className={auswahl?.planeintrag.id === b.planeintrag.id ? 'sel' : ''} style={{ cursor: 'pointer' }} onClick={() => setSel(b.planeintrag.id)}>
                      <td>{b.stunde}.</td>
                      <td>{kurzKlassen(idx, b.unterricht.klassenIds)}</td>
                      <td>{idx.faecher.get(b.unterricht.fachId)?.kuerzel}</td>
                      <td>{kurzLehrer(idx, b.unterricht.lehrerIds)}</td>
                      <td><span className="pill">{b.grund === 'lehrer' ? 'Lehrkraft fehlt' : b.grund === 'klasse' ? 'Klasse fehlt' : 'Raum gesperrt'}</span></td>
                      <td>{b.vertretung ? <span className={`pill ${b.vertretung.art === 'Entfall' ? 'warn' : 'ok'}`}>{b.vertretung.art} {kurzLehrer(idx, b.vertretung.vertreterIds)}</span> : <span className="pill err">offen</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {!betroffen.length && <div className="empty">Keine betroffenen Stunden.</div>}
            </div>
            {aufsichten.length > 0 && <AufsichtsVertretung datum={datum} />}
            <Sondereinsaetze datum={datum} liste={sonst} />
            <Nachrichten datum={datum} />
          </div>
          <div>{auswahl && <Regelung key={auswahl.planeintrag.id + datum} datum={datum} b={auswahl} />}</div>
        </div>
      )}
      {abwNeu && (
        <AbwesenheitDialog onClose={() => setAbwNeu(false)} a={{ id: newId('aw'), typ: 'lehrer', refId: data.lehrer[0]?.id ?? '', von: datum, bis: datum, stundeVon: 1, stundeBis: data.zeitraster.stunden.length, grund: 'Krankheit', text: '', begleitLehrerIds: [] }} />
      )}
    </>
  );
}

function Regelung({ datum, b }: { datum: string; b: BetroffeneStunde }) {
  const { data, idx, update } = useStore();
  const kandidaten = useMemo(() => vertretungsVorschlaege(data, datum, b.unterricht, b.stunde, idx), [data, datum, b, idx]);
  const raeume = useMemo(() => freieRaeume(data, datum, b.stunde, idx), [data, datum, b.stunde, idx]);
  const [text, setText] = useState(b.vertretung?.text ?? '');
  const [nachDatum, setNachDatum] = useState(b.vertretung?.nachDatum ?? datum);
  const [nachStunde, setNachStunde] = useState(b.vertretung?.nachStunde ?? b.stunde);
  const fach = idx.faecher.get(b.unterricht.fachId);
  const v = b.vertretung;

  const setze = (art: VertretungsArt, extra: Partial<Vertretung> = {}) =>
    update(`${art} ${kurzKlassen(idx, b.unterricht.klassenIds)} ${b.stunde}.`, (d) => {
      d.vertretungen = d.vertretungen.filter((x) => !(x.datum === datum && x.planeintragId === b.planeintrag.id));
      d.vertretungen.push(neueVertretung({
        datum, stunde: b.stunde, planeintragId: b.planeintrag.id, unterrichtId: b.unterricht.id, fachId: b.unterricht.fachId,
        raumId: art === 'Entfall' || art === 'Freisetzung' ? null : b.planeintrag.raumId, art, text, ...extra,
      }));
    });
  const max = Math.max(1, ...kandidaten.map((k) => k.punkte));

  return (
    <div className="card">
      <h2>{b.stunde}. Std. · {fach?.kuerzel} {kurzKlassen(idx, b.unterricht.klassenIds)} <span className="muted">({kurzLehrer(idx, b.unterricht.lehrerIds)})</span></h2>
      {v && (
        <div className="row" style={{ marginBottom: 10 }}>
          <span className="pill ok">Aktuell: {v.art} {kurzLehrer(idx, v.vertreterIds)} {v.raumId ? `· ${idx.raeume.get(v.raumId)?.kuerzel}` : ''}</span>
          <label className="check small"><input type="checkbox" checked={v.mehrarbeit} onChange={(e) => update('Mehrarbeit', (d) => { const x = d.vertretungen.find((y) => y.id === v.id); if (x) x.mehrarbeit = e.target.checked; })} /> Mehrarbeit</label>
          <button className="sm danger" onClick={() => update('Regelung entfernt', (d) => { d.vertretungen = d.vertretungen.filter((x) => x.id !== v.id); })}>Regelung entfernen</button>
        </div>
      )}
      <Field label="Hinweistext (erscheint im Vertretungsplan)"><input value={text} onChange={(e) => setText(e.target.value)} placeholder="z. B. Aufgaben im Moodle-Kurs" /></Field>
      <div className="row" style={{ margin: '10px 0' }}>
        <button onClick={() => setze('Entfall')} title={b.entfallMoeglich ? 'Randstunde – Entfall zulässig' : 'Achtung: Keine Randstunde bzw. Sek I'}>Entfall {b.entfallMoeglich ? '✔' : '⚠'}</button>
        <button onClick={() => setze('EVA')}>EVA</button>
        <button onClick={() => setze('Freisetzung')}>Freisetzung</button>
      </div>
      <h3>Vertretung durch</h3>
      <div style={{ maxHeight: 360, overflow: 'auto', border: '1px solid var(--line)', borderRadius: 6 }}>
        {kandidaten.map((k) => (
          <div key={k.lehrerId} className="cand">
            <b>{idx.lehrer.get(k.lehrerId)?.kuerzel}</b>
            <div className="small">
              <div>{lehrerName(idx.lehrer.get(k.lehrerId))}</div>
              <div className="muted">{k.gruende.join(' · ')}</div>
            </div>
            <div className="bar"><div style={{ width: `${Math.max(4, (k.punkte / max) * 100)}%`, background: k.punkte < 0 ? 'var(--err)' : undefined }} /></div>
            <div className="row" style={{ flexWrap: 'nowrap' }}>
              <button className="sm primary" onClick={() => setze('Vertretung', { vertreterIds: [k.lehrerId], mehrarbeit: k.mehrarbeit })}>Vertretung</button>
              <button className="sm" title="Mitbetreuung (Lehrkraft betreut zusätzlich)" onClick={() => setze('Mitbetreuung', { vertreterIds: [k.lehrerId], mehrarbeit: false })}>Mitbetr.</button>
            </div>
          </div>
        ))}
        {!kandidaten.length && <div className="empty">Keine freie Lehrkraft.</div>}
      </div>
      <MitbetreuungParallel datum={datum} b={b} onSetze={(id) => setze('Mitbetreuung', { vertreterIds: [id], mehrarbeit: false, text: text || 'Mitbetreuung' })} />
      <h3>Raumänderung</h3>
      <div className="row">
        <select id="raumwahl" defaultValue="">
          <option value="">– freien Raum wählen –</option>
          {raeume.map((r) => <option key={r} value={r}>{idx.raeume.get(r)?.kuerzel} ({idx.raeume.get(r)?.typ})</option>)}
        </select>
        <button onClick={() => {
          const r = (document.getElementById('raumwahl') as HTMLSelectElement).value;
          if (r) setze('Raumänderung', { raumId: r, vertreterIds: v?.vertreterIds.length ? v.vertreterIds : b.grund === 'raum' ? b.unterricht.lehrerIds : [] });
        }}>Raum ändern</button>
      </div>
      <h3>Verlegung</h3>
      <div className="row">
        <input type="date" value={nachDatum} onChange={(e) => setNachDatum(e.target.value)} />
        <NumberInput value={nachStunde} min={1} max={data.zeitraster.stunden.length} onChange={setNachStunde} style={{ width: 60 }} />
        <span className="small muted">. Stunde</span>
        <button onClick={() => setze('Verlegung', { nachDatum, nachStunde, vertreterIds: b.unterricht.lehrerIds })}>Verlegen</button>
      </div>
    </div>
  );
}

/** Lehrkräfte, die zur gleichen Zeit eine Parallelklasse unterrichten (für Mitbetreuung) */
function MitbetreuungParallel({ datum, b, onSetze }: { datum: string; b: BetroffeneStunde; onSetze: (lehrerId: string) => void }) {
  const { data, idx } = useStore();
  const jg = new Set(b.unterricht.klassenIds.map((k) => idx.klassen.get(k)?.jahrgang));
  const parallel = tagesplan(data, datum, idx)
    .filter((p) => p.stunde === b.stunde && p.id !== b.planeintrag.id)
    .map((p) => idx.unterricht.get(p.unterrichtId)!)
    .filter((u) => u.klassenIds.some((k) => jg.has(idx.klassen.get(k)?.jahrgang)))
    .flatMap((u) => u.lehrerIds.map((l) => ({ l, u })))
    .filter(({ l }) => !lehrerBelegt({ ...data, stundenplan: [] }, idx, l, datum, b.stunde));
  if (!parallel.length) return null;
  return (
    <>
      <h3>Mitbetreuung durch Parallelklasse</h3>
      <div className="row">
        {parallel.map(({ l, u }) => (
          <button key={l + u.id} className="sm" onClick={() => onSetze(l)}>{idx.lehrer.get(l)?.kuerzel} ({kurzKlassen(idx, u.klassenIds)} {idx.faecher.get(u.fachId)?.kuerzel})</button>
        ))}
      </div>
    </>
  );
}

function AufsichtsVertretung({ datum }: { datum: string }) {
  const { data, idx, update } = useStore();
  const offen = offeneAufsichten(data, datum);
  const plan = tagesplan(data, datum, idx);
  return (
    <div className="card">
      <h2>Pausenaufsichten ohne Lehrkraft</h2>
      {offen.map((a) => {
        const pause = data.pausen.find((p) => p.id === a.pauseId)!;
        const bereich = data.aufsichtsbereiche.find((b) => b.id === a.bereichId);
        const schon = data.vertretungen.find((v) => v.datum === datum && v.art === 'Sondereinsatz' && v.text.includes(a.id));
        const cand = data.lehrer.filter((l) => l.aktiv && l.id !== a.lehrerId && plan.some((p) => (p.stunde === pause.nachStunde || p.stunde === pause.nachStunde + 1) && idx.unterricht.get(p.unterrichtId)!.lehrerIds.includes(l.id)) && !lehrerBelegt(data, idx, l.id, datum, pause.nachStunde, []));
        return (
          <div key={a.id} className="row" style={{ marginBottom: 6 }}>
            <span style={{ minWidth: 240 }}>{pause.name} · {bereich?.name} <span className="muted">(für {idx.lehrer.get(a.lehrerId)?.kuerzel})</span></span>
            {schon ? <span className="pill ok">{kurzLehrer(idx, schon.vertreterIds)}</span> : (
              <MultiSelect single options={cand.map((l) => ({ value: l.id, label: l.kuerzel }))} value={[]} placeholder="Lehrkraft wählen" onChange={(v) => update('Aufsichtsvertretung', (d) => {
                d.vertretungen.push(neueVertretung({ datum, stunde: pause.nachStunde, art: 'Sondereinsatz', vertreterIds: v, text: `Pausenaufsicht ${bereich?.name ?? ''} nach ${pause.nachStunde}. Std. [${a.id}]` }));
              })} />
            )}
          </div>
        );
      })}
    </div>
  );
}

function Sondereinsaetze({ datum, liste }: { datum: string; liste: Vertretung[] }) {
  const { data, idx, update } = useStore();
  const [stunde, setStunde] = useState(1);
  const [lehrer, setLehrer] = useState<string[]>([]);
  const [text, setText] = useState('');
  return (
    <div className="card">
      <h2>Sondereinsätze / Bereitschaft</h2>
      {liste.map((v) => (
        <div key={v.id} className="row small" style={{ marginBottom: 4 }}>
          <span className="pill">{v.stunde}. Std.</span> {kurzLehrer(idx, v.vertreterIds)} – {v.text.replace(/\s*\[.*\]$/, '')}
          <button className="sm danger" onClick={() => update('Sondereinsatz gelöscht', (d) => { d.vertretungen = d.vertretungen.filter((x) => x.id !== v.id); })}>✕</button>
        </div>
      ))}
      <div className="row">
        <NumberInput value={stunde} min={1} max={data.zeitraster.stunden.length} onChange={setStunde} style={{ width: 50 }} />
        <div style={{ width: 120 }}><MultiSelect options={data.lehrer.map((l) => ({ value: l.id, label: l.kuerzel }))} value={lehrer} onChange={setLehrer} placeholder="Lehrkraft" /></div>
        <input placeholder="z. B. Prüfungsaufsicht" value={text} onChange={(e) => setText(e.target.value)} />
        <button disabled={!lehrer.length} onClick={() => { update('Sondereinsatz', (d) => { d.vertretungen.push(neueVertretung({ datum, stunde, art: 'Sondereinsatz', vertreterIds: lehrer, text, mehrarbeit: true })); }); setText(''); setLehrer([]); }}>Hinzufügen</button>
      </div>
    </div>
  );
}

function Nachrichten({ datum }: { datum: string }) {
  const { data, update } = useStore();
  const [text, setText] = useState('');
  const [intern, setIntern] = useState(false);
  const liste = data.tagesnachrichten.filter((n) => n.datum === datum);
  return (
    <div className="card">
      <h2>Nachrichten zum Tag</h2>
      {liste.map((n) => (
        <div key={n.id} className="row small" style={{ marginBottom: 4 }}>
          {n.intern && <span className="pill info">intern</span>} {n.text}
          <button className="sm danger" onClick={() => update('Nachricht gelöscht', (d) => { d.tagesnachrichten = d.tagesnachrichten.filter((x) => x.id !== n.id); })}>✕</button>
        </div>
      ))}
      <div className="row">
        <input style={{ flex: 1 }} placeholder="Nachricht für Schüler/Lehrkräfte" value={text} onChange={(e) => setText(e.target.value)} />
        <label className="check small"><input type="checkbox" checked={intern} onChange={(e) => setIntern(e.target.checked)} /> nur Lehrkräfte</label>
        <button disabled={!text} onClick={() => { update('Nachricht', (d) => { d.tagesnachrichten.push({ id: newId('tn'), datum, text, intern }); }); setText(''); }}>Hinzufügen</button>
      </div>
    </div>
  );
}
