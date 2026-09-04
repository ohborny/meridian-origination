#!/bin/bash
# ============================================================================
# Loads the per-database schema and seed files during container bootstrap.
#
# The Postgres entrypoint globs only the top level of /docker-entrypoint-initdb.d,
# so the sql/core, sql/credit, sql/compliance and sql/customer folders are not
# picked up on their own. Each SQL file carries its own "\c <database>" header,
# so psql is pointed at los_core and the file switches connections itself.
# ============================================================================
set -e

SQL_DIR=/docker-entrypoint-initdb.d/sql

for module in core credit compliance customer; do
    for f in $(ls "$SQL_DIR/$module"/*.sql | sort); do
        echo "[01_load_schemas] loading $module/$(basename "$f")"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname los_core -f "$f"
    done
done

echo "[01_load_schemas] all schemas and seed data loaded"
