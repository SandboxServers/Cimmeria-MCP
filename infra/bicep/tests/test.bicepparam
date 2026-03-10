using '../main.bicep'

param location = 'eastus'
param cosmosLocation = 'eastus2'
param functionAppName = 'cimmeria-mcp-bicep'
param storageAccountName = 'cimmeriamcpbicep'
param openaiAccountName = 'cimmeria-openai-bicep'
param cosmosAccountName = 'cimmeria-cosmos-bicep'
param searchServiceName = 'cimmeria-search-bicep'
param deploySearch = false
param cosmosFreeTier = false
