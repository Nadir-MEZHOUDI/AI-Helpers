#!/usr/bin/env bash
# One-time setup of MyApp.Gateway on the VPS. Safe to re-run: it never overwrites existing secrets.
#
# Run from your PC:   ssh vps 'bash -s' < deploy/gateway/bootstrap-vps.sh
#
# Creates in ~/myapp-gateway (no sudo needed):
#   .env   random Postgres password, admin key (X-Admin-Key header) and JWT signing key
# and the "myapp_gateway" role + database in the shared `postgres` container.
# Changing JWT_SIGNING_KEY later signs out every phone; the admin key can be changed any time.

set -euo pipefail

ROOT=${GATEWAY_ROOT:-$HOME/myapp-gateway}
PG_CONTAINER=${PG_CONTAINER:-postgres}

mkdir -p "$ROOT"
cd "$ROOT"
umask 077

if [ ! -s .env ]; then
    cat > .env <<EOF
POSTGRES_HOST=postgres
POSTGRES_DB=myapp_gateway
POSTGRES_USER=myapp_gateway
POSTGRES_PASSWORD=$(openssl rand -hex 24)
GATEWAY_ADMIN_KEY=$(openssl rand -hex 32)
JWT_SIGNING_KEY=$(openssl rand -hex 32)
GATEWAY_WEB_PORT=8094
EOF
    chmod 600 .env
    echo "Created $ROOT/.env (the admin key is GATEWAY_ADMIN_KEY)."
else
    echo ".env already exists (kept)."
fi

set -a
# shellcheck disable=SC1091
. ./.env
set +a

superuser=$(docker exec "$PG_CONTAINER" printenv POSTGRES_USER)
docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -q -U "$superuser" -d postgres <<SQL
DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$POSTGRES_USER') THEN
        CREATE ROLE "$POSTGRES_USER" LOGIN PASSWORD '$POSTGRES_PASSWORD';
    END IF;
END
\$\$;
SQL

if [ "$(docker exec "$PG_CONTAINER" psql -U "$superuser" -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$POSTGRES_DB'")" != "1" ]; then
    docker exec "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -q -U "$superuser" -d postgres -c "CREATE DATABASE \"$POSTGRES_DB\" OWNER \"$POSTGRES_USER\""
    echo "Created database $POSTGRES_DB."
else
    echo "Database $POSTGRES_DB already exists (kept)."
fi

echo "Bootstrap complete. The gateway listens on 127.0.0.1:${GATEWAY_WEB_PORT:-8094} once deployed."
