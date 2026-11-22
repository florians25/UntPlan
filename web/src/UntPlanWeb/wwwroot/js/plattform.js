// Plattform-Erkennung (klassisches Skript, im <head> vor dem ersten Zeichnen): setzt <html class="is-mac"> auf macOS
// (Safari, Chrome, Edge, Firefox). Nur dafür gelten in css/karten.css die Korrekturen für die Schriftmetrik (Avenir Next):
// Durchstreichungen mittig, Text in Labels genau mittig. Windows, Linux, Android und iPhone bleiben unverändert.
(function () {
  try {
    var uad = navigator.userAgentData;
    var plattform = (uad && uad.platform) || navigator.platform || '';
    var mac = /^mac/i.test(plattform) || (!plattform && /Macintosh/.test(navigator.userAgent || ''));
    if (mac) document.documentElement.classList.add('is-mac');
    // Windows: eigene Korrekturen der Strichstärke in css/karten.css (nachgemessen am Windows-Rendering gegen die Vorlage)
    if (/^win/i.test(plattform)) document.documentElement.classList.add('is-win');
    // Firefox (nicht macOS): Label im Kartenkopf sitzt dort 1px höher, siehe css/karten.css
    if (!mac && /Firefox\//.test(navigator.userAgent || '')) document.documentElement.classList.add('is-gecko');
    // Browser-Zoom bzw. Skalierung zwischen 100 % und 200 % (nicht macOS): --dpr = Pixelverhältnis. Damit bleibt der Farbbalken der
    // Stundenkarten wie in der Vorlage 4 Bildschirmpixel breit (Bild 279 bei 100 %, Bild 284 bei 125 %: je 4 px).
    if (!mac) {
      var dpr = function () {
        var d = window.devicePixelRatio || 1, st = document.documentElement.style;
        if (d > 1 && d < 2) st.setProperty('--dpr', String(d)); else st.removeProperty('--dpr');
        if (window.matchMedia) {
          var mq = window.matchMedia('(resolution: ' + d + 'dppx)');
          if (mq.addEventListener) mq.addEventListener('change', dpr, { once: true });
        }
      };
      dpr();
    }
  } catch (e) { /* ohne Erkennung: Standarddarstellung */ }
})();
