# Monitoring SDK

The Monitoring SDK publishes dashboards, data sources, and notifications to Azure Managed
Grafana. The repository's deployment pipeline invokes it through
`eng/deploy-managed-grafana.yml`.

By default, monitoring projects load dashboards from `dashboard/*.dashboard.json`, data
sources from `datasource/*.datasource.json`, and notifications from `notifications/*.json`.
Projects can override these locations:

```xml
<PropertyGroup>
  <DashboardDirectory>Path/To/DashboardFolder</DashboardDirectory>
  <DataSourceDirectory>Path/To/DataSourceFolder</DataSourceDirectory>
  <NotificationDirectory>Path/To/NotificationFolder</NotificationDirectory>
  <RetirementDirectory>Path/To/RetirementFolder</RetirementDirectory>
</PropertyGroup>
```

## Control alert rule evaluation cadence

Grafana evaluates every rule in a rule group at the group's interval. To manage that
interval, set `evaluationIntervalSeconds` on every managed rule in the group:

```json
{
  "folderUID": "dnceng",
  "ruleGroup": "Data Migration Alerts",
  "evaluationIntervalSeconds": 300
}
```

Grafana stores the evaluation interval on the rule group rather than on each rule.
Consequently, publishing an individual rule cannot preserve a migrated alert's native
evaluation cadence unless the SDK also updates the containing group. The SDK treats the
group as a unit so a cadence change cannot silently drop rules that were already present.

The SDK removes this deployment-only field from the individual rule payload, then updates
the complete Grafana rule group after all rules are published. Rules in the same group must
specify the same positive interval. Omitting the field leaves the existing Grafana group
interval unchanged.

## Retire managed resources

Removing an alert-rule or contact-point definition does not remove the deployed Grafana
resource. To retire exact resources, add an environment-specific
`retirements/<Environment>.retirement.json` file:

```json
{
  "alertRules": [
    "obsolete-alert-rule-uid"
  ],
  "contactPoints": [
    "Obsolete contact point name"
  ]
}
```

Retirement plans are report-only by default. After reviewing the deployment output and
confirming replacement coverage, notification-policy cleanup, stakeholder approval, and
rollback readiness, opt in to deletion with `-p:GrafanaAllowDeletes=true`.

Alert rules are deleted before contact points. Deletion is exact and idempotent, verifies
that each resource is absent, and refuses to delete a contact point while the Grafana
notification-policy tree still references it. Keep retirement entries until every
environment has applied and verified the deletion.

## Publish dashboards

Invoke the `PublishGrafana` target with the Azure Managed Grafana endpoint, an Admin API
token, the deployment environment, the Key Vault containing data-source secrets, and the
parameters file:

```powershell
dotnet build MyMonitoring.proj `
  -t:PublishGrafana `
  -p:GrafanaHost=$GrafanaEndpoint `
  -p:GrafanaAccessToken=$GrafanaAccessToken `
  -p:GrafanaKeyVaultName=$GrafanaKeyVaultName `
  -p:GrafanaEnvironment=$GrafanaEnvironment `
  -p:ParametersFile=parameters.json
```

The deployment pipeline also supplies its Azure service-connection identity so the SDK can
resolve Key Vault references.
