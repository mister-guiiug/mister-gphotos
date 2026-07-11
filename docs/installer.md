# Création de l'installeur Windows

Ce document décrit comment fabriquer l'installeur de **Google Photos Local Uploader** (fichier `mister-gphotos-Setup-1.0.0.exe`), ce que fait cet installeur sur la machine de l'utilisateur, et ce que la désinstallation supprime — ou conserve volontairement.

La première partie (fabrication) s'adresse à un développeur ; la seconde (comportement de l'installeur et désinstallation) est lisible par tous.

---

## 1. Prérequis (développeur)

| Outil | Rôle | Où l'obtenir |
|---|---|---|
| SDK .NET 8 | Compiler et publier l'application | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Inno Setup 6 | Compiler le script d'installeur `installer/setup.iss` | <https://jrsoftware.org/isdl.php> |

Après installation d'Inno Setup 6, vérifiez que le compilateur en ligne de commande `iscc` est accessible. S'il ne l'est pas, ajoutez son dossier au `PATH` (installation par défaut : `C:\Program Files (x86)\Inno Setup 6`) ou appelez-le par son chemin complet :

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

---

## 2. Procédure de fabrication (développeur)

Toutes les commandes s'exécutent depuis la racine du dépôt (`mister-tof-sync-desktop`).

### Étape 1 — Publier l'application

```powershell
.\build\publish.ps1
```

Ce script exécute `dotnet publish` sur `src\GPhotosUploader.App\GPhotosUploader.App.csproj` en configuration `Release`, pour le runtime `win-x64`, en mode **auto-contenu** (`--self-contained true`, `PublishSingleFile=false`). Le résultat est écrit dans :

```
dist\win-x64\
```

avec l'exécutable `dist\win-x64\GooglePhotosLocalUploader.exe`. L'application publiée embarque le runtime .NET : **aucune installation préalable de .NET n'est requise sur la machine cible.**

Optionnel mais recommandé avant publication : `.\build\build.ps1` compile la solution et exécute la suite de tests (`dotnet restore` + `dotnet build` + `dotnet test`).

### Étape 2 — Compiler l'installeur

```powershell
iscc installer\setup.iss
```

Le script `installer/setup.iss` empaquette tout le contenu de `dist\win-x64\*` (récursivement) et produit l'installeur dans :

```
dist\installer\mister-gphotos-Setup-1.0.0.exe
```

Le nom du fichier suit le motif `mister-gphotos-Setup-{version}` défini par `OutputBaseFilename` dans `setup.iss` (version actuelle : `1.0.0`, constante `MyAppVersion`).

