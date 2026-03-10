// =============================================================================
// Cimmeria MCP Server — Complete Azure Infrastructure
// =============================================================================

@description('Primary Azure region (OpenAI, AI Search)')
param location string = 'eastus'

@description('Azure region for Cosmos DB, Service Plan, and Function App')
param computeLocation string = 'eastus2'

@description('Name for the Function App')
param functionAppName string = 'cimmeria-mcp'

@description('App Service Plan name')
param servicePlanName string = 'EastUS2Plan'

@description('Storage account name')
param storageAccountName string = 'ailabstoragesc'

@description('Azure OpenAI account name')
param openaiAccountName string = 'ailab-openai-cady'

@description('Cosmos DB account name')
param cosmosAccountName string = 'cimmeria-cosmos'

@description('Azure AI Search service name')
param searchServiceName string = 'ailab-search-sc'

@description('Deploy Azure AI Search (false for testing — free tier limited to 1 per subscription)')
param deploySearch bool = true

@description('Enable Cosmos DB free tier (limited to 1 per subscription)')
param cosmosFreeTier bool = false

@description('Azure Key Vault name (globally unique)')
param keyVaultName string = 'cimmeria-mcp-kv'

@description('Log Analytics Workspace name')
param logAnalyticsName string = 'cimmeria-mcp-logs'

@description('Application Insights name')
param appInsightsName string = 'cimmeria-mcp-insights'

@description('Azure App Configuration name (globally unique)')
param appConfigName string = 'cimmeria-mcp-config'

@description('Azure Static Web App name')
param staticSiteName string = 'cimmeria-mcp-site'

@description('Deploy free-tier showcase resources (Key Vault, App Config, Monitoring)')
param deployShowcase bool = true

// =============================================================================
// Existing Resources
// =============================================================================

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

// =============================================================================
// Cosmos DB
// =============================================================================

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: computeLocation
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: cosmosFreeTier
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: computeLocation
        failoverPriority: 0
      }
    ]
    capabilities: [
      {
        name: 'EnableNoSQLVectorSearch'
      }
    ]
  }
}

resource db 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: 'cimmeria'
  properties: {
    resource: {
      id: 'cimmeria'
    }
  }
}

