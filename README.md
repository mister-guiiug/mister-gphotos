# Google Photos Local Uploader

Application de bureau pour **Windows 10 / 11** qui envoie vos photos locales vers **Google Photos**, dossier par dossier, de façon fiable et reprenable. Elle scanne un dossier racine (sous-dossiers inclus), construit un inventaire local (base SQLite avec empreinte SHA-256 de chaque fichier), puis uploade les images par lots vers votre compte Google — avec pause, reprise après coupure réseau ou fermeture de l'application, et journal détaillé.

L'interface est entièrement en français.

---

## Ce que fait l'application

- **Scan récursif** d'un dossier racine : détection des images compatibles, calcul du hash SHA-256, indexation dans une base SQLite locale. Le scan est relançable : un fichier déjà connu et inchangé n'est pas re-analysé.
- **Upload par lots** vers Google Photos : envoi des octets (endpoint `uploads`), puis création des médias par appels `mediaItems:batchCreate` (50 éléments maximum par appel — le lot par défaut est de 20 fichiers, avec 1 à 3 uploads simultanés, 2 par défaut).
- **Reprise après interruption** : chaque étape est persistée dans SQLite. Au redémarrage, les fichiers restés « en cours d'upload » sont remis en file d'attente ; un upload token encore frais (moins de 20 h) est réutilisé sans renvoyer les octets.
- **Gestion des erreurs** : nouvelles tentatives automatiques avec backoff exponentiel (plafonné à 60 s, avec aléa), en-tête `Retry-After` de Google honoré, arrêt de sécurité après 5 échecs réseau consécutifs. Les fichiers en erreur sont retentés tant que leur compteur reste sous le maximum configuré (5 par défaut) ; un bouton « Relancer les fichiers en erreur » permet de repartir de zéro.
- **Détection de doublons par contenu** (hash SHA-256) : un même contenu présent à deux endroits du disque n'est uploadé qu'une fois ; un fichier déjà uploadé par l'application n'est jamais renvoyé.
- **Suivi en temps réel** : progression globale et par fichier, débit, temps restant estimé, compteurs (détectés / en attente / uploadés / ignorés / erreurs), onglets « Journal », « Détails des fichiers » (avec filtres par statut) et « Historique » des lots.

## Ce que l'application ne fait PAS

