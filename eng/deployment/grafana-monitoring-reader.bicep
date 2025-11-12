// Module to grant Monitoring Reader role at subscription scope
// This must be deployed at subscription scope, so it's a separate module

targetScope = 'subscription'

@description('The principal ID of the Grafana managed identity')
param grafanaPrincipalId string

@description('The deployment environment (for unique naming)')
param environment string

@description('The managed identity resource ID (for unique naming)')
param identityResourceId string

// Monitoring Reader role ID
var monitoringReaderRoleId = '43d0d8ad-25c7-4714-9337-8ba259a9fe05'

// Grant Monitoring Reader role to Grafana managed identity
resource monitoringReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, identityResourceId, monitoringReaderRoleId, environment)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringReaderRoleId)
    principalId: grafanaPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Grants Grafana managed identity read access to Azure Monitor resources for ${environment} environment'
  }
}

output roleAssignmentId string = monitoringReaderRole.id
