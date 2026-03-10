using '../main.bicep'

param location = 'eastus'
param computeLocation = 'eastus2'
param functionAppName = 'cimmeria-mcp-bicep'
param servicePlanName = 'cimmeria-mcp-bicep-plan'
param storageAccountName = 'cimmeriamcpbicep'
param openaiAccountName = 'cimmeria-openai-bicep'
param cosmosAccountName = 'cimmeria-cosmos-bicep'
param searchServiceName = 'cimmeria-search-bicep'
param deploySearch = false
param cosmosFreeTier = false
param keyVaultName = 'cimmeria-bicep-kv'
param logAnalyticsName = 'cimmeria-bicep-logs'
param appInsightsName = 'cimmeria-bicep-insights'
param appConfigName = 'cimmeria-bicep-config'
param deployShowcase = true
