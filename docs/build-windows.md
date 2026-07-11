# Procédure de build Windows

Ce document explique comment compiler, tester et publier **Google Photos Local Uploader**
sur Windows 10 ou 11, depuis les sources. Les sections « Prérequis » et « Récupérer les
sources » sont accessibles à tous ; les sections suivantes (compilation, artefacts,
publication, dépannage) s'adressent à un développeur ou à un utilisateur à l'aise avec la
ligne de commande.

> **Important :** l'application est une application WPF ciblant `net8.0-windows`.
> La compilation ne fonctionne **que sur Windows** — il n'est pas possible de la builder
> sous Linux ou macOS.

---

## 1. Prérequis

| Outil | Version | Obligatoire ? | Téléchargement |
|---|---|---|---|
| SDK .NET | **8.x** (SDK, pas seulement le runtime) | Oui | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows | 10 ou 11, x64 | Oui | — |
| PowerShell | 5.1 (inclus dans Windows) ou PowerShell 7 | Oui (pour les scripts `build/`) | — |
| Git | Récente | Non (un ZIP des sources suffit) | <https://git-scm.com/download/win> |
| Inno Setup | **6** | Non (uniquement pour fabriquer l'installeur) | <https://jrsoftware.org/isdl.php> |

Pour vérifier que le SDK .NET 8 est bien installé, ouvrez un terminal (PowerShell) et tapez :

```powershell
dotnet --list-sdks
```

Vous devez voir au moins une ligne commençant par `8.` (par exemple `8.0.404`). Si la
commande `dotnet` n'est pas reconnue, voyez la section [Dépannage](#7-dépannage-courant).

Aucun autre outil n'est nécessaire : Visual Studio n'est **pas** requis (le SDK .NET
suffit), et les dépendances NuGet (`Microsoft.Data.Sqlite`, `CommunityToolkit.Mvvm`,
`xunit`, etc.) sont restaurées automatiquement depuis nuget.org lors du build. Le projet
n'utilise **aucun SDK Google** : les appels à l'API Google Photos sont faits en HTTP
direct, car le client .NET officiel PhotosLibrary est déprécié.

---

## 2. Récupérer les sources

Deux possibilités :

**Avec Git :**

```powershell
git clone <URL-du-dépôt> mister-tof-sync-desktop
cd mister-tof-sync-desktop
```

**Sans Git :** téléchargez l'archive ZIP des sources depuis la page du dépôt, puis
extrayez-la dans un dossier de votre choix (par exemple `C:\Src\mister-tof-sync-desktop`).

La racine du projet doit contenir :

```
GooglePhotosUploader.sln        Solution .NET (3 projets)
src/GPhotosUploader.Core/       Bibliothèque : modèles, base SQLite, services (net8.0)
src/GPhotosUploader.App/        Application WPF (net8.0-windows)
src/GPhotosUploader.Tests/      Tests xUnit (net8.0) — 54 tests
build/build.ps1                 Script : restauration + compilation + tests
build/publish.ps1               Script : publication auto-contenue win-x64
installer/setup.iss             Script Inno Setup (installeur Windows)
docs/                           Documentation
```

---

## 3. Build et tests avec `build/build.ps1` (méthode recommandée)

Depuis la racine du projet, dans PowerShell :

```powershell
.\build\build.ps1
```

Le script effectue, dans l'ordre, et **s'arrête à la première erreur** (il propage le code
de sortie de `dotnet`) :

1. `dotnet restore GooglePhotosUploader.sln` — restauration des packages NuGet
   (affiché : `=== Restauration des packages ===`) ;
2. `dotnet build GooglePhotosUploader.sln -c Release --no-restore` — compilation
   (affiché : `=== Compilation (Release) ===`) ;
3. `dotnet test src\GPhotosUploader.Tests\GPhotosUploader.Tests.csproj -c Release --no-build`
   — exécution des tests (affiché : `=== Tests ===`).

En cas de succès, le script affiche `Build et tests OK.` en vert.

Le script accepte un paramètre optionnel `-Configuration` (valeur par défaut : `Release`) :

```powershell
.\build\build.ps1 -Configuration Debug
```

> **Si Windows bloque l'exécution du script** (message « l'exécution de scripts est
> désactivée sur ce système »), lancez-le ainsi :
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File .\build\build.ps1
> ```

---

## 4. Build et tests manuels (sans script)

Équivalent commande par commande, depuis la racine :

```powershell
dotnet restore GooglePhotosUploader.sln
dotnet build GooglePhotosUploader.sln -c Release --no-restore
dotnet test src\GPhotosUploader.Tests\GPhotosUploader.Tests.csproj -c Release --no-build
```

Pour lancer l'application directement depuis les sources (utile en développement) :

```powershell
dotnet run --project src\GPhotosUploader.App\GPhotosUploader.App.csproj -c Debug
```

---

## 5. Structure des artefacts de build

Après un `dotnet build -c Release`, les binaires sont produits sous chaque projet :

```
src/GPhotosUploader.Core/bin/Release/net8.0/
    GPhotosUploader.Core.dll

