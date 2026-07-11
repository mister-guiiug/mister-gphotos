<#
.SYNOPSIS
    Optional fast track for Google Cloud preparation using the gcloud CLI.

.DESCRIPTION
    This script automates ONLY what Google allows to be automated:
      1. Creating the Google Cloud project.
      2. Enabling the "Photos Library API".

    The rest is NOT automatable — no public API, neither gcloud nor Terraform can
    create an "External" consent screen or a "Desktop app" OAuth client (Terraform's
    google_iap_brand / google_iap_client resources only cover Workspace organizations
    and IAP clients, which are unusable here). For those two steps, the script opens
    the console pages: follow the application's built-in wizard (Settings tab ->
    "Google Cloud setup wizard...") or the docs/google-cloud-setup.md guide.

.PARAMETER ProjectId
    Identifier of the project to create (lowercase, digits, hyphens; globally unique).
    Generated automatically if omitted.

.EXAMPLE
    .\scripts\setup-google-cloud.ps1
    .\scripts\setup-google-cloud.ps1 -ProjectId my-photos-uploader-42
#>
param(
    [string]$ProjectId
)

$ErrorActionPreference = "Stop"

# --- Prerequisite: gcloud CLI ---
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Host "The gcloud CLI is not installed." -ForegroundColor Yellow
    Write-Host "Two options:"
    Write-Host "  1. Install the Google Cloud SDK: https://cloud.google.com/sdk/docs/install"
    Write-Host "  2. Or do everything in the browser with the application's built-in wizard"
    Write-Host "     (Settings tab -> 'Google Cloud setup wizard...')."
    exit 1
}

if (-not $ProjectId) {
    $ProjectId = "photos-uploader-$(Get-Random -Minimum 100000 -Maximum 999999)"
}

# --- gcloud authentication if needed ---
# NB: no direct 2>$null redirection on a native command here — on Windows PowerShell
# 5.1 with $ErrorActionPreference = Stop it would turn gcloud's stderr into a fatal
# error. We go through cmd.exe, which handles the redirection natively.
$activeAccount = (cmd /c "gcloud auth list --filter=status:ACTIVE --format=value(account) 2>nul") -join ""
if ($LASTEXITCODE -ne 0) { $activeAccount = "" }
if (-not $activeAccount) {
    Write-Host "=== Google sign-in required (the browser will open) ===" -ForegroundColor Cyan
    gcloud auth login
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# --- 1. Create the project ---
Write-Host "=== Creating project '$ProjectId' ===" -ForegroundColor Cyan
gcloud projects create $ProjectId --name="Photos Uploader"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Project creation failed (identifier already taken?). Re-run with -ProjectId <other-name>." -ForegroundColor Yellow
    exit $LASTEXITCODE
}

# --- 2. Enable the Photos Library API ---
Write-Host "=== Enabling the Photos Library API ===" -ForegroundColor Cyan
gcloud services enable photoslibrary.googleapis.com --project $ProjectId
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# --- Remaining steps: manual, by Google constraint ---
Write-Host ""
Write-Host "Project '$ProjectId' created and API enabled." -ForegroundColor Green
Write-Host ""
Write-Host "Two steps remain that Google does not allow to be automated:" -ForegroundColor Yellow
Write-Host "  3. OAuth consent screen: type 'External', add your address as a test user."
Write-Host "  4. 'Desktop app' OAuth client: create it, then download the JSON."
Write-Host ""
Write-Host "Both pages will open in your browser. Then finish in the application:"
Write-Host "Settings tab -> 'Google Cloud setup wizard...' (steps 4 to 6)"
Write-Host "or import the downloaded JSON directly at the last step of the wizard."

Start-Process "https://console.cloud.google.com/apis/credentials/consent?project=$ProjectId"
Start-Process "https://console.cloud.google.com/apis/credentials/oauthclient?project=$ProjectId"
