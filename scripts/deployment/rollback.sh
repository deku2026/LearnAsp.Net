#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <namespace> <deployment>" >&2
  exit 2
fi

namespace="$1"
deployment="$2"

kubectl --namespace "$namespace" rollout history "deployment/$deployment"
kubectl --namespace "$namespace" rollout undo "deployment/$deployment"
kubectl --namespace "$namespace" rollout status \
  "deployment/$deployment" \
  --timeout=5m
kubectl --namespace "$namespace" get pods \
  --selector "app.kubernetes.io/name=$deployment" \
  --output=wide
