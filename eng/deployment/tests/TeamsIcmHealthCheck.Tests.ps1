BeforeAll {
  $templatePath = Join-Path $PSScriptRoot '..\..\deploy-teams-icm.yml'
  $template = Get-Content $templatePath -Raw
  $healthCheckText = [regex]::Match(
    $template,
    '(?ms)^              \$pendingHealthChecks =.*?(?=^            \})'
  ).Value
  if (!$healthCheckText) {
    throw 'Unable to locate the deployment health check.'
  }
  $healthCheck = [scriptblock]::Create($healthCheckText)
  $pollingStatements = $healthCheck.Ast.EndBlock.Statements | Where-Object {
    $_ -is [System.Management.Automation.Language.WhileStatementAst] -or
      $_ -is [System.Management.Automation.Language.IfStatementAst]
  }
  $pollHealthChecks = [scriptblock]::Create(
    ($pollingStatements.Extent.Text -join "`n")
  )
  $request = $healthCheck.Ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
      $node.GetCommandName() -eq 'az'
  }, $true)
  if (!$request) {
    throw 'Unable to locate the run-history request.'
  }
  $savedExitCode = $global:LASTEXITCODE
}

AfterAll {
  $global:LASTEXITCODE = $savedExitCode
}

Describe 'Teams-to-IcM post-enable health checks' {
  BeforeEach {
    $subscriptionId = 'test-subscription'
    $resourceGroup = 'test-resource-group'
    $workflowNames = @('dnceng-teams-icm-connector', 'dnceng-teams-icm-latency-monitor')
    $pendingHealthChecks = [System.Collections.Generic.HashSet[string]]::new(
      [string[]]$workflowNames,
      [System.StringComparer]::OrdinalIgnoreCase
    )
    $enablementStarted = [DateTime]::UtcNow.AddMinutes(-1)
    $healthCheckDeadline = [DateTime]::UtcNow.AddMinutes(1)
    $script:responses = [System.Collections.Generic.Queue[string]]::new()
    $successfulRun = @{
      value = @(@{ properties = @{
        status = 'Succeeded'
        startTime = [DateTime]::UtcNow.ToString('o')
      } })
    } | ConvertTo-Json -Depth 5 -Compress
    Mock az {
      $global:LASTEXITCODE = 0
      $script:responses.Dequeue()
    }
    Mock Start-Sleep {}
  }

  It 'passes the complete request through the Windows command-file boundary' -Skip:(!$IsWindows) {
    $azShim = Join-Path $TestDrive 'az.cmd'
    $captureArguments = Join-Path $TestDrive 'capture-arguments.ps1'
    'ConvertTo-Json -InputObject @($args) -Compress' |
      Set-Content $captureArguments -Encoding ascii
    @"
@echo off
"$PSHOME\pwsh.exe" -NoLogo -NoProfile -File "$captureArguments" %*
exit /b %errorlevel%
"@ | Set-Content $azShim -Encoding ascii
    $workflowResourceId = '/subscriptions/test/resourceGroups/test/providers/Microsoft.Logic/workflows/test'
    $invokeRequest = [scriptblock]::Create(
      ($request.Extent.Text -replace '^az ', '& $azShim ')
    )
    $arguments = (& $invokeRequest) | ConvertFrom-Json
    $LASTEXITCODE | Should -Be 0
    ($arguments -join "`n") | Should -Be (@(
      'rest'
      '--method'
      'get'
      '--url'
      "https://management.azure.com$workflowResourceId/runs?api-version=2016-06-01"
      '--url-parameters'
      '$top=20'
      '--output'
      'json'
    ) -join "`n")
  }

  It 'accepts successful post-enable runs from both scheduled workflows' {
    $script:responses.Enqueue($successfulRun)
    $script:responses.Enqueue($successfulRun)
    & $pollHealthChecks
    $pendingHealthChecks.Count | Should -Be 0
    Should -Invoke az -Times 2 -Exactly
    Should -Invoke Start-Sleep -Times 0 -Exactly
  }

  It 'does not accept old successes or recent failed runs as healthy' {
    $script:responses.Enqueue((@{ value = @(@{ properties = @{
      status = 'Succeeded'
      startTime = $enablementStarted.AddMinutes(-1).ToString('o')
    } }) } | ConvertTo-Json -Depth 5 -Compress))
    $script:responses.Enqueue((@{ value = @(@{ properties = @{
      status = 'Failed'
      startTime = [DateTime]::UtcNow.ToString('o')
    } }) } | ConvertTo-Json -Depth 5 -Compress))
    $script:responses.Enqueue($successfulRun)
    $script:responses.Enqueue($successfulRun)
    & $pollHealthChecks
    $pendingHealthChecks.Count | Should -Be 0
    Should -Invoke az -Times 4 -Exactly
    Should -Invoke Start-Sleep -Times 1 -Exactly
  }

  It 'fails explicitly when the run-history request fails' {
    Mock az { $global:LASTEXITCODE = 1 }
    { & $pollHealthChecks } | Should -Throw 'Unable to inspect post-enable runs for workflow *'
    $pendingHealthChecks.Count | Should -Be 2
  }

  It 'fails explicitly when the health-check deadline expires' {
    $healthCheckDeadline = [DateTime]::UtcNow.AddSeconds(-1)
    { & $pollHealthChecks } | Should -Throw '*did not complete a successful run within five minutes*'
    Should -Invoke az -Times 0 -Exactly
  }
}
