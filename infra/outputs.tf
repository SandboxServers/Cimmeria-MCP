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
