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

  tags = {
    project     = "cimmeria-mcp"
    environment = var.create_resource_group ? "test" : "production"
    managed-by  = "terraform"
    purpose     = "mcp-server"
  }
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
  tags                = local.tags

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
  tags                  = local.tags
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
  tags                = local.tags
}

# =============================================================================
# Key Vault (Standard tier — ~$0.03/10K operations for secrets)
# =============================================================================

data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "vault" {
  count                      = var.deploy_showcase ? 1 : 0
  name                       = var.key_vault_name
  location                   = local.rg_location
  resource_group_name        = local.rg_name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  enable_rbac_authorization  = true
  tags                       = local.tags
}

# Deployer (Terraform) gets Key Vault Administrator
resource "azurerm_role_assignment" "deployer_kv_admin" {
  count                = var.deploy_showcase ? 1 : 0
  scope                = azurerm_key_vault.vault[0].id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Store service keys in Key Vault
resource "azurerm_key_vault_secret" "openai_key" {
  count        = var.deploy_showcase ? 1 : 0
  name         = "openai-key"
  value        = azurerm_cognitive_account.openai.primary_access_key
  key_vault_id = azurerm_key_vault.vault[0].id
  depends_on   = [azurerm_role_assignment.deployer_kv_admin]
}

resource "azurerm_key_vault_secret" "cosmos_key" {
  count        = var.deploy_showcase ? 1 : 0
  name         = "cosmos-key"
  value        = azurerm_cosmosdb_account.cosmos.primary_key
  key_vault_id = azurerm_key_vault.vault[0].id
  depends_on   = [azurerm_role_assignment.deployer_kv_admin]
}

resource "azurerm_key_vault_secret" "search_key" {
  count        = var.deploy_search && var.deploy_showcase ? 1 : 0
  name         = "search-key"
  value        = azurerm_search_service.search[0].primary_key
  key_vault_id = azurerm_key_vault.vault[0].id
  depends_on   = [azurerm_role_assignment.deployer_kv_admin]
}

# =============================================================================
# Log Analytics Workspace (5 GB/month free ingestion)
# =============================================================================

resource "azurerm_log_analytics_workspace" "logs" {
  count               = var.deploy_showcase ? 1 : 0
  name                = var.log_analytics_name
  location            = local.rg_location
  resource_group_name = local.rg_name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

# =============================================================================
# Application Insights (5 GB/month free ingestion)
# =============================================================================

resource "azurerm_application_insights" "insights" {
  count               = var.deploy_showcase ? 1 : 0
  name                = var.app_insights_name
  location            = local.rg_location
  resource_group_name = local.rg_name
  workspace_id        = azurerm_log_analytics_workspace.logs[0].id
  application_type    = "web"
  tags                = local.tags
}

# =============================================================================
# App Configuration (Free tier — 10 MB storage, 1,000 requests/day)
# =============================================================================

resource "azurerm_app_configuration" "config" {
  count               = var.deploy_showcase ? 1 : 0
  name                = var.app_config_name
  location            = local.rg_location
  resource_group_name = local.rg_name
  sku                 = "free"
}

resource "azurerm_role_assignment" "deployer_appconfig" {
  count                = var.deploy_showcase ? 1 : 0
  scope                = azurerm_app_configuration.config[0].id
  role_definition_name = "App Configuration Data Owner"
  principal_id         = data.azurerm_client_config.current.object_id
}

# =============================================================================
# Diagnostic Settings (log to Log Analytics)
# =============================================================================

resource "azurerm_monitor_diagnostic_setting" "cosmos_diagnostics" {
  count                      = var.deploy_showcase ? 1 : 0
  name                       = "cosmos-diagnostics"
  target_resource_id         = azurerm_cosmosdb_account.cosmos.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs[0].id

  metric {
    category = "Requests"
    enabled  = true
  }
}

resource "azurerm_monitor_diagnostic_setting" "func_diagnostics" {
  count                      = var.deploy_showcase ? 1 : 0
  name                       = "func-diagnostics"
  target_resource_id         = azurerm_windows_function_app.func.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs[0].id

  enabled_log {
    category = "FunctionAppLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}

# =============================================================================
# Static Web App (Free tier — 100 GB bandwidth/month)
# =============================================================================

resource "azurerm_static_web_app" "site" {
  count               = var.deploy_showcase ? 1 : 0
  name                = var.static_site_name
  location            = local.rg_location
  resource_group_name = local.rg_name
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = local.tags
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
  tags                = local.tags
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
  tags                                           = local.tags

  identity {
    type = "SystemAssigned"
  }

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
      "COSMOS_ENDPOINT"                        = azurerm_cosmosdb_account.cosmos.endpoint
      "WEBSITE_RUN_FROM_PACKAGE"               = "1"
      "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED" = "1"
    },
    # Key Vault references for secrets (when showcase enabled), direct keys otherwise
    var.deploy_showcase ? {
      "OPENAI_KEY" = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.vault[0].name};SecretName=openai-key)"
      "COSMOS_KEY" = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.vault[0].name};SecretName=cosmos-key)"
    } : {
      "OPENAI_KEY" = azurerm_cognitive_account.openai.primary_access_key
      "COSMOS_KEY" = azurerm_cosmosdb_account.cosmos.primary_key
    },
    var.deploy_search ? {
      "SEARCH_ENDPOINT" = "https://${azurerm_search_service.search[0].name}.search.windows.net"
      "SEARCH_KEY"      = var.deploy_showcase ? "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.vault[0].name};SecretName=search-key)" : azurerm_search_service.search[0].primary_key
    } : {},
    # Application Insights (when showcase enabled)
    var.deploy_showcase ? {
      "APPINSIGHTS_INSTRUMENTATIONKEY"       = azurerm_application_insights.insights[0].instrumentation_key
      "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.insights[0].connection_string
    } : {}
  )

  lifecycle {
    ignore_changes = [
      site_config[0].application_insights_connection_string,
      site_config[0].application_insights_key,
    ]
  }
}

