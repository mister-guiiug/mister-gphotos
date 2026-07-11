# User guide — Google Photos Local Uploader

This guide is intended for the end user of **Google Photos Local Uploader**, the Windows (10/11) application that scans a local image folder, indexes the files, and then uploads them in batches to your Google Photos account, with automatic resumption after an interruption.

The interface language follows your Windows display language (English by default, French also available); it is not tied to a single language.

> **Before you begin — the application's known limitations**
>
> - Since the Google Photos API changes of 31 March 2025, a third-party application can only read **the media it created itself**. As the **Settings** tab states: "Google Photos does not allow this application to check your entire library. Duplicate detection is guaranteed only for files already indexed locally or uploaded by this application." If a photo already exists in Google Photos because you added it by other means (phone, website, etc.), the application cannot know this and will upload it again.
> - Uploaded photos count toward your **Google account storage** (original quality). The API does not offer the "storage saver" option.
> - The Google Photos limit per photo is **200 MB**.
> - The application **never deletes** your local files or your Google Photos media.
> - You must create **your own OAuth client** in the Google Cloud Console (type "Desktop application"). The detailed procedure is described in the `docs/google-cloud-setup.md` guide. You are never asked for a Google password inside the application: sign-in takes place in your browser.

---

## 1. Interface overview

The main window, titled **Google Photos Local Uploader**, is made up of four areas, from top to bottom.

### 1.1 "Configuration" area

| Element | Purpose |
|---|---|
| **Root folder:** | Path of the folder containing your photos (read-only in the field). |
| **Browse...** | Opens a folder picker ("Choose the root folder containing your photos"). The choice is saved immediately. |
| **Google account:** | Displays "No account connected" or "Connected: your@email". |
| **Connect my Google account** | Starts the OAuth authorization in your default browser. |
| **Disconnect** | Revokes and removes the refresh token (see section 8). |

### 1.2 Action bar

Five buttons: **Scan the folder**, **Start upload**, **Pause**, **Resume**, **Stop**. Each button is enabled only when its action is possible (for example, **Resume** is clickable only while paused).

### 1.3 "Progress" area

- A global progress bar with text such as "42.5% (850 uploaded / 2000 files)".
- The counters: **Detected**, **Pending**, **Uploaded** (green), **Skipped** (orange), **Errors** (red).
- A progress bar for the current file, with its name and the bytes sent (for example "IMG_0042.jpg — 3.2 MB / 8.1 MB").
- The throughput ("Throughput: 2.4 MB/s") and the estimate "Estimated time remaining: 1 h 05 min".
- The most recent error encountered, in red.

### 1.4 The state bar (bottom of the window)

It shows the current state (**Ready**, **Scanning**, **Uploading**, **Upload paused**, **Stopping...**) followed by the latest information message (scan finished, settings saved, etc.).

### 1.5 The four tabs

#### "Log" tab
The real-time activity feed (fixed-width font): connections, scans, successful uploads, warnings and errors. The last 500 lines are kept on screen; the full history remains available through the export (section 7) and the log files on disk.

#### "File details" tab
The list of indexed files (up to 2,000 rows displayed) with the columns **File**, **Status**, **Size**, **Uploaded at**, **Error / reason**, **Path**.

- **Filter:** drop-down list with the values **All**, **Pending**, **Uploaded**, **Errors**, **Skipped (local duplicate)**, **Skipped (already uploaded)**, **Skipped (incompatible)**.
- **Refresh**: reloads the list.
- **Retry failed files**: see section 6.

#### "History" tab
The list of the last 100 upload batches: **Batch**, **Started**, **Completed**, **Files**, **Succeeded**, **Failed**, **Status** ("Completed", "Stopped" or "In progress"). Two buttons: **Refresh** and **Export the log...** (section 7).

#### "Settings" tab

- **Google authentication**: the **"Google Cloud setup wizard..."** button (a 6-step wizard that opens the right console pages and imports the downloaded `client_secret_….json` file), and the **OAuth Client ID:** / **OAuth Client Secret:** fields for manual entry. The refresh token and the client secret are stored in Windows Credential Manager, never in plain text.
- **Upload**:
  - **Batch size (1 to 50 files):** — 20 by default (50 is the hard limit of the Google API).
  - **Max attempts on temporary error:** — 5 by default (bounded from 0 to 20).
  - **Concurrent uploads (1 to 3):** — 2 by default.
  - **Max size per photo (MB, Google limit: 200):** — 200 by default.
- **Included formats**: extensions separated by commas, without a dot. Default: `jpg,jpeg,png,webp,heic,heif,gif,tif,tiff,bmp,avif,ico,dng,cr2,cr3,crw,nef,nrw,arw,orf,raf,rw2,srw,pef,srf,sr2`. Remove an extension to exclude a format; add one to include it.
- **Save settings**: applies and saves the values (out-of-range values are automatically brought back within the limits).
- **Duplicate detection**: the orange box recalling the API limitation (the text quoted at the top of this guide).
- **Local data**: the **Delete the application's local data** button (section 9).

