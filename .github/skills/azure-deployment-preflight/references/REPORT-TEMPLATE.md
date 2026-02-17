# Preflight Report Template

Use this template to generate the `preflight-report.md` output.

---

## Template

````markdown
# Deployment Preflight Report

| Property | Value |
|----------|-------|
| **Date** | {YYYY-MM-DD HH:MM UTC} |
| **Status** | {Overall Status} |
| **Project Type** | {azd / bicep / arm} |
| **Deployment Scope** | {resourceGroup / subscription / managementGroup / tenant} |
| **Target** | {subscription/resource group identifier} |

## Validation Results

| Step | Tool | Status | Details |
|------|------|--------|---------|
| Syntax Validation | bicep build | {✅ Pass / ❌ Fail / ⏭️ Skipped} | {summary} |
| Preflight Validation | az deployment validate | {✅ Pass / ❌ Fail / ⏭️ Skipped} | {summary} |
| What-If Analysis | az deployment what-if | {✅ Pass / ❌ Fail / ⏭️ Skipped} | {summary} |

## Tools Executed

```bash
# List each command that was run
{command 1}
# Output: {brief result}

{command 2}
# Output: {brief result}
```

### Tool Versions

| Tool | Version |
|------|---------|
| Azure CLI | {version} |
| Bicep CLI | {version} |
| Azure Developer CLI | {version or N/A} |

## Issues

{If no issues: "No issues found."}

{If issues exist:}

Found **{X} errors** and **{Y} warnings**

### Errors ({X})

#### ❌ {Error Title}

- **Severity:** Error
- **Source:** {tool that reported the error}
- **Location:** {file:line:column if applicable}
- **Code:** {error code if available}
- **Message:** {error message}
- **Remediation:** {suggested fix}
- **Documentation:** {link if available}

### Warnings ({Y})

#### ⚠️ {Warning Title}

- **Severity:** Warning
- **Source:** {tool}
- **Message:** {warning message}
- **Recommendation:** {suggested action}

## What-If Results

{If what-if was not run: "What-if analysis was skipped. See Issues above for details."}

{If what-if succeeded:}

### Change Summary

| Change Type | Count |
|-------------|-------|
| Create | {n} |
| Modify | {n} |
| Delete | {n} |
| No Change | {n} |
| Ignore | {n} |

### Resources to Create ({n})

| Resource | Type | Location |
|----------|------|----------|
| {name} | {Microsoft.xxx/yyy} | {region} |

### Resources to Modify ({n})

| Resource | Type | Changes |
|----------|------|---------|
| {name} | {Microsoft.xxx/yyy} | {list of changed properties} |

<details>
<summary>Detailed property changes for {resource name}</summary>

```diff
- oldValue: xxx
+ newValue: yyy
```

</details>

### Resources to Delete ({n})

| Resource | Type | Location |
|----------|------|----------|
| {name} | {Microsoft.xxx/yyy} | {region} |

> ⚠️ **Warning:** The above resources will be deleted. Review carefully before proceeding.

### No Change ({n})

<details>
<summary>{n} resources unchanged</summary>

| Resource | Type |
|----------|------|
| {name} | {Microsoft.xxx/yyy} |

</details>

## Recommendations

{Numbered list of actionable recommendations based on findings}

1. {recommendation}
2. {recommendation}

## Next Steps

{Based on overall status:}

### If Status is ✅ Pass:
- Review the what-if changes above
- Proceed with deployment when ready
- Run: `{deployment command}`

### If Status is ⚠️ Warning:
- Review warnings above
- Address any concerns
- Re-run preflight if changes were made
- Proceed with caution

### If Status is ❌ Fail:
- Fix the errors listed above
- Re-run preflight validation
- Do not proceed with deployment until all errors are resolved
````

---

## Status Values

| Status | Meaning |
|--------|---------|
| ✅ Pass | All validations passed successfully |
| ⚠️ Warning | Validation passed with warnings; review recommended |
| ❌ Fail | One or more validations failed; deployment blocked |
| ⏭️ Skipped | Step was skipped (tool not available, auth failed, etc.) |

---

## Example Report

````markdown
# Deployment Preflight Report

| Property | Value |
|----------|-------|
| **Date** | 2025-01-15 14:30 UTC |
| **Status** | ⚠️ Warning |
| **Project Type** | azd |
| **Deployment Scope** | subscription |
| **Target** | my-subscription / my-resource-group |

## Validation Results

| Step | Tool | Status | Details |
|------|------|--------|---------|
| Syntax Validation | bicep build | ✅ Pass | No errors found |
| Preflight Validation | azd provision --preview | ✅ Pass | Template is valid |
| What-If Analysis | azd provision --preview | ⚠️ Warning | 3 creates, 1 modify, 0 deletes |

## Tools Executed

```bash
bicep build infra/main.bicep --stdout
# Output: Build succeeded with 0 errors and 0 warnings

azd provision --preview
# Output: Preview succeeded
```

### Tool Versions

| Tool | Version |
|------|---------|
| Azure CLI | 2.67.0 |
| Bicep CLI | 0.32.4 |
| Azure Developer CLI | 1.11.0 |

## Issues

Found **0 errors** and **1 warning**

### Warnings (1)

#### ⚠️ Existing Resource Will Be Modified

- **Severity:** Warning
- **Source:** what-if
- **Message:** Storage account 'mystorageacct' will have its SKU changed
- **Recommendation:** Verify the SKU change from Standard_LRS to Standard_GRS is intended

## What-If Results

### Change Summary

| Change Type | Count |
|-------------|-------|
| Create | 3 |
| Modify | 1 |
| Delete | 0 |
| No Change | 2 |

### Resources to Create (3)

| Resource | Type | Location |
|----------|------|----------|
| my-app | Microsoft.Web/sites | eastus |
| my-plan | Microsoft.Web/serverfarms | eastus |
| my-insights | Microsoft.Insights/components | eastus |

### Resources to Modify (1)

| Resource | Type | Changes |
|----------|------|---------|
| mystorageacct | Microsoft.Storage/storageAccounts | sku.name |

<details>
<summary>Detailed property changes for mystorageacct</summary>

```diff
- sku.name: Standard_LRS
+ sku.name: Standard_GRS
```

</details>

### No Change (2)

<details>
<summary>2 resources unchanged</summary>

| Resource | Type |
|----------|------|
| my-vault | Microsoft.KeyVault/vaults |
| my-db | Microsoft.DBforPostgreSQL/flexibleServers |

</details>

## Recommendations

1. Verify the storage account SKU change is intentional (Standard_LRS → Standard_GRS)
2. Review the 3 new resources that will be created
3. Consider tagging all new resources for cost tracking

## Next Steps

### Status: ⚠️ Warning
- Review the storage SKU change above
- If the change is intended, proceed with deployment
- Run: `azd provision`
````
