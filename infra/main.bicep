/// Codec Chat — Main Bicep orchestrator.
/// Deploys all Azure resources for the Codec chat application.
targetScope = 'resourceGroup'

// --- Parameters ---

@description('Azure region for all resources.')
param location string = 'centralus'

@description('Environment name used in resource naming (e.g., prod, staging).')
param environmentName string = 'prod'

@description('Google OAuth Client ID for authentication.')
@secure()
param googleClientId string

@description('PostgreSQL administrator password.')
@secure()
param postgresqlAdminPassword string

@description('API container image (defaults to quickstart placeholder).')
param apiContainerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Web container image (defaults to quickstart placeholder).')
param webContainerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

// --- Naming Convention ---
// {abbreviation}-codec-{env} (hyphens removed for resources that don't allow them)

var baseName = 'codec-${environmentName}'

var logAnalyticsName = 'log-${baseName}'
var containerRegistryName = replace('acr${baseName}', '-', '')
var postgresqlName = 'psql-${baseName}'
var storageAccountName = replace('st${baseName}', '-', '')
var keyVaultName = 'kv-${baseName}'
var containerAppsEnvName = 'cae-${baseName}'
var apiAppName = 'ca-${baseName}-api'
var webAppName = 'ca-${baseName}-web'

// --- Modules ---

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  params: {
    name: logAnalyticsName
    location: location
  }
}

module containerRegistry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  params: {
    name: containerRegistryName
    location: location
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    name: keyVaultName
    location: location
  }
}

module postgresql 'modules/postgresql.bicep' = {
  name: 'postgresql'
  params: {
    name: postgresqlName
    location: location
    administratorPassword: postgresqlAdminPassword
    keyVaultName: keyVault.outputs.name
  }
}

module storageAccount 'modules/storage-account.bicep' = {
  name: 'storage-account'
  params: {
    name: storageAccountName
    location: location
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'container-apps-env'
  params: {
    name: containerAppsEnvName
    location: location
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsWorkspaceName: logAnalyticsName
  }
}

// Store Google Client ID in Key Vault
module googleClientIdSecret 'modules/key-vault-secret.bicep' = {
  name: 'google-client-id-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Google--ClientId'
    secretValue: googleClientId
  }
}

module apiApp 'modules/container-app-api.bicep' = {
  name: 'container-app-api'
  params: {
    name: apiAppName
    location: location
    environmentId: containerAppsEnv.outputs.id
    containerRegistryLoginServer: containerRegistry.outputs.loginServer
    containerRegistryName: containerRegistry.outputs.name
    containerImage: apiContainerImage
    keyVaultName: keyVault.outputs.name
    keyVaultUri: keyVault.outputs.uri
    storageAccountName: storageAccount.outputs.name
    storageBlobEndpoint: storageAccount.outputs.blobEndpoint
    apiBaseUrl: 'https://${apiAppName}.${containerAppsEnv.outputs.defaultDomain}'
    corsAllowedOrigins: 'https://${webAppName}.${containerAppsEnv.outputs.defaultDomain}'
  }
}

module webApp 'modules/container-app-web.bicep' = {
  name: 'container-app-web'
  params: {
    name: webAppName
    location: location
    environmentId: containerAppsEnv.outputs.id
    containerRegistryLoginServer: containerRegistry.outputs.loginServer
    containerRegistryName: containerRegistry.outputs.name
    containerImage: webContainerImage
    publicApiBaseUrl: 'https://${apiAppName}.${containerAppsEnv.outputs.defaultDomain}'
    publicGoogleClientId: googleClientId
  }
}

// --- Outputs ---

output apiAppFqdn string = apiApp.outputs.fqdn
output webAppFqdn string = webApp.outputs.fqdn
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer
output postgresqlFqdn string = postgresql.outputs.fqdn
output storageBlobEndpoint string = storageAccount.outputs.blobEndpoint
output keyVaultUri string = keyVault.outputs.uri