---

## 2. First use, step by step

1. **Create your Google OAuth client**: launch the application, open the **Settings** tab and click **"Google Cloud setup wizard..."**. The wizard guides you through 6 steps (Google Cloud project, enabling the Photos Library API, consent screen, "Desktop application" OAuth client) by opening the right console pages, then imports the downloaded `client_secret_….json` file — the credentials are then saved automatically (the Client Secret goes into Windows Credential Manager).
2. **Manual alternative**: follow `docs/google-cloud-setup.md`, fill in **OAuth Client ID:** and **OAuth Client Secret:** in the **Settings** tab, then click **Save settings**.
3. Click **Connect my Google account**. Your browser opens the Google authorization page; sign in and accept the requested permissions (adding photos and reading only the media created by the application). A "Connection successful" page appears: you can close the browser window. You have 5 minutes; beyond that, the message "Authorization timed out (5 minutes). Please try again." appears.
4. Back in the application, the Configuration area displays "Connected: your@email".
5. Click **Browse...** and choose the root folder containing your photos. All subfolders will be scanned.
6. Click **Scan the folder**. The application enumerates the images, computes a fingerprint (SHA-256 hash) for each file and populates its local inventory. The state bar shows the progress and then a summary: "Scan complete: N images seen, N new, N duplicates, N incompatible."
7. Click **Start upload**. The pending files are sent in batches (20 by default). Follow the progress in the Progress area and the **Log** tab.
8. At the end, a summary appears: "Upload complete: N uploaded, N failed, N skipped."

You can run **Scan the folder** again at any time (for example after adding photos): a file that is already known and unchanged is not re-analyzed, and a file that has already been uploaded is never sent again.

---

## 3. Pause, resume, stop

- **Pause**: suspends the upload. The transfer of the current file finishes first, as the log indicates: "Upload paused. The current file will finish before actually stopping." The state changes to **Upload paused**.
- **Resume**: picks up exactly where the pause occurred, without sending anything again.
- **Stop**: interrupts the ongoing upload (or scan). Files that were being sent are marked **Paused** in the inventory, and the message "Upload stopped. Files in progress will resume at the next start." appears. Clicking **Start upload** again resumes the remaining work.

---

## 4. Resuming after a close or crash

The inventory is saved to a SQLite database at every state change: resumption is therefore reliable even after a crash or a power outage.

- **Normal window close**: the application cleanly stops the scan and the upload before closing; files in progress switch to **Paused**.
- **On the next start**: any file still marked "uploading" (in the case of a crash) is automatically put back in the queue. The log indicates this: "Resumed after interruption: N file(s) put back in the queue."
- **When you click Start upload**: **Paused** files are also put back in the queue, and then the upload resumes.
- **Optimization**: if a file had already been transferred to Google but not yet finalized (a crash between sending the bytes and creating the media), its upload token is kept. If it is less than 20 hours old, it is reused **without resending the bytes** (Google states a validity of about 24 h; the application keeps a safety margin). The log then indicates: "Upload token reused for … (bytes already sent)."

---

## 5. Reading file statuses

Statuses shown in the **Status** column of the **File details** tab:

| Status displayed | Meaning |
|---|---|
| **Detected** | File spotted by the scan, not yet queued (transient state). |
| **Pending** | File ready to be uploaded on the next pass. |
| **Uploading** | File currently being transferred to Google Photos. |
| **Uploaded** | File successfully created in Google Photos. The **Uploaded at** column shows the date. It will never be sent again. |
| **Skipped (local duplicate)** | Another local file has exactly the same content (same SHA-256 hash). Only the original will be sent; the **Error / reason** column shows the path of the original file. |
| **Skipped (already uploaded)** | A file with identical content has already been sent **by this application**. Nothing is sent again. |
| **Skipped (incompatible)** | Extension not included in the settings, empty file, or file exceeding the maximum size (the reason is given in **Error / reason**, for example "File too large (250 MB, limit 200 MB)"). |
| **Error** | The upload failed (reason in **Error / reason**). Temporary errors are retried automatically as long as the number of attempts stays under the configured maximum (5 by default). |
| **Paused** | File interrupted by a pause, a stop or a close; it will resume at the next **Start upload**. |

The **Pending** counter in the Progress area groups the *Detected*, *Pending*, *Uploading* and *Paused* files; **Skipped** groups the three "Skipped (…)" statuses.

---

## 6. Retrying failed files

