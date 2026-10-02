[CmdletBinding()]
param(
  [Parameter()]
  [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$deploymentInputs = @(
  'eng/deployment/teams-icm.bicep'
  'eng/deployment/teams-icm.workflow.json'
  'eng/deployment/teams-icm-connector-adapter.workflow.json'
  'eng/deployment/teams-icm-latency-monitor.workflow.json'
  'eng/deployment/teams-icm-operational-context.json'
)

$manifest = foreach ($relativePath in $deploymentInputs) {
  $fullPath = Join-Path $RepositoryRoot $relativePath
  if (!(Test-Path $fullPath -PathType Leaf)) {
    throw "Teams-to-IcM deployment input not found: $fullPath"
  }

  $fileHash = (Get-FileHash $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
  "$relativePath=$fileHash"
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($manifest -join "`n")
  $hash = $sha256.ComputeHash($bytes)
} finally {
  $sha256.Dispose()
}

($hash | ForEach-Object { $_.ToString('x2') }) -join ''
