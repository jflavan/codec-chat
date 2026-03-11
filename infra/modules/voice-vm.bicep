/// voice-vm.bicep — Azure VM hosting the mediasoup SFU and coturn TURN server.
///
/// Azure Container Apps does not support UDP ingress. Both mediasoup (WebRTC media
/// ports 40000-40100/udp) and coturn (STUN/TURN on 3478/udp+tcp) require raw UDP,
/// so they run here on a dedicated VM instead.
///
/// Deployment flow:
///   1. Bicep provisions the VM, VNet, NSG, and static public IP.
///   2. cloud-init installs Docker, nginx, and certbot on first boot.
///   3. CI/CD writes /opt/voice/docker-compose.yml (with secrets injected), then runs
///      `docker compose up -d` to start both containers.
///   4. nginx terminates TLS on port 443 and proxies to the SFU on localhost:3001.
///      Certbot manages Let's Encrypt certificate provisioning and renewal.

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

@description('Source address prefix allowed to call the SFU HTTPS API (port 443). Defaults to the AzureCloud service tag (Azure datacenter IPs only).')
param sfuApiAllowedSourcePrefix string = 'AzureCloud'

@description('Fully qualified domain name for the SFU API (e.g., sfu.codec-chat.com). Used for the nginx TLS proxy and certbot certificate.')
param sfuDomainName string = ''

@description('Email address for Let\'s Encrypt certificate notifications. Required when sfuDomainName is set.')
param certbotEmail string = ''

@description('VM SKU size. Must be an x86-based SKU available in the target region.')
param vmSize string = 'Standard_D2als_v7'

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
          sourceAddressPrefix: !empty(sshAllowedSourcePrefix) ? sshAllowedSourcePrefix : 'AzureCloud'
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
      // SFU HTTPS API — nginx TLS termination, called by the API Container App.
      // Restricted to AzureCloud IPs via sfuApiAllowedSourcePrefix.
      {
        name: 'allow-sfu-https'
        properties: {
          priority: 300
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: sfuApiAllowedSourcePrefix
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
      // Let's Encrypt HTTP-01 challenge validation (certbot).
      // Must be open to the internet; nginx serves only ACME challenges on port 80.
      {
        name: 'allow-certbot-http01'
        properties: {
          priority: 310
          protocol: 'Tcp'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '80'
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

// ── cloud-init: install Docker, nginx, and certbot; docker-compose.yml is deployed by CI/CD ───
// NOTE: customData can only be set on initial VM creation. Azure rejects changes
// to osProfile.customData on existing VMs. Use includeCustomData=true only for
// the first deployment.
var cloudInitTemplate = '''
#cloud-config

package_update: true
package_upgrade: false

packages:
  - docker.io
  - docker-compose-v2
  - curl
  - jq
  - nginx
  - certbot
  - python3-certbot-nginx

write_files:
  - path: /etc/nginx/sites-available/sfu-proxy
    content: |
      server {
          listen 80 default_server;
          server_name _;
          location /.well-known/acme-challenge/ {
              root /var/www/html;
          }
          location / {
              return 444;
          }
      }
      server {
          listen 443 ssl default_server;
          server_name __SFU_DOMAIN__;

          ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;
          ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;

          ssl_protocols TLSv1.2 TLSv1.3;
          ssl_prefer_server_ciphers on;

          location / {
              proxy_pass http://127.0.0.1:3001;
              proxy_set_header Host $host;
              proxy_set_header X-Real-IP $remote_addr;
              proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
              proxy_set_header X-Forwarded-Proto $scheme;
              proxy_read_timeout 30s;
              proxy_connect_timeout 5s;
          }

          location /health {
              proxy_pass http://127.0.0.1:3001/health;
          }
      }

runcmd:
  - systemctl enable docker
  - systemctl start docker
  - mkdir -p /opt/voice
  - chown azureuser:azureuser /opt/voice
  - ln -sf /etc/nginx/sites-available/sfu-proxy /etc/nginx/sites-enabled/sfu-proxy
  - rm -f /etc/nginx/sites-enabled/default
  - systemctl enable nginx
  - systemctl start nginx
  - certbot --nginx -d __SFU_DOMAIN__ --non-interactive --agree-tos --email __CERTBOT_EMAIL__ --redirect
  - systemctl reload nginx
'''

var cloudInit = replace(replace(cloudInitTemplate, '__SFU_DOMAIN__', sfuDomainName), '__CERTBOT_EMAIL__', certbotEmail)

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
output sfuApiUrl string = sfuDomainName != '' ? 'https://${sfuDomainName}' : 'http://${publicIp.properties.ipAddress}:${sfuPort}'
output turnServerUrl string = 'turn:${publicIp.properties.ipAddress}:${turnPort}'
