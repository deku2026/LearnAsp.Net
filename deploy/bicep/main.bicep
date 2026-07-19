targetScope = 'resourceGroup'

@description('Azure region for the Container Apps environment.')
param location string = resourceGroup().location

@description('Immutable gateway image, preferably pinned by digest.')
param image string

@secure()
@minLength(32)
param gatewaySigningKey string

@secure()
@minLength(32)
param gatewayInternalToken string

param catalogUrl string = 'http://catalog'
param enrollmentUrl string = 'http://enrollment'
param noticesUrl string = 'http://notices'
param otlpEndpoint string = 'http://otel-collector:4317'
param revisionSuffix string = take(uniqueString(deployment().name), 10)

resource environment 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: 'campus-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    zoneRedundant: true
  }
}

resource gateway 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'campus-gateway'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Multiple'
      maxInactiveRevisions: 20
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: [
        {
          name: 'gateway-signing-key'
          value: gatewaySigningKey
        }
        {
          name: 'gateway-internal-token'
          value: gatewayInternalToken
        }
      ]
    }
    template: {
      revisionSuffix: revisionSuffix
      terminationGracePeriodSeconds: 30
      containers: [
        {
          name: 'gateway'
          image: image
          args: [
            'Part07_DistributedComm.dll'
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'Distributed__Role', value: 'Gateway' }
            { name: 'Distributed__CatalogHttpUrl', value: catalogUrl }
            { name: 'Distributed__EnrollmentUrl', value: enrollmentUrl }
            { name: 'Distributed__NoticesUrl', value: noticesUrl }
            { name: 'GatewayAuth__Issuer', value: 'campus-gateway' }
            { name: 'GatewayAuth__Audience', value: 'campus-capstone' }
            { name: 'GatewayAuth__SigningKey', secretRef: 'gateway-signing-key' }
            { name: 'GatewayAuth__InternalToken', secretRef: 'gateway-internal-token' }
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: 'grpc' }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 10
              timeoutSeconds: 2
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 5
              timeoutSeconds: 3
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 2
        maxReplicas: 10
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output gatewayFqdn string = gateway.properties.configuration.ingress.fqdn
output gatewayPrincipalId string = gateway.identity.principalId
