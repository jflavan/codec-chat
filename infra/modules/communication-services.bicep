/// Azure Communication Services for transactional email (verification, etc.).
param name string
param location string = 'global'
param keyVaultName string

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: name
  location: location
  properties: {
    dataLocation: 'United States'
    linkedDomains: [
      emailDomain.id
    ]
  }
}

// Email service for sending transactional emails.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${name}-email'
  location: location
  properties: {
    dataLocation: 'United States'
  }
}

// Azure-managed domain — pre-verified, provides DoNotReply@<guid>.azurecomm.net.
resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: location
  properties: {
    domainManagement: 'AzureManaged'
  }
}

// Store the ACS connection string in Key Vault.
module connectionStringSecret 'key-vault-secret.bicep' = {
  name: '${name}-conn-str-secret'
  params: {
    keyVaultName: keyVaultName
    secretName: 'Email--ConnectionString'
    secretValue: acs.listKeys().primaryConnectionString
  }
}

output id string = acs.id
output name string = acs.name
output connectionStringSecretUri string = connectionStringSecret.outputs.secretUri
output senderAddress string = 'DoNotReply@${emailDomain.properties.fromSenderDomain}'
