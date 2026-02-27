/// voice-vm.bicep — Azure VM hosting the mediasoup SFU and coturn TURN server.
///
/// Azure Container Apps does not support UDP ingress. Both mediasoup (WebRTC media
/// ports 40000-40100/udp) and coturn (STUN/TURN on 3478/udp+tcp) require raw UDP,
/// so they run here on a dedicated Standard_B2s VM instead.
///
/// Deployment flow:
///   1. Bicep provisions the VM, VNet, NSG, and static public IP.
///   2. cloud-init installs Docker on first boot.
///   3. CI/CD SSHs in, writes /opt/voice/docker-compose.yml (with ANNOUNCED_IP and
///      TURN_SECRET injected from GitHub Actions secrets / Bicep outputs), then runs
///      `docker compose up -d` to start both containers.
///
/// The SFU HTTP API (port 3001) is exposed publicly so the API Container App can
/// reach it. Restrict this further once Container Apps VNet integration is added.

param name string
param location string

@description('SSH public key for the azureuser admin account.')
@secure()
param adminSshPublicKey string

@description('Name of the existing Azure Container Registry (for AcrPull role assignment).')
param containerRegistryName string

@description('VM admin username.')
param adminUsername string = 'azureuser'

@description('Source IP prefix allowed to SSH into the VM. Restrict to your operator CIDR in production (e.g. "203.0.113.0/24"). Defaults to "*" for convenience but should be tightened.')
param sshAllowedSourcePrefix string = '*'

// ── Port constants ──────────────────────────────────────────────────────────────
var sfuPort         = 3001
var turnPort        = 3478
var rtcMinPort      = 40000
var rtcMaxPort      = 40100
var relayMinPort    = 49152
var relayMaxPort    = 49200

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
          sourceAddressPrefix: sshAllowedSourcePrefix
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      // STUN/TURN — must be reachable by all WebRTC clients
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
      {
        name: 'allow-turn-tcp'
        properties: {
          priority: 201
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: string(turnPort)
        }
      }
      // coturn UDP relay range
      {
        name: 'allow-coturn-relay'
        properties: {
          priority: 210
          protocol: 'Udp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '${relayMinPort}-${relayMaxPort}'
        }
      }
      // mediasoup WebRTC media ports
      {
        name: 'allow-mediasoup-webrtc'
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
      // SFU HTTP API — called by the API Container App
      // TODO: tighten to Container Apps outbound CIDR once VNet integration is added
      {
        name: 'allow-sfu-api'
        properties: {
          priority: 300
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: string(sfuPort)
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

// ── cloud-init: install Docker only; docker-compose.yml is deployed by CI/CD ───
var cloudInit = '''
#cloud-config

package_update: true
package_upgrade: false

packages:
  - docker.io
  - docker-compose-plugin
  - curl
  - jq

runcmd:
  - systemctl enable docker
  - systemctl start docker
  - mkdir -p /opt/voice
  - chown azureuser:azureuser /opt/voice
'''

// ── Virtual Machine ─────────────────────────────────────────────────────────────
resource vm 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: name
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    hardwareProfile: { vmSize: 'Standard_B2s' }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'Standard_LRS' }
        diskSizeGB: 30
      }
    }
    osProfile: {
      computerName: name
      adminUsername: adminUsername
      customData: base64(cloudInit)
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
    networkProfile: {
      networkInterfaces: [{ id: nic.id }]
    }
  }
}

// ── AcrPull: allow the VM to pull the SFU image from the shared ACR ─────────────
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
output sfuApiUrl string = 'http://${publicIp.properties.ipAddress}:${sfuPort}'
output turnServerUrl string = 'turn:${publicIp.properties.ipAddress}:${turnPort}'
