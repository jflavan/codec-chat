# SFU Control API TLS Termination

## Problem

The SFU control API (port 3001) on the voice VM communicates over plain HTTP. The `X-Internal-Key` authentication header and all signaling traffic (room creation, transport negotiation, producer/consumer setup) travel unencrypted between the API Container App and the voice VM. The NSG restricts port 3001 to AzureCloud IPs, but the traffic is still unencrypted within Azure's network.

## Solution

Add an nginx reverse proxy on the voice VM that terminates TLS on port 443 and forwards to the SFU on localhost:3001. Use certbot with Let's Encrypt for automatic certificate provisioning and renewal. Bind the SFU to localhost only so it's unreachable from the network.

## Architecture

```
API Container App
    → HTTPS :443 (sfu.codec-chat.com)
        → nginx (TLS termination on VM)
            → HTTP :3001 (127.0.0.1 only)
                → SFU Express server
```

## DNS

An Azure DNS A record for `sfu.codec-chat.com` pointing to the VM's static public IP address. Managed in Bicep so the record stays in sync automatically.

Prerequisite: the `codec-chat.com` domain's registrar NS records must delegate to Azure DNS. If the DNS zone already exists outside of Bicep, reference it as an existing resource rather than creating a new one.

## Cloud-init

Extend the existing cloud-init in `voice-vm.bicep` to:

1. Install `nginx`, `certbot`, and `python3-certbot-nginx` packages
2. Write an nginx site config that proxies port 443 to `127.0.0.1:3001` with a placeholder self-signed cert
3. Run `certbot --nginx -d sfu.codec-chat.com --non-interactive --agree-tos --email <ops-email>` to obtain and install the Let's Encrypt certificate
4. Certbot's systemd timer (installed by default with the package) handles automatic renewal

Cloud-init only runs on first boot. For the existing VM where `includeCustomData=false`, the CD pipeline handles nginx/certbot setup via `az vm run-command` as a one-time migration step.

## NSG Rule Changes

| Rule | Current | New |
|---|---|---|
| `allow-sfu-api` | TCP 3001, source: AzureCloud | TCP 443, source: AzureCloud |
| `allow-certbot` (new) | — | TCP 80, source: `*` |

Port 80 is required for Let's Encrypt HTTP-01 challenge validation. Nginx serves only ACME challenges on this port.

## Docker-compose Changes

Bind the SFU port to localhost only:

```yaml
ports:
  - "127.0.0.1:3001:3001"
```

## Bicep Output Changes

`voice-vm.bicep` `sfuApiUrl` output changes from `http://<ip>:3001` to `https://sfu.codec-chat.com`. This flows through `main.bicep` → `container-app-api.bicep` → `Voice__MediasoupApiUrl` env var. No API code changes required.

## Migration Path

Since cloud-init cannot be changed on an existing VM:

1. CD pipeline checks if nginx is installed on the VM (one-time)
2. If not, runs `az vm run-command` to install nginx + certbot and obtain the cert
3. Subsequent deploys skip this step

## Files Changed

- `infra/modules/voice-vm.bicep` — cloud-init (nginx, certbot), NSG rules (443 + 80), DNS record, output uses HTTPS FQDN
- `infra/main.bicep` — pass SFU domain to voice-vm module, DNS zone wiring
- `infra/voice/docker-compose.yml` — bind SFU to `127.0.0.1:3001`
- `.github/workflows/cd.yml` — one-time nginx/certbot migration in deploy-voice job

## What Doesn't Change

- SFU application code (Express server, routes, auth middleware)
- `X-Internal-Key` authentication (now encrypted in transit)
- coturn on port 3478 (HMAC-SHA256 auth, no TLS needed)
- WebRTC media ports 40000-40100/udp (DTLS-encrypted by protocol)
- API HttpClient configuration (trusted cert, no code changes)
