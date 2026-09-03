#!/bin/bash
# Cria o segundo banco de dados na mesma instância PostgreSQL local. O primeiro banco
# (definido por POSTGRES_DB) pertence ao Ledger; este script cria o banco do Daily Balance.
# Em produção (Azure Database for PostgreSQL), cada serviço tem sua própria instância —
# aqui a separação por banco simula o isolamento de dados por contexto em ambiente local
# (ver docs/architecture/03-visao-de-containers-c4.md).
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    SELECT 'CREATE DATABASE verity_daily_balance'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'verity_daily_balance')\gexec
EOSQL
