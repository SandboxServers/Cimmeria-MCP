<#
.SYNOPSIS
    Rotates keys for Cosmos DB, OpenAI, and AI Search, storing new values in Key Vault.
.DESCRIPTION
    Monthly key rotation runbook for Azure Automation. Regenerates primary keys
    for Cosmos DB, Azure OpenAI, and Azure AI Search, then updates the corresponding
    Key Vault secrets. Function App Key Vault references auto-resolve to new values.
.PARAMETER ResourceGroupName
    Name of the Azure resource group containing all resources.
.PARAMETER KeyVaultName
    Name of the Key Vault to update with rotated keys.
.PARAMETER CosmosAccountName
    Name of the Cosmos DB account.
.PARAMETER OpenAIAccountName
    Name of the Azure OpenAI account.
.PARAMETER SearchServiceName
    Name of the Azure AI Search service.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,

    [Parameter(Mandatory = $true)]
    [string]$CosmosAccountName,

    [Parameter(Mandatory = $true)]
    [string]$OpenAIAccountName,

    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName
)

$ErrorActionPreference = 'Stop'

# Authenticate using the Automation Account's managed identity
Connect-AzAccount -Identity | Out-Null
Write-Output "Authenticated with managed identity"

# --- Cosmos DB ---
Write-Output "Rotating Cosmos DB primary key..."
$null = New-AzCosmosDBAccountKey -ResourceGroupName $ResourceGroupName -Name $CosmosAccountName -KeyKind "primary"
$cosmosKeys = Get-AzCosmosDBAccountKey -ResourceGroupName $ResourceGroupName -Name $CosmosAccountName
$cosmosSecret = ConvertTo-SecureString -String $cosmosKeys["PrimaryMasterKey"] -AsPlainText -Force
Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "cosmos-key" -SecretValue $cosmosSecret | Out-Null
Write-Output "Cosmos DB key rotated and stored in Key Vault"

# --- Azure OpenAI ---
Write-Output "Rotating Azure OpenAI primary key..."
$null = New-AzCognitiveServicesAccountKey -ResourceGroupName $ResourceGroupName -Name $OpenAIAccountName -KeyName Key1
$openaiKeys = Get-AzCognitiveServicesAccountKey -ResourceGroupName $ResourceGroupName -Name $OpenAIAccountName
$openaiSecret = ConvertTo-SecureString -String $openaiKeys.Key1 -AsPlainText -Force
Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "openai-key" -SecretValue $openaiSecret | Out-Null
Write-Output "Azure OpenAI key rotated and stored in Key Vault"

# --- Azure AI Search ---
Write-Output "Rotating AI Search admin key..."
$null = New-AzSearchAdminKey -ResourceGroupName $ResourceGroupName -ServiceName $SearchServiceName -KeyKind Primary -Force
$searchKeys = Get-AzSearchAdminKeyPair -ResourceGroupName $ResourceGroupName -ServiceName $SearchServiceName
$searchSecret = ConvertTo-SecureString -String $searchKeys.Primary -AsPlainText -Force
Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "search-key" -SecretValue $searchSecret | Out-Null
Write-Output "AI Search key rotated and stored in Key Vault"

Write-Output "Key rotation complete — all secrets updated in Key Vault '$KeyVaultName'"
