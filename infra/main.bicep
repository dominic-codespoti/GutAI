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
@description('CalorieNinjas API key')
param calorieNinjasApiKey string = ''

@secure()
@description('Edamam App ID')
param edamamAppId string = ''

@secure()
@description('Edamam App Key')
param edamamAppKey string = ''

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
var effectiveCalorieNinjasApiKey = empty(calorieNinjasApiKey) ? 'unused' : calorieNinjasApiKey
var effectiveEdamamAppId = empty(edamamAppId) ? 'unused' : edamamAppId
var effectiveEdamamAppKey = empty(edamamAppKey) ? 'unused' : edamamAppKey
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

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
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
        { name: 'calorieninjas-api-key', value: effectiveCalorieNinjasApiKey }
        { name: 'edamam-app-id', value: effectiveEdamamAppId }
        { name: 'edamam-app-key', value: effectiveEdamamAppKey }
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
            { name: 'ConnectionStrings__AzureStorage', secretRef: 'storage-connection' }
            { name: 'Jwt__Secret', secretRef: 'jwt-secret' }
            { name: 'Jwt__Issuer', value: 'GutAI' }
            { name: 'Jwt__Audience', value: 'GutAI' }
            { name: 'Jwt__ExpiryMinutes', value: '60' }
            { name: 'ExternalApis__UsdaApiKey', secretRef: 'usda-api-key' }
            { name: 'ExternalApis__CalorieNinjasApiKey', secretRef: 'calorieninjas-api-key' }
            { name: 'ExternalApis__EdamamAppId', secretRef: 'edamam-app-id' }
            { name: 'ExternalApis__EdamamAppKey', secretRef: 'edamam-app-key' }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 2
              periodSeconds: 3
              failureThreshold: 15
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
        rules: [
          {
            name: 'http-scaling'
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

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output storageAccountName string = storage.name
output resourceGroupName string = resourceGroup().name
