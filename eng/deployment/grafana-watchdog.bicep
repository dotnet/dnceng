// Standalone infrastructure for an authenticated Azure Managed Grafana watchdog.
//
// This template is intentionally NOT referenced from azure-pipelines.yml, deploy-managed-grafana.yml,
// or any other deployment template in this repo. It must be deployed manually (or from a dedicated
// pipeline added later) once the IcM connection and routing values are approved. See:
//   Documentation/ProjectDocs/Operations/Azure-Managed-Grafana-Watchdogs.md
//
// Deploy this template to the same resource group as the existing Grafana workspaces because it
// looks them up as `existing` resources to read their auto-generated endpoints and to grant the
// watchdog's managed identity Grafana Viewer access on each of them.
//
// Example deployment (do not run until the IcM connection parameters are known):
//   az deployment group create \
//     --resource-group <resource group containing the Grafana workspaces> \
//     --template-file eng/deployment/grafana-watchdog.bicep \
//     --parameters icmConnectionId=<IcM connection GUID> \
//                  icmConnectionName=<IcM connection name> \
//                  icmRoutingId=<IcM routing ID>

@description('''
GUID of the Azure Monitor Incident Action connection configured in IcM. This is service-specific and
must be supplied by the IcM service administrator.
''')
@minLength(1)
param icmConnectionId string

@description('Name of the Azure Monitor Incident Action connection configured in IcM.')
@minLength(1)
param icmConnectionName string

@description('Routing ID that has a verified matching rule on the supplied IcM connection.')
@minLength(1)
param icmRoutingId string

@description('Azure region for all resources created by this template.')
param location string = resourceGroup().location

@description('Base name used to derive resource names for the watchdog function, storage account, App Insights, and Log Analytics workspace.')
param baseName string = 'grafana-watchdog'

@description('Name of the production Grafana workspace to probe and grant Grafana Viewer access to. Must already exist in this resource group.')
@minLength(1)
param grafanaWorkspaceNameProduction string

@description('Name of the staging Grafana workspace to probe and grant Grafana Viewer access to. Must already exist in this resource group.')
@minLength(1)
param grafanaWorkspaceNameStaging string

@description('Name of the workflow Grafana workspace to probe and grant Grafana Viewer access to. Must already exist in this resource group.')
@minLength(1)
param grafanaWorkspaceNameWorkflow string

@description('Number of additional attempts made after an initial transient failure for a single HTTP probe.')
param retryCount int = 1

@description('Per-HTTP-request timeout applied to each probe attempt, formatted as a .NET TimeSpan string (hh:mm:ss).')
param requestTimeout string = '00:00:15'

@description('Enables both scheduled query alert rules. Leave false until the Function package is deployed and its first healthy cycle is verified.')
param enableAlerts bool = false

@description('Number of failed probe cycles for a single workspace required to fire the repeated-failures alert.')
param repeatedFailureThreshold int = 3

@description('Lookback window in minutes evaluated by the repeated-failures alert.')
param repeatedFailureWindowMinutes int = 30

@description('Lookback window in minutes evaluated by the missing-heartbeat alert. Should comfortably exceed the 5 minute probe cycle.')
param missingHeartbeatWindowMinutes int = 20

@description('How often (ISO 8601 duration) both alert rules are evaluated.')
param alertEvaluationFrequency string = 'PT5M'

