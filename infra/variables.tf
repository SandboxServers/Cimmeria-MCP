variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
  default     = "ailab-rg"
}

variable "location" {
  description = "Primary Azure region (OpenAI, AI Search, Service Plan, Function App)"
  type        = string
  default     = "eastus"
}

variable "compute_location" {
  description = "Azure region for Cosmos DB, Service Plan, and Function App"
  type        = string
  default     = "eastus2"
}

variable "service_plan_name" {
  description = "App Service Plan name"
  type        = string
  default     = "EastUS2Plan"
}

variable "function_app_name" {
  description = "Name for the Function App"
  type        = string
  default     = "cimmeria-mcp"
}

variable "storage_account_name" {
  description = "Storage account name"
  type        = string
  default     = "ailabstoragesc"
}

variable "openai_account_name" {
  description = "Azure OpenAI account name"
  type        = string
  default     = "ailab-openai-cady"
}

variable "cosmos_account_name" {
  description = "Cosmos DB account name"
  type        = string
  default     = "cimmeria-cosmos"
}

variable "search_service_name" {
  description = "Azure AI Search service name"
  type        = string
  default     = "ailab-search-sc"
}

variable "deploy_search" {
  description = "Deploy Azure AI Search (false for testing — free tier limited to 1 per subscription)"
  type        = bool
  default     = true
}

variable "create_resource_group" {
  description = "Create the resource group and storage account (true for test deployments)"
  type        = bool
  default     = false
}

variable "cosmos_free_tier" {
  description = "Enable Cosmos DB free tier (limited to 1 per subscription)"
  type        = bool
  default     = false
}
