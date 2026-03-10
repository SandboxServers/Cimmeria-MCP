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
            name: 'OPENAI_KEY'
            value: openai.listKeys().key1
          }
          {
            name: 'COSMOS_ENDPOINT'
            value: cosmos.properties.documentEndpoint
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
            value: search.listAdminKeys().primaryKey
          }
        ] : []
      )
    }
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
