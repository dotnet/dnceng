#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generate or retrieve SSL certificate from Azure Key Vault for Application Gateway
.DESCRIPTION
    Creates a self-signed certificate in Azure Key Vault for the cloudapp.azure.com custom domain.
    If the certificate already exists, it retrieves the secret URI.
    Application Gateway references the certificate directly from Key Vault via managed identity.
.PARAMETER DnsName
    The DNS name for the certificate (e.g., dnceng-managed-grafana-staging.westus2.cloudapp.azure.com)
.PARAMETER KeyVaultName
    The name of the Azure Key Vault to store the certificate
.PARAMETER CertificateName
    The name of the certificate in Key Vault (default: appgw-ssl-cert)
.PARAMETER ResourceGroupName
    The resource group name for the Key Vault
.EXAMPLE
    .\generate-appgw-cert.ps1 -DnsName "dnceng-managed-grafana-staging.westus2.cloudapp.azure.com" -KeyVaultName "dnceng-kv" -ResourceGroupName "monitoring-managed"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$DnsName,
    
    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,
    
    [Parameter(Mandatory = $false)]
    [string]$CertificateName = "appgw-ssl-cert",
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "westus2"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Azure Key Vault Certificate Setup" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "DNS Name: $DnsName" -ForegroundColor White
Write-Host "Key Vault: $KeyVaultName" -ForegroundColor White
Write-Host "Certificate: $CertificateName" -ForegroundColor White
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor White
Write-Host ""

# Check if Key Vault exists (should already exist from Grafana provisioning)
Write-Host "Verifying Key Vault exists..." -ForegroundColor Yellow
$kvExists = az keyvault show --name $KeyVaultName --resource-group $ResourceGroupName 2>$null

if (!$kvExists) {
    Write-Error "Key Vault '$KeyVaultName' not found. It should have been created during Grafana provisioning."
    Write-Host "Expected Key Vault names:" -ForegroundColor Yellow
    Write-Host "  Production: dnceng-amg-prod-kv" -ForegroundColor White
    Write-Host "  Staging: dnceng-amg-int-kv" -ForegroundColor White
    exit 1
}

Write-Host "✓ Key Vault exists (from Grafana provisioning)" -ForegroundColor Green

# Check if certificate already exists
Write-Host ""
Write-Host "Checking if certificate exists in Key Vault..." -ForegroundColor Yellow
$certExists = az keyvault certificate show `
    --vault-name $KeyVaultName `
    --name $CertificateName `
    --query "id" `
    --output tsv 2>$null

if ($certExists) {
    Write-Host "✓ Certificate '$CertificateName' already exists" -ForegroundColor Green
    Write-Host "  Using existing certificate" -ForegroundColor White
} else {
    Write-Host "Certificate not found. Creating self-signed certificate..." -ForegroundColor Yellow
    
    # Create certificate policy for self-signed cert
    $policy = @"
{
  "issuerParameters": {
    "name": "Self"
  },
  "x509CertificateProperties": {
    "subject": "CN=$DnsName",
    "subjectAlternativeNames": {
      "dnsNames": ["$DnsName"]
    },
    "validityInMonths": 12,
    "keyUsage": [
      "digitalSignature",
      "keyEncipherment"
    ],
    "ekus": [
      "1.3.6.1.5.5.7.3.1"
    ]
  },
  "keyProperties": {
    "exportable": true,
    "keyType": "RSA",
    "keySize": 2048,
    "reuseKey": false
  },
  "secretProperties": {
    "contentType": "application/x-pkcs12"
  }
}
"@
    
    $policyFile = Join-Path $env:TEMP "cert-policy-$([Guid]::NewGuid()).json"
    $policy | Out-File -FilePath $policyFile -Encoding UTF8
    
    # Create certificate in Key Vault
    Write-Host "Creating certificate in Key Vault (this may take 10-15 seconds)..." -ForegroundColor Yellow
    
    az keyvault certificate create `
        --vault-name $KeyVaultName `
        --name $CertificateName `
        --policy "@$policyFile" `
        --output none
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create certificate in Key Vault"
        Remove-Item $policyFile -Force
        exit 1
    }
    
    Remove-Item $policyFile -Force
    
    Write-Host "✓ Self-signed certificate created successfully" -ForegroundColor Green
}

# Get certificate secret ID (for Application Gateway)
Write-Host ""
Write-Host "Retrieving certificate secret ID..." -ForegroundColor Yellow

$secretId = az keyvault certificate show `
    --vault-name $KeyVaultName `
    --name $CertificateName `
    --query "sid" `
    --output tsv

if ([string]::IsNullOrEmpty($secretId)) {
    Write-Error "Failed to retrieve certificate secret ID"
    exit 1
}

# Get unversioned secret ID (recommended for App Gateway)
$unversionedSecretId = $secretId -replace '/[^/]+$', ''

Write-Host "✓ Certificate secret ID retrieved" -ForegroundColor Green
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Certificate Details" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Secret ID (versioned):" -ForegroundColor White
Write-Host "  $secretId" -ForegroundColor Gray
Write-Host ""
Write-Host "Secret ID (unversioned - recommended):" -ForegroundColor White
Write-Host "  $unversionedSecretId" -ForegroundColor Gray
Write-Host ""

# Get certificate details
$certDetails = az keyvault certificate show `
    --vault-name $KeyVaultName `
    --name $CertificateName `
    --output json | ConvertFrom-Json

$thumbprint = $certDetails.x509Thumbprint
$expiryDate = $certDetails.attributes.expires
$issuer = $certDetails.policy.issuerParameters.name

Write-Host "Thumbprint: $thumbprint" -ForegroundColor White
Write-Host "Issuer: $issuer" -ForegroundColor White
Write-Host "Expires: $expiryDate" -ForegroundColor White
Write-Host ""

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Next Steps" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Grant Application Gateway access to Key Vault" -ForegroundColor Yellow
Write-Host "   - Enable managed identity on Application Gateway" -ForegroundColor White
Write-Host "   - Grant 'Get' permission on secrets to the identity" -ForegroundColor White
Write-Host ""
Write-Host "2. Use the unversioned secret ID in Bicep template" -ForegroundColor Yellow
Write-Host "   - This allows automatic certificate rotation" -ForegroundColor White
Write-Host ""
Write-Host "    Self-signed certificate notes:" -ForegroundColor Yellow
Write-Host "   - Browser will show security warning" -ForegroundColor White
Write-Host "   - Valid for 12 months" -ForegroundColor White
Write-Host "   - For production, replace with CA-signed certificate" -ForegroundColor White
Write-Host ""

# Output for pipeline use
Write-Host "Setting pipeline variables..." -ForegroundColor Yellow
Write-Host "##vso[task.setvariable variable=KeyVaultSecretId]$unversionedSecretId"
Write-Host "##vso[task.setvariable variable=CertificateThumbprint]$thumbprint"
Write-Host "##vso[task.setvariable variable=KeyVaultName]$KeyVaultName"

Write-Host ""
Write-Host "✓ Certificate setup complete!" -ForegroundColor Green
