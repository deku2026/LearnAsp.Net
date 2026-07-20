# W9 full-stack acceptance script

Run in Debian WSL after starting the compose stack:

```
docker compose -f ~/Project/docker/docker-compose.yml up -d
bash scripts/w9/run-full-stack-acceptance.sh
```

The script verifies all 10 containers have role-appropriate evidence:

- **PostgreSQL**: connect + `SELECT 1`
- **Redis**: PING + SET/GET/TTL
- **RabbitMQ**: management API readiness
- **Kafka**: topic list (W9 Part12 adds real produce/consume/group/replay in the
  integration tests)
- **Keycloak**: OIDC discovery endpoint
- **Seq**: HTTP readiness
- **Aspire Dashboard**: HTTP readiness (OTLP ingest verified in W8)
- **Mailpit**: HTTP API readiness (W9 Part12 adds real SMTP + API assertions in
  the integration tests)
- **Adminer**: HTTP readiness (manual table inspection, operations UI only)
- **RedisInsight**: HTTP readiness (manual key/TTL inspection, operations UI only)

The summary is written to `artifacts/w9/full-stack-acceptance.md` with SDK, OS,
CPU, and commit SHA. The script never deletes persistent volumes unless
`RESET_ALL=1` is set.