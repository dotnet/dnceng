# Azure Managed Grafana Deployment Script
# This script deploys an Azure Managed Grafana workspace using Bicep

param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $true)]
    [string]$Location,
    
    [Parameter(Mandatory = $true)]
    [string]$GrafanaWorkspaceName,
    
    [Parameter(Mandatory = $false)]
    [string]$DeploymentName = "grafana-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    
    [Parameter(Mandatory = $false)]
    [switch]$WhatIf = $false
)

# Set error action preference
$ErrorActionPreference = "Stop"

Write-Host "=======================================" -ForegroundColor Cyan
Write-Host "Azure Managed Grafana Deployment Script" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan

try {
    # Check if Azure CLI is installed
    Write-Host "Checking Azure CLI installation..." -ForegroundColor Yellow
    az version 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not installed or not in PATH. Please install Azure CLI first."
    }
    Write-Host "✓ Azure CLI is installed" -ForegroundColor Green

    # Check if user is logged in
    Write-Host "Checking Azure authentication..." -ForegroundColor Yellow
    $account = az account show 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Not logged in to Azure. Please login..." -ForegroundColor Yellow
        az login
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to login to Azure"
        }
    }
    Write-Host "✓ Authenticated as: $($account.user.name)" -ForegroundColor Green

    # Set the subscription
    Write-Host "Setting subscription to: $SubscriptionId" -ForegroundColor Yellow
    az account set --subscription $SubscriptionId
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set subscription. Please check if the subscription ID is correct and you have access."
    }
    Write-Host "✓ Subscription set successfully" -ForegroundColor Green

    # Check if resource group exists, create if it doesn't
    Write-Host "Checking if resource group '$ResourceGroupName' exists..." -ForegroundColor Yellow
    az group show --name $ResourceGroupName 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Resource group doesn't exist. Creating..." -ForegroundColor Yellow
        az group create --name $ResourceGroupName --location $Location
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create resource group"
        }
        Write-Host "✓ Resource group created successfully" -ForegroundColor Green
    } else {
        Write-Host "✓ Resource group already exists" -ForegroundColor Green
    }

    # Get the Bicep file path
    $bicepFile = Join-Path $PSScriptRoot "azure-managed-grafana.bicep"
    if (!(Test-Path $bicepFile)) {
        throw "Bicep file not found at: $bicepFile"
    }
    Write-Host "✓ Bicep file found: $bicepFile" -ForegroundColor Green

    # Prepare deployment parameters
    $parameters = @{
        location = $Location
        grafanaWorkspaceName = $GrafanaWorkspaceName
        skuName = "Standard"
    }

    # Convert parameters to string format for Azure CLI
    $paramString = ($parameters.GetEnumerator() | ForEach-Object { "$($_.Key)=`"$($_.Value)`"" }) -join " "

    # Run deployment
    if ($WhatIf) {
        Write-Host "Running what-if deployment..." -ForegroundColor Yellow
        $cmd = "az deployment group what-if --resource-group $ResourceGroupName --template-file `"$bicepFile`" --parameters $paramString"
        Write-Host "Command: $cmd" -ForegroundColor Gray
        Invoke-Expression $cmd
    } else {
        Write-Host "Starting deployment..." -ForegroundColor Yellow
        Write-Host "Deployment name: $DeploymentName" -ForegroundColor Gray
        Write-Host "Resource group: $ResourceGroupName" -ForegroundColor Gray
        Write-Host "Grafana workspace name: $GrafanaWorkspaceName" -ForegroundColor Gray
        
        $cmd = "az deployment group create --resource-group $ResourceGroupName --name $DeploymentName --template-file `"$bicepFile`" --parameters $paramString"
        Write-Host "Command: $cmd" -ForegroundColor Gray
        
        $result = Invoke-Expression $cmd | ConvertFrom-Json
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "=======================================" -ForegroundColor Green
            Write-Host "✓ Deployment completed successfully!" -ForegroundColor Green
            Write-Host "=======================================" -ForegroundColor Green
            
            # Display outputs
            if ($result.properties.outputs) {
                Write-Host "Deployment Outputs:" -ForegroundColor Cyan
                $result.properties.outputs | ConvertTo-Json -Depth 3 | Write-Host
            }
            
            # Get the Grafana workspace details
            Write-Host "`nGrafana Workspace Details:" -ForegroundColor Cyan
            $grafana = az grafana show --name $GrafanaWorkspaceName --resource-group $ResourceGroupName | ConvertFrom-Json
            Write-Host "Workspace Name: $($grafana.name)" -ForegroundColor White
            Write-Host "Workspace URL: $($grafana.properties.endpoint)" -ForegroundColor White
            Write-Host "Location: $($grafana.location)" -ForegroundColor White
            Write-Host "SKU: $($grafana.sku.name)" -ForegroundColor White
            Write-Host "System Managed Identity: $($grafana.identity.principalId)" -ForegroundColor White
        } else {
            throw "Deployment failed"
        }
    }
}
catch {
    Write-Host "=======================================" -ForegroundColor Red
    Write-Host "❌ Error occurred during deployment:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "=======================================" -ForegroundColor Red
    exit 1
}

Write-Host "`n🎉 Script completed successfully!" -ForegroundColor Green
Write-Host "You can now access your Grafana workspace and configure it as needed." -ForegroundColor Yellow
