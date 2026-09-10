@description('Azure region for the production resources and managed connector.')
param location string = resourceGroup().location

@description('Name of the Consumption Logic App.')
param logicAppName string = 'dnceng-teams-icm'

@description('Name of the Microsoft Teams API connection. Do not authorize it with a personal identity.')
param teamsConnectionName string = 'dnceng-teams-icm-teams'

@description('Whether the workflow is enabled. Keep false until the Teams connection and ICM Provider allowlist are ready.')
param workflowEnabled bool = false

@description('Email address that receives failed-run alerts.')
param alertEmail string

@description('ID of the .NET Core Eng Services Partners Team.')
param teamId string = '4d73664c-9f2f-450d-82a5-c2f02756606d'

@description('ID of the Eng Services Requests channel.')
param channelId string = '19:bfc7969db4c445e095a803be203569c1@thread.skype'

@description('Managed-identity endpoint for the ICM Provider.')
param icmProviderEndpoint string = 'https://icmprovideraf.azurewebsites.net/api/mi/AddOrUpdateIcmIncident'

@description('Entra audience used to call the ICM Provider.')
param icmProviderAudience string = 'api://c12184db-8443-4228-8236-39f60fb104d7'

@description('DDFun ICM service connector ID.')
param icmConnectorId string = '9bfe0f4f-4dcf-4033-94a3-f463e90baf04'

@description('Routing rule on the DDFun connector. This is not the display name.')
param icmRoutingRuleId string = 'DDFUNCustomerRequests'

@allowed([
  3
  4
])
@description('Severity used when a free-form request does not explicitly specify Sev3 or Sev4.')
param defaultSeverity int = 3

var resourcePrefix = 'dnceng-teams-icm'
var storageAccountName = 'dncengicm${uniqueString(subscription().id, resourceGroup().id)}'
var storageTableName = 'TeamsIcmIntake'
var teamsManagedApiId = subscriptionResourceId(
  'Microsoft.Web/locations/managedApis',
  location,
  'teams'
)
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var workflowDefinition = loadJsonContent('teams-icm.workflow.json')
var connectorAdapterDefinition = loadJsonContent('teams-icm-connector-adapter.workflow.json')
var operationalContext = loadJsonContent('teams-icm-operational-context.json')
var connectorAdapterName = '${resourcePrefix}-connector'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM durable idempotency'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource intakeTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: storageTableName
}

resource teamsConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: teamsConnectionName
  location: location
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM channel read and reply'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  properties: {
    api: {
      id: teamsManagedApiId
    }
    displayName: teamsConnectionName
  }
}

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM message processor'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: workflowEnabled ? 'Enabled' : 'Disabled'
    definition: workflowDefinition
    parameters: {
      '$connections': {
        value: {
          teams: {
            connectionId: teamsConnection.id
            connectionName: teamsConnection.name
            id: teamsManagedApiId
          }
        }
      }
      storageTableEndpoint: {
        value: storageAccount.properties.primaryEndpoints.table
      }
      storageTableName: {
        value: intakeTable.name
      }
      teamId: {
        value: teamId
      }
      channelId: {
        value: channelId
      }
      icmProviderEndpoint: {
        value: icmProviderEndpoint
      }
      icmProviderAudience: {
        value: icmProviderAudience
      }
      icmConnectorId: {
        value: icmConnectorId
      }
      icmRoutingRuleId: {
        value: icmRoutingRuleId
      }
      defaultSeverity: {
        value: defaultSeverity
      }
      operationalContext: {
        value: operationalContext
      }
    }
  }
}

resource connectorAdapter 'Microsoft.Logic/workflows@2019-05-01' = {
  name: connectorAdapterName
  location: location
  tags: {
    Environment: 'Production'
    Purpose: 'Short-interval Teams channel polling'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: workflowEnabled ? 'Enabled' : 'Disabled'
    definition: connectorAdapterDefinition
    parameters: {
      '$connections': {
        value: {
          teams: {
            connectionId: teamsConnection.id
            connectionName: teamsConnection.name
            id: teamsManagedApiId
          }
        }
      }
      processorCallbackUrl: {
        value: listCallbackUrl('${logicApp.id}/triggers/Process_Teams_Message', '2019-05-01').value
      }
      teamId: {
        value: teamId
      }
      channelId: {
        value: channelId
      }
      storageTableEndpoint: {
        value: storageAccount.properties.primaryEndpoints.table
      }
      storageTableName: {
        value: intakeTable.name
      }
    }
  }
}

resource storageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, logicApp.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: logicApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageTableDataContributorRoleId
    )
  }
}

resource connectorStorageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, connectorAdapter.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: connectorAdapter.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageTableDataContributorRoleId
    )
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${resourcePrefix}-logs'
  location: location
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM monitoring'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  properties: {
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource workflowDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: logicApp
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource connectorAdapterDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: connectorAdapter
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource failureActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${resourcePrefix}-failures'
  location: 'global'
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM failed-run notifications'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  properties: {
    enabled: true
    groupShortName: 'TeamsIcm'
    emailReceivers: [
      {
        name: 'Rollout owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource failedRunsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${resourcePrefix}-failed-runs'
  location: 'global'
  tags: {
    Environment: 'Production'
    Purpose: 'Teams to IcM failed-run detection'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  properties: {
    actions: [
      {
        actionGroupId: failureActionGroup.id
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          metricName: 'RunsFailed'
          metricNamespace: 'Microsoft.Logic/workflows'
          name: 'Failed workflow runs'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
        }
      ]
    }
    description: 'Alerts when the Teams-to-IcM production processor fails.'
    enabled: true
    evaluationFrequency: 'PT5M'
    scopes: [
      logicApp.id
    ]
    severity: 2
    targetResourceRegion: location
    targetResourceType: 'Microsoft.Logic/workflows'
    windowSize: 'PT5M'
  }
}

resource connectorAdapterFailedRunsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${connectorAdapterName}-failed-runs'
  location: 'global'
  tags: {
    Environment: 'Production'
    Purpose: 'Teams connector adapter failed-run detection'
    Service: 'DncEng'
    WorkItem: '12465'
  }
  properties: {
    actions: [
      {
        actionGroupId: failureActionGroup.id
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          metricName: 'RunsFailed'
          metricNamespace: 'Microsoft.Logic/workflows'
          name: 'Failed connector adapter runs'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
        }
      ]
    }
    description: 'Alerts when the Teams channel polling adapter fails.'
    enabled: true
    evaluationFrequency: 'PT5M'
    scopes: [
      connectorAdapter.id
    ]
    severity: 2
    targetResourceRegion: location
    targetResourceType: 'Microsoft.Logic/workflows'
    windowSize: 'PT5M'
  }
}

output logicAppName string = logicApp.name
output logicAppPrincipalId string = logicApp.identity.principalId
output connectorAdapterName string = connectorAdapter.name
output storageAccountName string = storageAccount.name
output storageTableName string = intakeTable.name
output teamsConnectionId string = teamsConnection.id
output workflowEnabled bool = workflowEnabled
