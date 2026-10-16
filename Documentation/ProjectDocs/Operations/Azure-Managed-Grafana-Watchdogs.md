# Azure Managed Grafana watchdogs

## Purpose

Azure Managed Grafana requires Entra authentication for its data-plane API. An anonymous HTTP
availability test can prove that the front door responds, but it cannot prove that authenticated
Grafana requests work. This watchdog uses a managed identity to test authenticated API access and
validates response bodies rather than relying only on HTTP status codes.

Environment-specific Azure resources, Incident Action configuration, ownership, and deployment
values are maintained in the internal .NET Engineering Services operations runbook. Do not add
those values to this public document or source defaults.

## Architecture

```text
Probe user-assigned managed identity
  |
  v
.NET 8 timer-triggered Function (every 5 minutes)
  1. Request https://dashboard.azure.com/.default token
  2. Authenticated GET {workspace}/api/health
  3. Authenticated GET {workspace}/api/org
  4. Validate both response bodies
  5. Emit one availability result per workspace
  6. Emit one heartbeat after the cycle completes
  |
  v
Workspace-based Application Insights and Log Analytics
  |
  +-- Scheduled query alert: repeated workspace failures
  |
  +-- Scheduled query alert: missing watchdog heartbeat
  |
  v
Azure Monitor Action Group with native IcM Incident Action
```

## Why this is a separate watchdog

The watchdog is intended to verify the authenticated Grafana data plane and the monitoring path, not
only Azure Managed Grafana's platform availability. A platform can satisfy its availability target
while requests made with the team's managed identity fail because of Entra, RBAC, organization
access, or environment configuration.

Running this check inside a Helix service such as Metrics Observer would reuse existing hosting, but
it would also couple the monitor for the shared Grafana workspaces to Helix deployment and service
health. A Helix outage or rollout could then suppress the independent signal used to investigate
Helix and other engineering services. The standalone Function keeps that failure domain separate and
emits its own missing-heartbeat signal.

The Function runs on Flex Consumption with two explicitly selected user-assigned identities. The
probe identity has Grafana Viewer access and is selected for `DefaultAzureCredential` through
`AZURE_CLIENT_ID`. The storage identity accesses the Function host storage and private deployment
container through managed identity. Shared-key access is disabled on the dedicated storage account,
so no storage key or expiring SAS token is stored in the app configuration.

A workspace is healthy only when:

- `/api/health` returns HTTP 200 and JSON with `database` equal to `ok`.
- `/api/org` returns HTTP 200 and JSON with a positive `id`.

The endpoints test different failures. `/api/health` verifies that Grafana and its database report
healthy. `/api/org` requires an authenticated request and verifies that the managed identity can
access a Grafana organization. Keeping both prevents a healthy service response from masking a
broken authentication or authorization path.

Each HTTP call gets one bounded retry by default for HTTP 408, HTTP 429, HTTP 5xx,
`HttpRequestException`, or the configured per-request timeout. Other HTTP failures and invalid
response bodies are not retried. An expected failure in one workspace, including token acquisition,
does not prevent the next workspace from being probed. An unexpected software failure aborts the
cycle and suppresses its heartbeat so the missing-heartbeat alert can detect the watchdog failure.

The Function emits:

- One `GrafanaWorkspaceProbe` availability record per workspace and cycle, with `WorkspaceName`,
  `EndpointResults`, and `AttemptCount` properties.
- One `GrafanaWatchdogHeartbeat` event after a completed cycle, with `WorkspacesProbed` and
  `FailedWorkspaces` properties.

Adaptive sampling is disabled for these low-volume alert signals. The Function awaits a telemetry
flush before completing each invocation; a failed flush fails the invocation rather than reporting a
successful cycle whose monitoring records were not accepted by the telemetry channel.

## Source layout

