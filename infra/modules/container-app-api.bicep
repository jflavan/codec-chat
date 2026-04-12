/// API Container App running ASP.NET Core with SignalR.
param name string
param location string
param environmentId string
param containerRegistryLoginServer string
param containerRegistryName string
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

param keyVaultName string
param keyVaultUri string
param storageAccountName string
param storageBlobEndpoint string

@description('Allowed CORS origins (e.g., [https://codec-chat.com, https://admin.codec-chat.com]).')
param corsAllowedOrigins array
param apiBaseUrl string

@description('LiveKit server URL (e.g., ws://<voice-vm-ip>:7880).')
param livekitServerUrl string = ''

@description('Key Vault secret URL for the LiveKit API key. Leave empty if voice is not enabled.')
param livekitApiKeyKvUrl string = ''

@description('Key Vault secret URL for the LiveKit API secret. Leave empty if voice is not enabled.')
@secure()
param livekitApiSecretKvUrl string = ''

@description('Key Vault secret URL for the JWT signing secret (email/password auth).')
@secure()
param jwtSecretKvUrl string

@description('Key Vault secret URL for the GitHub PAT. Leave empty to disable in-app bug reporting.')
param gitHubTokenKvUrl string = ''

@description('Key Vault secret URL for the Redis connection string. Leave empty to disable Redis caching and SignalR backplane.')
param redisConnectionStringKvUrl string = ''

@description('Application Insights connection string for OpenTelemetry export. Leave empty to disable. Passed as a plain value (not Key Vault) because the ingestion key is write-only and not a security-sensitive credential.')
param appInsightsConnectionString string = ''

@description('Key Vault secret URL for the Azure Communication Services connection string (email sending).')
param emailConnectionStringKvUrl string = ''

@description('Sender email address for transactional emails.')
param emailSenderAddress string = ''

@description('Frontend base URL for email verification links.')
param frontendBaseUrl string = ''

@description('reCAPTCHA v3 site key')
param recaptchaSiteKey string = ''

@description('Google Cloud project ID for reCAPTCHA Enterprise')
param recaptchaProjectId string = ''

@description('Key Vault secret URL for the VAPID public key (Web Push). Leave empty to disable push notifications.')
param vapidPublicKeyKvUrl string = ''

@description('Key Vault secret URL for the VAPID private key (Web Push). Leave empty to disable push notifications.')
param vapidPrivateKeyKvUrl string = ''

@description('Custom domain name for the API (e.g., api.codec-chat.com). Leave empty to skip.')
param customDomainName string = ''

@description('Resource ID of the managed certificate for the custom domain.')
param managedCertificateId string = ''

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: containerRegistryName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