var uniqueSuffix = uniqueString(resourceGroup().id, baseName)
var storageAccountName = toLower(take('gwds${uniqueSuffix}', 24))
var functionAppName = toLower('${baseName}-${uniqueSuffix}')
var appServicePlanName = '${baseName}-plan'
var appInsightsName = '${baseName}-ai'
var logAnalyticsName = '${baseName}-law'
var actionGroupName = '${baseName}-icm'
var grafanaViewerRoleId = '60921a7e-fef1-4a43-9b16-a26c52ad4769'
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var probeIdentityName = '${baseName}-probe'
var storageIdentityName = '${baseName}-storage'
var deploymentStorageContainerName = 'app-package-${take(uniqueSuffix, 24)}'
var repeatedFailureWindow = 'PT${repeatedFailureWindowMinutes}M'
var repeatedFailureLookback = '${repeatedFailureWindowMinutes}m'
var missingHeartbeatWindow = 'PT${missingHeartbeatWindowMinutes}M'
var missingHeartbeatLookback = '${missingHeartbeatWindowMinutes}m'
var alertRuleExpression = format('{0}{1}', '$', '{data.essentials.alertRule}')
var descriptionExpression = format('{0}{1}', '$', '{data.essentials.description}')
var firedDateTimeExpression = format('{0}{1}', '$', '{data.essentials.firedDateTime}')
var monitorConditionExpression = format('{0}{1}', '$', '{data.essentials.monitorCondition}')
var originAlertIdExpression = format('{0}{1}', '$', '{data.essentials.originAlertId}')
var severityExpression = format('{0}{1}', '$', '{data.essentials.severity}')
var watchdogRunbookUrl = 'https://github.com/dotnet/dnceng/blob/main/Documentation/ProjectDocs/Operations/Azure-Managed-Grafana-Watchdogs.md'

// A row is returned only when a workspace has repeatedFailureThreshold or more failed probes
// (AvailabilityTelemetry.Success == false) within repeatedFailureWindow; no rows means healthy.
var repeatedFailureQuery = '''
AppAvailabilityResults
| where TimeGenerated > ago({0})
| where Name == "GrafanaWorkspaceProbe" and Success == false
| summarize FailedCycles = count() by WorkspaceName = tostring(Properties["WorkspaceName"])
| where FailedCycles >= {1}
'''

// A single row (HeartbeatCount == 0) is returned only when no GrafanaWatchdogHeartbeat event was
// recorded in the window, meaning the watchdog itself stopped running; otherwise zero rows.
var missingHeartbeatQuery = '''
union isfuzzy=true
  (AppEvents
  | where TimeGenerated > ago({0})
  | where Name == "GrafanaWatchdogHeartbeat"
  | project TimeGenerated),
  (datatable(TimeGenerated: datetime) [])
| summarize HeartbeatCount = count()
| where HeartbeatCount == 0
'''

resource grafanaProduction 'Microsoft.Dashboard/grafana@2023-09-01' existing = {
  name: grafanaWorkspaceNameProduction
}

resource grafanaStaging 'Microsoft.Dashboard/grafana@2023-09-01' existing = {
  name: grafanaWorkspaceNameStaging
}

resource grafanaWorkflow 'Microsoft.Dashboard/grafana@2023-09-01' existing = {
  name: grafanaWorkspaceNameWorkflow
}

resource probeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: probeIdentityName
  location: location
}

resource storageIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: storageIdentityName
  location: location
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Workspace-based Application Insights: AppAvailabilityResults / AppEvents are queried by the
// scheduled query alerts below via the component resource scope, per Azure Monitor's documented
// behavior for workspace-based Application Insights resources.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
  }
}

