# Google Photos Local Uploader

Desktop application for **Windows 10 / 11** that uploads your local photos to **Google Photos**, folder by folder, in a reliable and resumable way. It scans a root folder (subfolders included), builds a local inventory (SQLite database with a SHA-256 fingerprint of each file), then uploads the images in batches to your Google account — with pause, resume after a network outage or application shutdown, and a detailed log.

The interface is available in several languages and follows your operating system's display language (English by default, French also provided).

---

## What the application does

- **Recursive scan** of a root folder: detection of compatible images, SHA-256 hash computation, indexing in a local SQLite database. The scan is re-runnable: a file that is already known and unchanged is not re-analyzed.
- **Batch upload** to Google Photos: sending the bytes (`uploads` endpoint), then creating the media through `mediaItems:batchCreate` calls (50 items maximum per call — the default batch is 20 files, with 1 to 3 concurrent uploads, 2 by default).
- **Resume after interruption**: each step is persisted in SQLite. On restart, files that remained "uploading" are put back in the queue; an upload token that is still fresh (less than 20 h old) is reused without resending the bytes.
- **Error handling**: automatic retries with exponential backoff (capped at 60 s, with jitter), Google's `Retry-After` header honored, safety stop after 5 consecutive network failures. Files in error are retried as long as their counter stays below the configured maximum (5 by default); a "Retry failed files" button lets you start over from scratch.
- **Content-based duplicate detection** (SHA-256 hash): the same content present in two locations on the disk is uploaded only once; a file already uploaded by the application is never resent.
- **Real-time monitoring**: overall and per-file progress, throughput, estimated time remaining, counters (detected / pending / uploaded / skipped / errors), "Log", "File details" (with filters by status) and "History" of batches tabs.

## What the application does NOT do

