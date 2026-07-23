#!/usr/bin/env bash
set -euo pipefail

deploy_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$deploy_dir"
set -a
source ./.env
set +a

: "${AGE_RECIPIENT:?AGE_RECIPIENT is required}"
: "${RCLONE_REMOTE:?RCLONE_REMOTE is required}"

backup_dir="$deploy_dir/backups"
mkdir -p "$backup_dir"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="$backup_dir/dailygate-$timestamp.sql.gz.age"

docker compose exec -T db pg_dump -U "${POSTGRES_USER:-dailygate}" -d "${POSTGRES_DB:-dailygate}" --format=plain \
  | gzip -9 \
  | age -r "$AGE_RECIPIENT" -o "$backup_path"

for retention_tier in daily weekly monthly; do
  rclone mkdir "$RCLONE_REMOTE/$retention_tier"
done
rclone copyto "$backup_path" "$RCLONE_REMOTE/daily/dailygate-$timestamp.sql.gz.age"
day_of_week="$(date -u +%u)"
day_of_month="$(date -u +%d)"
if [[ "$day_of_week" == "7" ]]; then
  rclone copyto "$backup_path" "$RCLONE_REMOTE/weekly/dailygate-$timestamp.sql.gz.age"
fi
if [[ "$day_of_month" == "01" ]]; then
  rclone copyto "$backup_path" "$RCLONE_REMOTE/monthly/dailygate-$timestamp.sql.gz.age"
fi
rclone delete "$RCLONE_REMOTE/daily" --min-age 8d
rclone delete "$RCLONE_REMOTE/weekly" --min-age 57d
rclone delete "$RCLONE_REMOTE/monthly" --min-age 367d
find "$backup_dir" -type f -name 'dailygate-*.sql.gz.age' -mtime +7 -delete
printf 'Encrypted backup uploaded: %s\n' "$backup_path"
