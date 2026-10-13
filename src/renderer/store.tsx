// Zentraler Zustand: Schuldatei, Rückgängig/Wiederholen, Speichern, Meldungen.

import { produce, type Draft } from 'immer';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { emptySchool, migrate } from '../core/factory';
import { buildIndex, type DataIndex } from '../core/index';
import type { SchoolData } from '../core/model';
import { api } from './api';

const MAX_HISTORY = 100;

export interface Toast {
  id: number;
  text: string;
  typ: 'info' | 'ok' | 'fehler';
}

interface Store {
  data: SchoolData;
  idx: DataIndex;
  filePath: string | null;
  dirty: boolean;
  update: (label: string, recipe: (d: Draft<SchoolData>) => void) => void;
  replace: (d: SchoolData, path?: string | null) => void;
  undo: () => void;
  redo: () => void;
  canUndo: boolean;
  canRedo: boolean;
  undoLabel: string | null;
  save: (saveAs?: boolean) => Promise<boolean>;
  open: () => Promise<void>;
  openPath: (p: string) => Promise<void>;
  newSchool: (d?: SchoolData) => Promise<void>;
  toast: (text: string, typ?: Toast['typ']) => void;
  toasts: Toast[];
  view: string;
  setView: (v: string, param?: string) => void;
  viewParam: string | undefined;
}

const Ctx = createContext<Store | null>(null);

export function StoreProvider({ children }: { children: ReactNode }) {
  const [data, setData] = useState<SchoolData>(() => emptySchool());
  const [filePath, setFilePath] = useState<string | null>(null);
  const [dirty, setDirtyState] = useState(false);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [view, setViewState] = useState('dashboard');
  const [viewParam, setViewParam] = useState<string | undefined>();
  const past = useRef<{ d: SchoolData; label: string }[]>([]);
  const future = useRef<{ d: SchoolData; label: string }[]>([]);
  const [, force] = useState(0);
  const dataRef = useRef(data);
  dataRef.current = data;

  const setDirty = useCallback((v: boolean) => {
    setDirtyState(v);
    api.setDirty(v);
  }, []);

  const toast = useCallback((text: string, typ: Toast['typ'] = 'info') => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, text, typ }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), typ === 'fehler' ? 7000 : 3500);
  }, []);

  const update = useCallback(
    (label: string, recipe: (d: Draft<SchoolData>) => void) => {
      const prev = dataRef.current;
      const next = produce(prev, recipe);
      if (next === prev) return;
      past.current.push({ d: prev, label });
      if (past.current.length > MAX_HISTORY) past.current.shift();
      future.current = [];
      dataRef.current = next;
      setData(next);
      setDirty(true);
    },
    [setDirty],
  );

  const replace = useCallback(
    (d: SchoolData, path: string | null = null) => {
      past.current = [];
      future.current = [];
      dataRef.current = d;
      setData(d);
      setFilePath(path);
      setDirty(false);
    },
    [setDirty],
  );

  const undo = useCallback(() => {
    const p = past.current.pop();
    if (!p) return;
    future.current.push({ d: dataRef.current, label: p.label });
    dataRef.current = p.d;
    setData(p.d);
    setDirty(true);
    force((x) => x + 1);
  }, [setDirty]);

  const redo = useCallback(() => {
    const f = future.current.pop();
    if (!f) return;
    past.current.push({ d: dataRef.current, label: f.label });
    dataRef.current = f.d;
    setData(f.d);
    setDirty(true);
    force((x) => x + 1);
  }, [setDirty]);

  const save = useCallback(
    async (saveAs = false) => {
      try {
        const p = await api.save(JSON.stringify(dataRef.current, null, 1), saveAs);
        if (!p) return false;
        setFilePath(p);
        setDirty(false);
        toast('Gespeichert', 'ok');
        return true;
      } catch (e) {
        toast(`Speichern fehlgeschlagen: ${(e as Error).message}`, 'fehler');
        return false;
      }
    },
    [setDirty, toast],
  );

  const load = useCallback(
    (r: { path: string; content: string } | null) => {
      if (!r) return;
      try {
        const d = migrate(JSON.parse(r.content));
        replace(d, r.path);
        toast(`Geöffnet: ${d.meta.schulname}`, 'ok');
      } catch (e) {
        toast(`Datei konnte nicht gelesen werden: ${(e as Error).message}`, 'fehler');
      }
    },
    [replace, toast],
  );

  const open = useCallback(async () => load(await api.openDialog()), [load]);
  const openPath = useCallback(async (p: string) => load(await api.openPath(p)), [load]);
  const newSchool = useCallback(
    async (d?: SchoolData) => {
      await api.newFile();
      replace(d ?? emptySchool());
      setDirty(true);
    },
    [replace, setDirty],
  );

  const setView = useCallback((v: string, param?: string) => {
    setViewState(v);
    setViewParam(param);
  }, []);

  // Letzte Datei beim Start öffnen
  useEffect(() => {
    api.openLast().then((r) => r && load(r));
  }, [load]);

  const idx = useMemo(() => buildIndex(data), [data]);

  const value: Store = {
    data,
    idx,
    filePath,
    dirty,
    update,
    replace,
    undo,
    redo,
    canUndo: past.current.length > 0,
    canRedo: future.current.length > 0,
    undoLabel: past.current[past.current.length - 1]?.label ?? null,
    save,
    open,
    openPath,
    newSchool,
    toast,
    toasts,
    view,
    setView,
    viewParam,
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useStore(): Store {
  const s = useContext(Ctx);
  if (!s) throw new Error('StoreProvider fehlt');
  return s;
}
