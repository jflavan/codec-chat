/// Azure DNS zone for managing domain records.
param zoneName string
param location string = 'global'

resource zone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: zoneName
  location: location
}

output name string = zone.name
