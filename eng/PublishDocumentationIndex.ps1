# Script converts markdown documentation to pdfs 

param(
    [string]
    $markdownDir = "./arcade/Documentation",
    [string]
    $pdfDir = "./arcade/src/AIChatbot/PDFs",
    [string]
    $azureStorageBlobEndpoint = "https://stlwgeqj45b3lbe.blob.core.windows.net/content",
    [string]
    $azureStorageContainer = "content",
    [string]
    $azureSearchServiceEndpoint = "https://gptkb-lwgeqj45b3lbe.search.windows.net/",
    [string]
    $azureSearchIndex = "gptkbindex",
    [string]
    $azureFormRecognizerServiceEndpoint = "https://cog-fr-lwgeqj45b3lbe.cognitiveservices.azure.com/",
    [string]
    $azureTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"

    # [Parameter(Mandatory=$true)]
    # [string]
    # $servicePrincipalId,

    # [Parameter(Mandatory=$true)]
    # [string]
    # $servicePrincipalSecret
)

# git checkout https://github.com/dotnet/arcade.git
# git clone "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade"

if (!(Test-Path $pdfDir)) {
    New-Item -ItemType Directory -Path $pdfDir | Out-Null
}

write-host "environments variables: $env:PATH"
$env:Path += ";C:\Program Files\MiKTeX\miktex\bin\x64"
write-host "environments variables: $env:PATH"


Get-ChildItem $markdownDir -Recurse -Filter "*.md" | ForEach-Object {
    write-Host "Converting file: $($_.Name)"
    $pdfPath = Join-Path $pdfDir $_.Name.Replace(".md", ".pdf")
    pandoc $_.FullName -o $pdfPath --pdf-engine=xelatex
    
    
}

dotnet run --project "dnceng/src/prepdocs/PrepareDocs/PrepareDocs.csproj" -- `
    "$pdfDir/*.pdf" `
    --storageendpoint $azureStorageBlobEndpoint `
    --container $azureStorageContainer `
    --searchendpoint $azureSearchServiceEndpoint `
    --searchindex $azureSearchIndex `
    --formrecognizerendpoint $azureFormRecognizerServiceEndpoint `
    --tenantid $azureTenantId `
    --verbose
    # --servicePrincipalId "asdf" `
    # --servicePrincipalSecret "asdf" `
