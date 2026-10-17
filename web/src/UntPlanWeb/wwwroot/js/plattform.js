// Plattform-Erkennung (klassisches Skript, im <head> vor dem ersten Zeichnen): setzt <html class="is-mac"> auf macOS
// (Safari, Chrome, Edge, Firefox). Nur dafür gelten in css/karten.css die Korrekturen für die Schriftmetrik (Avenir Next):
// Durchstreichungen mittig, Text in Labels genau mittig. Windows, Linux, Android und iPhone bleiben unverändert.
(function () {
  try {
    var uad = navigator.userAgentData;
    var plattform = (uad && uad.platform) || navigator.platform || '';
    var mac = /^mac/i.test(plattform) || (!plattform && /Macintosh/.test(navigator.userAgent || ''));
    if (mac) document.documentElement.classList.add('is-mac');
  } catch (e) { /* ohne Erkennung: Standarddarstellung */ }
})();
