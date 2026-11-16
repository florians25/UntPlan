// Handy wie Desktop: Geräte schmaler als 1024 px zeigen die Weboberfläche in der Desktop-Ansicht (feste Seitenbreite 1024 px,
// Seitenleiste und Wochenplan wie am Rechner, Zoomen und seitliches Schieben möglich) – keine eigene Handy-Formatierung mehr.
// Synchron im <head> vor den Stilen geladen, damit die Seite gleich in der richtigen Breite aufgebaut wird.
(() => {
  const BREITE = 1024;
  const m = document.querySelector('meta[name="viewport"]');
  const schmal = Math.min(screen.width, screen.height) < BREITE && Math.max(screen.width, screen.height) < 1366;
  if (m && schmal) m.setAttribute('content', `width=${BREITE}, viewport-fit=cover`);
})();
