SELECT format('SELECT %L || ''|'' || count(*)::text FROM %I.%I;',
              table_schema || '.' || table_name, table_schema, table_name)
FROM information_schema.tables
WHERE table_schema <> 'information_schema' AND left(table_schema, 3) <> 'pg_' AND table_type = 'BASE TABLE'
ORDER BY table_schema, table_name
\gexec
