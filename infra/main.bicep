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

@description('Admin container image (defaults to quickstart placeholder).')
param adminContainerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Custom domain for the web app (e.g., codec-chat.com). Leave empty to skip custom domain binding.')
param webCustomDomain string = ''

@description('Custom domain for the API (e.g., api.codec-chat.com). Leave empty to skip custom domain binding.')
param apiCustomDomain string = ''

@description('Custom domain for the admin app (e.g., admin.codec-chat.com). Leave empty to skip custom domain binding.')
param adminCustomDomain string = ''

@description('Set to true on a second deployment pass to bind managed TLS certificates to custom domains. Requires a prior deployment with this set to false so that hostnames are registered first.')
param bindCertificates bool = false

@description('Deploy the voice VM (mediasoup SFU + coturn). Set to false to skip voice infrastructure.')
param voiceVmEnabled bool = false

@description('SSH public key for the voice VM admin user. Required when voiceVmEnabled is true.')
@secure()
param voiceAdminSshPublicKey string = ''

@description('Source IP or CIDR allowed to SSH into the voice VM. Set to your operator CIDR (e.g. "203.0.113.0/24"). Defaults to empty string; the voice-vm module will use "AzureCloud" (deny internet SSH) when this is empty and voiceVmEnabled is false.')
param voiceSshAllowedSourcePrefix string = ''

@description('GitHub fine-grained PAT with issues:write scope for in-app bug reporting. Leave empty to disable.')
@secure()
param gitHubToken string = ''

@description('HMAC-SHA256 secret for signing locally-issued JWTs (email/password auth). Must be at least 32 characters.')
@secure()
param jwtSecret string

@description('Deploy Azure Cache for Redis for distributed caching and SignalR backplane.')
param redisEnabled bool = true

@description('HMAC-SHA256 shared secret for coturn time-limited credentials. Required when voiceVmEnabled is true.')
@secure()
param voiceTurnSecret string = ''

@description('Shared secret for the SFU internal API. Required when voiceVmEnabled is true.')
@secure()
param voiceSfuInternalKey string = ''

@description('FQDN for the SFU API TLS endpoint (e.g., sfu.codec-chat.com). Required when voiceVmEnabled is true.')
param sfuDomainName string = ''

@description('Email for Let\'s Encrypt certificate notifications. Required when voiceVmEnabled is true.')
param certbotEmail string = ''

@description('Deploy Azure Communication Services for transactional email. Requires the Microsoft.Communication resource provider to be registered on the subscription.')
param emailEnabled bool = true

@description('Sender email address for transactional emails (e.g., noreply@codec.app). Requires a verified Azure Communication Services Email domain.')
param emailSenderAddress string = 'DoNotReply@codec.app'

@description('reCAPTCHA Enterprise API key for bot verification.')
@secure()
param recaptchaSecretKey string = ''

@description('reCAPTCHA v3 site key (public, used in frontend and API).')
param recaptchaSiteKey string = ''

@description('Google Cloud project ID for reCAPTCHA Enterprise.')
param recaptchaProjectId string = ''

@description('VAPID public key for Web Push notifications.')
@secure()
param vapidPublicKey string = ''

@description('VAPID private key for Web Push notifications.')
@secure()
param vapidPrivateKey string = ''

@description('Email address for Azure Monitor alert notifications. Leave empty to skip email alerts.')
param alertEmailAddress string = ''

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
var adminAppName = 'ca-${baseName}-admin'
var redisCacheName = 'redis-${baseName}'
var voiceVmName = 'vm-${baseName}-voice'
var appInsightsName = 'appi-${baseName}'
var communicationServicesName = 'acs-${baseName}'
var actionGroupName = 'ag-${baseName}'

