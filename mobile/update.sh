#!/bin/sh
# UntPlanMobile (zusammen mit UntPlanWeb) aktualisieren – im Ordner mit der docker-compose-Datei aufrufen:
#   ./update.sh                    Sicherung von UntPlanWeb (SQLite bzw. MariaDB/MySQL), neue Images laden, neu starten
#   ./update.sh --ohne-sicherung   ohne Sicherung
# Läuft UntPlanMobile allein (ohne UntPlanWeb in derselben Compose-Datei), wird nur das Mobile-Image aktualisiert.
set -eu
HIER=$(cd "$(dirname "$0")" && pwd)
if docker compose config --services 2>/dev/null | grep -qx "${UNTPLANWEB_DIENST:-untplanweb}"; then
  # gemeinsames Update-Skript von UntPlanWeb (liegt im Repository unter web/update.sh, sonst aus dem Netz)
  if [ -f "$HIER/../web/update.sh" ]; then exec sh "$HIER/../web/update.sh" "$@"; fi
  curl -fsSL https://raw.githubusercontent.com/florians25/UntPlan/main/web/update.sh | sh -s -- "$@"
  exit $?
fi
DIENST=${UNTPLANMOBILE_DIENST:-untplanmobile}
echo "Neues Image für $DIENST laden …"
docker compose pull "$DIENST"
docker compose up -d "$DIENST"
docker image prune -f >/dev/null 2>&1 || true
echo "Fertig."
