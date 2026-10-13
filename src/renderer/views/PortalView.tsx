import { useEffect, useRef, useState } from 'react';
import { todayISO } from '../../core/dates';
import type { PortalMeldung } from '../../core/sync';
import { buildSyncPayload, uebernehmeMeldungen } from '../../core/syncPayload';
import { api } from '../api';
import { Field, NumberInput, TextInput } from '../components/ui';
import { useStore } from '../store';

export function PortalView() {
  const { data, update, toast } = useStore();
  const s = data.sync;
  const [status, setStatus] = useState<string>('');
  const [busy, setBusy] = useState(false);
  const [meldungen, setMeldungen] = useState<PortalMeldung[]>([]);
  const [log, setLog] = useState<string[]>([]);
  const req = { portalUrl: s.portalUrl, schulId: s.schulId, apiKey: s.apiKey };
  const set = (k: keyof typeof s, v: any) => update('Portal-Einstellung', (d) => { (d.sync as any)[k] = v; });
  const addLog = (t: string) => setLog((l) => [`${new Date().toLocaleTimeString('de-DE')} ${t}`, ...l].slice(0, 50));

  const testen = async () => {
    setBusy(true);
    try {
      const r = await api.portalStatus(req);
      setStatus(`✔ Verbunden: ${r.name} (Version ${r.version})`);
      addLog('Verbindungstest erfolgreich');
    } catch (e) {
      setStatus(`✖ ${(e as Error).message}`);
    } finally {
      setBusy(false);
    }
  };

  const synchronisieren = async () => {
    setBusy(true);
    try {
      const payload = buildSyncPayload(data, todayISO());
      const r = await api.portalPush(req, payload);
      update('Synchronisation', (d) => { d.sync.letzteSynchronisation = new Date().toISOString(); d.sync.letzteRevision = r.revision; });
      addLog(`Hochgeladen: ${payload.stundenplan.length} Planeinträge, ${payload.vertretungen.length} Vertretungen → Revision ${r.revision}`);
      const m = await api.portalPull(req, 0);
      setMeldungen(m.meldungen);
      addLog(`${m.meldungen.length} neue Meldung(en) aus dem Portal`);
      toast('Synchronisation erfolgreich', 'ok');
    } catch (e) {
      addLog(`Fehler: ${(e as Error).message}`);
      toast(`Synchronisation fehlgeschlagen: ${(e as Error).message}`, 'fehler');
    } finally {
      setBusy(false);
    }
  };

  // Automatische Synchronisation alle 5 Minuten
  const syncRef = useRef(synchronisieren);
  syncRef.current = synchronisieren;
  useEffect(() => {
    if (!s.autoSync || !s.portalUrl) return;
    const t = setInterval(() => syncRef.current(), 5 * 60_000);
    return () => clearInterval(t);
  }, [s.autoSync, s.portalUrl]);

  const uebernehmen = async (ids: string[]) => {
    const auswahl = meldungen.filter((m) => ids.includes(m.id));
    let res = { uebernommen: [] as string[], abgelehnt: [] as string[] };
    update('Portal-Meldungen übernommen', (d) => { res = uebernehmeMeldungen(d as any, auswahl); });
    try {
      await api.portalAck(req, [...res.uebernommen, ...res.abgelehnt]);
    } catch (e) {
      toast(`Quittierung fehlgeschlagen: ${(e as Error).message}`, 'fehler');
    }
    setMeldungen((m) => m.filter((x) => !ids.includes(x.id)));
    addLog(`${res.uebernommen.length} Meldung(en) übernommen, ${res.abgelehnt.length} abgelehnt`);
  };

  return (
    <div className="grid2" style={{ alignItems: 'start' }}>
      <div className="card">
        <h2>Verbindung zum UntPlan Web-Portal</h2>
        <p className="muted small">Das Web-Portal veröffentlicht Stundenpläne und Vertretungen für Schüler, Eltern und Lehrkräfte und nimmt Krankmeldungen entgegen. Die Desktop-Anwendung bleibt die führende Datenquelle. Für Tests kann der mitgelieferte Referenzserver gestartet werden: <kbd>npm run portal</kbd> (http://localhost:8787).</p>
        <div className="col" style={{ gap: 10 }}>
          <Field label="Portal-Adresse"><TextInput value={s.portalUrl} placeholder="https://portal.meine-schule.de" onChange={(v) => set('portalUrl', v.trim())} /></Field>
          <Field label="Schul-ID im Portal"><TextInput value={s.schulId} placeholder={data.meta.schulnummer || 'z. B. Schulnummer'} onChange={(v) => set('schulId', v.trim())} /></Field>
          <Field label="API-Schlüssel"><input type="password" value={s.apiKey} onChange={(e) => set('apiKey', e.target.value)} /></Field>
          <Field label="Vertretungen veröffentlichen für (Tage)"><NumberInput value={s.tageVoraus} min={1} max={30} onChange={(n) => set('tageVoraus', n)} /></Field>
          <label className="check"><input type="checkbox" checked={s.autoSync} onChange={(e) => set('autoSync', e.target.checked)} /> automatisch alle 5 Minuten synchronisieren (solange UntPlan geöffnet ist)</label>
        </div>
        <div className="row" style={{ marginTop: 12 }}>
          <button disabled={busy || !s.portalUrl} onClick={testen}>Verbindung testen</button>
          <button className="primary" disabled={busy || !s.portalUrl || !s.schulId} onClick={synchronisieren}>{busy ? 'Bitte warten…' : '🔄 Jetzt synchronisieren'}</button>
        </div>
        {status && <p className="small">{status}</p>}
        <p className="muted small">Letzte Synchronisation: {s.letzteSynchronisation ? new Date(s.letzteSynchronisation).toLocaleString('de-DE') : 'noch nie'} · Revision {s.letzteRevision}</p>
        <p className="muted small">Datenschutz: Übertragen werden nur Kürzel, Namen und dienstliche E-Mail-Adressen der Lehrkräfte, Klassen, Räume, Pläne und Vertretungen. Abwesenheitsgründe (z. B. Krankheit) werden nicht übertragen. Die Übertragung erfolgt verschlüsselt (HTTPS) und erfordert einen Auftragsverarbeitungsvertrag mit dem Portalbetreiber.</p>
      </div>
      <div>
        <div className="card">
          <h2>Meldungen aus dem Portal</h2>
          {meldungen.length === 0 && <p className="muted">Keine offenen Meldungen. (Werden beim Synchronisieren abgerufen.)</p>}
          {meldungen.map((m) => (
            <div key={m.id} className="befund">
              <span className="pill info">{m.typ === 'abwesenheit' ? 'Abwesenheit' : 'Nachricht'}</span>
              <span style={{ flex: 1 }} className="small">
                <b>{m.lehrerKuerzel ?? m.absender}</b> {m.von && `${m.von.split('-').reverse().join('.')}${m.bis && m.bis !== m.von ? ' – ' + m.bis.split('-').reverse().join('.') : ''}`} {m.grund} {m.text}
              </span>
              <button className="sm primary" onClick={() => uebernehmen([m.id])}>Übernehmen</button>
            </div>
          ))}
          {meldungen.length > 1 && <button style={{ marginTop: 8 }} onClick={() => uebernehmen(meldungen.map((m) => m.id))}>Alle übernehmen</button>}
        </div>
        <div className="card">
          <h2>Protokoll</h2>
          {log.map((l, i) => <div key={i} className="small">{l}</div>)}
          {!log.length && <p className="muted small">–</p>}
        </div>
      </div>
    </div>
  );
}
