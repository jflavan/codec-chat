/// DNS A record pointing to a static IP address.
/// The parent DNS zone must already exist in the same resource group.
param zoneName string
param recordName string
param ipAddress string

resource zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: zoneName
}

resource record 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  parent: zone
  name: recordName
  properties: {
    TTL: 300
    ARecords: [
      { ipv4Address: ipAddress }
    ]
  }
}
