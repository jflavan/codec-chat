/// voice-vm.bicep — Azure VM hosting the LiveKit server.
///
/// Azure Container Apps does not support UDP ingress. LiveKit requires raw UDP for
/// WebRTC media ports (50000-60000/udp) and built-in TURN (3478/udp), so it runs
/// here on a dedicated VM instead.
///
/// Deployment flow:
///   1. Bicep provisions the VM, VNet, NSG, and static public IP.
///   2. cloud-init installs Docker on first boot.
///   3. CI/CD writes /opt/voice/docker-compose.yml and /opt/voice/livekit.yaml
///      (with secrets injected), then runs `docker compose up -d` to start LiveKit.
///   4. LiveKit handles its own WebSocket signaling on port 7880 (no nginx needed).

param name string
param location string

@description('Include cloud-init customData in osProfile. Set to true only for the initial VM deployment. Azure does not allow changing customData on an existing VM.')
param includeCustomData bool = false

@description('SSH public key for the azureuser admin account.')
@secure()
param adminSshPublicKey string

@description('Name of the existing Azure Container Registry (for AcrPull role assignment).')
param containerRegistryName string

@description('VM admin username.')
param adminUsername string = 'azureuser'

@description('Source IP prefix allowed to SSH into the VM. Set to your operator CIDR (e.g. "203.0.113.0/24"). When empty, SSH is restricted to the AzureCloud service tag (Azure datacenter IPs). Defaults to empty string so non-voice deployments (voiceVmEnabled = false) do not require this parameter.')
param sshAllowedSourcePrefix string = ''

@description('VM SKU size. Must be an x86-based SKU available in the target region.')
param vmSize string = 'Standard_D2als_v7'

// ── Port constants ──────────────────────────────────────────────────────────────
var turnPort        = 3478
var signalPort      = 7880
var rtcTcpPort      = 7881
var rtcMinPort      = 50000
var rtcMaxPort      = 60000

// ── Static public IP ────────────────────────────────────────────────────────────
resource publicIp 'Microsoft.Network/publicIPAddresses@2023-09-01' = {
  name: '${name}-pip'
  location: location
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: name }
  }
}

// ── Network Security Group ──────────────────────────────────────────────────────
resource nsg 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: '${name}-nsg'
  location: location
  properties: {
    securityRules: [
      // SSH — restrict to operator IPs via sshAllowedSourcePrefix
      {
        name: 'allow-ssh'
        properties: {
          priority: 100
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: !empty(sshAllowedSourcePrefix) ? sshAllowedSourcePrefix : 'AzureCloud'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      // LiveKit built-in TURN — must be reachable by all WebRTC clients
      {
        name: 'allow-turn-udp'
        properties: {
          priority: 200
          protocol: 'Udp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: string(turnPort)
        }
      }
      // LiveKit WebRTC media ports
      {
        name: 'allow-livekit-webrtc'
        properties: {
          priority: 220
          protocol: 'Udp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '${rtcMinPort}-${rtcMaxPort}'
        }
      }
      // LiveKit signal port (WebSocket) — must be reachable by all clients
      {
        name: 'allow-livekit-signal'
        properties: {
          priority: 300
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: string(signalPort)
        }
      }
      // LiveKit RTC TCP fallback
      {
        name: 'allow-livekit-rtc-tcp'
        properties: {
          priority: 310
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: string(rtcTcpPort)
        }
      }
    ]
  }
}

// ── VNet + subnet ───────────────────────────────────────────────────────────────
resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: '${name}-vnet'
  location: location
  properties: {
    addressSpace: { addressPrefixes: ['10.1.0.0/24'] }
    subnets: [
      {
        name: 'voice'
        properties: {
          addressPrefix: '10.1.0.0/24'
          networkSecurityGroup: { id: nsg.id }
        }
      }
    ]
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-09-01' = {
  name: '${name}-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: { id: '${vnet.id}/subnets/voice' }
          publicIPAddress: { id: publicIp.id }
          privateIPAllocationMethod: 'Dynamic'
        }
      }
    ]
  }
}

// ── cloud-init: install Docker; docker-compose.yml + livekit.yaml deployed by CI/CD ───
// NOTE: customData can only be set on initial VM creation. Azure rejects changes
// to osProfile.customData on existing VMs. Use includeCustomData=true only for
// the first deployment.
var cloudInit = '''
#cloud-config

package_update: true
package_upgrade: false

packages:
  - docker.io
  - docker-compose-v2
  - curl
  - jq

runcmd:
  - systemctl enable docker
  - systemctl start docker
  - mkdir -p /opt/voice
  - chown azureuser:azureuser /opt/voice
'''

var baseOsProfile = {
  computerName: name
  adminUsername: adminUsername
  linuxConfiguration: {
    disablePasswordAuthentication: true
    ssh: {
      publicKeys: [
        {
          path: '/home/${adminUsername}/.ssh/authorized_keys'
          keyData: adminSshPublicKey
        }
      ]
    }
  }
}

// ── Virtual Machine ─────────────────────────────────────────────────────────────
resource vm 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: name
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    hardwareProfile: { vmSize: vmSize }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'Standard_LRS' }
        diskSizeGB: 30
      }
    }
    osProfile: includeCustomData ? union(baseOsProfile, { customData: base64(cloudInit) }) : baseOsProfile
    networkProfile: {
      networkInterfaces: [{ id: nic.id }]
    }
  }
}

// ── AcrPull: allow the VM to pull images from the shared ACR ─────────────────
// Retained for potential future use of custom sidecar images alongside LiveKit.
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: containerRegistryName
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, vm.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: containerRegistry
  properties: {
    principalId: vm.identity.principalId
    principalType: 'ServicePrincipal'
    // AcrPull built-in role
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// ── Outputs ─────────────────────────────────────────────────────────────────────
output publicIpAddress string = publicIp.properties.ipAddress
output fqdn string = publicIp.properties.dnsSettings.fqdn
output livekitUrl string = 'ws://${publicIp.properties.ipAddress}:${signalPort}'
