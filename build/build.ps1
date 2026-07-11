# Compile la solution et exécute les tests.
# Prérequis : SDK .NET 8 (https://dotnet.microsoft.com/download/dotnet/8.0)
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "=== Restoring packages ===" -ForegroundColor Cyan
dotnet restore "$root\GooglePhotosUploader.sln"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== Building ($Configuration) ===" -ForegroundColor Cyan
dotnet build "$root\GooglePhotosUploader.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== Tests ===" -ForegroundColor Cyan
dotnet test "$root\src\GPhotosUploader.Tests\GPhotosUploader.Tests.csproj" -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Build and tests OK." -ForegroundColor Green
