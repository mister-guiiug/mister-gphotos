# Intégration et livraison continues (GitHub Actions)

Le dépôt contient deux workflows dans `.github/workflows/`.

## 1. CI — `ci.yml`

**Déclenchement** : à chaque poussée sur `main`/`master`, à chaque pull request, ou
manuellement (onglet **Actions** → « Run workflow »).

**Ce qu'il fait**, sur un runner `windows-latest` :

1. installe le SDK .NET 8 ;
2. restaure, compile la solution en `Release` ;
3. exécute les 54 tests xUnit ;
4. publie le rapport de tests (`*.trx`) en artefact.

C'est le filet de sécurité : aucune régression ne passe inaperçue.

## 2. Release — `release.yml`

**Déclenchement** :

- **automatique** en poussant un tag de version `vX.Y.Z` (ex. `v1.2.3`) ;
- **manuel** via l'onglet Actions en saisissant la version.

**Ce qu'il produit**, sur `windows-latest` :

1. déduit la version depuis le tag (`v1.2.3` → `1.2.3`) ou depuis la saisie manuelle ;
2. compile et teste (la version est injectée dans les binaires via `-p:Version=`) ;
3. publie deux artefacts **auto-contenus** (aucun .NET requis sur la machine cible) :
   - un **dossier** win-x64, empaqueté ensuite par l'installeur ;
   - un **exécutable portable** (fichier unique, ~70 Mo) ;
4. installe **Inno Setup** (via Chocolatey) et construit l'installeur
   `GooglePhotosLocalUploader-Setup-<version>.exe` ;
5. calcule les empreintes **SHA-256** (`SHA256SUMS.txt`) ;
6. crée (ou met à jour) la **Release GitHub** correspondant au tag et y attache :
   - `GooglePhotosLocalUploader-Setup-<version>.exe` (installeur, recommandé) ;
   - `GooglePhotosLocalUploader-<version>-portable.exe` (portable, un seul fichier) ;
   - `SHA256SUMS.txt`.

La Release utilise le `GITHUB_TOKEN` fourni automatiquement (permission `contents: write`) :
aucun secret à configurer.

## Publier une version

```powershell
# 1. Mettre à jour le numéro de version dans src/GPhotosUploader.App/GPhotosUploader.App.csproj
#    (<Version>1.2.3</Version>) — facultatif mais recommandé pour cohérence.
# 2. Commiter, puis créer et pousser le tag :
git tag v1.2.3
git push origin v1.2.3
```

Le workflow **Release** démarre, et quelques minutes plus tard la page **Releases** du
dépôt contient l'installeur et la version portable prêts à télécharger.

> **Convention de version** : le tag doit être de la forme `vX.Y.Z`. Un suffixe de
> pré-version (`v1.2.3-beta`) fonctionne pour le nom des fichiers, mais gardez `X.Y.Z`
> numérique en tête (contrainte des numéros de version .NET et Windows).

## Prérequis côté dépôt

- Le dépôt doit être hébergé sur GitHub et les **Actions activées** (par défaut).
- Aucun secret ni runner auto-hébergé : tout tourne sur les runners Windows fournis par
  GitHub avec le jeton intégré.