# Function App → Key Vault Secrets User (read secrets via Key Vault references)
resource "azurerm_role_assignment" "func_kv_reader" {
  count                = var.deploy_showcase ? 1 : 0
  scope                = azurerm_key_vault.vault[0].id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_windows_function_app.func.identity[0].principal_id
}

# Function App → App Configuration Data Reader
resource "azurerm_role_assignment" "func_appconfig_reader" {
  count                = var.deploy_showcase ? 1 : 0
  scope                = azurerm_app_configuration.config[0].id
  role_definition_name = "App Configuration Data Reader"
  principal_id         = azurerm_windows_function_app.func.identity[0].principal_id
}

# =============================================================================
# Azure Monitor Action Group + Budget Alert (free)
# =============================================================================

resource "azurerm_monitor_action_group" "alerts" {
  count               = var.deploy_showcase && var.alert_email != "" ? 1 : 0
  name                = "cimmeria-mcp-alerts"
  resource_group_name = local.rg_name
  short_name          = "cimmeria"
  tags                = local.tags

  email_receiver {
    name          = "admin"
    email_address = var.alert_email
  }
}

resource "azurerm_consumption_budget_resource_group" "budget" {
  count             = var.deploy_showcase && var.alert_email != "" ? 1 : 0
  name              = "cimmeria-mcp-monthly"
  resource_group_id = var.create_resource_group ? azurerm_resource_group.rg[0].id : data.azurerm_resource_group.rg[0].id
  amount            = 10
  time_grain        = "Monthly"

  time_period {
    start_date = "2026-03-01T00:00:00Z"
  }

  notification {
    operator       = "GreaterThanOrEqualTo"
    threshold      = 80
    threshold_type = "Actual"
    contact_groups = [azurerm_monitor_action_group.alerts[0].id]
  }

  notification {
    operator       = "GreaterThanOrEqualTo"
    threshold      = 100
    threshold_type = "Forecasted"
    contact_groups = [azurerm_monitor_action_group.alerts[0].id]
  }
}
