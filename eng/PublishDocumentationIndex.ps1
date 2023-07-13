# Script converts markdown documentation to pdfs 

param(
    [string]
    $markdownDir,
    [string]
    $pdfDir,
    [string]
    $azureStorageBlobEndpoint,
    [string]
    $azureStorageContainer,
    [string]
    $azureSearchServiceEndpoint,
    [string]
    $azureSearchIndex,
    [string]
    $azureFormRecognizerServiceEndpoint,
    [string]
    $azureTenantId
)

$markdownDir = "./Documentation"
$pdfDir = "./src/AIChatbot/PDFs"
$azureStorageBlobEndpoint = "https://stlwgeqj45b3lbe.blob.core.windows.net/content"
$azureStorageContainer = "content"
$azureSearchServiceEndpoint = "https://gptkb-lwgeqj45b3lbe.search.windows.net/"
$azureSearchIndex = "gptkbindex"
$azureFormRecognizerServiceEndpoint = "https://cog-fr-lwgeqj45b3lbe.cognitiveservices.azure.com/"
$azureTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"


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

dotnet run --project "src/AIChatbot/prepdocs/PrepareDocs/PrepareDocs.csproj" -- `
    "$pdfDir/*.pdf" `
    --storageendpoint $azureStorageBlobEndpoint `
    --container $azureStorageContainer `
    --searchendpoint $azureSearchServiceEndpoint `
    --searchindex $azureSearchIndex `
    --formrecognizerendpoint $azureFormRecognizerServiceEndpoint `
    --tenantid $azureTenantId `
    --verbose
