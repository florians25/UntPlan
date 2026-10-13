// Web-Worker: führt die Stundenplan-Optimierung aus, ohne die Oberfläche zu blockieren.
import { erstelleStundenplan, type PlanerOptionen } from '../core/planer';
import type { SchoolData } from '../core/model';

self.onmessage = (e: MessageEvent<{ data: SchoolData; opt: Partial<PlanerOptionen> }>) => {
  const { data, opt } = e.data;
  try {
    const res = erstelleStundenplan(data, {
      ...opt,
      onProgress: (anteil, kosten) => (self as unknown as Worker).postMessage({ typ: 'fortschritt', anteil, kosten }),
    });
    (self as unknown as Worker).postMessage({ typ: 'fertig', res });
  } catch (err) {
    (self as unknown as Worker).postMessage({ typ: 'fehler', meldung: (err as Error).message });
  }
};
