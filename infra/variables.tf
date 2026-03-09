variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "resource_group_name" {
  description = "Existing resource group"
  type        = string
  default     = "ailab-rg"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus2"
}

variable "function_app_name" {
  description = "Name for the Function App"
  type        = string
  default     = "cimmeria-mcp"
}

variable "storage_account_name" {
  description = "Existing storage account name"
  type        = string
  default     = "ailabstoragesc"
}

variable "openai_endpoint" {
  description = "Azure OpenAI endpoint URL"
  type        = string
  sensitive   = true
}

variable "openai_key" {
  description = "Azure OpenAI API key"
  type        = string
  sensitive   = true
}

variable "search_endpoint" {
  description = "Azure AI Search endpoint URL"
  type        = string
  sensitive   = true
}

variable "search_key" {
  description = "Azure AI Search API key"
  type        = string
  sensitive   = true
}
