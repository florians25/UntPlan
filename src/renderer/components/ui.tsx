import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { TAGE_KURZ, hasSlot, type Slot } from '../../core/model';

export function Modal({ title, onClose, children, footer, wide }: { title: ReactNode; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [onClose]);
  return (
    <div className="modal-back" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={`modal ${wide ? 'wide' : ''}`} role="dialog">
        <header>
          {title}
          <span className="spacer" />
          <button className="ghost" onClick={onClose} aria-label="Schließen">✕</button>
        </header>
        <div className="body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="field">
      {label}
      {children}
    </label>
  );
}

export function NumberInput({ value, onChange, step = 1, min, max, style }: { value: number; onChange: (n: number) => void; step?: number; min?: number; max?: number; style?: React.CSSProperties }) {
  const [txt, setTxt] = useState(String(value));
  useEffect(() => setTxt(String(value).replace('.', ',')), [value]);
  return (
    <input
      style={style}
      inputMode="decimal"
      value={txt}
      onChange={(e) => setTxt(e.target.value)}
      onBlur={() => {
        let n = Number(txt.replace(',', '.'));
        if (Number.isNaN(n)) n = value;
        if (min !== undefined) n = Math.max(min, n);
        if (max !== undefined) n = Math.min(max, n);
        n = Math.round(n / step) * step;
        setTxt(String(n).replace('.', ','));
        if (n !== value) onChange(n);
      }}
      onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
    />
  );
}

/** Texteingabe, die erst beim Verlassen übernimmt (eine Undo-Stufe pro Änderung) */
export function TextInput({ value, onChange, placeholder, style, className }: { value: string; onChange: (s: string) => void; placeholder?: string; style?: React.CSSProperties; className?: string }) {
  const [txt, setTxt] = useState(value);
  useEffect(() => setTxt(value), [value]);
  return (
    <input
      className={className}
      style={style}
      value={txt}
      placeholder={placeholder}
      onChange={(e) => setTxt(e.target.value)}
      onBlur={() => txt !== value && onChange(txt)}
      onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
    />
  );
}

export interface Option {
  value: string;
  label: string;
}

export function MultiSelect({ options, value, onChange, placeholder = '–', single }: { options: Option[]; value: string[]; onChange: (v: string[]) => void; placeholder?: string; single?: boolean }) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const h = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false);
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [open]);
  const label = value.map((v) => options.find((o) => o.value === v)?.label ?? '?').join(', ');
  const shown = useMemo(() => options.filter((o) => o.label.toLowerCase().includes(filter.toLowerCase())), [options, filter]);
  return (
    <div className="multisel" ref={ref}>
      <button type="button" className="multisel-btn ghost" onClick={() => setOpen(!open)} title={label}>
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{label || <span className="muted">{placeholder}</span>}</span>
        <span className="muted">▾</span>
      </button>
      {open && (
        <div className="multisel-pop">
          {options.length > 8 && <input autoFocus placeholder="Suchen…" value={filter} onChange={(e) => setFilter(e.target.value)} style={{ width: '100%', marginBottom: 4 }} />}
          {shown.map((o) => (
            <label key={o.value}>
              <input
                type={single ? 'radio' : 'checkbox'}
                checked={value.includes(o.value)}
                onChange={(e) => {
                  if (single) {
                    onChange([o.value]);
                    setOpen(false);
                  } else onChange(e.target.checked ? [...value, o.value] : value.filter((v) => v !== o.value));
                }}
              />
              {o.label}
            </label>
          ))}
          {!shown.length && <div className="muted small" style={{ padding: 6 }}>Keine Einträge</div>}
        </div>
      )}
    </div>
  );
}

