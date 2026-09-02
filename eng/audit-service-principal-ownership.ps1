#!/usr/bin/env pwsh

<#
.SYNOPSIS
Audits Microsoft Entra application and service principal ownership.

.DESCRIPTION
Produces a read-only JSON report for application registrations and their
enterprise service principals. By default, the audit discovers objects owned
by the signed-in user. For a reviewed inventory, provide a manifest containing
the application IDs that are in scope.

The script never changes owners or application configuration.

.PARAMETER ManifestPath
Path to a JSON manifest containing an array of application IDs. The manifest
can be an array or an object with an "applications" array:

{
  "applications": [
    { "appId": "00000000-0000-0000-0000-000000000000" }
  ]
}

.PARAMETER MinimumActiveUserOwners
Minimum number of enabled user owners required on both the application
registration and each service principal. Defaults to 2.

.PARAMETER OutputPath
Optional path to write the JSON report. The report is always written to the
success output stream as well.

.PARAMETER IncludeOwnerDetails
Includes owner display names and types in the report. Owner object IDs are
never emitted.

.PARAMETER FailOnFindings
Exits with code 2 when the report contains findings. Graph or input failures
exit with code 1.
#>

[CmdletBinding(DefaultParameterSetName = "CurrentUser")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Manifest")]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ManifestPath,

    [Parameter(ParameterSetName = "CurrentUser")]
    [switch]$CurrentUser,

    [ValidateRange(1, 20)]
    [int]$MinimumActiveUserOwners = 2,

    [string]$OutputPath,

    [switch]$IncludeOwnerDetails,

    [switch]$FailOnFindings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-GraphAccessToken {
    $token = az account get-access-token `
        --resource-type ms-graph `
        --query accessToken `
        --output tsv

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "Unable to acquire a Microsoft Graph token. Run 'az login' and retry."
    }

    return $token
}

function Invoke-GraphCollection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [hashtable]$Headers
    )

    $items = @()
    $nextLink = if ($Uri.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
        $Uri
    } else {
        "https://graph.microsoft.com/v1.0/$($Uri.TrimStart('/'))"
    }

    while ($nextLink) {
        try {
            $response = Invoke-RestMethod -Method Get -Uri $nextLink -Headers $Headers
        } catch {
            throw "Microsoft Graph request failed for '$nextLink': $($_.Exception.Message)"
        }

        $items += @($response.value)
        $nextLink = if ($response.PSObject.Properties.Name -contains "@odata.nextLink") {
            $response.'@odata.nextLink'
        } else {
            $null
        }
    }

    return $items
}

function Get-OwnerAudit {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("applications", "servicePrincipals")]
        [string]$ResourceType,

        [Parameter(Mandatory = $true)]
        [string]$ObjectId,

        [Parameter(Mandatory = $true)]
        [hashtable]$Headers,

        [Parameter(Mandatory = $true)]
        [bool]$IncludeDetails
    )

    $owners = @(Invoke-GraphCollection `
        -Uri "$ResourceType/$ObjectId/owners?`$select=id,displayName,accountEnabled,servicePrincipalType" `
        -Headers $Headers)

    $userOwners = @($owners | Where-Object { $_.'@odata.type' -eq "#microsoft.graph.user" })
    $servicePrincipalOwners = @($owners | Where-Object { $_.'@odata.type' -eq "#microsoft.graph.servicePrincipal" })
    $otherOwners = @($owners | Where-Object {
        $_.'@odata.type' -notin @("#microsoft.graph.user", "#microsoft.graph.servicePrincipal")
    })

    $activeUserOwners = @($userOwners | Where-Object { $_.accountEnabled -eq $true })
    $disabledUserOwners = @($userOwners | Where-Object { $_.accountEnabled -eq $false })
    $unknownStateUserOwners = @($userOwners | Where-Object { $null -eq $_.accountEnabled })

    $summary = [ordered]@{
        total = $owners.Count
        activeUsers = $activeUserOwners.Count
        disabledUsers = $disabledUserOwners.Count
        usersWithUnknownState = $unknownStateUserOwners.Count
        servicePrincipals = $servicePrincipalOwners.Count
        otherDirectoryObjects = $otherOwners.Count
    }

    if ($IncludeDetails) {
        $summary.details = @($owners | ForEach-Object {
            [ordered]@{
                displayName = $_.displayName
                type = $_.'@odata.type'
                accountEnabled = if ($_.'@odata.type' -eq "#microsoft.graph.user") {
                    $_.accountEnabled
                } else {
                    $null
                }
            }
        })
    }

    return [pscustomobject]@{
        Raw = $owners
        Summary = [pscustomobject]$summary
    }
}

function Add-InventoryObject {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Inventory,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Application", "ServicePrincipal")]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        [object]$Object
    )

    if ([string]::IsNullOrWhiteSpace($Object.appId)) {
        throw "$Kind '$($Object.displayName)' does not have an application ID."
    }

    if (-not $Inventory.ContainsKey($Object.appId)) {
        $Inventory[$Object.appId] = [ordered]@{
            appId = $Object.appId
            requestedDisplayName = $null
            applications = @()
            servicePrincipals = @()
        }
    }

    if ($Kind -eq "Application") {
        $Inventory[$Object.appId].applications += $Object
    } else {
        $Inventory[$Object.appId].servicePrincipals += $Object
    }
}

$accessToken = Get-GraphAccessToken
$headers = @{
    Authorization = "Bearer $accessToken"
}

$inventory = @{}
$scopeDescription = $null

if ($PSCmdlet.ParameterSetName -eq "Manifest") {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $manifestEntries = if ($manifest.PSObject.Properties.Name -contains "applications") {
        @($manifest.applications)
    } else {
        @($manifest)
    }

    if ($manifestEntries.Count -eq 0) {
        throw "The manifest does not contain any applications."
    }

    foreach ($entry in $manifestEntries) {
        $appIdText = if ($entry -is [string]) { $entry } else { $entry.appId }
        $parsedAppId = [Guid]::Empty
        if (-not [Guid]::TryParse($appIdText, [ref]$parsedAppId)) {
            throw "Manifest application ID '$appIdText' is not a valid GUID."
        }

        $appId = $parsedAppId.ToString()
        if (-not $inventory.ContainsKey($appId)) {
            $requestedDisplayName = if (
                $entry -isnot [string] -and
                $entry.PSObject.Properties.Name -contains "displayName"
            ) {
                $entry.displayName
            } else {
                $null
            }
            $inventory[$appId] = [ordered]@{
                appId = $appId
                requestedDisplayName = $requestedDisplayName
                applications = @()
                servicePrincipals = @()
            }
        }
    }

    foreach ($item in @($inventory.Values)) {
        $applications = @(Invoke-GraphCollection `
            -Uri "applications?`$filter=appId eq '$($item.appId)'&`$select=id,appId,displayName,serviceManagementReference" `
            -Headers $headers)
        $servicePrincipals = @(Invoke-GraphCollection `
            -Uri "servicePrincipals?`$filter=appId eq '$($item.appId)'&`$select=id,appId,displayName,servicePrincipalType" `
            -Headers $headers)

        $item.applications = $applications
        $item.servicePrincipals = $servicePrincipals
    }

    $scopeDescription = "Explicit application manifest: $ManifestPath"
} else {
    $ownedApplications = @(Invoke-GraphCollection `
        -Uri "me/ownedObjects/microsoft.graph.application?`$select=id,appId,displayName,serviceManagementReference" `
        -Headers $headers)
    $ownedServicePrincipals = @(Invoke-GraphCollection `
        -Uri "me/ownedObjects/microsoft.graph.servicePrincipal?`$select=id,appId,displayName,servicePrincipalType" `
        -Headers $headers)

    foreach ($application in $ownedApplications) {
        Add-InventoryObject -Inventory $inventory -Kind Application -Object $application
    }
    foreach ($servicePrincipal in $ownedServicePrincipals) {
        Add-InventoryObject -Inventory $inventory -Kind ServicePrincipal -Object $servicePrincipal
    }

    # Current-user discovery can find only one side of an application/SP pair.
    # Resolve the other side by appId so ownership differences are visible.
    foreach ($item in @($inventory.Values)) {
        if ($item.applications.Count -eq 0) {
            $item.applications = @(Invoke-GraphCollection `
                -Uri "applications?`$filter=appId eq '$($item.appId)'&`$select=id,appId,displayName,serviceManagementReference" `
                -Headers $headers)
        }
        if ($item.servicePrincipals.Count -eq 0) {
            $item.servicePrincipals = @(Invoke-GraphCollection `
                -Uri "servicePrincipals?`$filter=appId eq '$($item.appId)'&`$select=id,appId,displayName,servicePrincipalType" `
                -Headers $headers)
        }
    }

    $scopeDescription = "Objects owned by the signed-in user. This is discovery data, not a complete DNCENG inventory."
}

$results = @()

foreach ($item in @($inventory.Values | Sort-Object appId)) {
    $issues = [System.Collections.Generic.List[string]]::new()
    $applicationResults = @()
    $servicePrincipalResults = @()
    $applicationOwnerIds = @()

    if ($item.applications.Count -eq 0) {
        $issues.Add("ApplicationRegistrationMissing")
    } elseif ($item.applications.Count -gt 1) {
        $issues.Add("MultipleApplicationRegistrationsForAppId")
    }

    foreach ($application in $item.applications) {
        $ownerAudit = Get-OwnerAudit `
            -ResourceType applications `
            -ObjectId $application.id `
            -Headers $headers `
            -IncludeDetails $IncludeOwnerDetails.IsPresent

        $applicationOwnerIds += @($ownerAudit.Raw | ForEach-Object { $_.id })

        if ($ownerAudit.Summary.total -lt $MinimumActiveUserOwners) {
            $issues.Add("ApplicationOwnerCountBelowMinimum")
        }
        if ($ownerAudit.Summary.activeUsers -lt $MinimumActiveUserOwners) {
            $issues.Add("ApplicationActiveUserOwnerCountBelowMinimum")
        }
        if ($ownerAudit.Summary.disabledUsers -gt 0) {
            $issues.Add("ApplicationHasDisabledUserOwner")
        }
        if ([string]::IsNullOrWhiteSpace($application.serviceManagementReference)) {
            $issues.Add("ApplicationMissingServiceManagementReference")
        }

        $applicationResults += [ordered]@{
            objectId = $application.id
            displayName = $application.displayName
            serviceManagementReference = $application.serviceManagementReference
            owners = $ownerAudit.Summary
        }
    }

    if ($item.servicePrincipals.Count -eq 0) {
        $issues.Add("ServicePrincipalMissing")
    } elseif ($item.servicePrincipals.Count -gt 1) {
        $issues.Add("MultipleServicePrincipalsForAppId")
    }

    $applicationOwnerIds = @($applicationOwnerIds | Sort-Object -Unique)

    foreach ($servicePrincipal in $item.servicePrincipals) {
        $ownerAudit = Get-OwnerAudit `
            -ResourceType servicePrincipals `
            -ObjectId $servicePrincipal.id `
            -Headers $headers `
            -IncludeDetails $IncludeOwnerDetails.IsPresent

        if ($ownerAudit.Summary.total -lt $MinimumActiveUserOwners) {
            $issues.Add("ServicePrincipalOwnerCountBelowMinimum")
        }
        if ($ownerAudit.Summary.activeUsers -lt $MinimumActiveUserOwners) {
            $issues.Add("ServicePrincipalActiveUserOwnerCountBelowMinimum")
        }
        if ($ownerAudit.Summary.disabledUsers -gt 0) {
            $issues.Add("ServicePrincipalHasDisabledUserOwner")
        }

        $servicePrincipalOwnerIds = @(
            $ownerAudit.Raw | ForEach-Object { $_.id } | Sort-Object -Unique
        )
        if ($item.applications.Count -gt 0) {
            $ownerDifference = @(
                Compare-Object -ReferenceObject $applicationOwnerIds -DifferenceObject $servicePrincipalOwnerIds
            )
            if ($ownerDifference.Count -gt 0) {
                $issues.Add("ApplicationAndServicePrincipalOwnersDiffer")
            }
        }

        $servicePrincipalResults += [ordered]@{
            objectId = $servicePrincipal.id
            displayName = $servicePrincipal.displayName
            servicePrincipalType = $servicePrincipal.servicePrincipalType
            owners = $ownerAudit.Summary
        }
    }

    $uniqueIssues = @($issues | Sort-Object -Unique)
    $displayName = if ($applicationResults.Count -gt 0) {
        $applicationResults[0].displayName
    } elseif ($servicePrincipalResults.Count -gt 0) {
        $servicePrincipalResults[0].displayName
    } else {
        $item.requestedDisplayName
    }

    $results += [ordered]@{
        appId = $item.appId
        displayName = $displayName
        compliant = $uniqueIssues.Count -eq 0
        issues = $uniqueIssues
        applications = $applicationResults
        servicePrincipals = $servicePrincipalResults
    }
}

$noncompliant = @($results | Where-Object { -not $_["compliant"] })
$issueCounts = [ordered]@{}
$allIssues = @($results | ForEach-Object { @($_["issues"]) })
foreach ($issue in @($allIssues | Where-Object { $_ } | Sort-Object -Unique)) {
    $issueCounts[$issue] = @($results | Where-Object { $_["issues"] -contains $issue }).Count
}

$report = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    readOnly = $true
    scope = $scopeDescription
    minimumActiveUserOwners = $MinimumActiveUserOwners
    summary = [ordered]@{
        applicationIds = $results.Count
        compliant = $results.Count - $noncompliant.Count
        withFindings = $noncompliant.Count
        issueCounts = $issueCounts
    }
    results = $results
}

$json = $report | ConvertTo-Json -Depth 15

if ($OutputPath) {
    $parentDirectory = Split-Path -Parent $OutputPath
    if ($parentDirectory -and -not (Test-Path -LiteralPath $parentDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8NoBOM
}

$json

if ($FailOnFindings -and $noncompliant.Count -gt 0) {
    exit 2
}