resource icmActionGroup 'Microsoft.Insights/actionGroups@2024-10-01-preview' = {
  name: actionGroupName
  location: 'Global'
  properties: {
    groupShortName: 'grafana-wd'
    enabled: true
    incidentReceivers: [
      {
        name: 'DDFun Customer Requests'
        connection: {
          id: icmConnectionId
          name: icmConnectionName
        }
        incidentManagementService: 'Icm'
        mappings: {
          'icm.automitigationenabled': 'true'
          'icm.correlationid': originAlertIdExpression
          'icm.description': descriptionExpression
          'icm.impactstartdate': firedDateTimeExpression
          'icm.monitorid': alertRuleExpression
          'icm.occurringlocation.environment': 'PROD'
          'icm.routingid': icmRoutingId
          'icm.severity': severityExpression
          'icm.title': format('[{0}] {1} - {2}', monitorConditionExpression, alertRuleExpression, descriptionExpression)
          'icm.tsgid': watchdogRunbookUrl
        }
      }
    ]
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource deploymentStorageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: deploymentStorageContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource functionStorageBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, storageIdentity.id, storageBlobDataOwnerRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: storageIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionStorageQueueContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, storageIdentity.id, storageQueueDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
    principalId: storageIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionStorageTableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, storageIdentity.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: storageIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${probeIdentity.id}': {}
      '${storageIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: storageIdentity.properties.clientId
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: probeIdentity.properties.clientId
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'GrafanaWatchdog__RetryCount'
          value: string(retryCount)
        }
        {
          name: 'GrafanaWatchdog__RequestTimeout'
          value: requestTimeout
        }
        {
          name: 'GrafanaWatchdog__Workspaces__0__Name'
          value: grafanaWorkspaceNameProduction
        }
        {
          name: 'GrafanaWatchdog__Workspaces__0__Endpoint'
          value: grafanaProduction.properties.endpoint
        }
        {
          name: 'GrafanaWatchdog__Workspaces__1__Name'
          value: grafanaWorkspaceNameStaging
        }
        {
          name: 'GrafanaWatchdog__Workspaces__1__Endpoint'
          value: grafanaStaging.properties.endpoint
        }
        {
          name: 'GrafanaWatchdog__Workspaces__2__Name'
          value: grafanaWorkspaceNameWorkflow
        }
        {
          name: 'GrafanaWatchdog__Workspaces__2__Endpoint'
          value: grafanaWorkflow.properties.endpoint
        }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}${deploymentStorageContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: storageIdentity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
  }
  dependsOn: [
    deploymentStorageContainer
    functionStorageBlobOwner
    functionStorageQueueContributor
    functionStorageTableContributor
  ]
}

resource grafanaViewerForProduction 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaProduction.id, probeIdentity.id, grafanaViewerRoleId)
  scope: grafanaProduction
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaViewerRoleId)
    principalId: probeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource grafanaViewerForStaging 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaStaging.id, probeIdentity.id, grafanaViewerRoleId)
  scope: grafanaStaging
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaViewerRoleId)
    principalId: probeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource grafanaViewerForWorkflow 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaWorkflow.id, probeIdentity.id, grafanaViewerRoleId)
  scope: grafanaWorkflow
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaViewerRoleId)
    principalId: probeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource repeatedFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-repeated-failures'
  location: location
  properties: {
    displayName: 'Grafana Watchdog - Repeated Probe Failures'
    description: 'Fires when a Grafana workspace has ${repeatedFailureThreshold} or more failed authenticated probe cycles within ${repeatedFailureWindow}. Routed through the watchdog IcM Incident Action.'
    severity: 2
    enabled: enableAlerts
    evaluationFrequency: alertEvaluationFrequency
    windowSize: repeatedFailureWindow
    skipQueryValidation: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    targetResourceTypes: [
      'microsoft.operationalinsights/workspaces'
    ]
    criteria: {
      allOf: [
        {
          query: replace(replace(repeatedFailureQuery, '{0}', repeatedFailureLookback), '{1}', string(repeatedFailureThreshold))
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        icmActionGroup.id
      ]
    }
  }
  dependsOn: [
    appInsights
  ]
}

resource missingHeartbeatAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-missing-heartbeat'
  location: location
  properties: {
    displayName: 'Grafana Watchdog - Missing Heartbeat'
    description: 'Fires when no GrafanaWatchdog heartbeat event has been recorded within ${missingHeartbeatWindow}, indicating the watchdog Function itself has stopped running (as opposed to running but seeing probe failures). Routed through the watchdog IcM Incident Action.'
    severity: 1
    enabled: enableAlerts
    evaluationFrequency: alertEvaluationFrequency
    windowSize: missingHeartbeatWindow
    skipQueryValidation: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    targetResourceTypes: [
      'microsoft.operationalinsights/workspaces'
    ]
    criteria: {
      allOf: [
        {
          query: replace(missingHeartbeatQuery, '{0}', missingHeartbeatLookback)
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        icmActionGroup.id
      ]
    }
  }
  dependsOn: [
    appInsights
  ]
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = probeIdentity.properties.principalId
output appInsightsName string = appInsights.name
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output icmActionGroupResourceId string = icmActionGroup.id