/** Raster zum Markieren von Zeitfenstern (Sperrzeiten, Bereitschaften) */
export function SlotGrid({ tage, stunden, value, onChange, green }: { tage: number; stunden: number; value: Slot[]; onChange: (v: Slot[]) => void; green?: boolean }) {
  const toggle = (tag: number, stunde: number) => {
    onChange(hasSlot(value, tag, stunde) ? value.filter((s) => !(s.tag === tag && s.stunde === stunde)) : [...value, { tag, stunde }]);
  };
  const toggleRow = (stunde: number) => {
    const all = Array.from({ length: tage }, (_, t) => hasSlot(value, t, stunde)).every(Boolean);
    const rest = value.filter((s) => s.stunde !== stunde);
    onChange(all ? rest : [...rest, ...Array.from({ length: tage }, (_, t) => ({ tag: t, stunde }))]);
  };
  const toggleCol = (tag: number) => {
    const all = Array.from({ length: stunden }, (_, s) => hasSlot(value, tag, s + 1)).every(Boolean);
    const rest = value.filter((s) => s.tag !== tag);
    onChange(all ? rest : [...rest, ...Array.from({ length: stunden }, (_, s) => ({ tag, stunde: s + 1 }))]);
  };
  return (
    <table className={`slotgrid ${green ? 'green' : ''}`}>
      <thead>
        <tr>
          <th />
          {Array.from({ length: tage }, (_, t) => (
            <th key={t} style={{ cursor: 'pointer' }} onClick={() => toggleCol(t)}>{TAGE_KURZ[t]}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {Array.from({ length: stunden }, (_, s) => (
          <tr key={s}>
            <th style={{ cursor: 'pointer' }} onClick={() => toggleRow(s + 1)}>{s + 1}.</th>
            {Array.from({ length: tage }, (_, t) => (
              <td key={t} className={hasSlot(value, t, s + 1) ? 'on' : ''} onClick={() => toggle(t, s + 1)} />
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function Confirm({ text, onYes, onNo, yes = 'Ja' }: { text: ReactNode; onYes: () => void; onNo: () => void; yes?: string }) {
  return (
    <Modal title="Bitte bestätigen" onClose={onNo} footer={<><button onClick={onNo}>Abbrechen</button><button className="primary" onClick={onYes}>{yes}</button></>}>
      {text}
    </Modal>
  );
}

export function useConfirm(): [ReactNode, (text: ReactNode, action: () => void, yes?: string) => void] {
  const [state, setState] = useState<{ text: ReactNode; action: () => void; yes?: string } | null>(null);
  const node = state ? (
    <Confirm text={state.text} yes={state.yes} onNo={() => setState(null)} onYes={() => { state.action(); setState(null); }} />
  ) : null;
  return [node, (text, action, yes) => setState({ text, action, yes })];
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>;
}

export function Search({ value, onChange, placeholder = 'Suchen…' }: { value: string; onChange: (s: string) => void; placeholder?: string }) {
  return <input type="search" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} style={{ width: 220 }} />;
}

/** Sortierhilfe für Tabellen */
export function useSort<T>(rows: T[], keys: Record<string, (r: T) => string | number>, initial: string) {
  const [key, setKey] = useState(initial);
  const [asc, setAsc] = useState(true);
  const sorted = useMemo(() => {
    const f = keys[key];
    if (!f) return rows;
    return [...rows].sort((a, b) => {
      const x = f(a);
      const y = f(b);
      const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'de', { numeric: true });
      return asc ? c : -c;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, key, asc]);
  const th = (k: string, label: string, style?: React.CSSProperties) => (
    <th className="sortable" style={style} onClick={() => (k === key ? setAsc(!asc) : (setKey(k), setAsc(true)))}>
      {label} {k === key ? (asc ? '▲' : '▼') : ''}
    </th>
  );
  return { sorted, th };
}

export function textColor(bg: string): string {
  const m = bg.replace('#', '');
  if (m.length < 6) return '#111';
  const r = parseInt(m.slice(0, 2), 16);
  const g = parseInt(m.slice(2, 4), 16);
  const b = parseInt(m.slice(4, 6), 16);
  return r * 0.299 + g * 0.587 + b * 0.114 > 150 ? '#111' : '#fff';
}
