# Azure Managed Grafana watchdogs (AB#12372)

## Purpose

Azure Managed Grafana requires Entra authentication for its data-plane API. An anonymous HTTP
availability test can prove that the front door responds, but it cannot prove that authenticated
Grafana requests work. This watchdog uses a managed identity to test authenticated API access and
validates response bodies rather than relying only on HTTP status codes.

The watchdog is owned by .NET Core Engineering Services and tracked by
[AB#12372](https://dev.azure.com/dnceng/internal/_workitems/edit/12372), under the alert migration in
[AB#12284](https://dev.azure.com/dnceng/internal/_workitems/edit/12284).

## Architecture

```text
System-assigned managed identity
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

Each cycle probes these existing workspaces in the `monitoring-managed` resource group:

| Workspace | Role |
|---|---|
| `dnceng-grafana` | Production |
| `dnceng-grafana-staging` | Staging |
| `dnceng-workflow-grafana` | Workflow |

A workspace is healthy only when:

- `/api/health` returns HTTP 200 and JSON with `database` equal to `ok`.
- `/api/org` returns HTTP 200 and JSON with a positive `id`.

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

The Bicep template is intentionally not referenced from any deployment pipeline. Do not deploy it
until the DDFun IcM service administrator supplies and approves the Incident Action connection and
routing values tracked by
[AB#12394](https://dev.azure.com/dnceng/internal/_workitems/edit/12394).

The template requires:

- `icmConnectionId`: GUID of the Azure Monitor Incident Action connection configured in IcM.
- `icmConnectionName`: name of that connection.
- `icmRoutingId`: routing ID with a verified matching rule on that connection.
- `functionPackageUri`: SAS-protected URI for the published Function zip.

Deploy the template to the `monitoring-managed` resource group in subscription
`a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1` because it references the three Grafana workspaces there.
It creates dedicated Log Analytics and Application Insights resources, a Linux Consumption Function
with a system-assigned identity, Grafana Viewer assignments, an Azure Monitor Action Group with a
native IcM Incident Action, and two scheduled query alerts.

```powershell
dotnet publish src\GrafanaWatchdog\Microsoft.DncEng.GrafanaWatchdog -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath grafana-watchdog.zip

az deployment group create `
  --resource-group monitoring-managed `
  --template-file eng\deployment\grafana-watchdog.bicep `
  --parameters icmConnectionId="<IcM connection GUID>" `
               icmConnectionName="<IcM connection name>" `
               icmRoutingId="<verified routing ID>" `
               functionPackageUri="<SAS URI for grafana-watchdog.zip>"
```

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
AppEvents
| where TimeGenerated > ago(20m)
| where Name == "GrafanaWatchdogHeartbeat"
| summarize HeartbeatCount = count()
| where HeartbeatCount == 0
```

Both rules evaluate every five minutes, auto-mitigate, and route only to the Action Group created by
the template. Its native Incident Action maps Common Alert Schema fields into the IcM title,
description, severity, correlation ID, impact start time, monitor ID, and runbook URL.

## Controlled negative test

After deployment, validate the entire path using staging:

1. Confirm recent successful `GrafanaWorkspaceProbe` records for all three workspaces and a recent
   `GrafanaWatchdogHeartbeat`.
2. Remove the watchdog identity's Grafana Viewer assignment from `dnceng-grafana-staging` only.
3. Confirm staging produces failed workspace-cycle records, the repeated-failure alert activates,
   and the expected IcM notification is created.
4. Restore the role assignment immediately.
5. Confirm staging probes recover and the alert auto-mitigates.

```powershell
az role assignment delete `
  --assignee "<function principal ID>" `
  --role "Grafana Viewer" `
  --scope "<staging Grafana resource ID>"

az role assignment create `
  --assignee "<function principal ID>" `
  --role "Grafana Viewer" `
  --scope "<staging Grafana resource ID>"
```

Do not run this test against production. Do not disable the Function to test the missing-heartbeat
rule without a coordinated maintenance window.

## Rollback

Remove the three Grafana Viewer assignments for the Function identity, then delete only the resources
created by `grafana-watchdog.bicep`: the Function app, Consumption plan, storage account, Application
Insights component, Log Analytics workspace, Action Group, and both scheduled query rules. The
template does not modify Grafana dashboards or Grafana-managed alert rules.

## Current blocker

Azure Monitor supports a native IcM Incident Action through the preview
`incidentReceivers` Action Group contract. The DDFun IcM service currently has this Azure Monitor
connector:

| Setting | IcM value |
|---|---|
| Service | `DDFun Services Management` (`28131`) |
| Connection name | `DDFunServicesManagement-AzureMonitor` |
| Connection ID | `2f105876-e9e7-4395-ab04-b52a081e076f` |
| Status | Disabled |
| Enabled clouds | None |
| Routing ID | `DDFUNCustomerRequests` |
| Routing target | `DDFun Services Management / DDFun Customer Requests` |
| Default severity | 3 |

These values were verified in the IcM service administration portal on August 28, 2026. The routing
rule exists, but the connection cannot be used while it is disabled and enabled in no clouds.

The existing Grafana contact point at
`http://token-agent:5000/IcmConnector?icMRoutingId=DDFUNCustomerRequests` is internal to Grafana's
alerting environment and is not the Azure Monitor connection above.

Ask a current DDFun IcM Service Admin to approve and enable
`DDFunServicesManagement-AzureMonitor` for the Public cloud. The IcM portal currently lists Austin
Mager, Avi Pimentel, Casey Hulbert, Jason Howard, Mikolaj Konczak, and Alex Svyatenkiy as Service
Admins. After enablement, validate the Incident Action in staging and record the evidence in
AB#12394 before deploying this template to production.
