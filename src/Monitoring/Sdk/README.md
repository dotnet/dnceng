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
</PropertyGroup>
```

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
