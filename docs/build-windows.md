# Windows Build Procedure

This document explains how to compile, test, and publish **Google Photos Local Uploader**
on Windows 10 or 11, from source. The "Prerequisites" and "Getting the sources" sections
are accessible to everyone; the following sections (compilation, artifacts, publishing,
troubleshooting) are intended for a developer or a user comfortable with the command line.

> **Important:** the application is a WPF application targeting `net8.0-windows`.
> Compilation works **only on Windows** — it is not possible to build it on
> Linux or macOS.

---

## 1. Prerequisites

| Tool | Version | Required? | Download |
|---|---|---|---|
| .NET SDK | **8.x** (SDK, not just the runtime) | Yes | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows | 10 or 11, x64 | Yes | — |
| PowerShell | 5.1 (included with Windows) or PowerShell 7 | Yes (for the `build/` scripts) | — |
| Git | Recent | No (a source ZIP is enough) | <https://git-scm.com/download/win> |
| Inno Setup | **6** | No (only to build the installer) | <https://jrsoftware.org/isdl.php> |

To verify that the .NET 8 SDK is installed, open a terminal (PowerShell) and type:

```powershell
dotnet --list-sdks
```

You should see at least one line starting with `8.` (for example `8.0.404`). If the
`dotnet` command is not recognized, see the [Troubleshooting](#7-common-troubleshooting)
section.

No other tool is required: Visual Studio is **not** needed (the .NET SDK is
sufficient), and the NuGet dependencies (`Microsoft.Data.Sqlite`, `CommunityToolkit.Mvvm`,
`xunit`, etc.) are restored automatically from nuget.org during the build. The project
uses **no Google SDK**: calls to the Google Photos API are made in direct HTTP, because
the official .NET PhotosLibrary client is deprecated.

---

## 2. Getting the sources

Two options:

**With Git:**

```powershell
git clone <URL-du-dépôt> mister-tof-sync-desktop
cd mister-tof-sync-desktop
```

**Without Git:** download the source ZIP archive from the repository page, then extract
it into a folder of your choice (for example `C:\Src\mister-tof-sync-desktop`).

The project root must contain:

```
MisterGPhotos.sln        .NET solution (3 projects)
src/MisterGPhotos.Core/       Library: models, SQLite database, services (net8.0)
src/MisterGPhotos.Core/Resources/  Localization resources
src/MisterGPhotos.App/        WPF application (net8.0-windows)
src/MisterGPhotos.App/Localization/  Localization
src/MisterGPhotos.Tests/      xUnit tests (net8.0) — 59 tests
build/build.ps1                 Script: restore + compile + tests
build/publish.ps1               Script: self-contained win-x64 publish
installer/setup.iss             Inno Setup script (Windows installer)
docs/                           Documentation
```

---

## 3. Build and tests with `build/build.ps1` (recommended method)

From the project root, in PowerShell:

```powershell
.\build\build.ps1
```

The script performs, in order, and **stops at the first error** (it propagates the exit
code from `dotnet`):

1. `dotnet restore MisterGPhotos.sln` — restores the NuGet packages
   (displays: `=== Restoring packages ===`);
2. `dotnet build MisterGPhotos.sln -c Release --no-restore` — compilation
   (displays: `=== Building (Release) ===`);
3. `dotnet test src\MisterGPhotos.Tests\MisterGPhotos.Tests.csproj -c Release --no-build`
   — runs the tests (displays: `=== Tests ===`).

On success, the script displays `Build and tests OK.` in green.

The script accepts an optional `-Configuration` parameter (default value: `Release`):

```powershell
.\build\build.ps1 -Configuration Debug
```

> **If Windows blocks script execution** (message "running scripts is disabled on this
> system"), run it as follows:
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File .\build\build.ps1
> ```

---

## 4. Manual build and tests (without the script)

The command-by-command equivalent, from the root:

```powershell
dotnet restore MisterGPhotos.sln
dotnet build MisterGPhotos.sln -c Release --no-restore
dotnet test src\MisterGPhotos.Tests\MisterGPhotos.Tests.csproj -c Release --no-build
```

To run the application directly from source (useful during development):

```powershell
dotnet run --project src\MisterGPhotos.App\MisterGPhotos.App.csproj -c Debug
```

---

## 5. Build artifact structure

After a `dotnet build -c Release`, the binaries are produced under each project:

```
src/MisterGPhotos.Core/bin/Release/net8.0/
    MisterGPhotos.Core.dll
    fr/MisterGPhotos.Core.resources.dll   <- French localization satellite assembly

src/MisterGPhotos.App/bin/Release/net8.0-windows/
    MisterGPhotos.exe      <- WPF executable (AssemblyName of the App project)
    MisterGPhotos.dll
    MisterGPhotos.Core.dll
    CommunityToolkit.Mvvm.dll
    Microsoft.Data.Sqlite.dll (+ SQLitePCLRaw dependencies)
    ...

src/MisterGPhotos.Tests/bin/Release/net8.0/
    MisterGPhotos.Tests.dll
```

Note that the executable is named **`MisterGPhotos.exe`** (and not
"MisterGPhotos.App.exe"): the App project's `AssemblyName` is `MisterGPhotos`. The
projects, folders, namespaces and other assemblies all use the `MisterGPhotos.*` naming,
matching the `mister-gphotos` repository (the product display name stays
"Google Photos Local Uploader").

The executable produced by `dotnet build` is **framework-dependent**: it requires the
.NET 8 (Desktop) runtime to be installed on the machine. For an executable that works
without a prior .NET installation, use the self-contained publish below.

---

## 6. Self-contained publish with `build/publish.ps1`

From the project root:

```powershell
.\build\publish.ps1
```

The script runs:

```powershell
dotnet publish src\MisterGPhotos.App\MisterGPhotos.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o dist\win-x64
```

Characteristics:

- **Self-contained** (`--self-contained true`): the .NET 8 runtime is embedded. The
  target machine has **nothing to install** beforehand.
- **Multi-file** (`PublishSingleFile=false`): the folder contains the executable and its
  DLLs, which is the format expected by the installer script.
- Optional `-Runtime` parameter (default value: `win-x64`); the output folder follows the
  chosen runtime (`dist\<runtime>`).

**Where to find the executable:** at the end, the script displays the exact path:

```
Publish complete: <root>\dist\win-x64
Executable: <root>\dist\win-x64\MisterGPhotos.exe
```

You can run `dist\win-x64\MisterGPhotos.exe` directly, or copy the entire
`dist\win-x64\` folder to another Windows 10/11 x64 machine.

### Building the Windows installer (optional)

Prerequisites: Inno Setup 6 installed, and the publish (`.\build\publish.ps1`) already
done — the installer packages the contents of `dist\win-x64\`. Then:

```powershell
iscc installer\setup.iss
```

The installer is written to `dist\installer\` under the name
`mister-gphotos-Setup-1.0.0.exe`. It installs **without administrator rights**
(`PrivilegesRequired=lowest`) and is in French.

On uninstall, the local data (`%APPDATA%\MisterGPhotos`, which contains
`app.db` and `logs\`) and the secrets in the Windows Credential Manager are
**intentionally not removed**: use the "Delete local data" button in the application
before uninstalling if you want to erase everything.

---

## 7. Common troubleshooting

### "dotnet" is not recognized as a command

- The .NET 8 SDK is not installed: download it from
  <https://dotnet.microsoft.com/download/dotnet/8.0> (be sure to choose the **SDK**, not
  the "Runtime" only, and the **x64** installer).
- If you just installed it, **close and reopen** the terminal (the `PATH` is only updated
  for new terminals).
- Then verify with `dotnet --list-sdks` that an `8.x` version appears.

### The SDK is installed but compilation fails (NETSDK1045 or similar)

You probably only have an older SDK (6.x, 7.x). The project targets
`net8.0` / `net8.0-windows`: install the **8.x** SDK in addition (multiple SDKs can
coexist without conflict).

### NuGet restore fails behind a corporate proxy (NU1301 errors, timeouts)

`dotnet restore` must be able to reach `https://api.nuget.org`. Behind a proxy:

1. **Environment variables** (the simplest, valid for the current session):

   ```powershell
   $env:HTTP_PROXY  = "http://proxy.exemple.local:8080"
   $env:HTTPS_PROXY = "http://proxy.exemple.local:8080"
   .\build\build.ps1
   ```

2. **Persistent NuGet configuration**: add a section in
   `%APPDATA%\NuGet\NuGet.Config`:

   ```xml
   <configuration>
     <config>
       <add key="http_proxy" value="http://proxy.example.local:8080" />
       <!-- if the proxy requires authentication: -->
       <add key="http_proxy.user" value="DOMAIN\user" />
     </config>
   </configuration>
   ```

3. If your proxy **inspects TLS** (corporate certificate), the company's root certificate
   must be present in the Windows certificate store, otherwise the connection to
   nuget.org will be rejected.

4. If your company provides an internal NuGet mirror (Artifactory, Nexus…), declare it as
   a source: `dotnet nuget add source <URL-du-miroir> -n interne`.

### "Running scripts is disabled on this system"

The PowerShell execution policy blocks `.ps1` files. One-off workaround, without modifying
the machine's configuration:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\build.ps1
```

### Some tests fail

The suite (59 xUnit tests: `CoreLogicTests`, `DatabaseTests`, `FileScannerTests`,
`OAuthClientConfigTests`, `LocalizationTests`) is expected to be **entirely green** and
needs no network access or Google account. A failure generally indicates an unsuitable SDK
or a local modification of the sources. Restart cleanly:

```powershell
dotnet clean MisterGPhotos.sln
.\build\build.ps1
```

### `iscc` is not recognized

Inno Setup 6 is not installed, or its folder (by default
`C:\Program Files (x86)\Inno Setup 6`) is not in the `PATH`. You can call the compiler by
its full path:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

### The Inno Setup installer fails with "Source file not found … dist\win-x64\*"

The `installer\setup.iss` script packages `dist\win-x64\`: run `.\build\publish.ps1`
first, then run `iscc installer\setup.iss` again.

---

## Reminders about what the build produces (limitations, stated frankly)

- The application uses the Google Photos Library API over direct HTTP with OAuth 2.0
  (Authorization Code + PKCE, loopback redirect `http://127.0.0.1:{port}/`). Each user
  creates **their own OAuth client** in the Google Cloud Console — see
  `docs/google-cloud-setup.md`. The build therefore contains **no Google credentials**.
- Since the API changes of March 31, 2025, the application can only read back the media
  **it created itself**: duplicate detection on the Google side is guaranteed only for
  files already indexed locally or uploaded by this application (see
  `docs/known-limitations.md`).
- The application **never** deletes a local file or a Google Photos media item.
