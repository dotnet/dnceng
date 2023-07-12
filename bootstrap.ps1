if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
  Write-Warning "Script must be run in Admin Mode!"
  exit 1
}

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

New-LocalGroup -Name "DncEngConfigurationUsers" -ErrorAction Continue
Add-LocalGroupMember -Group "DncEngConfigurationUsers" -Member $(whoami) -ErrorAction Continue

dotnet run --project .\src\Microsoft.DncEng.Configuration.Bootstrap\Microsoft.DncEng.Configuration.Bootstrap.csproj -- -r "https://vault.azure.net" -r "https://management.azure.com"