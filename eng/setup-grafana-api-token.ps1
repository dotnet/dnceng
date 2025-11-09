#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up Grafana API token in Key Vault for dashboard publishing
.DESCRIPTION
    This script helps you create and store a Grafana API token in Azure Key Vault
    for use by the dashboard publishing pipeline.
.PARAMETER Environment
    The deployment environment (Staging or Production)
.PARAMETER ApiToken
    The Grafana API token (if you already have one)
.PARAMETER KeyVaultName
    The name of the Key Vault to store the token in (optional, defaults to environment-specific vault)
.EXAMPLE
    .\setup-grafana-api-token.ps1 -Environment Staging
.EXAMPLE
    .\setup-grafana-api-token.ps1 -Environment Production -ApiToken "glsa_xxx"
.EXAMPLE
    .\setup-grafana-api-token.ps1 -Environment Staging -KeyVaultName "custom-keyvault"
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Staging", "Production")]
    [string]$Environment,
    
    [Parameter(Mandatory=$false)]
    [string]$ApiToken,
    
    [Parameter(Mandatory=$true)]
    [string]$KeyVaultName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Determine workspace and Key Vault names
$workspaceName = if ($Environment -eq "Production") { "dnceng-grafana" } else { "dnceng-grafana-staging" }
$resourceGroup = "monitoring-managed"
$keyVaultName = $KeyVaultName
$tokenSecretName = "grafana-admin-api-key"

Write-Host "=========================================="
Write-Host "Setup Grafana API Token"
Write-Host "=========================================="
Write-Host "Environment:    $Environment"
Write-Host "Workspace:      $workspaceName"
Write-Host "Key Vault:      $keyVaultName"
Write-Host "Secret Name:    $tokenSecretName"
Write-Host ""

# Get Grafana endpoint
Write-Host "Getting Grafana workspace endpoint..."
$grafanaInfo = az grafana show --name $workspaceName --resource-group $resourceGroup --query "{endpoint:properties.endpoint, status:properties.provisioningState}" -o json | ConvertFrom-Json

if (-not $grafanaInfo -or $grafanaInfo.status -ne "Succeeded") {
    Write-Error "Grafana workspace '$workspaceName' is not ready. Status: $($grafanaInfo.status)"
    exit 1
}

$grafanaEndpoint = $grafanaInfo.endpoint
Write-Host "✓ Grafana Endpoint: $grafanaEndpoint"
Write-Host ""

# Check if token already exists
Write-Host "Checking if API token already exists in Key Vault..."
$existingToken = az keyvault secret show --vault-name $keyVaultName --name $tokenSecretName --query "value" -o tsv 2>$null

if ($existingToken) {
    Write-Host "✓ Found existing token in Key Vault"
    Write-Host ""
    Write-Host "Validating token..."
    
    # Test if the token is still valid by calling Grafana API
    $headers = @{
        "Authorization" = "Bearer $existingToken"
        "Content-Type" = "application/json"
    }
    
    try {
        # Test the token by getting org info (lightweight API call)
        $testResponse = Invoke-RestMethod -Uri "$grafanaEndpoint/api/org" -Method Get -Headers $headers -ErrorAction Stop
        Write-Host "✓ Token is valid and working!"
        Write-Host "  Organization: $($testResponse.name)"
        Write-Host ""
        Write-Host "Using existing token. No need to create a new one."
        Write-Host ""
        Write-Host "=========================================="
        Write-Host "✓ Setup Complete!"
        Write-Host "=========================================="
        Write-Host ""
        Write-Host "The existing API token in Key Vault is valid."
        Write-Host "  Key Vault: $keyVaultName"
        Write-Host "  Secret:    $tokenSecretName"
        Write-Host ""
        Write-Host "The pipeline can publish dashboards to:"
        Write-Host "  $grafanaEndpoint"
        Write-Host ""
        exit 0
    } catch {
        Write-Host "⚠ Existing token is invalid or expired"
        Write-Host "  Error: $($_.Exception.Message)"
        Write-Host ""
        Write-Host "A new token will be created..."
        Write-Host ""
    }
}

