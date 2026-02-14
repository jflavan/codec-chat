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

@description('Custom domain for the web app (e.g., codec-chat.com). Leave empty to skip custom domain binding.')
param webCustomDomain string = ''

@description('Custom domain for the API (e.g., api.codec-chat.com). Leave empty to skip custom domain binding.')
param apiCustomDomain string = ''

@description('Set to true on a second deployment pass to bind managed TLS certificates to custom domains. Requires a prior deployment with this set to false so that hostnames are registered first.')
param bindCertificates bool = false

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

// Use custom domains for URLs when provided, otherwise fall back to default Container Apps domain
var effectiveApiUrl = apiCustomDomain != '' ? 'https://${apiCustomDomain}' : 'https://${apiAppName}.${containerAppsEnv.outputs.defaultDomain}'
var effectiveWebUrl = webCustomDomain != '' ? 'https://${webCustomDomain}' : 'https://${webAppName}.${containerAppsEnv.outputs.defaultDomain}'

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

// Managed TLS certificates for custom domains.
// These must deploy AFTER the container apps so the hostnames are already registered.
module apiCert 'modules/managed-certificate.bicep' = if (apiCustomDomain != '') {
  name: 'api-managed-cert'
  dependsOn: [apiApp]
  params: {
    environmentName: containerAppsEnv.outputs.name
    location: location
    domainName: apiCustomDomain
    certificateName: 'cert-api'
  }
}

module webCert 'modules/managed-certificate.bicep' = if (webCustomDomain != '') {
  name: 'web-managed-cert'
  dependsOn: [webApp]
  params: {
    environmentName: containerAppsEnv.outputs.name
    location: location
    domainName: webCustomDomain
    certificateName: 'cert-web'
  }
}

// Compute cert resource IDs deterministically to avoid implicit dependency from app → cert
// (which would create a circular dependency with the cert → app dependsOn above).
var apiCertId = bindCertificates && apiCustomDomain != '' ? '${containerAppsEnv.outputs.id}/managedCertificates/cert-api' : ''
var webCertId = bindCertificates && webCustomDomain != '' ? '${containerAppsEnv.outputs.id}/managedCertificates/cert-web' : ''

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
    apiBaseUrl: effectiveApiUrl
    corsAllowedOrigins: effectiveWebUrl
    customDomainName: apiCustomDomain
    managedCertificateId: apiCertId
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
    publicApiBaseUrl: effectiveApiUrl
    publicGoogleClientId: googleClientId
    customDomainName: webCustomDomain
    managedCertificateId: webCertId
  }
}

// --- Outputs ---

output apiAppFqdn string = apiApp.outputs.fqdn
output webAppFqdn string = webApp.outputs.fqdn
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer
output postgresqlFqdn string = postgresql.outputs.fqdn
output storageBlobEndpoint string = storageAccount.outputs.blobEndpoint
output keyVaultUri string = keyVault.outputs.uri
