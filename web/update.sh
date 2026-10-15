#!/bin/sh
# UntPlanWeb (und UntPlanMobile) aktualisieren: Sicherung → neue Images laden → neu starten.
# Die Datenbank-Migrationen laufen beim Start von UntPlanWeb automatisch.
#
#   ./update.sh                        sofort aktualisieren (im Ordner mit der docker-compose-Datei aufrufen)
#   ./update.sh --wenn-angefordert     nur, wenn in der Administration „Update installieren“ geklickt wurde
#                                      (UNTPLANWEB_UPDATE_MODUS=datei), z. B. per cron jede Minute:
#                                      * * * * * cd /opt/untplan && ./update.sh --wenn-angefordert >> update.log 2>&1
#
# Umgebungsvariablen: COMPOSE_FILE (z. B. docker-compose.mariadb.yml), SICHERUNGEN (Ordner für MariaDB-Sicherungen,
# Standard ./sicherungen), UNTPLANWEB_DIENST (Dienstname, Standard untplanweb).
set -eu

DIENST=${UNTPLANWEB_DIENST:-untplanweb}
SICHERUNGEN=${SICHERUNGEN:-./sicherungen}

if [ "${1:-}" = "--wenn-angefordert" ]; then
  docker compose exec -T "$DIENST" test -f /data/update/angefordert.json 2>/dev/null || exit 0
  echo "$(date '+%F %T') Update angefordert"
fi

echo "Sicherung …"
if docker compose ps --services --status running | grep -qx mariadb; then
  mkdir -p "$SICHERUNGEN"
  datei="$SICHERUNGEN/untplanweb-$(date +%Y%m%d-%H%M%S).sql"
  docker compose exec -T mariadb sh -c 'exec mariadb-dump -uroot -p"$MARIADB_ROOT_PASSWORD" --single-transaction --default-character-set=utf8mb4 "$MARIADB_DATABASE"' > "$datei"
  echo "MariaDB gesichert: $datei"
else
  # SQLite: konsistente Kopie im Volume (/data/sicherungen)
  docker compose exec -T "$DIENST" dotnet UntPlanWeb.dll --sichern
fi

docker compose exec -T "$DIENST" rm -f /data/update/angefordert.json 2>/dev/null || true
echo "Neue Images laden …"
docker compose pull
docker compose up -d
docker image prune -f >/dev/null 2>&1 || true
echo "Fertig. Version: Administration → Übersicht → Version & Updates."
