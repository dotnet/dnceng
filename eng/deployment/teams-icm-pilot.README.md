# Teams-to-IcM pilot

This deployment implements the AB#12383 pilot for the `.NET Eng Services` Team and the
`IcM Intake Automation Test` channel. It creates disabled-by-default Consumption Logic Apps,
a user-authorized Teams connection, durable Azure Table state, managed identities, and
failed-run monitoring.

The deployment uses a 15-second Logic Apps recurrence and the user-authorized Teams connector's
`Get messages in a channel` action. This avoids the connector trigger's fixed three-minute
polling interval while preserving one IcM for every new root thread without requiring an
`@mention`, custom Teams app, tenant-wide Graph permission, or resource-specific consent (RSC).

The workflow accepts root channel messages only. For a normal free-form post, it maps the Teams
subject to the IcM title, the message body to impact and description, the sender to reporter, and
the Teams message link to evidence. Missing customer and infrastructure details use `Not specified`,
the requested action defaults to `Investigate the reported issue`, and severity defaults to 4.
Writing `Sev3`, `Sev 3`, or `Severity 3` in the message selects severity 3.
Other or missing severity text defaults to Sev4; it never creates Sev1 or Sev2.

Customers can optionally override every inferred value by using this exact field order with a
non-empty value for every field:

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

The connector and routing rule are fixed in the deployment. Teams content cannot select an IcM
destination, and the workflow rejects severities other than 3 or 4.

The processor also loads `teams-icm-operational-context.json`, a versioned catalog of DDFun
ownership, intake guidance, playbook matching signals, remediation guidance, validation criteria,
and canonical links. Physical-machine or reimaging language selects the Reimaging On-Prem
Machines playbook. Messages without a specific match use the DDFun intake runbook as an explicit
fallback; a missing match never prevents incident creation. Every incident records the catalog
version used to compose its operational context.

The workflow reserves each message with a Table Storage `POST`, then records state transitions
with full-entity `PUT` updates. Keep every persisted field in each replacement body; the Logic Apps
HTTP action does not support Table Storage's `MERGE` verb.

## Intake architecture

The deployment separates intake adapters from the processor:

```text
15-second recurrence
  -> dnceng-teams-icm-pilot-connector
       - reads up to 100 channel messages through the Teams connector
       - selects root threads newer than its durable watermark
       - advances the watermark only after all selected messages succeed
  -> dnceng-teams-icm-pilot
       - reserves the message ID in Azure Table Storage
       - extracts structured or free-form intake
       - creates the DDFun IcM
       - replies in the originating thread
```

The poller stores `LastSuccessfulPoll` in the `TeamsIcmSystem` / `ChannelPoller` Table entity. On
first deployment it looks back one hour. Each run uses its start time as the closed upper bound,
processes matching root messages sequentially, and writes the new watermark only after every
processor invocation succeeds. Overlapping reads are safe because the processor independently
reserves each Teams message ID.

## Deploy safely

Validate the templates:

```powershell
az bicep build --file eng/deployment/teams-icm-pilot.bicep
```

Create the resource group and preview the deployment:

```powershell
az group create `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --name dnceng-teams-icm-pilot `
  --location westus2

az deployment group what-if `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --resource-group dnceng-teams-icm-pilot `
  --template-file eng/deployment/teams-icm-pilot.bicep `
  --parameters alertEmail=<pilot-owner-email>
```

Deploy with `workflowEnabled=false`. Before enabling the workflow:

1. Resolve the Logic App identity's Application (client) ID from the `logicAppPrincipalId`
   deployment output, add that Application ID to the ICM Provider Test managed-identity caller
   allowlist, and deploy the provider:

   ```powershell
   az ad sp show --id <logicAppPrincipalId> --query appId --output tsv
   ```

   The ICM Provider authorizes the token's `appid`/`azp` claim, not the service principal object ID.
2. Authorize the `dnceng-teams-icm-pilot-teams` API connection with the pilot operator's Teams
   identity.
3. Confirm the Teams connection reports `Connected`.
4. Enable the workflow with a second deployment using `workflowEnabled=true`.

### Enable polling

```powershell
az deployment group create `
  --subscription a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1 `
  --resource-group dnceng-teams-icm-pilot `
  --template-file eng/deployment/teams-icm-pilot.bicep `
  --parameters alertEmail=<pilot-owner-email> `
               workflowEnabled=true
```