// .NET configuration binds indexed env vars (Cors__AllowedOrigins__0, __1, ...) to string[]
var corsEnvVars = [ for (origin, i) in corsAllowedOrigins: {
  name: 'Cors__AllowedOrigins__${i}'
  value: origin
}]

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        customDomains: customDomainName != '' && managedCertificateId != '' ? [
          {
            name: customDomainName
            certificateId: managedCertificateId
            bindingType: 'SniEnabled'
          }
        ] : customDomainName != '' ? [
          {
            name: customDomainName
            bindingType: 'Disabled'
          }
        ] : []
        corsPolicy: {
          allowedOrigins: corsAllowedOrigins
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS']
          allowedHeaders: ['*']
          allowCredentials: true
        }
      }
      secrets: concat([
        {
          name: 'connection-string'
          keyVaultUrl: '${keyVaultUri}secrets/ConnectionStrings--Default'
          identity: 'system'
        }
        {
          name: 'google-client-id'
          keyVaultUrl: '${keyVaultUri}secrets/Google--ClientId'
          identity: 'system'
        }
        {
          name: 'global-admin-email'
          keyVaultUrl: '${keyVaultUri}secrets/GlobalAdmin--Email'
          identity: 'system'
        }
        {
          name: 'jwt-secret'
          keyVaultUrl: jwtSecretKvUrl
          identity: 'system'
        }
        // Transitional: old revisions still reference these deleted mediasoup secrets.
        // Keep them with dummy values until all old revisions are deactivated, then remove.
        {
          name: 'voice-turn-secret'
          value: 'deprecated'
        }
        {
          name: 'voice-sfu-internal-key'
          value: 'deprecated'
        }
      ], livekitApiKeyKvUrl != '' ? [
        {
          name: 'livekit-api-key'
          keyVaultUrl: livekitApiKeyKvUrl
          identity: 'system'
        }
      ] : [], livekitApiSecretKvUrl != '' ? [
        {
          name: 'livekit-api-secret'
          keyVaultUrl: livekitApiSecretKvUrl
          identity: 'system'
        }
      ] : [], gitHubTokenKvUrl != '' ? [
        {
          name: 'github-token'
          keyVaultUrl: gitHubTokenKvUrl
          identity: 'system'
        }
      ] : [], redisConnectionStringKvUrl != '' ? [
        {
          name: 'redis-connection-string'
          keyVaultUrl: redisConnectionStringKvUrl
          identity: 'system'
        }
      ] : [], emailConnectionStringKvUrl != '' ? [
        {
          name: 'email-connection-string'
          keyVaultUrl: emailConnectionStringKvUrl
          identity: 'system'
        }
      ] : [], vapidPublicKeyKvUrl != '' ? [
        {
          name: 'vapid-public-key'
          keyVaultUrl: vapidPublicKeyKvUrl
          identity: 'system'
        }
      ] : [], vapidPrivateKeyKvUrl != '' ? [
        {
          name: 'vapid-private-key'
          keyVaultUrl: vapidPrivateKeyKvUrl
          identity: 'system'
        }
      ] : [], [
        {
          name: 'recaptcha-secret-key'
          keyVaultUrl: '${keyVaultUri}secrets/Recaptcha--SecretKey'
          identity: 'system'
        }
      ])
      registries: [
        {
          server: containerRegistryLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connection-string'
            }
            {
              name: 'Google__ClientId'
              secretRef: 'google-client-id'
            }
            {
              name: 'Api__BaseUrl'
              value: apiBaseUrl
            }
          ], corsEnvVars, [
            {
              name: 'Storage__Provider'
              value: 'AzureBlob'
            }
            {
              name: 'Storage__AzureBlob__ServiceUri'
              value: storageBlobEndpoint
            }
            {
              name: 'GlobalAdmin__Email'
              secretRef: 'global-admin-email'
            }
            {
              name: 'Jwt__Secret'
              secretRef: 'jwt-secret'
            }
            {
              name: 'OTEL_SERVICE_NAME'
              value: 'codec-api'
            }
          ], livekitServerUrl != '' ? [
            {
              name: 'LiveKit__ServerUrl'
              value: livekitServerUrl
            }
          ] : [], livekitApiKeyKvUrl != '' ? [
            {
              name: 'LiveKit__ApiKey'
              secretRef: 'livekit-api-key'
            }
          ] : [], livekitApiSecretKvUrl != '' ? [
            {
              name: 'LiveKit__ApiSecret'
              secretRef: 'livekit-api-secret'
            }
          ] : [], gitHubTokenKvUrl != '' ? [
            {
              name: 'GitHub__Token'
              secretRef: 'github-token'
            }
          ] : [], redisConnectionStringKvUrl != '' ? [
            {
              name: 'Redis__ConnectionString'
              secretRef: 'redis-connection-string'
            }
          ] : [], appInsightsConnectionString != '' ? [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ] : [], emailConnectionStringKvUrl != '' ? [
            {
              name: 'Email__ConnectionString'
              secretRef: 'email-connection-string'
            }
          ] : [], emailSenderAddress != '' ? [
            {
              name: 'Email__SenderAddress'
              value: emailSenderAddress
            }
          ] : [], frontendBaseUrl != '' ? [
            {
              name: 'Frontend__BaseUrl'
              value: frontendBaseUrl
            }
          ] : [], vapidPublicKeyKvUrl != '' ? [
            {
              name: 'Vapid__PublicKey'
              secretRef: 'vapid-public-key'
            }
          ] : [], vapidPrivateKeyKvUrl != '' ? [
            {
              name: 'Vapid__PrivateKey'
              secretRef: 'vapid-private-key'
            }
            {
              name: 'Vapid__Subject'
              value: 'mailto:noreply@codec.chat'
            }
          ] : [], [
            {
              name: 'Recaptcha__SecretKey'
              secretRef: 'recaptcha-secret-key'
            }
            {
              name: 'Recaptcha__SiteKey'
              value: recaptchaSiteKey
            }
            {
              name: 'Recaptcha__ProjectId'
              value: recaptchaProjectId
            }
            {
              name: 'Recaptcha__Enabled'
              value: recaptchaSiteKey != '' ? 'true' : 'false'
            }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              periodSeconds: 10
              timeoutSeconds: 5
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// AcrPull role assignment on Container Registry
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, apiApp.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: containerRegistry
  properties: {
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// Storage Blob Data Contributor role assignment on Storage Account
resource storageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, apiApp.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: storageAccount
  properties: {
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

// Key Vault Secrets User role assignment on Key Vault
resource keyVaultSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

output id string = apiApp.id
output name string = apiApp.name
output fqdn string = apiApp.properties.configuration.ingress.fqdn
output principalId string = apiApp.identity.principalId
