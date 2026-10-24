#!/bin/sh
# отдельная база и пользователь на каждый сервис - инстанс общий, данные нет
set -e

create_db() {
    db="$1"
    password="$2"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
        CREATE USER $db WITH PASSWORD '$password';
        CREATE DATABASE $db OWNER $db;
        REVOKE ALL ON DATABASE $db FROM PUBLIC;
SQL
}

create_db gateway "$GATEWAY_DB_PASSWORD"
create_db monitoring "$MONITORING_DB_PASSWORD"
create_db incidents "$INCIDENTS_DB_PASSWORD"
create_db tgprobe "$TGPROBE_DB_PASSWORD"