// Use custom domains for URLs when provided, otherwise fall back to default Container Apps domain
var effectiveApiUrl = apiCustomDomain != '' ? 'https://${apiCustomDomain}' : 'https://${apiAppName}.${containerAppsEnv.outputs.defaultDomain}'
var effectiveWebUrl = webCustomDomain != '' ? 'https://${webCustomDomain}' : 'https://${webAppName}.${containerAppsEnv.outputs.defaultDomain}'
var effectiveAdminUrl = adminCustomDomain != '' ? 'https://${adminCustomDomain}' : 'https://${adminAppName}.${containerAppsEnv.outputs.defaultDomain}'

// --- Modules ---

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  params: {
    name: logAnalyticsName
    location: location
  }
}

module appInsights 'modules/application-insights.bicep' = {
  name: 'application-insights'
  params: {
    name: appInsightsName
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
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

// ── Redis Cache (distributed cache + SignalR backplane) ──────────────────────────

module redisCache 'modules/redis-cache.bicep' = if (redisEnabled) {
  name: 'redis-cache'
  params: {
    name: redisCacheName
    location: location
    keyVaultName: keyVault.outputs.name
  }
}

// ── JWT signing secret for email/password auth ───────────────────────────────────

module jwtSecretKv 'modules/key-vault-secret.bicep' = {
  name: 'jwt-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Jwt--Secret'
    secretValue: jwtSecret
  }
}

// ── GitHub PAT for in-app bug reporting ──────────────────────────────────────────

module gitHubTokenSecret 'modules/key-vault-secret.bicep' = if (gitHubToken != '') {
  name: 'github-token-secret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'GitHub--Token'
    secretValue: gitHubToken
  }
}

// ── VAPID keys for Web Push notifications ─────────────────────────────────────

module vapidPublicKeyKv 'modules/key-vault-secret.bicep' = if (vapidPublicKey != '') {
  name: 'vapid-public-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Vapid--PublicKey'
    secretValue: vapidPublicKey
  }
}

module vapidPrivateKeyKv 'modules/key-vault-secret.bicep' = if (vapidPrivateKey != '') {
  name: 'vapid-private-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Vapid--PrivateKey'
    secretValue: vapidPrivateKey
  }
}

// ── reCAPTCHA Enterprise secret key ───────────────────────────────────────────

module recaptchaSecretKv 'modules/key-vault-secret.bicep' = {
  name: 'recaptcha-secret-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Recaptcha--SecretKey'
    secretValue: recaptchaSecretKey
  }
}

// ── Azure Communication Services (transactional email) ────────────────────────

module communicationServices 'modules/communication-services.bicep' = if (emailEnabled) {
  name: 'communication-services'
  params: {
    name: communicationServicesName
    keyVaultName: keyVault.outputs.name
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
    sfuDomainName: sfuDomainName
    certbotEmail: certbotEmail
  }
}

// ── DNS zone + A record for the SFU TLS endpoint ──────────────────────────────
// Extract zone name (e.g., 'codec-chat.com') and record name (e.g., 'sfu') from the FQDN.
var sfuDomainParts = split(sfuDomainName != '' ? sfuDomainName : 'placeholder.invalid', '.')
var sfuDnsZoneName = '${sfuDomainParts[1]}.${sfuDomainParts[2]}'
var sfuDnsRecordName = sfuDomainParts[0]

module sfuDnsZone 'modules/dns-zone.bicep' = if (voiceVmEnabled && sfuDomainName != '') {
  name: 'sfu-dns-zone'
  params: {
    zoneName: sfuDnsZoneName
  }
}