src/GPhotosUploader.App/bin/Release/net8.0-windows/
    GooglePhotosLocalUploader.exe      <- exécutable WPF (AssemblyName du projet App)
    GooglePhotosLocalUploader.dll
    GPhotosUploader.Core.dll
    CommunityToolkit.Mvvm.dll
    Microsoft.Data.Sqlite.dll (+ dépendances SQLitePCLRaw)
    ...

src/GPhotosUploader.Tests/bin/Release/net8.0/
    GPhotosUploader.Tests.dll
```

Notez que l'exécutable s'appelle **`GooglePhotosLocalUploader.exe`** (et non
« GPhotosUploader.App.exe ») : c'est l'`AssemblyName` défini dans
`src/GPhotosUploader.App/GPhotosUploader.App.csproj`.

L'exécutable produit par `dotnet build` est **framework-dependent** : il nécessite que le
runtime .NET 8 (Desktop) soit installé sur la machine. Pour un exécutable qui fonctionne
sans installation préalable de .NET, utilisez la publication auto-contenue ci-dessous.

---

## 6. Publication auto-contenue avec `build/publish.ps1`

Depuis la racine du projet :

```powershell
.\build\publish.ps1
```

Le script exécute :

```powershell
dotnet publish src\GPhotosUploader.App\GPhotosUploader.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o dist\win-x64
```

Caractéristiques :

- **Auto-contenu** (`--self-contained true`) : le runtime .NET 8 est embarqué. La machine
  cible n'a **rien à installer** au préalable.
- **Multi-fichiers** (`PublishSingleFile=false`) : le dossier contient l'exécutable et ses
  DLL, ce qui est le format attendu par le script d'installeur.
- Paramètre optionnel `-Runtime` (valeur par défaut : `win-x64`) ; le dossier de sortie
  suit le runtime choisi (`dist\<runtime>`).

**Où trouver l'exécutable :** à la fin, le script affiche le chemin exact :

```
Publication terminée : <racine>\dist\win-x64
Exécutable : <racine>\dist\win-x64\GooglePhotosLocalUploader.exe
```

Vous pouvez lancer `dist\win-x64\GooglePhotosLocalUploader.exe` directement, ou copier
tout le dossier `dist\win-x64\` sur une autre machine Windows 10/11 x64.

### Fabriquer l'installeur Windows (optionnel)

Prérequis : Inno Setup 6 installé, et la publication (`.\build\publish.ps1`) déjà faite —
l'installeur empaquette le contenu de `dist\win-x64\`. Puis :

```powershell
iscc installer\setup.iss
```

L'installeur est écrit dans `dist\installer\` sous le nom
`mister-gphotos-Setup-1.0.0.exe`. Il s'installe **sans droits administrateur**
(`PrivilegesRequired=lowest`) et est en français.

À la désinstallation, les données locales (`%APPDATA%\GooglePhotosLocalUploader`, qui
contient `app.db` et `logs\`) et les secrets du Gestionnaire d'identifiants Windows ne
sont **volontairement pas supprimés** : utilisez le bouton « Supprimer les données
locales » dans l'application avant de désinstaller si vous voulez tout effacer.

---

## 7. Dépannage courant

### « dotnet » n'est pas reconnu comme commande

- Le SDK .NET 8 n'est pas installé : téléchargez-le sur
  <https://dotnet.microsoft.com/download/dotnet/8.0> (choisissez bien **SDK**, pas
  « Runtime » seul, et l'installeur **x64**).
- Si vous venez de l'installer, **fermez puis rouvrez** le terminal (le `PATH` n'est mis à
  jour que pour les nouveaux terminaux).
- Vérifiez ensuite avec `dotnet --list-sdks` qu'une version `8.x` apparaît.

### Le SDK est installé mais la compilation échoue (NETSDK1045 ou similaire)

Vous avez probablement uniquement un SDK plus ancien (6.x, 7.x). Le projet cible
`net8.0` / `net8.0-windows` : installez le SDK **8.x** en plus (plusieurs SDK peuvent
cohabiter sans conflit).

### La restauration NuGet échoue derrière un proxy d'entreprise (erreurs NU1301, timeouts)

`dotnet restore` doit pouvoir joindre `https://api.nuget.org`. Derrière un proxy :

