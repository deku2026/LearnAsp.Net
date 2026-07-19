# Azure Container Apps reference deployment

`main.bicep` is the ACA alternative to the Kubernetes manifests. It deploys a
managed environment and a production-shaped gateway Container App with
revision-based rollout, probes, scaling, resource limits, and secrets supplied
only as secure deployment parameters.

The template assumes PostgreSQL, RabbitMQ, OTLP, and the three internal services
already have private endpoints. In a real environment provision those with
separate lifecycle modules, private networking, managed identities, backups,
and monitoring.

```bash
az deployment group what-if \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --template-file deploy/bicep/main.bicep \
  --parameters image="$IMAGE" \
  --parameters gatewaySigningKey="$GATEWAY_SIGNING_KEY" \
  --parameters gatewayInternalToken="$GATEWAY_INTERNAL_TOKEN"
```

The GitHub deployment job uses OIDC and a protected environment. Configure
federated identity credentials for the repository/environment and store only
the Azure client, tenant, and subscription identifiers as GitHub secrets.