module sfuDnsRecord 'modules/dns-record.bicep' = if (voiceVmEnabled && sfuDomainName != '') {
  name: 'sfu-dns-record'
  dependsOn: [sfuDnsZone]
  params: {
    zoneName: sfuDnsZoneName
    recordName: sfuDnsRecordName
    ipAddress: voiceVm.outputs.publicIpAddress
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

module adminCert 'modules/managed-certificate.bicep' = if (adminCustomDomain != '') {
  name: 'admin-managed-cert'
  dependsOn: [adminApp]
  params: {
    environmentName: containerAppsEnv.outputs.name
    location: location
    domainName: adminCustomDomain
    certificateName: 'cert-admin'
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
var adminCertId = bindCertificates && adminCustomDomain != '' ? '${containerAppsEnv.outputs.id}/managedCertificates/cert-admin' : ''

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
    corsAllowedOrigins: [effectiveWebUrl, effectiveAdminUrl]
    customDomainName: apiCustomDomain
    managedCertificateId: apiCertId
    sfuApiUrl: voiceVm.?outputs.sfuApiUrl ?? ''
    turnServerUrl: voiceVm.?outputs.turnServerUrl ?? ''
    voiceTurnKvUrl: voiceVmEnabled ? '${keyVault.outputs.uri}secrets/Voice--TurnSecret' : ''
    voiceSfuInternalKeyKvUrl: voiceVmEnabled ? '${keyVault.outputs.uri}secrets/Voice--SfuInternalKey' : ''
    jwtSecretKvUrl: '${keyVault.outputs.uri}secrets/Jwt--Secret'
    gitHubTokenKvUrl: gitHubToken != '' ? '${keyVault.outputs.uri}secrets/GitHub--Token' : ''
    redisConnectionStringKvUrl: redisEnabled ? redisCache.outputs.connectionStringSecretUri : ''
    appInsightsConnectionString: appInsights.outputs.connectionString
    emailConnectionStringKvUrl: emailEnabled ? communicationServices.?outputs.connectionStringSecretUri ?? '' : ''
    emailSenderAddress: emailEnabled ? communicationServices.?outputs.senderAddress ?? emailSenderAddress : emailSenderAddress
    frontendBaseUrl: effectiveWebUrl
    recaptchaSiteKey: recaptchaSiteKey
    recaptchaProjectId: recaptchaProjectId
    vapidPublicKeyKvUrl: vapidPublicKey != '' ? '${keyVault.outputs.uri}secrets/Vapid--PublicKey' : ''
    vapidPrivateKeyKvUrl: vapidPrivateKey != '' ? '${keyVault.outputs.uri}secrets/Vapid--PrivateKey' : ''
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
    publicRecaptchaSiteKey: recaptchaSiteKey
    customDomainName: webCustomDomain
    managedCertificateId: webCertId
  }
}

module adminApp 'modules/container-app-admin.bicep' = {
  name: 'container-app-admin'
  params: {
    name: adminAppName
    location: location
    environmentId: containerAppsEnv.outputs.id
    containerRegistryLoginServer: containerRegistry.outputs.loginServer
    containerRegistryName: containerRegistry.outputs.name
    containerImage: adminContainerImage
    publicApiBaseUrl: effectiveApiUrl
    customDomainName: adminCustomDomain
    managedCertificateId: adminCertId
  }
}

// ── Azure Monitor alerts ────────────────────────────────────────────────────────

module monitorActionGroup 'modules/monitor-action-group.bicep' = {
  name: 'monitor-action-group'
  params: {
    name: actionGroupName
    alertEmailAddress: alertEmailAddress
  }
}

module monitorAlerts 'modules/monitor-alerts.bicep' = {
  name: 'monitor-alerts'
  params: {
    location: location
    actionGroupId: monitorActionGroup.outputs.id
    apiContainerAppName: apiAppName
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
    postgresqlServerId: postgresql.outputs.id
    environmentName: environmentName
  }
}

// --- Outputs ---

output apiAppFqdn string = apiApp.outputs.fqdn
output webAppFqdn string = webApp.outputs.fqdn
output adminAppFqdn string = adminApp.outputs.fqdn
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer
output postgresqlFqdn string = postgresql.outputs.fqdn
output storageBlobEndpoint string = storageAccount.outputs.blobEndpoint
output keyVaultUri string = keyVault.outputs.uri
output voiceVmPublicIp string = voiceVm.?outputs.publicIpAddress ?? ''
output voiceVmFqdn string = voiceVm.?outputs.fqdn ?? ''
output sfuApiUrl string = voiceVm.?outputs.sfuApiUrl ?? ''
output turnServerUrl string = voiceVm.?outputs.turnServerUrl ?? ''
