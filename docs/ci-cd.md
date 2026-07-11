# Continuous Integration and Delivery (GitHub Actions)

The repository contains two workflows in `.github/workflows/`.

## 1. CI — `ci.yml`

**Trigger**: on every push to `main`/`master`, on every pull request, or
manually (**Actions** tab → "Run workflow").

**What it does**, on a `windows-latest` runner:

1. installs the .NET 8 SDK;
2. restores and builds the solution in `Release`;
3. runs the 59 xUnit tests;
4. publishes the test report (`*.trx`) as an artifact.

This is the safety net: no regression goes unnoticed.

## 2. Release — `release.yml`

**Trigger**:

- **automatic** by pushing a version tag `vX.Y.Z` (e.g. `v1.2.3`);
- **manual** via the Actions tab by entering the version.

**What it produces**, on `windows-latest`:

1. derives the version from the tag (`v1.2.3` → `1.2.3`) or from manual input;
2. builds and tests (the version is injected into the binaries via `-p:Version=`);
3. publishes two **self-contained** artifacts (no .NET required on the target machine):
   - a win-x64 **folder**, subsequently packaged by the installer;
   - a **portable executable** (single file, ~70 MB);
4. installs **Inno Setup** (via Chocolatey) and builds the installer
   `mister-gphotos-Setup-<version>.exe`;
5. computes the **SHA-256** checksums (`SHA256SUMS.txt`);
6. creates (or updates) the **GitHub Release** corresponding to the tag and attaches:
   - `mister-gphotos-Setup-<version>.exe` (installer, recommended);
   - `mister-gphotos-<version>-portable.exe` (portable, single file);
   - `SHA256SUMS.txt`.

The Release uses the `GITHUB_TOKEN` provided automatically (permission `contents: write`):
no secret to configure.

## Publishing a version

```powershell
# 1. Update the version number in src/GPhotosUploader.App/GPhotosUploader.App.csproj
#    (<Version>1.2.3</Version>) — optional but recommended for consistency.
# 2. Commit, then create and push the tag:
git tag v1.2.3
git push origin v1.2.3
```

The **Release** workflow starts, and a few minutes later the repository's **Releases**
page contains the installer and the portable version ready to download.

> **Versioning convention**: the tag must be of the form `vX.Y.Z`. A pre-release
> suffix (`v1.2.3-beta`) works for the file names, but keep a numeric `X.Y.Z` at the
> front (a constraint of .NET and Windows version numbers).

## Repository-side prerequisites

- The repository must be hosted on GitHub with **Actions enabled** (the default).
- No secrets or self-hosted runner: everything runs on the Windows runners provided by
  GitHub with the built-in token.
