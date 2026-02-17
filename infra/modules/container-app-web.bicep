/// Web Container App running SvelteKit with adapter-node.
param name string
param location string
param environmentId string
param containerRegistryLoginServer string
param containerRegistryName string
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

param publicApiBaseUrl string
param publicGoogleClientId string

@description('Custom domain name for the web app (e.g., codec-chat.com). Leave empty to skip.')
param customDomainName string = ''

@description('Resource ID of the managed certificate for the custom domain.')
param managedCertificateId string = ''

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: containerRegistryName
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
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
        targetPort: 3000
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
      }
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
          name: 'web'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'NODE_ENV'
              value: 'production'
            }
            {
              name: 'PUBLIC_API_BASE_URL'
              value: publicApiBaseUrl
            }
            {
              name: 'PUBLIC_GOOGLE_CLIENT_ID'
              value: publicGoogleClientId
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 3000
                scheme: 'HTTP'
              }
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

// AcrPull role assignment on Container Registry
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, webApp.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: containerRegistry
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

output id string = webApp.id
output name string = webApp.name
output fqdn string = webApp.properties.configuration.ingress.fqdn
output principalId string = webApp.identity.principalId
