#!/bin/sh
# UntPlanWeb (und UntPlanMobile) aktualisieren: Sicherung → neue Images laden → neu starten.
# Die Datenbank-Migrationen laufen beim Start von UntPlanWeb automatisch.
#
#   ./update.sh                        sofort aktualisieren (im Ordner mit der docker-compose-Datei aufrufen)
#   ./update.sh --wenn-angefordert     nur, wenn in der Administration „Update installieren“ geklickt wurde
#                                      (UNTPLANWEB_UPDATE_MODUS=datei), z. B. per cron jede Minute:
#                                      * * * * * cd /opt/untplan && ./update.sh --wenn-angefordert >> update.log 2>&1
#   ./update.sh --ohne-sicherung       ohne vorherige Sicherung (nicht empfohlen)
#
# Datenbank:
#   SQLite (Standard)        konsistente Kopie im Volume (/data/sicherungen)
#   MariaDB/MySQL als Dienst  Dienst „mariadb“, „mysql“ oder „db“ in der Compose-Datei (oder DB_DIENST=…); Sicherung mit
#                            mariadb-dump bzw. mysqldump als komprimierte SQL-Datei in ./sicherungen
#   externer MySQL-Server    Sicherung mit mysqldump auf dem Host, wenn DB_HOST, DB_NAME, DB_BENUTZER und DB_PASSWORT gesetzt sind
#
# Umgebungsvariablen: COMPOSE_FILE (z. B. docker-compose.mariadb.yml – wird automatisch gewählt, wenn nur diese Datei läuft),
# SICHERUNGEN (Ordner, Standard ./sicherungen), UNTPLANWEB_DIENST (Standard untplanweb), DB_DIENST, SICHERUNGEN_BEHALTEN (Standard 14).
set -eu

DIENST=${UNTPLANWEB_DIENST:-untplanweb}
SICHERUNGEN=${SICHERUNGEN:-./sicherungen}
BEHALTEN=${SICHERUNGEN_BEHALTEN:-14}
SICHERN=1
ANGEFORDERT=0
for a in "$@"; do
  case "$a" in
    --wenn-angefordert) ANGEFORDERT=1 ;;
    --ohne-sicherung) SICHERN=0 ;;
    *) echo "Unbekannte Option: $a" >&2; exit 2 ;;
  esac
done

# Compose-Datei mit MariaDB automatisch verwenden, wenn keine docker-compose.yml daneben liegt
if [ -z "${COMPOSE_FILE:-}" ] && [ ! -f docker-compose.yml ] && [ -f docker-compose.mariadb.yml ]; then
  export COMPOSE_FILE=docker-compose.mariadb.yml
fi

laeuft() { docker compose ps --services --status running 2>/dev/null | grep -qx "$1"; }

if [ "$ANGEFORDERT" = 1 ]; then
  docker compose exec -T "$DIENST" test -f /data/update/angefordert.json 2>/dev/null || exit 0
  echo "$(date '+%F %T') Update angefordert"
fi

if [ "$SICHERN" = 1 ]; then
  echo "Sicherung …"
  mkdir -p "$SICHERUNGEN"
  stempel=$(date +%Y%m%d-%H%M%S)
  db_dienst=${DB_DIENST:-}
  if [ -z "$db_dienst" ]; then
    for s in mariadb mysql db; do if laeuft "$s"; then db_dienst=$s; break; fi; done
  fi
  if [ -n "$db_dienst" ]; then
    # MariaDB oder MySQL im Container: mariadb-dump (MariaDB) bzw. mysqldump (MySQL), Zugangsdaten aus den Variablen des Containers
    datei="$SICHERUNGEN/untplanweb-$stempel.sql.gz"
    docker compose exec -T "$db_dienst" sh -c '
      DUMP=$(command -v mariadb-dump || command -v mysqldump)
      PW=${MARIADB_ROOT_PASSWORD:-${MYSQL_ROOT_PASSWORD:-}}
      DB=${MARIADB_DATABASE:-${MYSQL_DATABASE:-untplanweb}}
      exec "$DUMP" -uroot -p"$PW" --single-transaction --quick --routines --default-character-set=utf8mb4 "$DB"' | gzip > "$datei"
    [ -s "$datei" ] || { echo "Sicherung fehlgeschlagen ($db_dienst) – Update abgebrochen." >&2; rm -f "$datei"; exit 1; }
    echo "Datenbank ($db_dienst) gesichert: $datei"
  elif [ -n "${DB_HOST:-}" ] && [ -n "${DB_NAME:-}" ]; then
    # externer MySQL/MariaDB-Server
    datei="$SICHERUNGEN/untplanweb-$stempel.sql.gz"
    DUMP=$(command -v mariadb-dump || command -v mysqldump || true)
    [ -n "$DUMP" ] || { echo "mysqldump/mariadb-dump nicht gefunden – bitte installieren oder DB_HOST nicht setzen." >&2; exit 1; }
    MYSQL_PWD="${DB_PASSWORT:-}" "$DUMP" -h "$DB_HOST" -P "${DB_PORT:-3306}" -u "${DB_BENUTZER:-untplanweb}" --single-transaction --quick \
      --default-character-set=utf8mb4 "$DB_NAME" | gzip > "$datei"
    echo "Datenbank ($DB_HOST/$DB_NAME) gesichert: $datei"
  else
    # SQLite: konsistente Kopie im Volume (/data/sicherungen)
    docker compose exec -T "$DIENST" dotnet UntPlanWeb.dll --sichern
  fi
  # alte Sicherungen aufräumen (die neuesten $BEHALTEN bleiben)
  ls -1t "$SICHERUNGEN"/untplanweb-*.sql* 2>/dev/null | tail -n +"$((BEHALTEN + 1))" | while read -r alt; do rm -f "$alt"; done
fi

docker compose exec -T "$DIENST" rm -f /data/update/angefordert.json 2>/dev/null || true
echo "Neue Images laden …"
docker compose pull
docker compose up -d --remove-orphans
docker image prune -f >/dev/null 2>&1 || true
echo "Fertig. Version: Administration → Übersicht → Version & Updates."
