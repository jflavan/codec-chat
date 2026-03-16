/// Azure Communication Services for transactional email (verification, etc.).
param name string
param location string = 'global'
param keyVaultName string

@description('Sender address for outbound email (e.g., DoNotReply@<acs-domain> or a custom verified domain).')
param senderAddress string = ''

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: name
  location: location
  properties: {
    dataLocation: 'United States'
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
