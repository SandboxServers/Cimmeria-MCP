variables {
  subscription_id       = "334a971b-d8c4-4bc9-adcc-db758b9d6e55"
  resource_group_name   = "cimmeria-mcp-test-tf"
  location              = "eastus"
  cosmos_location       = "eastus2"
  function_app_name     = "cimmeria-mcp-tf"
  storage_account_name  = "cimmeriamcptf"
  openai_account_name   = "cimmeria-openai-tf"
  cosmos_account_name   = "cimmeria-cosmos-tf"
  search_service_name   = "cimmeria-search-tf"
  deploy_search         = false
  create_resource_group = true
  cosmos_free_tier      = false
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
}
