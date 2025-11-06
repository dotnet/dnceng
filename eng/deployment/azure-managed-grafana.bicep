// Azure Managed Grafana Workspace Bicep Template
@description('The Azure region where the Grafana workspace will be deployed')
param location string

@description('The name of the Grafana workspace')
param grafanaWorkspaceName string

@description('The pricing tier for the Grafana workspace')
@allowed([
  'Standard'
  'Essential'
])
param skuName string = 'Standard'

@description('The pricing tier for the Grafana key vault')
@allowed([
  'standard'
  'premium'
])
param kvSkuName string = 'standard'

@description('The key vault sku family')
@allowed([
  'A'
  'premium'
])
param kvSkuFamily string = 'A'

@description('The deployment environment (Staging or Production)')
param environment string

@description('The tenant ID for Azure AD')
param tenantId string = tenant().tenantId

@description('The Azure AD Object ID of the .NET Engineering Services group')
param dotnetEngServicesGroupId string = '65d7fc1d-2744-4669-8779-5cd7d7a6b95b'

// User-assigned managed identity for Grafana
resource grafanaUserAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: environment == 'Production' ? 'dnceng-managed-grafana' : 'dnceng-managed-grafana-staging'
  location: location
  tags: {
    Environment: environment
    Purpose: 'Azure Managed Grafana'
    Service: 'DncEng'
  }
}

// Azure Key Vault for Grafana secrets
resource grafanaKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: environment == 'Production' ? 'dnceng-amg-prod-kv' : 'dnceng-amg-int-kv'
  location: location
  tags: {
    Environment: environment
    Purpose: 'Azure Managed Grafana Secrets'
    Service: 'DncEng'
  }
  properties: {
    sku: {
      family: kvSkuFamily
      name: kvSkuName
    }
    tenantId: tenantId
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enableRbacAuthorization: true
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

// Define Key Vault role IDs
var keyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var keyVaultCertificatesOfficerRoleId = 'a4417e6f-fecd-4de8-b567-7b0420556985'
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
var keyVaultCertificateUserRoleId = 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba'
var keyVaultCryptoUserRoleId = '12338af0-0e69-4776-bea7-57ae8d297424'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// Define Grafana Admin role ID
var grafanaAdminRoleId = '22926164-76b3-42b3-bc55-97df8dab3e41'

resource grafanaKeyVaultSecretsOfficerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, keyVaultSecretsOfficerRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficerRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Reader role to Grafana managed identity
resource grafanaKeyVaultReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, readerRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Key Vault Certificate User role to Grafana managed identity
resource grafanaKeyVaultCertificateUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, keyVaultCertificateUserRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCertificateUserRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Key Vault Certificates Officer role to Grafana managed identity
resource grafanaKeyVaultCertificatesOfficerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, keyVaultCertificatesOfficerRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCertificatesOfficerRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Key Vault Crypto User role to Grafana managed identity
resource grafanaKeyVaultCryptoUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, keyVaultCryptoUserRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCryptoUserRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Key Vault Secrets User role to Grafana managed identity
resource grafanaKeyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaKeyVault.id, grafanaUserAssignedIdentity.id, keyVaultSecretsUserRoleId)
  scope: grafanaKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: grafanaUserAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Azure Managed Grafana Workspace
resource grafanaWorkspace 'Microsoft.Dashboard/grafana@2023-09-01' = {
  name: grafanaWorkspaceName
  location: location
  sku: {
    name: skuName
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${grafanaUserAssignedIdentity.id}': {}
    }
  }
  properties: {
    deterministicOutboundIP: 'Enabled'
    apiKey: 'Enabled'
    autoGeneratedDomainNameLabelScope: 'TenantReuse'
    zoneRedundancy: 'Disabled'
    publicNetworkAccess: 'Enabled'
    grafanaIntegrations: {
      azureMonitorWorkspaceIntegrations: []
    }
  }
}

// Grant Grafana Admin role to .NET Engineering Services group
resource dotnetEngServicesGrafanaAdminRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafanaWorkspace.id, dotnetEngServicesGroupId, grafanaAdminRoleId)
  scope: grafanaWorkspace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaAdminRoleId)
    principalId: dotnetEngServicesGroupId
    principalType: 'Group'
  }
}

// Output the Grafana workspace details
output grafanaWorkspaceId string = grafanaWorkspace.id
output grafanaWorkspaceName string = grafanaWorkspace.name
output grafanaWorkspaceUrl string = grafanaWorkspace.properties.endpoint
output grafanaPrincipalId string = grafanaUserAssignedIdentity.properties.principalId
output grafanaTenantId string = grafanaUserAssignedIdentity.properties.tenantId
output grafanaWorkspaceLocation string = grafanaWorkspace.location
output grafanaUserAssignedIdentityId string = grafanaUserAssignedIdentity.id
output grafanaUserAssignedIdentityName string = grafanaUserAssignedIdentity.name

// Output Key Vault details
output keyVaultId string = grafanaKeyVault.id
output keyVaultName string = grafanaKeyVault.name
output keyVaultUri string = grafanaKeyVault.properties.vaultUri
