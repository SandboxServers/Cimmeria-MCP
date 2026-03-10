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

@description('Email for budget and monitoring alerts (empty to skip)')
param alertEmail string = ''

@description('Deploy free-tier showcase resources (Key Vault, App Config, Monitoring)')
param deployShowcase bool = true

@description('Azure API Management name (globally unique)')
param apiManagementName string = 'cimmeria-mcp-apim'

@description('Azure Automation Account name')
param automationAccountName string = 'cimmeria-mcp-automation'

@description('Azure SignalR Service name (globally unique)')
param signalrName string = 'cimmeria-mcp-signalr'

var tags = {
  project: 'cimmeria-mcp'
  'managed-by': 'bicep'
  purpose: 'mcp-server'
}

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
  tags: tags
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

resource leases 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'leases'
  properties: {
    resource: {
      id: 'leases'
      partitionKey: {
        paths: ['/id']
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
  tags: tags
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

resource gpt51ChatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-5-1-chat'
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.1-chat'
      version: '2025-11-13'
    }
  }
  dependsOn: [gpt41Deployment]
}

resource gpt51CodexMiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-5-1-codex-mini'
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.1-codex-mini'
      version: '2025-11-13'
    }
  }
  dependsOn: [gpt51ChatDeployment]
}

// =============================================================================
// Azure AI Search (skipped in test deployments)
// =============================================================================

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = if (deploySearch) {
  name: searchServiceName
  location: location
  tags: tags
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
  tags: tags
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
  tags: tags
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
  tags: tags
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
  tags: tags
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
  tags: tags
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
  tags: tags
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
          {
            name: 'AzureSignalRConnectionString'
            value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=signalr-connection)'
          }
          {
            name: 'APPINSIGHTS_RESOURCE_ID'
            value: insights.id
          }
          {
            name: 'COSMOS_RESOURCE_ID'
            value: cosmos.id
          }
          {
            name: 'COSMOS_CONNECTION_STRING'
            value: cosmos.listConnectionStrings().connectionStrings[0].connectionString
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
          {
            name: 'SEARCH_RESOURCE_ID'
            value: search.id
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
// Azure Monitor Action Group + Budget Alert (free)
// =============================================================================

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (deployShowcase && alertEmail != '') {
  name: 'cimmeria-mcp-alerts'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'cimmeria'
    enabled: true
    emailReceivers: [
      {
        name: 'admin'
        emailAddress: alertEmail
      }
    ]
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (deployShowcase && alertEmail != '') {
  name: 'cimmeria-mcp-monthly'
  properties: {
    category: 'Cost'
    amount: 10
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-03-01'
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactGroups: [actionGroup.id]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactGroups: [actionGroup.id]
      }
    }
  }
}

// =============================================================================
// API Management (Consumption tier — 1M calls/month free)
// =============================================================================

resource apim 'Microsoft.ApiManagement/service@2023-09-01-preview' = if (deployShowcase) {
  name: apiManagementName
  location: computeLocation
  tags: tags
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  properties: {
    publisherName: 'Cimmeria MCP'
    publisherEmail: alertEmail != '' ? alertEmail : 'noreply@cimmeria-mcp.dev'
  }
}

resource apimApi 'Microsoft.ApiManagement/service/apis@2023-09-01-preview' = if (deployShowcase) {
  parent: apim
  name: 'cimmeria-mcp-api'
  properties: {
    displayName: 'Cimmeria MCP'
    path: 'mcp'
    protocols: ['https']
    serviceUrl: 'https://${func.properties.defaultHostName}'
    apiRevision: '1'
  }
}

// =============================================================================
// Azure Automation (500 min/month free)
// =============================================================================

resource automation 'Microsoft.Automation/automationAccounts@2023-11-01' = if (deployShowcase) {
  name: automationAccountName
  location: computeLocation
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    sku: {
      name: 'Basic'
    }
  }
}

