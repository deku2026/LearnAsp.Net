SELECT 'CREATE DATABASE campus_w7_catalog'
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = 'campus_w7_catalog'
)\gexec

SELECT 'CREATE DATABASE campus_w7_enrollment'
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = 'campus_w7_enrollment'
)\gexec

SELECT 'CREATE DATABASE campus_w7_notices'
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = 'campus_w7_notices'
)\gexec

SELECT 'CREATE DATABASE campus_w8_troubleshooting'
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = 'campus_w8_troubleshooting'
)\gexec