- Elle **ne supprime jamais** un fichier local ni un média Google Photos.
- Elle **ne lit pas** votre bibliothèque Google Photos existante (voir l'encadré ci-dessous).
- Elle n'uploade pas de vidéos ni de fichiers dépassant la limite configurée (200 Mo maximum, limite photo de Google Photos).
- Elle ne propose pas l'option « économiseur de stockage » : l'API Google Photos ne l'offre pas. Les uploads se font en qualité d'origine.

> ### ⚠️ Limites de la détection de doublons
>
> Depuis les changements de la Google Photos Library API du **31 mars 2025**, une application tierce ne peut plus lire l'ensemble de votre bibliothèque : elle ne peut relire **que les médias qu'elle a elle-même créés**. Comme l'indique l'application dans l'onglet « Paramètres » :
>
> « Google Photos ne permet pas à cette application de vérifier toute votre bibliothèque. La détection des doublons est garantie uniquement pour les fichiers déjà indexés localement ou uploadés par cette application. »
>
> Concrètement : si une photo est déjà dans Google Photos parce que vous l'y avez mise **par un autre moyen** (application mobile, site web, autre outil), cette application ne peut pas le savoir et l'uploadera à nouveau. Google Photos peut alors la dédupliquer de son côté, mais ce comportement n'est pas garanti par l'API.

> ### ⚠️ Stockage de votre compte Google
>
> Les photos uploadées **comptent dans le quota de stockage de votre compte Google** (elles sont envoyées en qualité d'origine ; l'API ne propose pas l'option « économiseur de stockage »). Vérifiez votre espace disponible avant d'uploader une grande photothèque.

---

## Prérequis

| Prérequis | Détail |
|---|---|
| Système | Windows 10 ou Windows 11 (x64) |
| Compte Google | Un compte Google Photos avec suffisamment de stockage |
| Client OAuth personnel | Un projet Google Cloud avec un client OAuth de type « Application de bureau » que **vous** créez (Client ID + Client Secret) — voir [docs/google-cloud-setup.md](docs/google-cloud-setup.md) |
| Runtime | Aucun : la version publiée est auto-contenue (le SDK .NET 8 n'est requis que pour compiler depuis les sources) |

L'application n'utilise pas de client OAuth partagé : chaque utilisateur crée le sien dans Google Cloud Console. C'est une étape unique d'environ 15 minutes, guidée par l'**assistant intégré** (onglet « Paramètres ») ou pas à pas dans [docs/google-cloud-setup.md](docs/google-cloud-setup.md). Google n'expose aucune API permettant d'automatiser entièrement cette création (voir [docs/known-limitations.md](docs/known-limitations.md)). Aucun mot de passe Google n'est jamais saisi dans l'application : la connexion se fait dans votre navigateur (OAuth 2.0 Authorization Code + PKCE, redirection locale `http://127.0.0.1:{port}/`).

---

## Installation

### Option A — Via l'installeur (recommandé)

1. Récupérez l'installeur `mister-gphotos-Setup-<version>.exe` depuis la page **Releases** du dépôt (généré automatiquement par la CI/CD à chaque tag `vX.Y.Z`, voir [docs/ci-cd.md](docs/ci-cd.md)), ou compilez-le localement (`dist\installer\`).
2. Lancez-le : installation par utilisateur, sans droits administrateur (`PrivilegesRequired=lowest`), assistant en français, icône de bureau optionnelle.
3. Démarrez « Google Photos Local Uploader » depuis le menu Démarrer.

Une **version portable** (un seul fichier `.exe`, aucune installation) est également jointe à chaque Release.

À la désinstallation, les données locales (`%APPDATA%\GooglePhotosLocalUploader`) et les secrets du Gestionnaire d'identifiants Windows ne sont volontairement **pas** supprimés : utilisez d'abord le bouton « Supprimer les données locales de l'application » (onglet « Paramètres ») si vous voulez tout effacer.

### Option B — Compilation depuis les sources (développeurs)

Prérequis : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0), et [Inno Setup 6](https://jrsoftware.org/isdl.php) si vous voulez produire l'installeur.

```powershell
# 1. Compiler la solution et exécuter les tests (54 tests)
.\build\build.ps1

# 2. Publier l'exécutable auto-contenu win-x64 dans dist\win-x64\
.\build\publish.ps1
# -> dist\win-x64\GooglePhotosLocalUploader.exe

# 3. (Optionnel) Construire l'installeur dans dist\installer\
iscc installer\setup.iss
```

---

## Démarrage rapide en 5 étapes

1. **Configurer OAuth** — Dans l'onglet « Paramètres », cliquez sur **« Assistant de configuration Google Cloud... »** : il vous guide pas à pas (création du projet, activation de l'API, écran de consentement, client « Application de bureau »), ouvre les bonnes pages de la console et **importe le fichier `client_secret_….json`** téléchargé. Alternative manuelle : [docs/google-cloud-setup.md](docs/google-cloud-setup.md) ; utilisateurs de `gcloud` : `scripts\setup-google-cloud.ps1` automatise la partie automatisable.
2. **Connecter** — Cliquez sur « Connecter mon compte Google » : votre navigateur s'ouvre, vous autorisez l'application, puis revenez dans la fenêtre (message « Connexion réussie »). Vous disposez de 5 minutes pour terminer l'autorisation.
3. **Choisir le dossier** — Cliquez sur « Parcourir... » et sélectionnez le dossier racine contenant vos photos (les sous-dossiers sont inclus).
4. **Scanner** — Cliquez sur « Scanner le dossier » : l'application inventorie les images, calcule les empreintes et signale les nouveaux fichiers, doublons et fichiers incompatibles.
5. **Uploader** — Cliquez sur « Démarrer l'upload ». Vous pouvez à tout moment utiliser « Pause », « Reprendre » ou « Arrêter » : la progression est conservée et reprend là où elle s'était arrêtée, même après fermeture de l'application.

### Formats pris en charge par défaut

`jpg, jpeg, png, webp, heic, heif, gif, tif, tiff, bmp, avif, ico` ainsi que les formats RAW `dng, cr2, cr3, crw, nef, nrw, arw, orf, raf, rw2, srw, pef, srf, sr2`. La liste est modifiable dans l'onglet « Paramètres » (extensions séparées par des virgules, sans point).

---

## Données locales, secrets et confidentialité

- **Où sont les données ?** Dans `%APPDATA%\GooglePhotosLocalUploader\` : la base `app.db` (inventaire, paramètres, historique des lots — les entrées de journal en base de plus de 90 jours sont purgées au démarrage) et le dossier `logs\` (fichiers de journaux quotidiens, conservés tant que vous ne les supprimez pas).
- **Où sont les secrets ?** Le refresh token Google et le Client Secret OAuth sont stockés dans le **Gestionnaire d'identifiants Windows** (entrées `GooglePhotosLocalUploader/RefreshToken` et `GooglePhotosLocalUploader/OAuthClientSecret`), chiffrés par Windows — jamais en clair sur le disque. Seul le Client ID (non secret) est conservé dans la base SQLite.
- **Permissions demandées à Google** : les portées minimales `photoslibrary.appendonly` (ajouter des médias), `photoslibrary.readonly.appcreateddata` (relire uniquement les médias créés par l'application), `openid` et `email` (afficher le compte connecté). L'application ne peut ni lire le reste de votre bibliothèque, ni supprimer quoi que ce soit.
- **Tout effacer** : le bouton « Supprimer les données locales de l'application » (onglet « Paramètres ») révoque le token, efface les secrets du Gestionnaire d'identifiants, supprime la base et les journaux, puis ferme l'application. Vos photos locales et vos médias Google Photos ne sont pas touchés.

---

## Choix techniques (en bref)

L'application est écrite en **C# / .NET 8** avec **WPF**, plutôt que :

- **Electron / web embarqué** : beaucoup plus lourd (moteur Chromium complet) pour une application locale qui fait surtout du hachage de fichiers et du HTTP ; WPF donne une interface Windows native, sobre et rapide.
- **WinUI 3 / MAUI** : cibles pertinentes pour du multi-plateforme ou du design moderne, mais outillage moins stable ; WPF est mature, parfaitement supporté sur Windows 10/11 et suffisant pour cette interface.
- **SDK Google officiel** : le client .NET `Google.Apis.PhotosLibrary` est déprécié ; l'application appelle donc directement l'API HTTP (`/v1/uploads` et `/v1/mediaItems:batchCreate`), ce qui réduit les dépendances et suit exactement le protocole documenté.

Le reste de la pile : `Microsoft.Data.Sqlite` pour l'inventaire local (aucun serveur de base de données), `CommunityToolkit.Mvvm` pour le modèle MVVM, et une publication **auto-contenue win-x64** qui n'exige aucune installation de .NET sur la machine cible.

---

## Documentation

| Document | Public | Contenu |
|---|---|---|
| [docs/google-cloud-setup.md](docs/google-cloud-setup.md) | Tous | Créer son projet Google Cloud et son client OAuth « Application de bureau » (Client ID / Client Secret), pas à pas |
| [docs/user-guide.md](docs/user-guide.md) | Tous | Guide d'utilisation complet : écrans, statuts des fichiers, pause/reprise, filtres, export du journal, FAQ |
| [docs/known-limitations.md](docs/known-limitations.md) | Tous | Limites connues : détection des doublons, quotas API, stockage, mode Test OAuth |
| [docs/architecture.md](docs/architecture.md) | Développeurs | Architecture des projets `GPhotosUploader.Core` / `GPhotosUploader.App`, services, flux d'upload et logique de reprise |
| [docs/database-schema.md](docs/database-schema.md) | Développeurs | Schéma de la base SQLite (`app.db`) : tables, statuts, migrations |
| [docs/build-windows.md](docs/build-windows.md) | Développeurs | Compilation, tests et publication auto-contenue (`build\publish.ps1`) |
| [docs/installer.md](docs/installer.md) | Développeurs | Création de l'installeur Windows avec Inno Setup (`installer\setup.iss`) |
| [docs/ci-cd.md](docs/ci-cd.md) | Développeurs | CI/CD GitHub Actions : tests automatiques et Release (installeur + exe portable) sur tag `vX.Y.Z` |

---

## Structure du dépôt

```
GooglePhotosUploader.sln
src/
  GPhotosUploader.Core/    Logique métier : modèles, accès SQLite, scan, OAuth, client API, orchestration d'upload
  GPhotosUploader.App/     Interface WPF (MainWindow, assistant Google Cloud, ViewModels)
  GPhotosUploader.Tests/   Tests unitaires (54 tests : logique, base de données, scanner, identifiants OAuth)
build/
  build.ps1                Restauration, compilation, tests
  publish.ps1              Publication auto-contenue win-x64 dans dist\win-x64\
scripts/
  setup-google-cloud.ps1   (Optionnel) Voie rapide gcloud : crée le projet + active l'API
installer/
  setup.iss                Script Inno Setup (installeur produit dans dist\installer\)
docs/                      Documentation détaillée (voir la table ci-dessus)
```

---

*Google Photos est une marque de Google LLC. Cette application est un outil indépendant, non affilié à Google ; elle utilise l'API publique Google Photos Library avec le client OAuth que vous créez vous-même.*
