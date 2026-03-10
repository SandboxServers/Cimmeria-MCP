using './main.bicep'

param location = 'eastus'
param cosmosLocation = 'eastus2'
param functionAppName = 'cimmeria-mcp'
param storageAccountName = 'ailabstoragesc'
param openaiAccountName = 'ailab-openai-cady'
param cosmosAccountName = 'cimmeria-cosmos'
param searchServiceName = 'ailab-search-sc'
param deploySearch = true
param cosmosFreeTier = true
