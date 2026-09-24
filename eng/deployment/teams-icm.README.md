# Teams-to-IcM request intake

This deployment implements the production infrastructure for Engineering Services Request Intake
in the `.NET Core Eng Services Partners` Team. It creates disabled-by-default Consumption Logic
Apps, a Microsoft Teams connection placeholder, durable Azure Table state, managed identities,
Log Analytics diagnostics, and failed-run alerts.

The target channel is `Eng Services Requests`:

| Setting | Value |
| --- | --- |
| Team ID | `4d73664c-9f2f-450d-82a5-c2f02756606d` |
| Channel ID | `19:bfc7969db4c445e095a803be203569c1@thread.skype` |
| Default severity | Sev3 |
| IcM connector | `9bfe0f4f-4dcf-4033-94a3-f463e90baf04` |
| IcM routing rule | `DDFUNCustomerRequests` |
| Teams identity | `dnceng-req-intake@microsoft.onmicrosoft.com` |
| Credential vault | `HelixKV` |

The connector and routing rule are fixed deployment configuration. Teams content cannot select
an IcM destination, and the workflow accepts only Sev3 or Sev4. A free-form request defaults to
Sev3 unless its content explicitly selects Sev4. Customers can optionally provide every field:

```text
Title:
Who/customer affected:
Impact:
Affected service/infrastructure:
Evidence or build/job URL:
Requested action:
Reporter:
Severity: 3 or 4
```

## Architecture

```text
15-second recurrence
  -> dnceng-teams-icm-connector
       - reads up to 1,000 messages from Eng Services Requests
       - selects root threads newer than its durable watermark
       - advances the watermark only after selected messages succeed
  -> dnceng-teams-icm
       - reserves the message ID in Azure Table Storage
       - extracts structured or free-form intake
       - adds versioned DDFun operational context
       - creates the fixed-route DDFun IcM
       - replies in the originating Teams thread
1-minute recurrence
  -> dnceng-teams-icm-latency-monitor
       - pages through all in-flight durable states
       - checks each state against the Teams message creation time
       - fails when a thread has not reached ReplyPosted within two minutes
       - triggers the dedicated latency-objective alert
```

The processor stores `Processing`, `Created`, `ReplyPosted`, and `Failed` states in the
`TeamsIcmIntake` table, including the root message creation time used by the latency monitor.
Duplicate deliveries are safe because the message ID is reserved before IcM creation and is also
used in the provider's deterministic source ID.

If 1,000 messages fall inside one polling window, the adapter fails without advancing its
watermark. This surfaces the backlog through failed-run monitoring instead of silently skipping
messages that were not returned by pagination.

The processor loads `teams-icm-operational-context.json`. A missing specific playbook uses the
documented DDFun fallback and does not prevent incident creation.

## Identity gate

The Microsoft Teams managed connector used by the pilot was authorized with an individual
operator. Production must not authorize `dnceng-teams-icm-teams` with a personal identity.

Before enabling the workflows:

1. Prove and approve a non-personal Teams identity that can read the selected channel and post a
   reply to the originating root thread.
2. Confirm the resulting Teams connection reports `Connected`.
3. Resolve the processor system-assigned identity's application ID and add only that application
   ID to the production ICM Provider `AddOrUpdateIcmIncident` allowlist.
4. Confirm the alert recipient and operational owner.
5. Complete the controlled disabled-to-enabled rollout described in AB#12465.

The workflows remain disabled while any gate is open. Creating the resource group and disabled
resources does not make the channel live.

### Non-person account credential

The Teams connection uses delegated OAuth, so neither Logic App reads the account password at
runtime. The password is retained only for accountable recovery, connector reauthorization, and
rotation through CoreIdentity.

The Secret Manager declaration is `.vault-config/helixkv.yaml`. It stores
`dnceng-req-intake-account-microsoft` in the existing `HelixKV` account-credential vault as a
`domain-account`. The manifest contains no credential value, and neither Logic App identity has
access to the vault.

Initialize or rotate the password interactively:

```powershell
dotnet secret-manager synchronize `
  --force-secret=dnceng-req-intake-account-microsoft `
  .vault-config/helixkv.yaml
```

Use the password produced by CoreIdentity. Never place it in source control, deployment
parameters, workflow configuration, or pipeline variables.

## Validate and deploy disabled

```powershell
az bicep build --file eng/deployment/teams-icm.bicep

az group create `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --name dnceng-teams-icm-production `
  --location westus2

az deployment group what-if `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --resource-group dnceng-teams-icm-production `
  --template-file eng/deployment/teams-icm.bicep `
  --parameters alertEmail=<rollout-owner-email> workflowEnabled=false

az deployment group create `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --resource-group dnceng-teams-icm-production `
  --template-file eng/deployment/teams-icm.bicep `
  --parameters alertEmail=<rollout-owner-email> workflowEnabled=false
```

Resolve the processor identity's application ID from the deployment output:

```powershell
$principalId = az deployment group show `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --resource-group dnceng-teams-icm-production `
  --name <deployment-name> `
  --query properties.outputs.logicAppPrincipalId.value `
  --output tsv

az ad sp show --id $principalId --query appId --output tsv
```

The ICM Provider authorizes the token's `appid` or `azp` claim, not the service principal object ID.

## Controlled enablement

Do not enable the workflows until the architecture, identity, routing, alerting, and DDFun
readiness gates are recorded as complete on AB#12465.

During the attended rollout:

1. Enable the connector, processor, and latency-monitor workflows through a Bicep deployment with
   `workflowEnabled=true`.
2. Post one clearly labeled controlled root-thread request.
3. Confirm one correctly routed DDFun IcM, one thread reply, and `ReplyPosted` state.
4. Confirm a reply does not create an IcM and replaying the root does not create another IcM.
5. Confirm the two-minute incomplete-thread alert objective and failed-run alert delivery.
6. Resolve the controlled incident and keep the channel marked pre-launch until every Phase 2 gate
   passes.

If short-interval polling is not reliable, restore the Teams connector's fixed three-minute
`When a new channel message is added` trigger while preserving the processor and idempotency table.

## Pilot cleanup

After production acceptance, remove the `dnceng-teams-icm-pilot` resource group, its authorized
Teams connection, provider allowlist entry, test channel resources, role assignments, alerts, and
remaining pilot artifacts. Do not remove pilot resources before the production rollback window
ends.
