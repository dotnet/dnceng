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
