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
    $azureFormRecognizerServiceEndpoint = "https://cog-fr-lwgeqj45b3lbe.cognitiveservices.azure.com/"

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
#src\Microsoft.DncEng.AIChatBot\prepdocs\PrepareDocs\PrepareDocs.csproj
dotnet run --project "d:\a\1\dnceng\src\Microsoft.DncEng.AIChatBot\prepdocs\PrepareDocs\PrepareDocs.csproj" -- `
    "$pdfDir/*.pdf" `
    --storageendpoint $azureStorageBlobEndpoint `
    --container $azureStorageContainer `
    --searchendpoint $azureSearchServiceEndpoint `
    --searchindex $azureSearchIndex `
    --formrecognizerendpoint $azureFormRecognizerServiceEndpoint `
    --verbose
    # --servicePrincipalId "asdf" `
    # --servicePrincipalSecret "asdf" `
