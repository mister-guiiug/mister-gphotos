# Building the Windows installer

This document describes how to build the installer for **Google Photos Local Uploader** (the `mister-gphotos-Setup-1.0.0.exe` file), what this installer does on the user's machine, and what uninstalling removes — or deliberately keeps.

The first part (building) is aimed at a developer; the second (installer behavior and uninstallation) is readable by everyone.

---

## 1. Prerequisites (developer)

| Tool | Role | Where to get it |
|---|---|---|
| .NET 8 SDK | Compile and publish the application | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Inno Setup 6 | Compile the installer script `installer/setup.iss` | <https://jrsoftware.org/isdl.php> |

After installing Inno Setup 6, make sure the command-line compiler `iscc` is available. If it is not, add its folder to `PATH` (default installation: `C:\Program Files (x86)\Inno Setup 6`) or invoke it by its full path:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

---

## 2. Build procedure (developer)

All commands are run from the repository root (`mister-tof-sync-desktop`).

### Step 1 — Publish the application

```powershell
.\build\publish.ps1
```

This script runs `dotnet publish` on `src\GPhotosUploader.App\GPhotosUploader.App.csproj` in `Release` configuration, for the `win-x64` runtime, in **self-contained** mode (`--self-contained true`, `PublishSingleFile=false`). The output is written to:

```
dist\win-x64\
```

with the executable `dist\win-x64\GooglePhotosLocalUploader.exe`. The published application embeds the .NET runtime: **no prior .NET installation is required on the target machine.**

Optional but recommended before publishing: `.\build\build.ps1` compiles the solution and runs the test suite (`dotnet restore` + `dotnet build` + `dotnet test`).

### Step 2 — Compile the installer

```powershell
iscc installer\setup.iss
```

The `installer/setup.iss` script packages the entire contents of `dist\win-x64\*` (recursively) and produces the installer at:

```
dist\installer\mister-gphotos-Setup-1.0.0.exe
```

The file name follows the pattern `mister-gphotos-Setup-{version}` defined by `OutputBaseFilename` in `setup.iss` (current version: `1.0.0`, constant `MyAppVersion`).

> **Mandatory order**: Step 1 must precede Step 2. If `dist\win-x64\` does not exist or is out of date, `iscc` will fail or will package a stale version.

---

## 3. Installer behavior (general audience)

The `installer/setup.iss` script configures the installer as follows (actual settings from the file):

- **Per-user installation, no administrator rights**: `PrivilegesRequired=lowest`. No UAC elevation is requested. The proposed installation folder is `{autopf}\Google Photos Local Uploader`; without administrator rights, Inno Setup resolves it to the user's own "Program Files", typically `C:\Users\<your name>\AppData\Local\Programs\Google Photos Local Uploader`.
- **Language**: the installation wizard is in **French** (the only declared language: `compiler:Languages\French.isl`), using Inno Setup's modern interface (`WizardStyle=modern`).
- **Architecture**: 64-bit installation on x64-compatible systems (`ArchitecturesInstallIn64BitMode=x64compatible`). The published application targets `win-x64`.
- **Desktop icon**: offered during installation via a checkbox, **unchecked by default** (task `desktopicon`, flag `unchecked`).
- **Start menu**: a "Google Photos Local Uploader" shortcut is created; the program group selection page is not shown (`DisableProgramGroupPage=yes`).
- **End of installation**: a checkbox offers to launch the application immediately (`[Run]` section, flags `nowait postinstall skipifsilent`).
- **Compression**: `lzma2` with `SolidCompression=yes`.
- **Application identifier**: `AppId {7E9D2C4A-1F5B-4E83-9A6C-2B8D0E4F6153}` — it lets Windows recognize the application for updates and uninstallation.

---

## 4. Uninstallation: what is removed, what is kept (general audience)

### Removed by the uninstaller

- The program files installed in the installation folder (content copied from `dist\win-x64`).
- The shortcuts created by the installer (Start menu and, where applicable, the desktop icon).

### **Deliberately** kept by the uninstaller

- **The application's local data** in `%APPDATA%\GooglePhotosLocalUploader\`: the inventory database `app.db` and the `logs\` folder (daily logs).
- **The secrets** stored in Windows Credential Manager: the entries `GooglePhotosLocalUploader/RefreshToken` and `GooglePhotosLocalUploader/OAuthClientSecret`.

This choice is intentional: it allows you to reinstall or update the application without losing the inventory of files already uploaded, and without having to reconnect the Google account.

**To erase everything before uninstalling**: open the application and use the **"Delete the application's local data"** button (the "Local data" section of the interface). After confirmation, it erases the SQLite inventory, the logs, the settings, and the secrets in Windows Credential Manager, then closes the application. Your local photos and your Google Photos media are **never** touched — neither by this button nor by uninstallation: the application does not delete any local file or any Google Photos media.

If you uninstalled without using this button, you can still manually delete the `%APPDATA%\GooglePhotosLocalUploader\` folder and, in Windows Credential Manager (Control Panel → Credential Manager → Windows Credentials), the two entries mentioned above.

---

## 5. Code signing (optional, not provided)

The produced installer is **not digitally signed**: the project provides neither a code-signing certificate nor any signing configuration. Practical consequence: on first launch, Windows SmartScreen may display a "Windows protected your PC" warning; the user must click "More info" then "Run anyway".

If you have your own code-signing certificate (an OV/EV certificate purchased from a certificate authority, or via Azure Trusted Signing), you can sign:

1. **The published binaries** after the `publish.ps1` step (for example `dist\win-x64\GooglePhotosLocalUploader.exe`) with `signtool sign`.
2. **The installer itself**, either by signing `dist\installer\mister-gphotos-Setup-1.0.0.exe` after compilation, or by configuring Inno Setup's `SignTool` directive in `setup.iss`.

None of these steps are required for the installer to work; they only serve to reduce Windows security warnings.

---

## Quick recap (developer)

```powershell
# From the repository root:
.\build\build.ps1        # optional: compile + tests
.\build\publish.ps1      # publishes the self-contained app to dist\win-x64\
iscc installer\setup.iss # produces dist\installer\mister-gphotos-Setup-1.0.0.exe
```
