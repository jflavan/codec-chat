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

@description('Email address of the global admin user.')
@secure()
param globalAdminEmail string

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

@description('Deploy the voice VM (mediasoup SFU + coturn). Set to false to skip voice infrastructure.')
param voiceVmEnabled bool = false

@description('SSH public key for the voice VM admin user. Required when voiceVmEnabled is true.')
@secure()
param voiceAdminSshPublicKey string = ''

@description('Source IP or CIDR allowed to SSH into the voice VM. Set to your operator CIDR (e.g. "203.0.113.0/24"). Defaults to empty string; the voice-vm module will use "AzureCloud" (deny internet SSH) when this is empty and voiceVmEnabled is false.')
param voiceSshAllowedSourcePrefix string = ''

@description('HMAC-SHA256 shared secret for coturn time-limited credentials. Required when voiceVmEnabled is true.')
@secure()
param voiceTurnSecret string = ''

@description('Shared secret for the SFU internal API. Required when voiceVmEnabled is true.')
@secure()
param voiceSfuInternalKey string = ''

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
var voiceVmName = 'vm-${baseName}-voice'

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

// Store Global Admin Email in Key Vault
module globalAdminEmailSecret 'modules/key-vault-secret.bicep' = {
  name: 'global-admin-email-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'GlobalAdmin--Email'
    secretValue: globalAdminEmail
  }
}

// ── Voice VM (mediasoup SFU + coturn) ────────────────────────────────────────────
// Deployed only when voiceVmEnabled = true. Both services require UDP port exposure
// that Azure Container Apps cannot provide, so they run on a dedicated VM instead.

module voiceVm 'modules/voice-vm.bicep' = if (voiceVmEnabled) {
  name: 'voice-vm'
  params: {
    name: voiceVmName
    location: location
    adminSshPublicKey: voiceAdminSshPublicKey
    containerRegistryName: containerRegistryName
    sshAllowedSourcePrefix: voiceSshAllowedSourcePrefix
  }
}

// Store the TURN secret in Key Vault so the API Container App can reference it securely.
module voiceTurnSecretKv 'modules/key-vault-secret.bicep' = if (voiceVmEnabled) {
  name: 'voice-turn-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Voice--TurnSecret'
    secretValue: voiceTurnSecret
  }
}

// Store the SFU internal API key in Key Vault.
module voiceSfuInternalKeyKv 'modules/key-vault-secret.bicep' = if (voiceVmEnabled) {
  name: 'voice-sfu-internal-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Voice--SfuInternalKey'
    secretValue: voiceSfuInternalKey
  }
}

// ── Managed TLS certificates for custom domains ───────────────────────────────────
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
    sfuApiUrl: voiceVm.?outputs.sfuApiUrl ?? ''
    turnServerUrl: voiceVm.?outputs.turnServerUrl ?? ''
    voiceTurnKvUrl: voiceVmEnabled ? '${keyVault.outputs.uri}secrets/Voice--TurnSecret' : ''
    voiceSfuInternalKeyKvUrl: voiceVmEnabled ? '${keyVault.outputs.uri}secrets/Voice--SfuInternalKey' : ''
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
output voiceVmPublicIp string = voiceVm.?outputs.publicIpAddress ?? ''
output voiceVmFqdn string = voiceVm.?outputs.fqdn ?? ''
output sfuApiUrl string = voiceVm.?outputs.sfuApiUrl ?? ''
output turnServerUrl string = voiceVm.?outputs.turnServerUrl ?? ''
