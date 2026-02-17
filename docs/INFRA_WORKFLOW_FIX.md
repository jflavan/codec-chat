# Infrastructure Workflow Fix - Revision Deactivation

## Issue Summary

The `infra` GitHub Actions workflow was failing during infrastructure deployment with the following errors:

```
ContainerAppSecretInUse: Container App 'ca-codec-prod-api' has an active revision 
referencing a secret you are trying to delete. Please add secrets: connection-string,
google-client-id or deactive the revisions: ca-codec-prod-api--gh6032dfdr1 referencing 
this secret.

ContainerAppRegistryInUse: Container App 'ca-codec-prod-web' has active revisions 
pulling images from the registries you are trying to delete. Please add back registries 
acrcodecprod.azurecr.io or deactive the revisions: ca-codec-prod-web--gh6032dfdr1 
pulling images from these registries.
```

## Root Cause Analysis

The infrastructure workflow performs a two-step deployment process to handle TLS certificate provisioning for custom domains:

1. **Step 1** (`bindCertificates=false`): Deploy infrastructure to register custom domain hostnames and create managed certificates
   - Uses default quickstart images (`mcr.microsoft.com/k8se/quickstart:latest`)
   - When using quickstart images, the bicep templates set `isQuickstart=true`
   - This causes the templates to remove all secrets and container registry configurations

2. **Step 2** (`bindCertificates=true`): Redeploy to bind the certificates to the custom domains
   - Uses actual application images with full configuration

The problem occurred because:
- Active revisions from previous deployments were still running
- These revisions referenced secrets (connection strings, OAuth client IDs) and container registries
- Azure Container Apps prevents deletion of resources that are actively referenced
- This caused the deployment to fail in Step 1

## Solution

Added a new workflow step to deactivate all active revisions before infrastructure deployment:

```yaml
- name: Deactivate old container app revisions
  run: |
    # Deactivate all old revisions before infrastructure update to prevent conflicts
    # with secrets and registries being removed during the quickstart phase
    echo "Checking for active revisions on ca-codec-prod-api..."
    ACTIVE_API_REVISIONS=$(az containerapp revision list \
      --name ca-codec-prod-api \
      --resource-group rg-codec-prod \
      --query "[?properties.active].name" -o tsv 2>/dev/null || echo "")
    
    if [ -n "$ACTIVE_API_REVISIONS" ]; then
      for rev in $ACTIVE_API_REVISIONS; do
        echo "Deactivating API revision: $rev"
        az containerapp revision deactivate \
          --name ca-codec-prod-api \
          --resource-group rg-codec-prod \
          --revision "$rev" || echo "Failed to deactivate $rev (may not exist)"
      done
    else
      echo "No active API revisions found or container app doesn't exist yet"
    fi

    # Similar logic for web app...
```

### Key Features of the Solution

1. **Proactive Deactivation**: Deactivates all active revisions before attempting infrastructure changes
2. **Error Handling**: Uses `|| echo ""` pattern to handle cases where apps don't exist yet (first deployment)
3. **Clear Logging**: Provides informative output for troubleshooting
4. **Consistency**: Follows the same pattern used successfully in the `cd.yml` workflow
5. **Non-Disruptive**: Only deactivates revisions during infrastructure updates, not during normal application deployments

## Alternative Approaches Considered

1. **Use actual images instead of quickstart**: Would require always providing image tags, complicating the workflow
2. **Change to single-revision mode**: Would prevent zero-downtime deployments for application updates
3. **Modify bicep templates**: Would require complex conditional logic and state management

The chosen solution is minimal, surgical, and maintains the existing deployment architecture.

## Testing Recommendations

To verify the fix:

1. Trigger the infra workflow via workflow_dispatch
2. Verify the "Deactivate old container app revisions" step executes successfully
3. Confirm both deployment steps complete without secret/registry errors
4. Verify certificates are properly bound to custom domains
5. Verify application remains accessible after infrastructure update

## Related Files

- `.github/workflows/infra.yml`: Infrastructure deployment workflow (fixed)
- `.github/workflows/cd.yml`: Application deployment workflow (reference implementation)
- `infra/modules/container-app-api.bicep`: API container app bicep template
- `infra/modules/container-app-web.bicep`: Web container app bicep template

## Prevention

To prevent similar issues in the future:

1. Always deactivate old revisions when modifying secrets or registries
2. Use `activeRevisionsMode: 'Multiple'` carefully, understanding revision lifecycle
3. Test infrastructure changes in a staging environment first
4. Consider using separate resource groups for staging/production infrastructure testing

## References

- [Azure Container Apps Revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure Container Apps Multiple Revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage)
- GitHub Actions workflow run: https://github.com/jflavan/codec-chat/actions/runs/22081522161
