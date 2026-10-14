/* =====================================================================
   data.js — Stammdaten + Demodaten
   ---------------------------------------------------------------------
   Das Datenformat entspricht 1:1 dem, was timetable.php an
   openLessonModal(...) übergibt. Damit lässt sich der PHP-Adapter
   (adapter-php.js) ohne Umbau einhängen.

   Kartenfelder:
     lesson, span        Startstunde + Anzahl Stunden
     class, teacher, room, subject
     classOrig, teacherOrig, roomOrig, subjectOrig
     blockType           normal | change | cancelled | klausur | event | class-absent
     changed             ['room'] | ['teacher'] | ['class'] | ['subject']
     highlight           true = kräftiger grüner Rahmen (neue Änderung)
     info                Text für das ⓘ-Zeichen und die Detailansicht
     parallel            {index, count} für nebeneinanderliegende Karten
   ===================================================================== */

const LESSON_TIMES = {
  1:  { start:'07:45', end:'08:30' },
  2:  { start:'08:30', end:'09:15' },
  3:  { start:'09:35', end:'10:20' },
  4:  { start:'10:20', end:'11:05' },
  5:  { start:'11:25', end:'12:10' },
  6:  { start:'12:10', end:'12:55' },
  7:  { start:'13:15', end:'14:00' },
  8:  { start:'14:00', end:'14:45' },
  9:  { start:'15:05', end:'15:50' },
  10: { start:'15:50', end:'16:35' },
  11: { start:'16:55', end:'17:40' },
  12: { start:'17:40', end:'18:25' }
};

const SUBJECT_NAMES = {
  SUD:'Systementwicklung und Datenbanken',
  DEU:'Deutsch/Kommunikation',
  GID:'Geschäftsprozesse und IT-Dienstleistungen',
  FEN:'Fachenglisch',
  EVP:'Entwicklung vernetzter Prozesse',
  WISO:'Wirtschafts- und Sozialkunde',
  IHK:'IHK-Abschlussprüfung'
};

const TEACHER_NAMES = {
  WE:'Frau Wegener',
  FE:'Herr Feldmann',
  LN:'Frau Lindner',
  LQ:'Herr Lorenz-Quandt',
  TH:'Frau Thiel',
  SF:'Florian Scheib'
};

const ROOM_NAMES = {
  C013:'C013 · Computerraum',
  C021:'C021 · Klassenraum',
  C103:'C103 · Klassenraum',
  C005:'C005 · Klassenraum',
  C007:'C007 · Fachraum Netzwerktechnik',
  C122:'C122 · Klassenraum',
  A103:'A103 · Medienraum',
  B124:'B124 · Klassenraum',
  Online:'Onlineunterricht'
};

const ENTITIES = [
  { type:'mine',    id:'me',     label:'ScheibFlo', info:'Florian Scheib · Lehrkraft' },
  { type:'class',   id:'FI428',  label:'FI428',  info:'Klassenlehrkraft: WE' },
  { type:'class',   id:'FI401',  label:'FI401',  info:'Klassenlehrkraft: LN' },
  { type:'class',   id:'FI429',  label:'FI429',  info:'Klassenlehrkraft: TH' },
  { type:'teacher', id:'WE',     label:'WE',     info:'Frau Wegener' },
  { type:'teacher', id:'FE',     label:'FE',     info:'Herr Feldmann' },
  { type:'teacher', id:'LN',     label:'LN',     info:'Frau Lindner' },
  { type:'teacher', id:'TH',     label:'TH',     info:'Frau Thiel' },
  { type:'room',    id:'C007',   label:'C007',   info:'Gebäude C' },
  { type:'room',    id:'C021',   label:'C021',   info:'Gebäude C' },
  { type:'room',    id:'A103',   label:'A103',   info:'Gebäude A' }
];

/* ---------------------------------------------------------------------
   Wochenraster je Entität (Wochentag 1=Mo … 5=Fr)
   --------------------------------------------------------------------- */

/* Lehrerplan ScheibFlo: Zeile 1 = Klasse, Zeile 2 = Fach, Zeile 3 = Raum */
const BASE_ME = {
  3: [
    { lesson:1, span:2, class:'FE', subject:'SuD', room:'C013' },
    { lesson:3, span:2, class:'LQ', subject:'DEU', room:'C021' },
    { lesson:5, span:2, class:'TH', subject:'GID', room:'C103' }
  ],
  4: [
    { lesson:3, span:2, class:'WE', subject:'FEN', room:'A103' },
    { lesson:5, span:4, class:'LN', subject:'EvP', room:'C007' }
  ]
};

/* Klassenplan FI428: Zeile 1 = Lehrkraft, Zeile 2 = Fach, Zeile 3 = Raum */
const BASE_FI428 = {
  4: [
    { lesson:3, span:2, teacher:'WE', subject:'FEN', room:'B124' },
    { lesson:5, span:1, teacher:'FE', subject:'EvP', room:'C007' },
    { lesson:6, span:1, teacher:'FE', subject:'EvP', room:'C007' },
    { lesson:7, span:2, teacher:'LN', subject:'EvP', room:'C007' }
  ],
  2: [
    { lesson:1, span:2, teacher:'LQ', subject:'DEU', room:'C021' },
    { lesson:3, span:2, teacher:'TH', subject:'GID', room:'C103' }
  ]
};

const BASE_GENERIC = {
  2: [{ lesson:3, span:2, class:'FI429', subject:'WiSo', room:'C021' }],
  4: [{ lesson:5, span:2, class:'FI428', subject:'EvP', room:'C007' }]
};

/* ---------------------------------------------------------------------
   Sonderwochen — relativ zur aktuellen Woche (Offset in Wochen).
   Ein Eintrag ersetzt den betreffenden Tag vollständig.
   --------------------------------------------------------------------- */
