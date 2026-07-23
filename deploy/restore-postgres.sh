#!/usr/bin/env bash
set -euo pipefail

if [[ "${CONFIRM_RESTORE:-}" != "dailygate" ]]; then
  printf 'Restore replaces the current database. Re-run with CONFIRM_RESTORE=dailygate.\n' >&2
  exit 2
fi
if [[ $# -ne 1 || ! -f "$1" ]]; then
  printf 'Usage: CONFIRM_RESTORE=dailygate %s /absolute/path/to/backup.sql.gz.age\n' "$0" >&2
  exit 2
fi

deploy_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$deploy_dir"
set -a
source ./.env
set +a

docker compose exec -T db psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER:-dailygate}" -d "${POSTGRES_DB:-dailygate}" \
  -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
age --decrypt "$1" | gzip --decompress | docker compose exec -T db \
  psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER:-dailygate}" -d "${POSTGRES_DB:-dailygate}"
printf 'Database restore completed from %s\n' "$1"
