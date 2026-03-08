/// Azure Cache for Redis — distributed cache and SignalR backplane.
param name string
param location string
param keyVaultName string

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: name
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

// Store connection string in Key Vault.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Redis--ConnectionString'
  properties: {
    value: '${name}.redis.cache.windows.net:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

output id string = redis.id
output hostName string = redis.properties.hostName
output connectionStringSecretUri string = connectionStringSecret.properties.secretUri
