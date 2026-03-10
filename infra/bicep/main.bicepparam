using './main.bicep'

param location = 'eastus'
param computeLocation = 'eastus2'
param functionAppName = 'cimmeria-mcp'
param servicePlanName = 'EastUS2Plan'
param storageAccountName = 'ailabstoragesc'
param openaiAccountName = 'ailab-openai-cady'
param cosmosAccountName = 'cimmeria-cosmos'
param searchServiceName = 'ailab-search-sc'
param deploySearch = true
param cosmosFreeTier = true
param keyVaultName = 'cimmeria-mcp-kv'
param logAnalyticsName = 'cimmeria-mcp-logs'
param appInsightsName = 'cimmeria-mcp-insights'
param appConfigName = 'cimmeria-mcp-config'
param deployShowcase = true
