output "function_app_name" {
  value = azurerm_windows_function_app.func.name
}

output "function_app_url" {
  value = "https://${azurerm_windows_function_app.func.default_hostname}"
}

output "mcp_endpoint" {
  value = "https://${azurerm_windows_function_app.func.default_hostname}/runtime/webhooks/mcp"
}
