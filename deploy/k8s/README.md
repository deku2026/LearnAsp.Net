# Kubernetes deployment

The manifests deploy stateless Capstone services. PostgreSQL, RabbitMQ, and the
OTLP collector are platform-owned dependencies and must be ready before rollout.

Create `campus-secrets` from an approved secret manager or, for a disposable
cluster only, from protected shell environment variables:

```bash
kubectl create namespace campus --dry-run=client -o yaml | kubectl apply -f -
kubectl -n campus create secret generic campus-secrets \
  --from-literal=catalog-connection="$CATALOG_CONNECTION" \
  --from-literal=enrollment-connection="$ENROLLMENT_CONNECTION" \
  --from-literal=notices-connection="$NOTICES_CONNECTION" \
  --from-literal=rabbitmq-connection="$RABBITMQ_CONNECTION" \
  --from-literal=gateway-signing-key="$GATEWAY_SIGNING_KEY" \
  --from-literal=gateway-internal-token="$GATEWAY_INTERNAL_TOKEN"
kubectl apply --server-side --dry-run=server -f deploy/k8s/capstone.yaml
kubectl apply -f deploy/k8s/capstone.yaml
kubectl apply -f deploy/k8s/autoscaling.yaml
kubectl -n campus rollout status deployment/gateway --timeout=5m
```

Replace the example image and ingress host before deployment. Admission policy
should require immutable digests, signed provenance, non-root containers,
runtime-default seccomp, resource limits, and approved registries. Terminate TLS
at the ingress or upstream load balancer. KEDA resources require KEDA to be
installed; omit `autoscaling.yaml` if the cluster does not provide it.

Use `scripts/deployment/rollback.sh campus gateway` for a guarded rollback.
