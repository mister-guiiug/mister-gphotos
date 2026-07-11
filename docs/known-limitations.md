# Known limitations

This document honestly describes what **Google Photos Local Uploader** cannot do, and why. Most of these limitations do not come from the application itself, but from the rules Google imposes on its Photos API. They are stated here plainly so that you know exactly what to expect before starting an upload.

---

## 1. Duplicate detection does not cover your entire Google Photos library

This is the most important limitation. The application displays this warning in the Settings tab:

> "Google Photos does not allow this application to check your entire library. Duplicate detection is guaranteed only for files already indexed locally or uploaded by this application."

**Why?** Since the Google Photos API changes of **31 March 2025**, Google removed the permissions ("scopes") that allowed a third-party application to read a user's entire library. An application can now read back **only the media it created itself**. This application therefore uses only the `photoslibrary.appendonly` (upload) and `photoslibrary.readonly.appcreateddata` (reading back its own uploads) scopes — there is simply no longer any technical way to ask Google: "does this photo already exist somewhere in the account?".

**In practice:**

- If a photo is already present in your Google Photos because you added it **by another means** (mobile app, website, other software), this application cannot know that. It may therefore create a duplicate on the Google side.
- On the other hand, detection is reliable for everything that goes through the application: each scanned file is identified by its SHA-256 fingerprint in the local database. Two files with strictly identical content are detected, and a file already uploaded by the application is never uploaded again. These cases appear in the "File details" tab with the statuses "Skipped (local duplicate)" (`skipped_duplicate_local`) and "Skipped (already uploaded)" (`skipped_duplicate_remote_app_created`).

## 2. Uploads consume your Google account storage

Every file uploaded via the API is stored in **original quality** and is **counted against the storage quota** of your Google account (the 15 GB free tier, or your Google One subscription). The API does not offer the "storage saver" option available in the official Google Photos apps; this application can therefore neither compress on the Google side nor avoid the count. Before uploading a large photo library, check the available space on your account.

## 3. API quota: about 9,500 photos per day at most

By default, Google grants a Google Cloud project a quota of **10,000 requests per day** on the Photos Library API. Each photo costs one byte-upload request (`POST /v1/uploads`), then each batch triggers a creation call (`POST /v1/mediaItems:batchCreate`). With the default batch size of 20 files, a batch therefore consumes 21 requests, giving a theoretical ceiling of about **9,500 photos per day** — fewer in practice, because each retry after an error also consumes requests.

If the quota is reached, Google responds with a throttling error (HTTP 429 or 403 "quota"): the application logs it ("Google Photos request limit reached (429). Automatic retry."), waits while honoring the `Retry-After` header and an exponential backoff capped at 60 seconds, then retries. For a very large photo library, the full upload can therefore span several days; automatic resumption is designed for exactly this.

## 4. OAuth application in "Test" mode: reconnection every 7 days

You create your own OAuth client in the Google Cloud Console (type "Desktop app"). As long as your project's consent screen remains in **"Test"** status (the default setting), Google **expires the refresh token after 7 days**. Past that point, the application displays "The Google session has expired or been revoked. Reconnect your account." and you simply need to reconnect — nothing is lost, the local inventory and the queue are preserved.

To remove this expiration, you have to publish the OAuth application to "Production" in the Google Cloud Console, which may involve a verification process by Google. This choice is up to the user; the application works in both cases.

## 5. No videos in this version (photos only)

Version 1 handles **images only**. The default list of extensions contains no video format:

```
jpg,jpeg,png,webp,heic,heif,gif,tif,tiff,bmp,avif,ico,
dng,cr2,cr3,crw,nef,nrw,arw,orf,raf,rw2,srw,pef,srf,sr2
```

Video files present in the scanned folder are simply ignored during the scan. The 200 MB size limit (see point 8) and the upload flow are also sized for photos.

## 6. RAW files: uploaded as-is, compatibility depends on Google

RAW formats (DNG, CR2, CR3, CRW, NEF, NRW, ARW, ORF, RAF, RW2, SRW, PEF, SRF, SR2) are accepted by the scan and sent to Google with the generic MIME type `application/octet-stream` (there is no standardized MIME type for each proprietary RAW format). It is then **Google Photos that decides** whether to accept or reject the file according to its own list of supported formats. If Google rejects a file, the application records the exact message ("Google Photos rejected this file: ...") and the file moves to the "Error" status (`failed`), without blocking the rest of the batch.

## 7. No remote deletion possible — and no deletion, ever

The `photoslibrary.appendonly` scope used by the application allows **only adding** media. The API gives it no way to delete or modify anything in your Google Photos — and the application **never** deletes a file from your disk either. As the Settings tab states:

> "The application never deletes your local photos or your Google Photos media. The deletion below only concerns the inventory, logs, settings, and secrets stored by the application."

A consequence to be aware of: if you upload a photo by mistake, deleting it from Google Photos must be done **manually** (Google Photos website or app). The "Delete the application's local data" button erases only the `%APPDATA%\GooglePhotosLocalUploader\` folder (`app.db` database, `logs\` logs) and the secrets in Windows Credential Manager — neither your photos nor your online media.

## 8. 200 MB limit per photo

Google Photos imposes a maximum size of **200 MB per photo**. The application applies this limit before any upload ("Max size" setting, 200 MB by default, adjustable from 1 to 200 — it cannot be exceeded). A larger file will never be uploaded.

## 9. Files that are too large or in excluded formats: skipped, with the reason displayed

Any file that fails the compatibility check is marked "Skipped (incompatible)" (`skipped_incompatible`) — it is neither uploaded, nor deleted, nor blocking for the others. The exact reason is recorded and visible in the "File details" tab (filter "Skipped (incompatible)"):

- "Extension .xxx not supported" — the extension is not in the configured list;
- "Empty file" — 0-byte file;
- "File too large (NNN MB, limit 200 MB)" — beyond the maximum size.

Since the extension list is configurable in Settings, a file skipped for its extension can be picked up in a later scan if you add its extension to the list.

## 10. OAuth client creation cannot be automated

Google exposes **no public API** for creating an "External" consent screen or a "Desktop app" OAuth client: neither the `gcloud` CLI nor Terraform (the `google_iap_brand` / `google_iap_client` resources only cover Google Workspace organizations and IAP clients) can produce the credentials the application needs. This is why the application provides a **built-in wizard** (Settings tab -> "Google Cloud setup wizard...") that guides creation in the console, opens the correct pages directly, and imports the downloaded `client_secret_….json` file, along with an optional `scripts\setup-google-cloud.ps1` script that automates the only part that can be automated (project creation + API enablement, via `gcloud`).

---

*Document updated for version 1 of the application. The behaviors described here match the verified source code (services `GooglePhotosApi`, `GoogleAuthService`, `UploadService`, `FileScanner`, `CompatibilityChecker`) and the Google Photos API rules in effect since 31 March 2025.*