| Path | Purpose |
|---|---|
| `src/GrafanaWatchdog/Microsoft.DncEng.GrafanaWatchdog/` | Function app and probe logic |
| `src/GrafanaWatchdog/Microsoft.DncEng.GrafanaWatchdog.Tests/` | Unit tests |
| `eng/deployment/grafana-watchdog.bicep` | Standalone infrastructure |

## Deployment gate

The Bicep template is intentionally not referenced from any deployment pipeline. Do not deploy
incident routing until the owning service administrator has supplied and approved the Azure Monitor
Incident Action connection and routing values. A probe-only staging deployment may set
`deployIncidentRouting=false` before those values are available.

Until this gate is completed, the watchdog may run only as a probe-only staging deployment; its two
new alert rules do not exist or run. Existing Grafana monitoring remains unchanged. After incident
routing is deployed, the alert rules still remain disabled until the Function produces a verified
healthy cycle.

Incident routing requires:

- `icmConnectionId`: GUID of the Azure Monitor Incident Action connection configured in IcM.
- `icmConnectionName`: name of that connection.
- `icmRoutingId`: routing ID with a verified matching rule on that connection.
- `grafanaWorkspaceNameProduction`, `grafanaWorkspaceNameStaging`, and
  `grafanaWorkspaceNameWorkflow`: existing workspace resource names.

The workspace names are always required so the same template can be promoted without changing its
environment mapping. Use `targetEnvironment` to select `production`, `staging`, `workflow`, or
`all`. A staging-only validation must set `targetEnvironment=staging`; the template then grants
Grafana Viewer only on the staging workspace. The constrained parameter prevents an empty selection.

Set `deployIncidentRouting=false` while validating the Function, authenticated probes, and telemetry
before the IcM connector is enabled. This omits the Action Group and both scheduled-query rules.
It does not validate alerting or incident delivery; those remain gated on the approved connector.
Use a fresh staging-specific `baseName`. Azure Resource Manager deployments are incremental, so
turning a probe or routing flag off does not delete resources or role assignments created by an
earlier deployment. Follow the rollback procedure to remove a previous deployment before changing
its scope.

The template must be deployed to the resource group containing those workspaces. It creates
dedicated Log Analytics and Application Insights resources, a Linux Flex Consumption Function with
separate probe and storage user-assigned identities, a private deployment container, Grafana Viewer
and storage data-plane assignments, an Azure Monitor Action Group with a native IcM Incident Action,
and two scheduled query alerts. Both alerts are disabled by default so the missing-heartbeat rule
cannot page during the separate package deployment and RBAC propagation steps.

Obtain the values and approval procedure from the internal operations runbook. Do not place them in
scripts, source defaults, PR descriptions, or public issue comments.

## Deployment outline

Publish the Function package, deploy the Bicep template with approved environment-specific
parameters, and deploy the package to the resulting Function app:

```powershell
dotnet publish src\GrafanaWatchdog\Microsoft.DncEng.GrafanaWatchdog -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath grafana-watchdog.zip

$deployment = az deployment group create `
  --subscription "<subscription ID or name>" `
  --resource-group "<resource group containing the Grafana workspaces>" `
  --template-file eng\deployment\grafana-watchdog.bicep `
  --parameters baseName="grafana-watchdog-staging" `
               grafanaWorkspaceNameProduction="<production workspace name>" `
               grafanaWorkspaceNameStaging="<staging workspace name>" `
               grafanaWorkspaceNameWorkflow="<workflow workspace name>" `
               targetEnvironment="staging" `
               deployIncidentRouting=false `
  | ConvertFrom-Json

$functionAppName = $deployment.properties.outputs.functionAppName.value

az functionapp deployment source config-zip `
  --subscription "<subscription ID or name>" `
  --resource-group "<resource group containing the Grafana workspaces>" `
  --name $functionAppName `
  --src grafana-watchdog.zip
```

