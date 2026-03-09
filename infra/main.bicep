targetScope = 'resourceGroup'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Full container image reference (e.g., ghcr.io/owner/repo:sha)')
param containerImage string

@secure()
@description('JWT signing secret (min 32 chars)')
param jwtSecret string

@secure()
@description('USDA FoodData Central API key')
param usdaApiKey string = ''

@secure()
@description('GHCR username (GitHub username)')
param ghcrUsername string = ''

@secure()
@description('GHCR password (GitHub PAT with read:packages)')
param ghcrPassword string = ''

var prefix = 'gutai-prod'
var tags = {
  project: 'gutai'
}

// Azure Container Apps requires non-empty secret values.
// Use 'unused' as placeholder for optional API keys not yet configured.
var effectiveUsdaApiKey = empty(usdaApiKey) ? 'unused' : usdaApiKey

// ── Storage Account (Table Storage) ──
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: replace('${prefix}storage', '-', '')
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// ── Log Analytics (required by Container Apps) ──
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ──
resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ── Container App (API) ──
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=core.windows.net'

@description('Azure OpenAI (AI Foundry) endpoint URL')
param azureOpenAIEndpoint string = ''

@description('Azure OpenAI deployment name')
param azureOpenAIDeploymentName string = 'gpt-4o-mini'

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      maxInactiveRevisions: 2
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['*']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-password'
        }
      ]
      secrets: [
        { name: 'storage-connection', value: storageConnectionString }
        { name: 'jwt-secret', value: jwtSecret }
        { name: 'usda-api-key', value: effectiveUsdaApiKey }
        { name: 'ghcr-password', value: ghcrPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
            { name: 'DOTNET_RUNNING_IN_CONTAINER', value: 'true' }
            { name: 'DOTNET_GCServer', value: '0' }
            { name: 'ConnectionStrings__AzureStorage', secretRef: 'storage-connection' }
            { name: 'Jwt__Secret', secretRef: 'jwt-secret' }
            { name: 'Jwt__Issuer', value: 'GutAI' }
            { name: 'Jwt__Audience', value: 'GutAI' }
            { name: 'Jwt__ExpiryMinutes', value: '60' }
            { name: 'ExternalApis__UsdaApiKey', secretRef: 'usda-api-key' }
            { name: 'AzureOpenAI__Endpoint', value: azureOpenAIEndpoint }
            { name: 'AzureOpenAI__DeploymentName', value: azureOpenAIDeploymentName }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              failureThreshold: 30
              timeoutSeconds: 5
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
              timeoutSeconds: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 3
              timeoutSeconds: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '25'
              }
            }
          }
        ]
      }
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output storageAccountName string = storage.name
output resourceGroupName string = resourceGroup().name