const OVERLAYS = {
  me: {
    /* Vergangene Woche: Vertretungen, Entfall, Raumwechsel */
    '-1': {
      3: [
        { lesson:1, span:2, class:'FE', subject:'SuD', room:'C013' },
        { lesson:3, span:1, class:'FE', classOrig:'LQ', subject:'DEU', room:'C021',
          blockType:'change', changed:['class'],
          info:'Vertretung für LQ (Fortbildung). Material liegt im Kursraum.' },
        { lesson:4, span:1, class:'FE', classOrig:'LQ', subject:'DEU', room:'C021',
          blockType:'change', changed:['class'] },
        { lesson:5, span:2, class:'TH', subject:'GID', room:'C103',
          blockType:'cancelled', info:'Entfall — Klasse auf Betriebsbesichtigung.' }
      ],
      4: [
        { lesson:3, span:2, class:'WE', subject:'FEN', room:'C122', roomOrig:'A103',
          blockType:'change', changed:['room'] },
        { lesson:5, span:4, class:'LN', subject:'EvP', room:'C007' }
      ]
    },
    /* Aktuelle Woche: ein Raumwechsel am Donnerstag */
    '0': {
      3: [
        { lesson:1, span:2, class:'FE', subject:'SuD', room:'C013' },
        { lesson:3, span:2, class:'LQ', subject:'DEU', room:'C021' },
        { lesson:5, span:2, class:'TH', subject:'GID', room:'C103' }
      ],
      4: [
        { lesson:3, span:2, class:'WE', subject:'FEN', room:'C005', roomOrig:'A103',
          blockType:'change', changed:['room'] },
        { lesson:5, span:4, class:'LN', subject:'EvP', room:'C007' }
      ]
    },
    /* Nächste Woche: Onlineunterricht + Klausur */
    '1': {
      3: [
        { lesson:1, span:1, class:'FE', classOrig:'FE', subject:'SuD', room:'Online', roomOrig:'C013',
          blockType:'change', changed:['class','room'], highlight:true,
          info:'Findet online statt. Zugangslink im Kursraum.' },
        { lesson:2, span:1, class:'FE', classOrig:'FE', subject:'SuD', room:'Online', roomOrig:'C013',
          blockType:'change', changed:['class','room'], highlight:true,
          info:'Findet online statt. Zugangslink im Kursraum.' },
        { lesson:3, span:2, class:'LQ', subject:'DEU', room:'C021' },
        { lesson:5, span:2, class:'TH', subject:'GID', room:'A103', roomOrig:'C103',
          blockType:'klausur', changed:['room'],
          info:'Klausur GID — Dauer 90 Minuten, Hilfsmittel: Taschenrechner.' }
      ],
      4: [
        { lesson:3, span:2, class:'WE', subject:'FEN', room:'A103' },
        { lesson:5, span:4, class:'LN', subject:'EvP', room:'C007' }
      ]
    }
  },

  FI428: {
    /* Aktuelle Woche: IHK-Termin am Mittwoch, Vertretungen am Donnerstag */
    '0': {
      2: [],
      3: [
        { lesson:1, span:6, subject:'IHK-Abschlussprüfung Teil 1',
          teacher:'FI401, FI428, FI429', room:'',
          blockType:'event', fullday:true,
          info:'IHK-Abschlussprüfung Teil 1 — Einlass ab 07:30 Uhr, Raumaufteilung am Aushang.' }
      ],
      4: [
        { lesson:3, span:2, teacher:'WE', subject:'FEN', room:'B124', roomOrig:'A103',
          blockType:'change', changed:['room'] },
        { lesson:5, span:1, teacher:'FE', teacherOrig:'LN', subject:'EvP', room:'C007',
          blockType:'change', changed:['teacher'],
          info:'Vertretung für LN. Bitte Laptops mitbringen.' },
        { lesson:6, span:1, teacher:'FE', teacherOrig:'LN', subject:'EvP', room:'C007',
          blockType:'change', changed:['teacher'] },
        { lesson:7, span:2, teacher:'LN', subject:'EvP', room:'C007',
          blockType:'cancelled', info:'Entfall wegen Prüfungsaufsicht.' }
      ]
    }
  }
};

/* Abwesenheits-Bänder: entityId -> weekOffset -> [{weekday, from, to, count}] */
const ABSENCE_BANDS = {
  FI428: {
    '0': [{ weekday:3, from:1, to:6, count:3,
            info:'Klasse abwesend — IHK-Prüfung (3 Kurse betroffen).' }]
  }
};

/* Mitteilungen */
const MESSAGES = [
  { id:1, title:'Raumänderung Donnerstag', from:'Sekretariat', date:-1, unread:true,
    text:'Der Englischunterricht am Donnerstag findet in C005 statt. Der Raum A103 wird für die Prüfungsvorbereitung benötigt.' },
  { id:2, title:'Klausurplan Kalenderwoche 36', from:'Abteilungsleitung', date:-2, unread:true,
    text:'Der Klausurplan für die kommende Woche ist veröffentlicht. Bitte prüfen Sie Ihre Aufsichten im Stundenplan.' },
  { id:3, title:'Wartungsfenster am Wochenende', from:'IT-Administration', date:-5, unread:false,
    text:'Am Samstag zwischen 08:00 und 12:00 Uhr steht das Portal nicht zur Verfügung. Der Stundenplan bleibt in der App offline verfügbar.' }
];

window.TT_SEED = {
  LESSON_TIMES, SUBJECT_NAMES, TEACHER_NAMES, ROOM_NAMES,
  ENTITIES, BASE_ME, BASE_FI428, BASE_GENERIC, OVERLAYS, ABSENCE_BANDS, MESSAGES
};