During an upload, each temporary error (network, quota, server) is retried automatically with an increasing delay (exponential backoff capped at 60 seconds, honoring Google's "Retry-After" directive). A file that has exhausted its attempts (5 by default, adjustable in **Settings**) stays in the **Error** status.

To retry it:

1. Open the **File details** tab (use the **Errors** filter to see only those and read the **Error / reason** column).
2. Fix the cause if needed (network restored, file put back in place, extension re-enabled, etc.).
3. Click **Retry failed files**. The attempt counter is reset to zero and all failed files return to **Pending**. The state bar confirms: "N failed file(s) put back in the queue."
4. Click **Start upload**.

---

## 7. Exporting the log

In the **History** tab, click **Export the log...**. A save dialog proposes a name such as `google-photos-uploader-log-20260711-1430.txt`. The file contains the 10,000 most recent log entries (timestamp, level, source, message).

Independently of this export, the application also writes a daily log file to disk: `%APPDATA%\GooglePhotosLocalUploader\logs\app-YYYYMMDD.log`. Database entries older than 90 days are purged automatically at startup.

---

## 8. Disconnecting the Google account

Click **Disconnect** in the Configuration area. A confirmation appears: "Disconnect the Google account? The refresh token will be revoked and removed from Windows Credential Manager."

When you confirm:

- the refresh token is revoked with Google (if you are offline, the remote revocation is simply ignored, but the local refresh token is erased anyway);
- the **Client Secret is kept** in Windows Credential Manager: you can reconnect an account (the same one or another) without retyping it. It is erased only by "Delete the application's local data";
- the refresh token and the client secret are removed from Windows Credential Manager;
- the application again displays "No account connected".

Your local inventory (files already uploaded, history) is kept. You can reconnect the same account or a different one at any time.

> You can also revoke the application's access from your Google account (myaccount.google.com, Security section). In that case, the application will display on the next upload: "Google session expired: reconnect your account then restart the upload."

---

## 9. Deleting the application's local data

In the **Settings** tab, **Local data** section, click **Delete the application's local data**. After confirmation, the application:

1. stops any ongoing upload;
2. disconnects the Google account and erases the secrets from Windows Credential Manager;
3. deletes the `%APPDATA%\GooglePhotosLocalUploader\` folder (the `app.db` database: inventory, history, settings, logs);
4. closes.

As the confirmation dialog reminds you: **your local photos and your Google Photos media are NOT touched**. However, the application loses the memory of what has already been uploaded: after a new scan, the files will be considered never sent and would be uploaded again (creating duplicates on Google's side, which the API does not allow to be detected).

---

## 10. FAQ

**What happens if I move or delete a file after the scan?**
If the file no longer exists at the time of its upload, it switches to the **Error** status with the reason "File not found (moved or deleted since the scan)." and is no longer retried automatically. During a new scan, files that are not found (and not yet uploaded) are marked "missing" in the inventory. If the file was moved elsewhere **within the root folder**, the scan finds it under its new path; thanks to the SHA-256 hash, if it had already been uploaded by the application, it is marked **Skipped (already uploaded)** and is not sent again.

**What happens if I lose the network connection during an upload?**
Each file is retried automatically with an increasing delay (1 s, 2 s, 4 s… capped at 60 s, plus a random component). After 5 consecutive network failures, a circuit breaker stops the session with the message "Too many consecutive network errors. Check your Internet connection then restart the upload." Nothing is lost: the files remain pending or in a retriable error state, and a single click on **Start upload** resumes the work once the network is restored.

**What happens if the token expires?**
There are three different "tokens", all handled automatically:
- the **access token** (short-lived) is refreshed automatically, including in the middle of an upload;
- the **refresh token** (long-lived) can be revoked or expire on Google's side; in that case the upload stops with the message "Google session expired: reconnect your account then restart the upload." — click **Connect my Google account**, then restart;
- a file's **upload token** (bytes already transferred but media not yet created) is reused if it is less than 20 hours old; beyond that, the file's bytes are simply resent.

**Can the application detect that a photo is already in Google Photos?**
Only if **it** was the one that put it there. The API gives it no access to the rest of your library (see the box in the **Settings** tab).

**What happens if I modify a file that has already been uploaded?**
On the next scan, the content change is detected (different hash): the file returns to **Pending** and the new version is uploaded as a new media item. The old version remains in Google Photos: the application never deletes anything.

**Are videos supported?**
The default format list contains only image formats (including RAW). The size limit configured in the application targets photos (Google limit: 200 MB).

**Can I run the application twice at the same time?**
No: only one instance can run at a time (the two would compete for the same inventory). If you relaunch the application while it is already open, a message "Google Photos Local Uploader is already running." appears and the second instance closes.

**Where are my data and my secrets stored?**
The inventory, the history and the settings: `%APPDATA%\GooglePhotosLocalUploader\app.db`. The logs: `%APPDATA%\GooglePhotosLocalUploader\logs\`. The refresh token and the OAuth client secret: in Windows Credential Manager (entries `GooglePhotosLocalUploader/RefreshToken` and `GooglePhotosLocalUploader/OAuthClientSecret`), never in plain text on disk. The Client ID, which is not a secret, is stored in the database.