resource keyRotationRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = if (deployShowcase) {
  parent: automation
  name: 'Rotate-Keys'
  location: computeLocation
  tags: tags
  properties: {
    runbookType: 'PowerShell'
    logProgress: true
    logVerbose: false
  }
}

// RBAC: Automation → Contributor on RG
resource automationContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployShowcase) {
  name: guid(resourceGroup().id, automation.id, 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalId: automation.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// RBAC: Automation → Key Vault Secrets Officer
resource automationKvOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployShowcase) {
  name: guid(vault.id, automation.id, 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: automation.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// =============================================================================
// Azure SignalR Service (Free tier — 20 connections, 20K messages/day)
// =============================================================================

resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = if (deployShowcase) {
  name: signalrName
  location: computeLocation
  tags: tags
  sku: {
    name: 'Free_F1'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Serverless'
      }
    ]
  }
}

resource signalrSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployShowcase) {
  parent: vault
  name: 'signalr-connection'
  properties: {
    value: signalr.listKeys().primaryConnectionString
  }
}

// =============================================================================
// Monitoring Reader RBAC (for metrics endpoint)
// =============================================================================

resource funcMonitoringReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployShowcase) {
  name: guid(resourceGroup().id, func.id, '43d0d8ad-25c7-4714-9337-8ba259a9fe05')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '43d0d8ad-25c7-4714-9337-8ba259a9fe05')
    principalId: func.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// =============================================================================
// Azure Portal Dashboard (free — IaC-defined)
// =============================================================================

resource dashboard 'Microsoft.Portal/dashboards@2020-09-01-preview' = if (deployShowcase) {
  name: 'cimmeria-mcp-dashboard'
  location: computeLocation
  tags: tags
  properties: {
    lenses: [
      {
        order: 0
        parts: [
          {
            position: { x: 0, y: 0, colSpan: 12, rowSpan: 1 }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              inputs: []
              settings: {
                content: {
                  settings: {
                    title: 'Cimmeria MCP Server'
                    subtitle: 'Operational Dashboard'
                    content: 'Real-time metrics for the Cimmeria MCP Server infrastructure.'
                  }
                }
              }
            }
          }
          {
            position: { x: 0, y: 1, colSpan: 6, rowSpan: 4 }
            metadata: {
              type: 'Extension/Microsoft_Azure_Monitoring/PartType/MetricsChartPart'
              inputs: [
                {
                  name: 'queryInputs'
                  value: {
                    id: func.id
                    chartType: 2
                    timespan: { duration: 'PT24H' }
                    metrics: [
                      {
                        resourceMetadata: { id: func.id }
                        name: 'FunctionExecutionCount'
                        aggregationType: 1
                        metricVisualization: { displayName: 'Execution Count' }
                      }
                    ]
                    title: 'Function Executions (24h)'
                  }
                }
              ]
            }
          }
          {
            position: { x: 6, y: 1, colSpan: 6, rowSpan: 4 }
            metadata: {
              type: 'Extension/Microsoft_Azure_Monitoring/PartType/MetricsChartPart'
              inputs: [
                {
                  name: 'queryInputs'
                  value: {
                    id: cosmos.id
                    chartType: 2
                    timespan: { duration: 'PT24H' }
                    metrics: [
                      {
                        resourceMetadata: { id: cosmos.id }
                        name: 'NormalizedRUConsumption'
                        aggregationType: 3
                        metricVisualization: { displayName: 'RU Consumption %' }
                      }
                    ]
                    title: 'Cosmos DB RU Consumption (24h)'
                  }
                }
              ]
            }
          }
        ]
      }
    ]
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
output apimGatewayUrl string = deployShowcase ? apim.properties.gatewayUrl : ''
output signalrHostname string = deployShowcase ? signalr.properties.hostName : ''
output dashboardUrl string = deployShowcase ? 'https://portal.azure.com/#@/dashboard/arm${dashboard.id}' : ''
