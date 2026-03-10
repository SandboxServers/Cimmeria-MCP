variables {
  subscription_id       = "334a971b-d8c4-4bc9-adcc-db758b9d6e55"
  resource_group_name   = "cimmeria-mcp-test-tf"
  location              = "eastus"
  compute_location      = "eastus2"
  function_app_name     = "cimmeria-mcp-tf"
  service_plan_name     = "cimmeria-mcp-tf-plan"
  storage_account_name  = "cimmeriamcptf"
  openai_account_name   = "cimmeria-openai-tf"
  cosmos_account_name   = "cimmeria-cosmos-tf"
  search_service_name   = "cimmeria-search-tf"
  deploy_search         = false
  create_resource_group = true
  cosmos_free_tier      = false
  key_vault_name        = "cimmeria-tf-kv"
  log_analytics_name    = "cimmeria-tf-logs"
  app_insights_name     = "cimmeria-tf-insights"
  app_config_name       = "cimmeria-tf-config"
  static_site_name      = "cimmeria-tf-site"
  deploy_showcase       = true
}

run "validate_plan" {
  command = plan

  assert {
    condition     = azurerm_cosmosdb_account.cosmos.name == "cimmeria-cosmos-tf"
    error_message = "Cosmos DB account name mismatch"
  }

  assert {
    condition     = azurerm_cognitive_account.openai.name == "cimmeria-openai-tf"
    error_message = "OpenAI account name mismatch"
  }

  assert {
    condition     = azurerm_service_plan.plan.sku_name == "Y1"
    error_message = "Service plan should use Y1 (Consumption)"
  }
}

run "deploy_all_resources" {
  command = apply

  assert {
    condition     = output.mcp_endpoint != ""
    error_message = "MCP endpoint should not be empty"
  }

  assert {
    condition     = output.openai_endpoint != ""
    error_message = "OpenAI endpoint should not be empty"
  }

  assert {
    condition     = output.cosmos_endpoint != ""
    error_message = "Cosmos endpoint should not be empty"
  }

  assert {
    condition     = output.search_endpoint == ""
    error_message = "Search endpoint should be empty when deploy_search=false"
  }

  assert {
    condition     = output.key_vault_uri != ""
    error_message = "Key Vault URI should not be empty when deploy_showcase=true"
  }

  assert {
    condition     = output.app_config_endpoint != ""
    error_message = "App Config endpoint should not be empty when deploy_showcase=true"
  }

  assert {
    condition     = output.function_app_principal_id != ""
    error_message = "Function App should have a managed identity"
  }
}