1. **Variables d'environnement** (le plus simple, valable pour la session en cours) :

   ```powershell
   $env:HTTP_PROXY  = "http://proxy.exemple.local:8080"
   $env:HTTPS_PROXY = "http://proxy.exemple.local:8080"
   .\build\build.ps1
   ```

2. **Configuration NuGet persistante** : ajoutez dans
   `%APPDATA%\NuGet\NuGet.Config` une section :

   ```xml
   <configuration>
     <config>
       <add key="http_proxy" value="http://proxy.exemple.local:8080" />
       <!-- si le proxy exige une authentification : -->
       <add key="http_proxy.user" value="DOMAINE\utilisateur" />
     </config>
   </configuration>
   ```

3. Si votre proxy **inspecte le TLS** (certificat d'entreprise), le certificat racine de
   l'entreprise doit être présent dans le magasin de certificats Windows, sinon la
   connexion à nuget.org sera rejetée.

4. Si votre entreprise fournit un miroir NuGet interne (Artifactory, Nexus…), déclarez-le
   comme source : `dotnet nuget add source <URL-du-miroir> -n interne`.

### « L'exécution de scripts est désactivée sur ce système »

La stratégie d'exécution PowerShell bloque les `.ps1`. Contournement ponctuel, sans
modifier la configuration de la machine :

```powershell
powershell -ExecutionPolicy Bypass -File .\build\build.ps1
```

### Des tests échouent

La suite (54 tests xUnit : `CoreLogicTests`, `DatabaseTests`, `FileScannerTests`, `OAuthClientConfigTests`) est
attendue **entièrement verte** et n'a besoin d'aucun accès réseau ni compte Google. Un
échec signale en général un SDK inadapté ou une modification locale des sources. Relancez
proprement :

```powershell
dotnet clean GooglePhotosUploader.sln
.\build\build.ps1
```

### `iscc` n'est pas reconnu

Inno Setup 6 n'est pas installé, ou son dossier (par défaut
`C:\Program Files (x86)\Inno Setup 6`) n'est pas dans le `PATH`. Vous pouvez appeler le
compilateur par son chemin complet :

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

### L'installeur Inno Setup échoue avec « Source file not found … dist\win-x64\* »

Le script `installer\setup.iss` empaquette `dist\win-x64\` : exécutez d'abord
`.\build\publish.ps1`, puis relancez `iscc installer\setup.iss`.

---

## Rappels sur ce que le build produit (limites, énoncées franchement)

- L'application utilise l'API Google Photos Library en HTTP direct avec OAuth 2.0
  (Authorization Code + PKCE, redirection loopback `http://127.0.0.1:{port}/`). Chaque
  utilisateur crée **son propre client OAuth** dans Google Cloud Console — voir
  `docs/google-cloud-setup.md`. Le build ne contient donc **aucun identifiant Google**.
- Depuis les changements d'API du 31 mars 2025, l'application ne peut relire que les
  médias **qu'elle a elle-même créés** : la détection des doublons côté Google est
  garantie uniquement pour les fichiers déjà indexés localement ou uploadés par cette
  application (voir `docs/known-limitations.md`).
- L'application ne supprime **jamais** de fichier local ni de média Google Photos.
