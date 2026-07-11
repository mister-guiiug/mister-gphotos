# Publishes the application as a self-contained Windows x64 executable into dist\.
# The result requires no prior .NET installation on the target machine.
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = "$root\dist\$Runtime"

Write-Host "=== Self-contained publish ($Runtime) ===" -ForegroundColor Cyan
dotnet publish "$root\src\MisterGPhotos.App\MisterGPhotos.App.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publish complete: $out" -ForegroundColor Green
Write-Host "Executable: $out\MisterGPhotos.exe"