This enables both the poller and processor. The poller uses the same authorized Teams connection
for channel reads that the processor uses for thread replies.

### Revert to the fixed connector trigger

The previous implementation used the Teams connector's `When a new channel message is added`
trigger directly on the processor. It remains the rollback option if the 15-second recurrence
causes sustained throttling or reliability problems. Restoring that trigger removes the polling
adapter and watermark but preserves the Teams connection, message-ID reservations, IcM
idempotency keys, Table state transitions, and reply behavior. Its documented trade-off is a
fixed polling latency of up to approximately three minutes.

## Production rollout plan

The MVP uses a user-authorized Teams connection in a test channel. Do not treat that connection
or the pilot resource group as the production deployment.

1. **Replace the personal connection identity.**
   - Create a dedicated Entra application and service principal for the automation.
   - Grant only the permissions required to read the selected channel and post thread replies.
   - Confirm that the Teams managed connector supports the intended service-principal
     authentication flow. This is a rollout gate: if it does not, select an approved non-personal
     identity mechanism rather than retaining an individual operator's connection.
   - Scope the ICM Provider authorization to only
     `POST /api/mi/AddOrUpdateIcmIncident`, and confirm the production principal is the identity
     authorized by the provider.

2. **Create the production intake channel.**
   - Create a dedicated channel in the `Partners` Team.
   - Agree on the channel name, channel type, owners, membership, retention, and posting guidance
     before creation.
   - Record the production Team and channel IDs and supply them as deployment parameters; do not
     replace the test defaults without peer review.

3. **Provision production separately.**
   - Use a production resource group, storage account, Table, Log Analytics workspace, action
     group, Teams connection, processor, and polling adapter.
   - Deploy both workflows disabled.
   - Authorize the non-personal Teams connection, apply the Table role assignments, deploy the
     endpoint-scoped ICM Provider authorization, and confirm the connection reports `Connected`.
   - Confirm failed-run diagnostics and alert recipients before enabling intake.

4. **Run a controlled rollout.**
   - Enable the processor and poller during an attended rollout window.
   - Post one clearly labeled Sev4 root-thread test in the new channel.
   - Measure message-to-IcM latency and verify exactly one correctly routed DDFun incident, one
     originating-thread reply, durable `ReplyPosted` state, and no incident from replies.
   - Resolve the test incident immediately.
   - Observe polling runs, connector throttling, Table state, processor failures, and alert
     delivery before announcing general availability.

5. **Complete the code and authorization rollout.**
   - Merge and deploy the endpoint-scoped ICM Provider authorization.
   - Review and merge the dnceng deployment change.
   - Publish operator ownership, support escalation, monitoring, and rollback instructions.
   - If short-interval polling is not reliable, restore the fixed three-minute connector trigger
     without changing processor or idempotency state.

6. **Remove all test resources after production acceptance.**
   - Resolve any remaining test incidents and remove or archive the test Teams channel as agreed
     with its owners.
   - Delete the `dnceng-teams-icm-pilot` resource group, including its Logic Apps, API connection,
     storage account and Table data, Log Analytics workspace, action group, alerts, and role
     assignments.
   - Remove the pilot identity from the ICM Provider allowlist and delete any remaining pilot
     Entra objects or consent grants.
   - Confirm that no scheduled workflow, alert, diagnostic setting, generated package, or local
     test artifact remains.
   - The previously deleted purge-protected Graph experiment Key Vault cannot be purged before
     retention expiry; verify that it remains deleted and allow Azure retention to expire.

The Test ICM Provider creates real production IcM incidents. Use only controlled Sev4 test
messages, verify routing to `DDFUNSERVICESMANAGEMENT\DDFuncustomerrequests`, and resolve every
test incident promptly.
