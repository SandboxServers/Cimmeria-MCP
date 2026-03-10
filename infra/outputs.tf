output "function_app_name" {
  value = azurerm_windows_function_app.func.name
}

output "function_app_url" {
  value = "https://${azurerm_windows_function_app.func.default_hostname}"
}

output "mcp_endpoint" {
  value = "https://${azurerm_windows_function_app.func.default_hostname}/runtime/webhooks/mcp"
}

output "openai_endpoint" {
  value     = azurerm_cognitive_account.openai.endpoint
  sensitive = true
}

output "cosmos_endpoint" {
  value     = azurerm_cosmosdb_account.cosmos.endpoint
  sensitive = true
}

output "search_endpoint" {
  value = var.deploy_search ? "https://${azurerm_search_service.search[0].name}.search.windows.net" : ""
}

output "key_vault_uri" {
  value = var.deploy_showcase ? azurerm_key_vault.vault[0].vault_uri : ""
}

output "app_config_endpoint" {
  value = var.deploy_showcase ? azurerm_app_configuration.config[0].endpoint : ""
}

output "app_insights_connection_string" {
  value     = var.deploy_showcase ? azurerm_application_insights.insights[0].connection_string : ""
  sensitive = true
}

output "log_analytics_workspace_id" {
  value = var.deploy_showcase ? azurerm_log_analytics_workspace.logs[0].workspace_id : ""
}

output "function_app_principal_id" {
  value       = azurerm_windows_function_app.func.identity[0].principal_id
  description = "System-assigned managed identity principal ID"
}

output "static_site_url" {
  value = var.deploy_showcase ? "https://${azurerm_static_web_app.site[0].default_host_name}" : ""
}
