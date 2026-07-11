# Publie l'application en exécutable auto-contenu Windows x64 dans dist\.
# Le résultat n'exige aucune installation préalable de .NET sur la machine cible.
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = "$root\dist\$Runtime"

Write-Host "=== Self-contained publish ($Runtime) ===" -ForegroundColor Cyan
dotnet publish "$root\src\GPhotosUploader.App\GPhotosUploader.App.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publish complete: $out" -ForegroundColor Green
Write-Host "Executable: $out\GooglePhotosLocalUploader.exe"
