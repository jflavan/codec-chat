# Discord Import Wizard

A step-by-step wizard for importing Discord server content into Codec. One component, two entry points.

## Entry Points

1. **Server list sidebar** — "Import from Discord" button next to "Create Server". Opens wizard with step 1 defaulting to "Create new server".
2. **Server settings Discord Import tab** — shows import status/re-sync/claim if an import exists, or an "Import from Discord" button if not. Button opens wizard with step 1 pre-selecting the current server.

## Wizard Steps

### Step 1: Choose Destination

- Radio: "Create a new server" (shows name input) or "Import into existing server" (dropdown of servers user owns)
- Pre-selected based on entry point
- Next button validates a name is entered or a server is selected

### Step 2: Set Up Your Bot

Static instructions with numbered steps:

1. Go to Discord Developer Portal (link)
2. Create a New Application
3. Go to Bot settings, enable Server Members Intent + Message Content Intent
4. Copy the Bot Token (for step 3)
5. Paste your Application ID below -- generates the OAuth2 invite URL with correct permissions

Input: Application ID -- generates clickable invite link.

This step is informational -- Next is always available.

### Step 3: Connect

- Inputs: Bot Token, Discord Guild ID
- "Validate" button calls Discord API to verify, shows guild name + icon + member count on success
- Next only enabled after successful validation

### Step 4: Import Progress

- Triggers `POST /discord-import` (creating the server first via `POST /servers` if "Create new" was selected)
- Live progress via SignalR: stage label, message count, progress bar
- On completion: summary stats, "Go to Server" button
- On failure: error message, "Retry" button
- "Close" button available at any time -- import continues in background
- Re-opening the wizard or the settings tab shows current progress

## Component Structure

```
DiscordImportWizard.svelte        -- modal shell, step navigation, state
  WizardStepDestination.svelte    -- step 1
  WizardStepBotSetup.svelte       -- step 2
  WizardStepConnect.svelte        -- step 3
  WizardStepProgress.svelte       -- step 4
```

## State

All wizard state lives in the wizard component via `$state` runes. No new store. Uses existing `serverStore` for server list and import API calls.

## Modified Components

- **`ServerDiscordImport.svelte`** -- simplified to status view + "Import from Discord" button that opens the wizard
- **`ChannelSidebar.svelte`** or **`ServerSidebar.svelte`** -- add "Import from Discord" button near "Create Server"
- **`+page.svelte`** or root layout -- mount the wizard modal

## Backend Changes

None. The frontend calls existing endpoints:
- `POST /servers` to create a new server (if needed)
- `POST /servers/{serverId}/discord-import` to start the import
- `GET /servers/{serverId}/discord-import` for status