Azure role assignments can take several minutes to become effective after the template creates
them. If infrastructure creation or package deployment returns a storage authorization error, wait
for RBAC propagation and retry the failed deployment command. Do not enable shared-key access or add
a storage key as a workaround.

Do not treat a successful infrastructure deployment as proof that the watchdog is running. After
package deployment, confirm the timer function is listed, wait for a completed cycle, and query for
recent successful `GrafanaWorkspaceProbe` records plus a `GrafanaWatchdogHeartbeat`.

Require every configured workspace to have a recent successful probe and require a heartbeat count
greater than zero. These checks also validate the table and column names used by the two alert rules,
whose deployment-time query validation is intentionally skipped during workspace bootstrap.

After those checks succeed and the IcM connector is approved, redeploy the same staging `baseName`
with `deployIncidentRouting=true`, the approved `icmConnectionId`, `icmConnectionName`, and
`icmRoutingId`, and `enableAlerts=false`. Verify that the Action Group and both disabled alert rules
exist and that their queries evaluate successfully. Then redeploy with `enableAlerts=true`. This is
the point at which the watchdog begins paging; do not enable the rules before a healthy cycle and
incident-routing configuration are verified.

## Alert queries

The alert rules scope their queries to the dedicated Log Analytics workspace.

### Repeated workspace failures

The default configuration fires after three failed workspace cycles within 30 minutes:

```kusto
AppAvailabilityResults
| where TimeGenerated > ago(30m)
| where Name == "GrafanaWorkspaceProbe" and Success == false
| summarize FailedCycles = count() by WorkspaceName = tostring(Properties["WorkspaceName"])
| where FailedCycles >= 3
```

### Missing heartbeat

The default configuration fires when no completed-cycle heartbeat arrives for 20 minutes:

```kusto
union isfuzzy=true
  (AppEvents
  | where TimeGenerated > ago(20m)
  | where Name == "GrafanaWatchdogHeartbeat"
  | project TimeGenerated),
  (datatable(TimeGenerated: datetime) [])
| summarize HeartbeatCount = count()
| where HeartbeatCount == 0
```

Both rules evaluate every five minutes, auto-mitigate, and route only to the Action Group created by
the template. Their initial query validation is skipped because the dedicated workspace does not
create the Application Insights tables until telemetry first arrives; both rules still explicitly
depend on the Application Insights component. The missing-heartbeat query includes an empty
`datatable` fallback so a missing `AppEvents` table still produces the zero-heartbeat result during
bootstrap. The repeated-failure rule may report a query evaluation error until the first
`AppAvailabilityResults` record creates that table. After the first completed cycle, verify both
queries manually and check both scheduled query rules in Azure Monitor for evaluation errors before
considering the watchdog operational. The Action Group's native Incident Action maps Common Alert
Schema fields into the IcM title, description, severity, correlation ID, impact start time, monitor
ID, and runbook URL.

## Controlled negative test

After deployment, validate the entire path using the designated non-production workspace:

1. Confirm recent successful `GrafanaWorkspaceProbe` records for every configured workspace and a
   recent `GrafanaWatchdogHeartbeat`.
2. Remove the probe identity's Grafana Viewer assignment from the non-production workspace only.
3. Confirm that workspace produces failed cycle records, the repeated-failure alert activates, and
   the expected incident notification is created.
4. Restore the role assignment immediately.
5. Confirm probes recover and the alert auto-mitigates.

Do not run this test against production. Do not disable the Function to test the missing-heartbeat
rule without a coordinated maintenance window. Follow the internal operations runbook for the exact
target, evidence, ownership, and recovery requirements.

## Rollback

Remove the Grafana Viewer assignments for the probe identity, then delete only the resources created
by `grafana-watchdog.bicep`: the Function app, probe and storage identities, Flex Consumption plan,
deployment/runtime storage account, Application Insights component, Log Analytics workspace, Action
Group, and both scheduled query rules. The template does not modify Grafana dashboards or
Grafana-managed alert rules.
