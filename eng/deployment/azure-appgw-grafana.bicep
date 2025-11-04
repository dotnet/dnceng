// Azure Application Gateway with cloudapp.azure.com domains for Azure Managed Grafana
@description('The Azure region where the Application Gateway will be deployed')
param location string

@description('The deployment environment (Staging or Production)')
param environment string

@description('The Grafana workspace endpoint URL (without https://)')
param grafanaEndpoint string

@description('The SKU name for Application Gateway')
@allowed([
  'Standard_v2'
  'WAF_v2'
])
param skuName string = 'Standard_v2'

@description('The SKU tier for Application Gateway')
@allowed([
  'Standard_v2'
  'WAF_v2'
])
param skuTier string = 'Standard_v2'

@description('The capacity (instance count) for Application Gateway')
@minValue(1)
@maxValue(10)
param capacity int = 2

@description('Tags to apply to resources')
param resourceTags object = {
  Environment: environment
  Purpose: 'Azure Managed Grafana Custom Domain'
  Service: 'DncEng'
}

// Generate custom domain name based on environment and region
// Format: dnceng-managed-grafana[-staging].{region}.cloudapp.azure.com
var regionShortName = location == 'westus2' ? 'westus2' : location
var publicDnsLabel = environment == 'Production' ? 'dnceng-managed-grafana' : 'dnceng-managed-grafana-staging'
var customDomainName = '${publicDnsLabel}.${regionShortName}.cloudapp.azure.com'

// Resource names
var appGwName = environment == 'Production' ? 'dnceng-grafana-appgw' : 'dnceng-grafana-staging-appgw'
var publicIpName = environment == 'Production' ? 'dnceng-grafana-pip' : 'dnceng-grafana-staging-pip'
var vnetName = environment == 'Production' ? 'dnceng-grafana-vnet' : 'dnceng-grafana-staging-vnet'
var subnetName = 'appgw-subnet'
var backendPoolName = 'grafana-backend-pool'
var frontendPortName = 'http-port'
var frontendIpConfigName = 'appgw-frontend-ip'
var httpSettingName = 'grafana-http-setting'
var listenerName = 'http-listener'
var ruleName = 'grafana-routing-rule'
var probeName = 'grafana-health-probe'

// Virtual Network for Application Gateway
resource vnet 'Microsoft.Network/virtualNetworks@2023-05-01' = {
  name: vnetName
  location: location
  tags: resourceTags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.0.0.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// Public IP for Application Gateway with custom DNS label (creates cloudapp.azure.com domain)
resource publicIp 'Microsoft.Network/publicIPAddresses@2023-05-01' = {
  name: publicIpName
  location: location
  tags: resourceTags
  sku: {
    name: 'Standard'
    tier: 'Regional'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
    dnsSettings: {
      domainNameLabel: publicDnsLabel
    }
    idleTimeoutInMinutes: 4
  }
}

// Application Gateway
resource applicationGateway 'Microsoft.Network/applicationGateways@2023-05-01' = {
  name: appGwName
  location: location
  tags: resourceTags
  properties: {
    sku: {
      name: skuName
      tier: skuTier
      capacity: capacity
    }
    gatewayIPConfigurations: [
      {
        name: 'appgw-ip-config'
        properties: {
          subnet: {
            id: vnet.properties.subnets[0].id
          }
        }
      }
    ]
    frontendIPConfigurations: [
      {
        name: frontendIpConfigName
        properties: {
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
    frontendPorts: [
      {
        name: frontendPortName
        properties: {
          port: 80
        }
      }
    ]
    backendAddressPools: [
      {
        name: backendPoolName
        properties: {
          backendAddresses: [
            {
              fqdn: grafanaEndpoint
            }
          ]
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: httpSettingName
        properties: {
          port: 443
          protocol: 'Https'
          cookieBasedAffinity: 'Enabled'
          pickHostNameFromBackendAddress: true
          requestTimeout: 30
          probe: {
            id: resourceId('Microsoft.Network/applicationGateways/probes', appGwName, probeName)
          }
        }
      }
    ]
    httpListeners: [
      {
        name: listenerName
        properties: {
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, frontendIpConfigName)
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, frontendPortName)
          }
          protocol: 'Http'
          requireServerNameIndication: false
        }
      }
    ]
    requestRoutingRules: [
      {
        name: ruleName
        properties: {
          ruleType: 'Basic'
          priority: 100
          httpListener: {
            id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, listenerName)
          }
          backendAddressPool: {
            id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, backendPoolName)
          }
          backendHttpSettings: {
            id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, httpSettingName)
          }
        }
      }
    ]
    probes: [
      {
        name: probeName
        properties: {
          protocol: 'Https'
          path: '/api/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
          minServers: 0
          match: {
            statusCodes: [
              '200-399'
            ]
          }
        }
      }
    ]
    enableHttp2: true
  }
}

// Outputs
output applicationGatewayId string = applicationGateway.id
output applicationGatewayName string = applicationGateway.name
output publicIpAddress string = publicIp.properties.ipAddress
output publicDnsLabel string = publicDnsLabel
output customDomainName string = customDomainName
output customDomainUrl string = 'http://${customDomainName}'
output vnetId string = vnet.id
output vnetName string = vnet.name

// Usage instructions
output usageInstructions string = 'Access Grafana at: http://${customDomainName} (Application Gateway proxies HTTP to HTTPS backend)'
output accessUrl string = 'http://${customDomainName}'