# Get API token if not provided
if (-not $ApiToken) {
    Write-Host "=========================================="
    Write-Host "Automated Service Account Creation"
    Write-Host "=========================================="
    Write-Host ""
    Write-Host "This will automatically create a Grafana service account and token."
    Write-Host "Using Azure CLI to authenticate to Grafana..."
    Write-Host ""
    
    # Check if AMG extension is installed
    Write-Host "Checking Azure CLI Grafana extension..."
    $amgExtension = az extension list --query "[?name=='amg'].version" -o tsv
    if (-not $amgExtension) {
        Write-Host "Installing Azure Managed Grafana CLI extension..."
        az extension add --name amg --only-show-errors
        Write-Host "✓ Extension installed"
    } else {
        Write-Host "✓ Azure Managed Grafana extension already installed (version $amgExtension)"
    }
    Write-Host ""
    
    # Create service account using Azure CLI
    Write-Host "Creating service account 'grafana-admin'..."
    
    $serviceAccountJson = az grafana service-account create `
        --name $workspaceName `
        --resource-group $resourceGroup `
        --service-account "grafana-admin" `
        --role "Admin" `
        -o json 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        # Check if it already exists
        if ($serviceAccountJson -like "*already exists*" -or $serviceAccountJson -like "*409*") {
            Write-Host "⚠ Service account 'grafana-admin' already exists, retrieving it..."
            
            $listJson = az grafana service-account list `
                --name $workspaceName `
                --resource-group $resourceGroup `
                -o json
            
            $serviceAccounts = $listJson | ConvertFrom-Json
            $serviceAccount = $serviceAccounts | Where-Object { $_.name -eq "grafana-admin" } | Select-Object -First 1
            
            if (-not $serviceAccount) {
                Write-Error "Failed to find existing service account 'grafana-admin'"
                exit 1
            }
            
            $serviceAccountId = $serviceAccount.id
            Write-Host "✓ Found existing service account with ID: $serviceAccountId"
        } else {
            Write-Error "Failed to create service account:"
            Write-Host $serviceAccountJson
            exit 1
        }
    } else {
        $serviceAccount = $serviceAccountJson | ConvertFrom-Json
        $serviceAccountId = $serviceAccount.id
        Write-Host "✓ Service account created with ID: $serviceAccountId"
    }
    
    Write-Host ""
    
    # Create service account token (expires in 1 day = 86400 seconds)
    Write-Host "Creating service account token (expires in 1 day)..."
    
    $tokenName = "ci-cd-token-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    
    $tokenJson = az grafana service-account token create `
        --name $workspaceName `
        --resource-group $resourceGroup `
        --service-account $serviceAccountId `
        --token $tokenName `
        --time-to-live "1d" `
        -o json
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create service account token:"
        Write-Host $tokenJson
        exit 1
    }
    
    $tokenResponse = $tokenJson | ConvertFrom-Json
    $ApiToken = $tokenResponse.key
    
    Write-Host "✓ Service account token created"
    Write-Host "  Token name: $tokenName"
    Write-Host "  Token ID: $($tokenResponse.id)"
    Write-Host "  Expires in: 1 day (86400 seconds)"
    Write-Host ""
}

# Validate token format (Grafana service account tokens start with "glsa_")
if (-not $ApiToken.StartsWith("glsa_")) {
    Write-Warning "Token doesn't start with 'glsa_' - this might not be a service account token"
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") {
        Write-Host "Aborted."
        exit 1
    }
}

# Store in Key Vault
Write-Host ""
Write-Host "Storing API token in Key Vault..."

try {
    az keyvault secret set `
        --vault-name $keyVaultName `
        --name $tokenSecretName `
        --value $ApiToken `
        --output none
    
    Write-Host "✓ Token stored successfully in Key Vault"
} catch {
    Write-Error "Failed to store token in Key Vault: $_"
    Write-Host ""
    Write-Host "Make sure you have the following permissions on the Key Vault:"
    Write-Host "- Key Vault Secrets Officer (or Contributor)"
    Write-Host ""
    Write-Host "You can grant yourself access with:"
    Write-Host "az role assignment create --role 'Key Vault Secrets Officer' \"
    Write-Host "  --assignee <your-user-principal-id> \"
    Write-Host "  --scope /subscriptions/<subscription-id>/resourceGroups/$resourceGroup/providers/Microsoft.KeyVault/vaults/$keyVaultName"
    exit 1
}

Write-Host ""
Write-Host "=========================================="
Write-Host "✓ Setup Complete!"
Write-Host "=========================================="
Write-Host ""
Write-Host "The API token has been stored in:"
Write-Host "  Key Vault: $keyVaultName"
Write-Host "  Secret:    $tokenSecretName"
Write-Host ""
Write-Host "The pipeline can now publish dashboards to:"
Write-Host "  $grafanaEndpoint"
Write-Host ""
Write-Host "To test dashboard publishing locally, run:"
Write-Host "  dotnet build src\Monitoring\Monitoring.ArcadeServices\Monitoring.ArcadeServices.proj \"
Write-Host "    -t:PublishGrafana \"
Write-Host "    -p:GrafanaHost=$grafanaEndpoint \"
Write-Host "    -p:GrafanaAccessToken=<token> \"
Write-Host "    -p:GrafanaKeyVaultName=$keyVaultName \"
Write-Host "    -p:GrafanaEnvironment=$Environment \"
Write-Host "    -p:ParametersFile=parameters.json"
Write-Host ""
