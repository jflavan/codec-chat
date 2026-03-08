# Infrastructure Pipeline — Zero-Downtime Deploys

## Overview

The `infra.yml` pipeline deploys Azure infrastructure via Bicep without causing production downtime. It auto-triggers on `infra/` changes after CI passes on `main`, and chains into the CD pipeline when complete.

## How It Works

### Zero-Downtime Image Preservation

The key problem with infrastructure deploys was that Bicep container app modules use a default quickstart image (`mcr.microsoft.com/k8se/quickstart:latest`). Deploying Bicep without specifying the currently running image would reset containers to the placeholder, taking the site down.

**Solution:** Before deploying, the pipeline queries Azure for the currently running container images and passes them as parameters to the Bicep template:

```yaml
- name: Resolve current container images
  run: |
    API_IMAGE=$(az containerapp show \
      --name ca-codec-prod-api \
      --resource-group rg-codec-prod \
      --query "properties.template.containers[0].image" -o tsv 2>/dev/null || echo "")
    # Falls back to quickstart placeholder for first-time deployments
```

This ensures the running application images are preserved through infrastructure changes.

### Smart Certificate Deployment

Azure managed TLS certificates require a two-pass deployment: first register hostnames, then bind certificates. The pipeline checks whether certificates already exist and skips the first pass when they do:

```yaml
- name: Check if managed certificates exist
  run: |
    CERT_LIST=$(az containerapp env certificate list ...)
    if echo "$CERT_LIST" | grep -q "cert-api" && ...; then
      echo "certs-exist=true" >> "$GITHUB_OUTPUT"
    fi
```

On established environments, this reduces the deploy to a single Bicep pass.

### Auto-Trigger with Change Detection

The pipeline uses `workflow_run` to trigger after CI completes on `main`, then checks for `infra/` changes using `git diff-tree`:

```yaml
on:
  workflow_dispatch:
  workflow_run:
    workflows: [ci]
    branches: [main]
    types: [completed]
```

The `check-infra` job gates the actual deployment:
- **Manual dispatch** — always runs (skips change detection)
- **CI trigger** — only runs if `infra/` files changed in the commit
- **CI failure** — skipped entirely (`workflow_run.conclusion == 'success'` guard)

### Pipeline Chaining

After infrastructure deploys, the pipeline triggers CD to deploy any pending application changes:

```yaml
- name: Trigger CD pipeline
  run: gh workflow run cd.yml --ref main
```

The CD pipeline has a corresponding `check-skip` job that yields to the infra pipeline when `infra/` changes are detected, preventing duplicate runs.

### Shared Concurrency Group

Both `infra.yml` and `cd.yml` share the `deploy-prod` concurrency group with `cancel-in-progress: false`. This prevents race conditions where both pipelines try to modify the same Azure resources simultaneously.

## Pipeline Flow

```
Push to main → CI
                 │
                 ├─ infra/ changes? → Infra pipeline → CD pipeline
                 │
                 └─ No infra changes → CD pipeline (directly)
```

## Previous Approach (Superseded)

The original infra pipeline deactivated all active container app revisions before deploying Bicep. This solved secret/registry conflicts but caused production downtime during every infrastructure deployment. The current approach (image resolution) preserves running revisions and avoids any service interruption.

## Related Files

- `.github/workflows/infra.yml` — Infrastructure deployment workflow
- `.github/workflows/cd.yml` — Application deployment workflow
- `infra/main.bicep` — Main Bicep orchestrator (accepts `apiContainerImage` / `webContainerImage` params)
- `infra/modules/container-app-api.bicep` — API container app module
- `infra/modules/container-app-web.bicep` — Web container app module
