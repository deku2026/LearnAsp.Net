# W8 full-stack Compose

This stack builds the W7 Capstone roles and W8 labs from the same patch-pinned
.NET 10 SDK/runtime families and non-root chiseled runtime image. It also runs
PostgreSQL, Redis, RabbitMQ, Aspire Dashboard, Seq, Keycloak, Mailpit, and
Kafka.

```bash
cp deploy/docker/.env.example deploy/docker/.env
# Replace every placeholder with a local random value.
docker compose --env-file deploy/docker/.env \
  -f deploy/docker/compose.yml config --quiet
docker compose --env-file deploy/docker/.env \
  -f deploy/docker/compose.yml up -d --build
docker compose --env-file deploy/docker/.env \
  -f deploy/docker/compose.yml ps
```

Useful endpoints:

- Capstone gateway: `http://localhost:7000`
- W8 telemetry lab: `http://localhost:7081`
- Deployment probe lab: `http://localhost:7090`
- Aspire Dashboard: `http://localhost:18888`
- Seq: `http://localhost:5341`

The Compose stack is for local integration only. Production credentials belong
in a platform secret store, stateful services should use managed offerings or a
dedicated operator, and the Dashboard must not use anonymous mode.

Stop without deleting data with `docker compose ... down`. Add `--volumes` only
when deliberately resetting every local database and broker.
