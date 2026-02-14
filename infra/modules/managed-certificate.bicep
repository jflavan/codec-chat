/// Managed TLS certificate for a custom domain on Azure Container Apps.
param environmentName string
param location string
param domainName string
param certificateName string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

resource managedCert 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = {
  parent: environment
  name: certificateName
  location: location
  properties: {
    subjectName: domainName
    domainControlValidation: 'TXT'
  }
}

output id string = managedCert.id
