/// API Container App running ASP.NET Core with SignalR.
param name string
param location string
param environmentId string
param containerRegistryLoginServer string
param containerRegistryName string
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

var isQuickstart = containerImage == 'mcr.microsoft.com/k8se/quickstart:latest'

param keyVaultName string
param keyVaultUri string
param storageAccountName string
param storageBlobEndpoint string

param corsAllowedOrigins string
param apiBaseUrl string

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

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: isQuickstart ? 80 : 8080
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
          allowedOrigins: [corsAllowedOrigins]
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS']
          allowedHeaders: ['*']
          allowCredentials: true
        }
      }
      secrets: isQuickstart ? [] : [
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
      ]
      registries: isQuickstart ? [] : [
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
          env: isQuickstart ? [] : [
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
            {
              name: 'Cors__AllowedOrigins'
              value: corsAllowedOrigins
            }
            {
              name: 'Storage__Provider'
              value: 'AzureBlob'
            }
            {
              name: 'Storage__AzureBlob__ServiceUri'
              value: storageBlobEndpoint
            }
          ]
          probes: isQuickstart ? [] : [
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
