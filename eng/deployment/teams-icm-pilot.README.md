# Teams-to-IcM pilot

This deployment implements the AB#12383 pilot for the `.NET Eng Services` Team and the
`IcM Intake Automation Test` channel. It creates a disabled-by-default Consumption Logic App,
a user-authorized Teams connection, durable Azure Table state, managed-identity access to that
state, and failed-run monitoring.

The workflow accepts root channel messages only. A request must use this exact field order and
must provide a non-empty value for every field:

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

The workflow reserves each message with a Table Storage `POST`, then records state transitions
with full-entity `PUT` updates. Keep every persisted field in each replacement body; the Logic Apps
HTTP action does not support Table Storage's `MERGE` verb.

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

The Test ICM Provider creates real production IcM incidents. Use only controlled Sev4 test
messages, verify routing to `DDFUNSERVICESMANAGEMENT\DDFuncustomerrequests`, and resolve every
test incident promptly.
