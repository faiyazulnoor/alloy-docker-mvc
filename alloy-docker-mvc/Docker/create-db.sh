#!/bin/bash

echo "Detecting SQL tools..."

# 1. Dynamically find sqlcmd
if [ -f /opt/mssql-tools18/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools18/bin/sqlcmd
    FLAGS="-C" # New tools need the Trust Certificate flag
elif [ -f /opt/mssql-tools/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools/bin/sqlcmd
    FLAGS=""
else
    echo "ERROR: sqlcmd not found!"
    exit 1
fi

echo "Using: $SQLCMD"

# 2. Loop until SQL is ready
for i in {1..50}; do
    # Try to connect and check if the DB exists
    # We use [${DB_NAME}] in the query as well for safety
    DB_CHECK=$($SQLCMD -S localhost -U sa -P "$SA_PASSWORD" $FLAGS -Q "SET NOCOUNT ON; SELECT database_id FROM sys.databases WHERE name = '${DB_NAME}'" -h -1 | tr -d '[:space:]')
    
    if [ $? -eq 0 ]; then
        if [ -n "$DB_CHECK" ] && [ "$DB_CHECK" != "NULL" ] && [ "$DB_CHECK" != "" ]; then
            echo "Database [${DB_NAME}] already exists. Checking status..."
         else
            echo "Database not found. Creating [${DB_NAME}]..."
            if [ -f "/var/opt/mssql/host_data/${DB_DIRECTORY}/${DB_NAME}.mdf" ]; then
                echo "MDF found, but LDF might be missing. Attaching and rebuilding log..."
                # Changed to ATTACH_REBUILD_LOG
                $SQLCMD -S localhost -U sa -P "$SA_PASSWORD" $FLAGS -Q "CREATE DATABASE [${DB_NAME}] ON (FILENAME='/var/opt/mssql/host_data/${DB_DIRECTORY}/${DB_NAME}.mdf') FOR ATTACH_REBUILD_LOG;"
            else
                echo "No files found. Creating fresh database..."
                $SQLCMD -S localhost -U sa -P "$SA_PASSWORD" $FLAGS -Q "CREATE DATABASE [${DB_NAME}];"
            fi
        fi

        # 3. VERIFY ONLINE STATUS
        # This prevents the web-1 container from failing while the DB is still initializing
        echo "Verifying [${DB_NAME}] is ONLINE..."
        for j in {1..30}; do
            STATUS=$($SQLCMD -S localhost -U sa -P "$SA_PASSWORD" $FLAGS -Q "SET NOCOUNT ON; SELECT state_desc FROM sys.databases WHERE name = '${DB_NAME}'" -h -1 | tr -d '[:space:]')
            if [ "$STATUS" == "ONLINE" ]; then
                echo "Database [${DB_NAME}] is ONLINE and ready for connections!"
                exit 0
            fi
            echo "Database status is: $STATUS. Waiting..."
            sleep 1
        done
        break
    fi

    echo "SQL Server starting up... (Attempt $i)"
    sleep 2
done

exit 1