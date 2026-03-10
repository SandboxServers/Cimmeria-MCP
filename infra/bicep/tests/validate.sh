#!/bin/bash
set -euo pipefail

RG="cimmeria-mcp-test-bicep"
LOCATION="eastus2"
TEMPLATE="../main.bicep"
PARAMS="./test.bicepparam"

echo "=== Bicep Validation Test ==="

# Create test resource group
echo "Creating test resource group: $RG"
az group create --name "$RG" --location "$LOCATION" --output none

# Compile Bicep to verify syntax
echo "Compiling Bicep template..."
az bicep build --file "$TEMPLATE"

# What-if deployment (dry run)
echo "Running what-if deployment..."
az deployment group what-if \
  --resource-group "$RG" \
  --template-file "$TEMPLATE" \
  --parameters "$PARAMS" \
  --no-prompt

# Deploy
echo "Deploying to test resource group..."
az deployment group create \
  --resource-group "$RG" \
  --template-file "$TEMPLATE" \
  --parameters "$PARAMS" \
  --name bicep-test \
  --no-prompt

# Show outputs
echo "Deployment outputs:"
az deployment group show \
  --resource-group "$RG" \
  --name bicep-test \
  --query properties.outputs

# Cleanup
echo "Cleaning up test resource group..."
az group delete --name "$RG" --yes --no-wait

echo "=== Bicep validation complete ==="
