<#
.SYNOPSIS
    Voie rapide (optionnelle) de préparation Google Cloud avec la CLI gcloud.

.DESCRIPTION
    Ce script automatise UNIQUEMENT ce que Google permet d'automatiser :
      1. Création du projet Google Cloud.
      2. Activation de la « Photos Library API ».

    Le reste N'EST PAS automatisable — aucune API publique, ni gcloud, ni
    Terraform, ne peut créer un écran de consentement « Externe » ni un client
    OAuth de type « Application de bureau » (les ressources google_iap_brand /
    google_iap_client de Terraform ne couvrent que les organisations Workspace
    et les clients IAP, inutilisables ici). Pour ces deux étapes, le script
    ouvre les pages de la console : suivez l'assistant intégré de l'application
    (onglet Paramètres → « Assistant de configuration Google Cloud... ») ou le
    guide docs/google-cloud-setup.md.

.PARAMETER ProjectId
    Identifiant du projet à créer (minuscules, chiffres, tirets ; unique au
    niveau mondial). Généré automatiquement si omis.

.EXAMPLE
    .\scripts\setup-google-cloud.ps1
    .\scripts\setup-google-cloud.ps1 -ProjectId mon-photos-uploader-42
#>
param(
    [string]$ProjectId
)

$ErrorActionPreference = "Stop"

# --- Prérequis : gcloud CLI ---
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Host "La CLI gcloud n'est pas installée." -ForegroundColor Yellow
    Write-Host "Deux options :"
    Write-Host "  1. Installer le Google Cloud SDK : https://cloud.google.com/sdk/docs/install"
    Write-Host "  2. Ou tout faire dans le navigateur avec l'assistant intégré de l'application"
    Write-Host "     (onglet Paramètres -> « Assistant de configuration Google Cloud... »)."
    exit 1
}

if (-not $ProjectId) {
    $ProjectId = "photos-uploader-$(Get-Random -Minimum 100000 -Maximum 999999)"
}

# --- Authentification gcloud si nécessaire ---
# NB : pas de redirection 2>$null directe sur une commande native ici — sous Windows
# PowerShell 5.1 avec $ErrorActionPreference = Stop, elle transformerait le stderr de
# gcloud en erreur fatale. On passe par cmd.exe qui gère la redirection nativement.
$activeAccount = (cmd /c "gcloud auth list --filter=status:ACTIVE --format=value(account) 2>nul") -join ""
if ($LASTEXITCODE -ne 0) { $activeAccount = "" }
if (-not $activeAccount) {
    Write-Host "=== Connexion Google requise (le navigateur va s'ouvrir) ===" -ForegroundColor Cyan
    gcloud auth login
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# --- 1. Création du projet ---
Write-Host "=== Création du projet '$ProjectId' ===" -ForegroundColor Cyan
gcloud projects create $ProjectId --name="Photos Uploader"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Échec de la création du projet (identifiant déjà pris ?). Relancez avec -ProjectId <autre-nom>." -ForegroundColor Yellow
    exit $LASTEXITCODE
}

# --- 2. Activation de la Photos Library API ---
Write-Host "=== Activation de la Photos Library API ===" -ForegroundColor Cyan
gcloud services enable photoslibrary.googleapis.com --project $ProjectId
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# --- Étapes restantes : manuelles par contrainte Google ---
Write-Host ""
Write-Host "Projet '$ProjectId' créé et API activée." -ForegroundColor Green
Write-Host ""
Write-Host "Il reste 2 étapes que Google ne permet pas d'automatiser :" -ForegroundColor Yellow
Write-Host "  3. Écran de consentement OAuth : type « Externes », ajoutez votre adresse en utilisateur test."
Write-Host "  4. Client OAuth « Application de bureau » : créez-le puis téléchargez le JSON."
Write-Host ""
Write-Host "Les deux pages vont s'ouvrir dans votre navigateur. Terminez ensuite dans l'application :"
Write-Host "onglet Paramètres -> « Assistant de configuration Google Cloud... » (étapes 4 à 6)"
Write-Host "ou importez directement le JSON téléchargé à la dernière étape de l'assistant."

Start-Process "https://console.cloud.google.com/apis/credentials/consent?project=$ProjectId"
Start-Process "https://console.cloud.google.com/apis/credentials/oauthclient?project=$ProjectId"
