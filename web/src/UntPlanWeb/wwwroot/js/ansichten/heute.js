// Heute: der nächste Unterrichtstag mit Stunden (links) und Seitenbereichen wie im Original (Mitteilungen, Nachrichten zum Tag,
// Prüfungen, Hausaufgaben bzw. offene Stunden, Termine, eigene Meldungen).
import { e, icon, holen, datumLang, datumKurz, heuteIso, plusTage, meldungText, statusMarker, leer, panel, stundenText, datumKachel } from '../ui.js';
import { tagesListe, kartenBinden } from '../plangitter.js';

function begruessung() {
  const h = new Date().getHours();
  return h < 11 ? 'Guten Morgen' : h < 18 ? 'Guten Tag' : 'Guten Abend';
}

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('Heute');
  const d = await holen('/api/app/heute');
  if (!d.paket) {
    seite.innerHTML = panel('Noch keine Daten vorhanden', `<div class="u-panel-rumpf">
      <p class="text-body-secondary mb-0">Sobald die Schule die Daten aus UntPlan überträgt, erscheinen hier Stundenplan und Vertretungen.</p>
      ${ich.admin ? '<a class="btn btn-primary btn-sm mt-3" href="#/admin/sync">Synchronisation einrichten</a>' : ''}</div>`, { symbol: 'info' });
    return;
  }
  // Prüfungen und letzte Mitteilungen ergänzen (nicht kritisch)
  const filter = d.typ === 'klasse' ? `&klasse=${encodeURIComponent(d.element ?? '')}` : d.element ? `&lehrer=${encodeURIComponent(d.element)}` : '';
  const [pruefungen, post] = await Promise.all([
    holen(`/api/app/pruefungen?von=${heuteIso()}&bis=${plusTage(heuteIso(), 21)}${filter}`).catch(() => []),
    holen('/api/app/mitteilungen').catch(() => []),
  ]);
  const planLink = d.element ? `#/plan/${d.typ}/${encodeURIComponent(d.element)}/${d.datum}` : '#/plan';
  const tagText = d.datum === heuteIso() ? 'Heute' : 'Nächster Schultag';
  const name = (ich.anzeigename || ich.login).split(/\s+/)[0];

  const mitteilungen = post.slice(0, 4);
  seite.innerHTML = `
    <p class="u-unterzeile">${begruessung()}, ${e(name)} · ${e(datumLang(heuteIso()))}</p>
    ${ich.kollegium && d.offeneStunden ? `<div class="alert alert-warning d-flex align-items-center gap-2">${icon('offen')}
      <span>${d.offeneStunden} ${d.offeneStunden === 1 ? 'Stunde' : 'Stunden'} der letzten zwei Wochen ohne Lehrstoff.</span>
      <a class="alert-link ms-auto fw-semibold" href="#/offen">Offene Stunden</a></div>` : ''}
    <div class="row g-3">
      <div class="col-xl-7">
        ${panel(`${tagText}: ${datumLang(d.datum)}${d.woche ? ` · ${d.woche}-Woche` : ''}`, `<div class="u-panel-rumpf" data-tag>
            ${d.hinweise.map(h => `<div class="alert alert-warning py-2 mb-2">${e(h)}</div>`).join('')}
            ${!d.element ? leer('Ihrem Konto ist keine Lehrkraft oder Klasse zugeordnet.', 'meinplan')
              : d.karten.length || d.aufsichten.length || d.ereignisse.length ? tagesListe(d.karten, d.aufsichten, d.typ, d.ereignisse) : leer('Kein Unterricht an diesem Tag.', 'meinplan')}
          </div>`, { symbol: 'meinplan', rechts: `${d.aenderungen ? `<span class="u-chip gruen">${d.aenderungen} ${d.aenderungen === 1 ? 'Änderung' : 'Änderungen'}</span>` : ''}<a class="btn btn-sm btn-outline-secondary" href="${planLink}">Wochenplan</a>` })}
      </div>
      <div class="col-xl-5 d-flex flex-column gap-3">
        ${d.nachrichten.length ? panel('Nachrichten zum Tag', `<ul class="u-liste">${d.nachrichten.map(n => `<li><span>${e(n)}</span></li>`).join('')}</ul>`, { symbol: 'nachrichten' }) : ''}
        ${panel('Mitteilungen', mitteilungen.length ? `<ul class="u-liste">${mitteilungen.map(m => `<li class="klickbar" data-mt="${m.id}">
            <span class="u-avatar" style="width:34px;height:34px;font-size:.75rem">${e(kuerzel(m.absender))}</span>
            <div class="u-zeilentext"><div class="titel ${m.gelesen ? 'fw-normal' : ''}">${m.wichtig ? '<span class="u-chip rot me-1">wichtig</span>' : ''}${e(m.betreff)}</div>
              <div class="unter">${e(m.absender)} · ${e(kurzzeit(m.erstellt))}</div></div>${m.gelesen ? '' : '<span class="u-zahl" style="min-width:10px;height:10px;padding:0"></span>'}</li>`).join('')}</ul>`
          : leer('Keine Mitteilungen.', 'mitteilungen'), { symbol: 'mitteilungen', rechts: `${d.ungelesen ? `<span class="u-chip orange">${d.ungelesen} neu</span>` : ''}<a class="u-mehr" href="#/mitteilungen">Alle</a>` })}
        ${panel('Prüfungen', pruefungen.length ? `<ul class="u-liste">${pruefungen.slice(0, 5).map(p => `<li>${datumKachel(p.datum)}
            <div class="u-zeilentext"><div class="titel">${e(p.titel)}</div><div class="unter">${stundenText(p.stundeVon, p.stundeBis)} Std.${p.beginn ? ` · ${e(p.beginn)}–${e(p.ende)}` : ''} · ${e(p.klassen.join(', '))}${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</div></div>
            ${p.status === 'vorgemerkt' ? '<span class="u-chip">vorgemerkt</span>' : '<span class="u-chip gelb">Prüfung</span>'}</li>`).join('')}</ul>`
          : leer('Keine Prüfungen in den nächsten drei Wochen.', 'pruefung'), { symbol: 'pruefung', rechts: '<a class="u-mehr" href="#/pruefungen">Alle</a>' })}
        ${!ich.kollegium ? panel('Hausaufgaben', d.hausaufgaben.length ? `<ul class="u-liste">${d.hausaufgaben.map(h => `<li>
            <div class="u-zeilentext"><div class="titel">${e(h.fach)}</div><div>${e(h.hausaufgabe)}</div></div>${h.faellig ? `<span class="u-chip ${h.faellig <= plusTage(heuteIso(), 1) ? 'orange' : ''}">bis ${datumKurz(h.faellig)}</span>` : ''}</li>`).join('')}</ul>`
          : leer('Keine offenen Hausaufgaben.', 'hausaufgaben'), { symbol: 'hausaufgaben', rechts: '<a class="u-mehr" href="#/hausaufgaben">Alle</a>' }) : ''}
        ${d.termine.length ? panel('Termine', `<ul class="u-liste">${d.termine.map(t => `<li>${datumKachel(t.von)}
            <div class="u-zeilentext"><div class="titel">${e(t.titel || t.art)}</div><div class="unter">${t.bis && t.bis !== t.von ? `bis ${datumKurz(t.bis)} · ` : ''}${t.stundeVon ? `${stundenText(t.stundeVon, t.stundeBis)} Std. · ` : ''}${e(t.art === 'klausur' ? 'Prüfung' : 'Veranstaltung')}${t.klassen?.length ? ' · ' + e(t.klassen.join(', ')) : ''}</div></div></li>`).join('')}</ul>`, { symbol: 'kalender' }) : ''}
        ${ich.kollegium ? panel('Meine Meldungen', d.meldungen.length ? `<ul class="u-liste">${d.meldungen.map(m => `<li><div class="u-zeilentext">${e(meldungText(m))}</div>${statusMarker(m.status)}</li>`).join('')}</ul>`
          : leer('Keine Meldungen.', 'meldenbuchen'), { symbol: 'meldenbuchen', rechts: '<a class="u-mehr" href="#/melden/abwesenheit">Neue Meldung</a>' }) : ''}
      </div>
    </div>`;
  kartenBinden(seite.querySelector('[data-tag]'), d.karten, kontext);
  seite.querySelectorAll('[data-mt]').forEach(li => li.onclick = () => kontext.navigieren('#/mitteilungen/eingang/' + li.dataset.mt));
}

function kuerzel(name) {
  const t = String(name || '?').replace(/\(.*\)/, '').trim().split(/\s+/);
  return (t.length > 1 ? t[0][0] + t[t.length - 1][0] : t[0].slice(0, 2)).toUpperCase();
}

function kurzzeit(s) {
  const d = new Date(String(s).replace(' ', 'T'));
  if (isNaN(d)) return s;
  return d.toDateString() === new Date().toDateString() ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) : d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
}