> **Ordre obligatoire** : l'étape 1 doit précéder l'étape 2. Si `dist\win-x64\` n'existe pas ou est obsolète, `iscc` échouera ou empaquettera une version périmée.

---

## 3. Comportement de l'installeur (tout public)

Le script `installer/setup.iss` configure l'installeur ainsi (paramètres réels du fichier) :

- **Installation par utilisateur, sans droits administrateur** : `PrivilegesRequired=lowest`. Aucune élévation UAC n'est demandée. Le dossier d'installation proposé est `{autopf}\Google Photos Local Uploader` ; sans droits administrateur, Inno Setup le résout vers le « Program Files » propre à l'utilisateur, typiquement `C:\Users\<votre nom>\AppData\Local\Programs\Google Photos Local Uploader`.
- **Langue** : l'assistant d'installation est en **français** (seule langue déclarée : `compiler:Languages\French.isl`), avec l'interface moderne d'Inno Setup (`WizardStyle=modern`).
- **Architecture** : installation en mode 64 bits sur les systèmes compatibles x64 (`ArchitecturesInstallIn64BitMode=x64compatible`). L'application publiée cible `win-x64`.
- **Icône sur le Bureau** : proposée pendant l'installation via une case à cocher, **décochée par défaut** (tâche `desktopicon`, drapeau `unchecked`).
- **Menu Démarrer** : un raccourci « Google Photos Local Uploader » est créé ; la page de choix du groupe de programmes n'est pas affichée (`DisableProgramGroupPage=yes`).
- **Fin d'installation** : une case propose de lancer l'application immédiatement (section `[Run]`, drapeaux `nowait postinstall skipifsilent`).
- **Compression** : `lzma2` avec `SolidCompression=yes`.
- **Identifiant d'application** : `AppId {7E9D2C4A-1F5B-4E83-9A6C-2B8D0E4F6153}` — il permet à Windows de reconnaître l'application pour les mises à jour et la désinstallation.

---

## 4. Désinstallation : ce qui est supprimé, ce qui est conservé (tout public)

### Supprimé par le désinstalleur

- Les fichiers du programme installés dans le dossier d'installation (contenu copié depuis `dist\win-x64`).
- Les raccourcis créés par l'installeur (menu Démarrer et, le cas échéant, icône du Bureau).

### Conservé **volontairement** par le désinstalleur

- **Les données locales de l'application** dans `%APPDATA%\GooglePhotosLocalUploader\` : la base d'inventaire `app.db` et le dossier `logs\` (journaux quotidiens).
- **Les secrets** enregistrés dans le Gestionnaire d'identifiants Windows : entrées `GooglePhotosLocalUploader/RefreshToken` et `GooglePhotosLocalUploader/OAuthClientSecret`.

Ce choix est délibéré : il permet de réinstaller ou de mettre à jour l'application sans perdre l'inventaire des fichiers déjà uploadés ni devoir reconnecter le compte Google.

**Pour tout effacer avant de désinstaller** : ouvrez l'application et utilisez le bouton **« Supprimer les données locales de l'application »** (section « Données locales » de l'interface). Après confirmation, il efface l'inventaire SQLite, les journaux, les paramètres et les secrets du Gestionnaire d'identifiants Windows, puis ferme l'application. Vos photos locales et vos médias Google Photos ne sont **jamais** touchés — ni par ce bouton, ni par la désinstallation : l'application ne supprime aucun fichier local ni aucun média Google Photos.

Si vous avez désinstallé sans utiliser ce bouton, vous pouvez encore supprimer manuellement le dossier `%APPDATA%\GooglePhotosLocalUploader\` et, dans le Gestionnaire d'identifiants Windows (Panneau de configuration → Gestionnaire d'identifiants → Informations d'identification Windows), les deux entrées citées ci-dessus.

---

## 5. Signature de code (optionnelle, non fournie)

L'installeur produit n'est **pas signé numériquement** : le projet ne fournit ni certificat de signature de code ni configuration de signature. Conséquence pratique : au premier lancement, Windows SmartScreen peut afficher un avertissement « Windows a protégé votre ordinateur » ; l'utilisateur doit cliquer sur « Informations complémentaires » puis « Exécuter quand même ».

Si vous disposez de votre propre certificat de signature de code (certificat OV/EV acheté auprès d'une autorité de certification, ou via Azure Trusted Signing), vous pouvez signer :

1. **Les binaires publiés** après l'étape `publish.ps1` (par exemple `dist\win-x64\GooglePhotosLocalUploader.exe`) avec `signtool sign`.
2. **L'installeur lui-même**, soit en signant `dist\installer\mister-gphotos-Setup-1.0.0.exe` après compilation, soit en configurant la directive `SignTool` d'Inno Setup dans `setup.iss`.

Aucune de ces étapes n'est requise pour que l'installeur fonctionne ; elles servent uniquement à réduire les avertissements de sécurité de Windows.

---

## Récapitulatif rapide (développeur)

```powershell
# Depuis la racine du dépôt :
.\build\build.ps1        # optionnel : compile + tests
.\build\publish.ps1      # publie l'app auto-contenue dans dist\win-x64\
iscc installer\setup.iss # produit dist\installer\mister-gphotos-Setup-1.0.0.exe
```