- It **never deletes** a local file or a Google Photos media.
- It **does not read** your existing Google Photos library (see the box below).
- It does not upload videos or files exceeding the configured limit (200 MB maximum, Google Photos' photo limit).
- It does not offer the "storage saver" option: the Google Photos API does not provide it. Uploads are done in original quality.

> ### ⚠️ Limits of duplicate detection
>
> Since the Google Photos Library API changes of **March 31, 2025**, a third-party application can no longer read your entire library: it can only read back **the media it created itself**. As the application indicates in the "Settings" tab:
>
> "Google Photos does not allow this application to check your entire library. Duplicate detection is guaranteed only for files already indexed locally or uploaded by this application."
>
> Concretely: if a photo is already in Google Photos because you put it there **by another means** (mobile app, website, another tool), this application cannot know it and will upload it again. Google Photos may then deduplicate it on its side, but this behavior is not guaranteed by the API.

> ### ⚠️ Storage of your Google account
>
> Uploaded photos **count against your Google account's storage quota** (they are sent in original quality; the API does not offer the "storage saver" option). Check your available space before uploading a large photo library.

---

## Prerequisites

| Prerequisite | Detail |
|---|---|
| System | Windows 10 or Windows 11 (x64) |
| Google account | A Google Photos account with enough storage |
| Personal OAuth client | A Google Cloud project with a "Desktop app" type OAuth client that **you** create (Client ID + Client Secret) — see [docs/google-cloud-setup.md](docs/google-cloud-setup.md) |
| Runtime | None: the published version is self-contained (the .NET 8 SDK is only required to compile from source) |

The application does not use a shared OAuth client: each user creates their own in the Google Cloud Console. This is a one-time step of about 15 minutes, guided by the **built-in wizard** ("Settings" tab) or step by step in [docs/google-cloud-setup.md](docs/google-cloud-setup.md). Google exposes no API allowing this creation to be fully automated (see [docs/known-limitations.md](docs/known-limitations.md)). No Google password is ever entered in the application: sign-in happens in your browser (OAuth 2.0 Authorization Code + PKCE, local redirect `http://127.0.0.1:{port}/`).

---

## Installation

### Option A — Via the installer (recommended)

1. Get the installer `mister-gphotos-Setup-<version>.exe` from the repository's **Releases** page (generated automatically by the CI/CD on each `vX.Y.Z` tag, see [docs/ci-cd.md](docs/ci-cd.md)), or build it locally (`dist\installer\`).
2. Run it: per-user installation, without administrator rights (`PrivilegesRequired=lowest`), wizard in English, optional desktop icon.
3. Start "Google Photos Local Uploader" from the Start menu.

A **portable version** (a single `.exe` file, no installation) is also attached to each Release.

On uninstall, the local data (`%APPDATA%\GooglePhotosLocalUploader`) and the secrets in the Windows Credential Manager are deliberately **not** removed: first use the "Delete the application's local data" button ("Settings" tab) if you want to erase everything.

### Option B — Compilation from source (developers)

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and [Inno Setup 6](https://jrsoftware.org/isdl.php) if you want to produce the installer.

```powershell
# 1. Build the solution and run the tests (59 tests)
.\build\build.ps1

# 2. Publish the self-contained win-x64 executable to dist\win-x64\
.\build\publish.ps1
# -> dist\win-x64\GooglePhotosLocalUploader.exe

# 3. (Optional) Build the installer into dist\installer\
iscc installer\setup.iss
```

---

## Quick start in 5 steps

1. **Configure OAuth** — In the "Settings" tab, click **"Google Cloud setup wizard..."**: it guides you step by step (project creation, API activation, consent screen, "Desktop app" client), opens the right console pages and **imports the downloaded `client_secret_….json` file**. Manual alternative: [docs/google-cloud-setup.md](docs/google-cloud-setup.md); `gcloud` users: `scripts\setup-google-cloud.ps1` automates the automatable part.
2. **Connect** — Click "Connect my Google account": your browser opens, you authorize the application, then return to the window (message "Connection successful"). You have 5 minutes to complete the authorization.
3. **Choose the folder** — Click "Browse..." and select the root folder containing your photos (subfolders are included).
4. **Scan** — Click "Scan the folder": the application inventories the images, computes the fingerprints and reports new files, duplicates and incompatible files.
5. **Upload** — Click "Start upload". At any time you can use "Pause", "Resume" or "Stop": progress is preserved and resumes where it left off, even after the application is closed.

### Formats supported by default

`jpg, jpeg, png, webp, heic, heif, gif, tif, tiff, bmp, avif, ico` as well as the RAW formats `dng, cr2, cr3, crw, nef, nrw, arw, orf, raf, rw2, srw, pef, srf, sr2`. The list can be modified in the "Settings" tab (extensions separated by commas, without a dot).

---

## Local data, secrets and privacy

- **Where is the data?** In `%APPDATA%\GooglePhotosLocalUploader\`: the `app.db` database (inventory, settings, batch history — log entries in the database older than 90 days are purged at startup) and the `logs\` folder (daily log files, kept until you delete them).
- **Where are the secrets?** The Google refresh token and the OAuth Client Secret are stored in the **Windows Credential Manager** (entries `GooglePhotosLocalUploader/RefreshToken` and `GooglePhotosLocalUploader/OAuthClientSecret`), encrypted by Windows — never in clear text on the disk. Only the Client ID (not secret) is kept in the SQLite database.
- **Permissions requested from Google**: the minimal scopes `photoslibrary.appendonly` (add media), `photoslibrary.readonly.appcreateddata` (read back only the media created by the application), `openid` and `email` (display the connected account). The application can neither read the rest of your library nor delete anything.
- **Erase everything**: the "Delete the application's local data" button ("Settings" tab) revokes the token, erases the secrets from the Credential Manager, deletes the database and the logs, then closes the application. Your local photos and your Google Photos media are not touched.

---

## Technical choices (in brief)

The application is written in **C# / .NET 8** with **WPF**, rather than:

- **Electron / embedded web**: much heavier (a full Chromium engine) for a local application that mostly does file hashing and HTTP; WPF gives a native, lean and fast Windows interface.
- **WinUI 3 / MAUI**: relevant targets for cross-platform or modern design, but with less stable tooling; WPF is mature, perfectly supported on Windows 10/11 and sufficient for this interface.
- **Official Google SDK**: the `Google.Apis.PhotosLibrary` .NET client is deprecated; the application therefore calls the HTTP API directly (`/v1/uploads` and `/v1/mediaItems:batchCreate`), which reduces dependencies and follows exactly the documented protocol.

The rest of the stack: `Microsoft.Data.Sqlite` for the local inventory (no database server), `CommunityToolkit.Mvvm` for the MVVM model, and a **self-contained win-x64** publication that requires no .NET installation on the target machine.

---

## Documentation

| Document | Audience | Content |
|---|---|---|
| [docs/google-cloud-setup.md](docs/google-cloud-setup.md) | Everyone | Create your Google Cloud project and your "Desktop app" OAuth client (Client ID / Client Secret), step by step |
| [docs/user-guide.md](docs/user-guide.md) | Everyone | Complete usage guide: screens, file statuses, pause/resume, filters, log export, FAQ |
| [docs/known-limitations.md](docs/known-limitations.md) | Everyone | Known limits: duplicate detection, API quotas, storage, OAuth Test mode |
| [docs/architecture.md](docs/architecture.md) | Developers | Architecture of the `GPhotosUploader.Core` / `GPhotosUploader.App` projects, services, upload flow and resume logic |
| [docs/database-schema.md](docs/database-schema.md) | Developers | Schema of the SQLite database (`app.db`): tables, statuses, migrations |
| [docs/build-windows.md](docs/build-windows.md) | Developers | Compilation, tests and self-contained publication (`build\publish.ps1`) |
| [docs/installer.md](docs/installer.md) | Developers | Creation of the Windows installer with Inno Setup (`installer\setup.iss`) |
| [docs/ci-cd.md](docs/ci-cd.md) | Developers | GitHub Actions CI/CD: automatic tests and Release (installer + portable exe) on `vX.Y.Z` tag |

---

## Repository structure

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

*Google Photos is a trademark of Google LLC. This application is an independent tool, not affiliated with Google; it uses the public Google Photos Library API with the OAuth client that you create yourself.*