resource codeChunks 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'code-chunks'
  properties: {
    resource: {
      id: 'code-chunks'
      partitionKey: {
        paths: ['/source_project']
        kind: 'Hash'
      }
      vectorEmbeddingPolicy: {
        vectorEmbeddings: [
          {
            path: '/embedding'
            dataType: 'float32'
            distanceFunction: 'cosine'
            dimensions: 505
          }
        ]
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/embedding/*' }
          { path: '/_etag/?' }
        ]
        vectorIndexes: [
          {
            path: '/embedding'
            type: 'flat'
          }
        ]
      }
    }
    options: {
      throughput: 400
    }
  }
}

resource knowledgeGraph 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'knowledge-graph'
  properties: {
    resource: {
      id: 'knowledge-graph'
      partitionKey: {
        paths: ['/pk']
        kind: 'Hash'
      }
    }
    options: {
      throughput: 400
    }
  }
}

// =============================================================================
// Azure OpenAI
// =============================================================================

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openaiAccountName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {}
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'text-embedding-3-small'
  sku: {
    name: 'Standard'
    capacity: 120
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      version: '1'
    }
  }
}

resource gpt4oMiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4o-mini'
  sku: {
    name: 'Standard'
    capacity: 30
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
  }
  dependsOn: [embeddingDeployment]
}

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4o'
  sku: {
    name: 'Standard'
    capacity: 30
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
  }
  dependsOn: [gpt4oMiniDeployment]
}

resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4-1'
  sku: {
    name: 'Standard'
    capacity: 30
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
  }
  dependsOn: [gpt4oDeployment]
}

resource gpt54Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-5-4'
  sku: {
    name: 'Standard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.4'
      version: '2026-03-05'
    }
  }
  dependsOn: [gpt41Deployment]
}

// =============================================================================
// Azure AI Search (skipped in test deployments)
// =============================================================================

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = if (deploySearch) {
  name: searchServiceName
  location: location
  sku: {
    name: 'free'
  }
  properties: {}
}

// =============================================================================
// Key Vault (Standard tier — ~$0.03/10K operations)
// =============================================================================

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = if (deployShowcase) {
  name: keyVaultName
  location: computeLocation
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: false
  }
}

resource openaiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployShowcase) {
  parent: vault
  name: 'openai-key'
  properties: {
    value: openai.listKeys().key1
  }
}

resource cosmosKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployShowcase) {
  parent: vault
  name: 'cosmos-key'
  properties: {
    value: cosmos.listKeys().primaryMasterKey
  }
}

resource searchKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployShowcase && deploySearch) {
  parent: vault
  name: 'search-key'
  properties: {
    value: search.listAdminKeys().primaryKey
  }
}

// =============================================================================
// Log Analytics Workspace (5 GB/month free ingestion)
// =============================================================================

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (deployShowcase) {
  name: logAnalyticsName
  location: computeLocation
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// =============================================================================
// Application Insights (5 GB/month free ingestion)
// =============================================================================

resource insights 'Microsoft.Insights/components@2020-02-02' = if (deployShowcase) {
  name: appInsightsName
  location: computeLocation
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// =============================================================================
// App Configuration (Free tier — 10 MB storage, 1,000 requests/day)
// =============================================================================

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = if (deployShowcase) {
  name: appConfigName
  location: computeLocation
  sku: {
    name: 'free'
  }
}

// =============================================================================
// Diagnostic Settings
// =============================================================================

resource cosmosDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (deployShowcase) {
  name: 'cosmos-diagnostics'
  scope: cosmos
  properties: {
    workspaceId: logs.id
    metrics: [
      {
        category: 'Requests'
        enabled: true
      }
    ]
  }
}

// =============================================================================
// Static Web App (Free tier — 100 GB bandwidth/month)
// =============================================================================

resource staticSite 'Microsoft.Web/staticSites@2023-12-01' = if (deployShowcase) {
  name: staticSiteName
  location: computeLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// =============================================================================
// Service Plan + Function App
// =============================================================================

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: servicePlanName
  location: computeLocation
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

resource func 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: computeLocation
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      appSettings: union(
        [
          {
            name: 'AzureWebJobsStorage'
            value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
          }
          {
            name: 'FUNCTIONS_EXTENSION_VERSION'
            value: '~4'
          }
          {
            name: 'FUNCTIONS_WORKER_RUNTIME'
            value: 'dotnet-isolated'
          }
          {
            name: 'OPENAI_ENDPOINT'
            value: openai.properties.endpoint
          }
          {
            name: 'COSMOS_ENDPOINT'
            value: cosmos.properties.documentEndpoint
          }
        ],
        deployShowcase ? [
          {
            name: 'OPENAI_KEY'
            value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=openai-key)'
          }
          {
            name: 'COSMOS_KEY'
            value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=cosmos-key)'
          }
          {
            name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
            value: insights.properties.InstrumentationKey
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: insights.properties.ConnectionString
          }
        ] : [
          {
            name: 'OPENAI_KEY'
            value: openai.listKeys().key1
          }
          {
            name: 'COSMOS_KEY'
            value: cosmos.listKeys().primaryMasterKey
          }
        ],
        deploySearch ? [
          {
            name: 'SEARCH_ENDPOINT'
            value: 'https://${search.name}.search.windows.net'
          }
          {
            name: 'SEARCH_KEY'
            value: deployShowcase ? '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=search-key)' : search.listAdminKeys().primaryKey
          }
        ] : []
      )
    }
  }
}

// Function App diagnostic settings
resource funcDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (deployShowcase) {
  name: 'func-diagnostics'
  scope: func
  properties: {
    workspaceId: logs.id
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// RBAC: Function App → Key Vault Secrets User
resource funcKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployShowcase) {
  name: guid(vault.id, func.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: func.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// RBAC: Function App → App Configuration Data Reader
resource funcAppConfigRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployShowcase) {
  name: guid(appConfig.id, func.id, '516239f1-63e1-4d78-a4de-a74fb236a071')
  scope: appConfig
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '516239f1-63e1-4d78-a4de-a74fb236a071')
    principalId: func.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// =============================================================================
// Outputs
// =============================================================================

output functionAppName string = func.name
output functionAppUrl string = 'https://${func.properties.defaultHostName}'
output mcpEndpoint string = 'https://${func.properties.defaultHostName}/runtime/webhooks/mcp'
output openaiEndpoint string = openai.properties.endpoint
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output searchEndpoint string = deploySearch ? 'https://${search.name}.search.windows.net' : ''
output keyVaultUri string = deployShowcase ? vault.properties.vaultUri : ''
output appConfigEndpoint string = deployShowcase ? appConfig.properties.endpoint : ''
output functionAppPrincipalId string = func.identity.principalId
output staticSiteUrl string = deployShowcase ? 'https://${staticSite.properties.defaultHostname}' : ''
