# =============================================================================
# Resource Group + Storage (existing for production, created for tests)
# =============================================================================

resource "azurerm_resource_group" "rg" {
  count    = var.create_resource_group ? 1 : 0
  name     = var.resource_group_name
  location = var.location
}

data "azurerm_resource_group" "rg" {
  count = var.create_resource_group ? 0 : 1
  name  = var.resource_group_name
}

resource "azurerm_storage_account" "storage" {
  count                    = var.create_resource_group ? 1 : 0
  name                     = var.storage_account_name
  resource_group_name      = local.rg_name
  location                 = local.rg_location
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

data "azurerm_storage_account" "storage" {
  count               = var.create_resource_group ? 0 : 1
  name                = var.storage_account_name
  resource_group_name = var.resource_group_name
}

locals {
  rg_name            = var.create_resource_group ? azurerm_resource_group.rg[0].name : data.azurerm_resource_group.rg[0].name
  rg_location        = var.create_resource_group ? azurerm_resource_group.rg[0].location : data.azurerm_resource_group.rg[0].location
  storage_name       = var.create_resource_group ? azurerm_storage_account.storage[0].name : data.azurerm_storage_account.storage[0].name
  storage_access_key = var.create_resource_group ? azurerm_storage_account.storage[0].primary_access_key : data.azurerm_storage_account.storage[0].primary_access_key
}

# =============================================================================
# Cosmos DB
# =============================================================================

resource "azurerm_cosmosdb_account" "cosmos" {
  name                = var.cosmos_account_name
  location            = local.rg_location
  resource_group_name = local.rg_name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"
  free_tier_enabled   = var.cosmos_free_tier

  automatic_failover_enabled = true

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = var.compute_location
    failover_priority = 0
  }

  capabilities {
    name = "EnableNoSQLVectorSearch"
  }
}

resource "azurerm_cosmosdb_sql_database" "db" {
  name                = "cimmeria"
  resource_group_name = local.rg_name
  account_name        = azurerm_cosmosdb_account.cosmos.name
}

resource "azurerm_cosmosdb_sql_container" "code_chunks" {
  name                  = "code-chunks"
  resource_group_name   = local.rg_name
  account_name          = azurerm_cosmosdb_account.cosmos.name
  database_name         = azurerm_cosmosdb_sql_database.db.name
  partition_key_paths   = ["/source_project"]
  partition_key_version = 2
  throughput            = 400

  indexing_policy {
    indexing_mode = "consistent"

    included_path {
      path = "/*"
    }

    excluded_path {
      path = "/embedding/*"
    }

    excluded_path {
      path = "/_etag/?"
    }
  }

  # Vector embedding policy is managed outside Terraform (ARM API / az CLI)
  # since azurerm provider doesn't natively support vectorEmbeddingPolicy.
  lifecycle {
    ignore_changes = [indexing_policy]
  }
}

resource "azurerm_cosmosdb_sql_container" "knowledge_graph" {
  name                  = "knowledge-graph"
  resource_group_name   = local.rg_name
  account_name          = azurerm_cosmosdb_account.cosmos.name
  database_name         = azurerm_cosmosdb_sql_database.db.name
  partition_key_paths   = ["/pk"]
  partition_key_version = 2
  throughput            = 400
}

# =============================================================================
# Azure OpenAI
# =============================================================================

resource "azurerm_cognitive_account" "openai" {
  name                  = var.openai_account_name
  location              = var.location
  resource_group_name   = local.rg_name
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = var.openai_account_name
}

resource "azurerm_cognitive_deployment" "embedding" {
  name                 = "text-embedding-3-small"
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "text-embedding-3-small"
    version = "1"
  }

  sku {
    name     = "Standard"
    capacity = 120
  }

  lifecycle {
    ignore_changes = [sku[0].capacity]
  }
}

resource "azurerm_cognitive_deployment" "gpt_4o_mini" {
  name                 = "gpt-4o-mini"
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "gpt-4o-mini"
    version = "2024-07-18"
  }

  sku {
    name     = "Standard"
    capacity = 30
  }

  lifecycle {
    ignore_changes = [sku[0].capacity]
  }

  depends_on = [azurerm_cognitive_deployment.embedding]
}

resource "azurerm_cognitive_deployment" "gpt_4o" {
  name                 = "gpt-4o"
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "gpt-4o"
    version = "2024-11-20"
  }

  sku {
    name     = "Standard"
    capacity = 30
  }

  lifecycle {
    ignore_changes = [sku[0].capacity]
  }

  depends_on = [azurerm_cognitive_deployment.gpt_4o_mini]
}

resource "azurerm_cognitive_deployment" "gpt_4_1" {
  name                 = "gpt-4-1"
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "gpt-4.1"
    version = "2025-04-14"
  }

  sku {
    name     = "Standard"
    capacity = 30
  }

  lifecycle {
    ignore_changes = [sku[0].capacity, rai_policy_name]
  }

  depends_on = [azurerm_cognitive_deployment.gpt_4o]
}

resource "azurerm_cognitive_deployment" "gpt_5_4" {
  name                 = "gpt-5-4"
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "gpt-5.4"
    version = "2026-03-05"
  }

  sku {
    name     = "Standard"
    capacity = 1
  }

  depends_on = [azurerm_cognitive_deployment.gpt_4_1]
}

# =============================================================================
# Azure AI Search (skipped in test deployments)
# =============================================================================

resource "azurerm_search_service" "search" {
  count               = var.deploy_search ? 1 : 0
  name                = var.search_service_name
  location            = var.location
  resource_group_name = local.rg_name
  sku                 = "free"
}

# =============================================================================
# Service Plan + Function App
# =============================================================================

resource "azurerm_service_plan" "plan" {
  name                = var.service_plan_name
  location            = var.compute_location
  resource_group_name = local.rg_name
  os_type             = "Windows"
  sku_name            = "Y1"
}

resource "azurerm_windows_function_app" "func" {
  name                                          = var.function_app_name
  location                                      = var.compute_location
  resource_group_name                           = local.rg_name
  service_plan_id                               = azurerm_service_plan.plan.id
  storage_account_name                          = local.storage_name
  storage_account_access_key                    = local.storage_access_key
  builtin_logging_enabled                       = false
  client_certificate_mode                       = "Required"
  ftp_publish_basic_authentication_enabled      = false
  webdeploy_publish_basic_authentication_enabled = false

  site_config {
    ftps_state        = "FtpsOnly"
    http2_enabled     = true
    use_32_bit_worker = false

    application_stack {
      dotnet_version              = "v10.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = merge(
    {
      "OPENAI_ENDPOINT"                        = azurerm_cognitive_account.openai.endpoint
      "OPENAI_KEY"                             = azurerm_cognitive_account.openai.primary_access_key
      "COSMOS_ENDPOINT"                        = azurerm_cosmosdb_account.cosmos.endpoint
      "COSMOS_KEY"                             = azurerm_cosmosdb_account.cosmos.primary_key
      "WEBSITE_RUN_FROM_PACKAGE"               = "1"
      "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED" = "1"
    },
    var.deploy_search ? {
      "SEARCH_ENDPOINT" = "https://${azurerm_search_service.search[0].name}.search.windows.net"
      "SEARCH_KEY"      = azurerm_search_service.search[0].primary_key
    } : {}
  )

  lifecycle {
    ignore_changes = [
      site_config[0].application_insights_connection_string,
      site_config[0].application_insights_key,
    ]
  }
}
