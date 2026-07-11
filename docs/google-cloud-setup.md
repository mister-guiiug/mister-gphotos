# Google Cloud setup (OAuth) — step-by-step guide

This guide is intended for any user of **Google Photos Local Uploader**, with no prior
technical knowledge required. It explains how to create, in Google Cloud, the OAuth
credentials the application needs to send your photos to your Google Photos account.

Plan for about **15 minutes**. All you need is a browser and your Google account.

> **Three ways to do it — pick yours:**
>
> 1. **The built-in wizard (recommended)**: in the application, **"Settings"** tab →
>    **"Google Cloud setup wizard..."** button. It follows exactly the steps in this
>    guide, opens the right console pages at each step, then **imports the downloaded
>    `client_secret_….json` file** — nothing left to copy by hand.
> 2. **This guide**, to do everything manually in the browser.
> 3. **The `scripts\setup-google-cloud.ps1` script** (for users of the `gcloud` CLI):
>    it automates steps 1 and 2 (project + enabling the API), then opens the pages for
>    steps 3 and 4.
>
> **Why not full automation?** Google exposes **no public API** to create an "External"
> consent screen or a "Desktop app" OAuth client: neither `gcloud` nor Terraform
> (`google_iap_brand` / `google_iap_client` only cover Google Workspace organizations and
> IAP clients) can do it. These two steps must therefore be done in the console,
> whatever the tool.

> **Why is this step necessary?**
> The application does not have a Google "key" shared among all users: each person creates
> **their own OAuth client** in **their own** Google Cloud console. It is free, requires no
> credit card, and guarantees that no one but you controls access to your account. The
> application will **never** ask for your Google password: sign-in happens in your browser,
> directly on Google's pages.

---

## What you should know before you start (limitations, stated plainly)

- **Since 31 March 2025**, the Google Photos Library API no longer allows an application to
  read your entire Google Photos library. An application can only read back **the media it
  created itself**. As a result, shown as-is in the application: "Google Photos does not
  allow this application to check your entire library. Duplicate detection is guaranteed
  only for files already indexed locally or uploaded by this application."
- Uploaded photos **count toward your Google account storage** (original quality). The API
  does not offer the "storage saver" option.
- Google Photos limits each photo to **200 MB**.
- The API enforces a **daily request quota** (10,000 by default, see the
  [Quotas](#photos-library-api-quotas) section below).
- The application **never** deletes your local files or your Google Photos media.

---

## Step 1 — Create a Google Cloud project

1. Open the Google Cloud console: <https://console.cloud.google.com/> and sign in with the
   Google account that will receive the photos.
2. If this is your first visit, accept the terms of service when Google presents them.
3. At the top of the page, click the **project selector** (next to the "Google Cloud"
   logo), then **"New Project"**.
4. Give the project a name, for example `Photos Local Uploader`, leave the other fields at
   their defaults, then click **"Create"**.
5. Wait a few seconds, then **select this new project** in the project selector (make sure
   its name does appear at the top of the page for all the following steps).

## Step 2 — Enable the "Photos Library API"

1. In the left-hand menu, open **"APIs & Services"** then **"Library"**
   (or go directly to <https://console.cloud.google.com/apis/library>).
2. In the search field, type **`Photos Library API`**.
3. Click the **"Photos Library API"** result, then the **"Enable"** button.

Without this activation, sign-in will fail with an access-denied error.

## Step 3 — Configure the OAuth consent screen

The consent screen is the page Google will show you in the browser at sign-in time, asking
you to authorize the application.

> Depending on the console version, this configuration is found under
> **"APIs & Services" → "OAuth consent screen"**, or under the
> **"Google Auth Platform"** section. The labels may vary slightly, but the choices to
> make remain the same.

1. Open **"APIs & Services" → "OAuth consent screen"**.
2. User type (**Audience**): choose **"External"** (the "Internal" option only exists for
   Google Workspace organizations). Click **"Create"**.
3. Fill in the required fields:
   - **Application name**: for example `Google Photos Local Uploader`;
   - **User support email**: your own address;
   - **Developer contact information**: your own address as well.
4. Leave the rest at the defaults and save. Adding the "scopes" on this screen is optional:
   the application will request them itself at sign-in time (the exact list is given
   [below](#permissions-scopes-requested-by-the-application)).
5. **Test users**: in the "Test users" section (or "Audience" → "Test users"), click
   **"Add users"** and add **your own Gmail address** (that of the destination Google
   Photos account). Without this, Google will block sign-in with a message like "Access
   blocked: this app has not completed the Google verification process".

Your application is then in **"Test" mode** (publishing status "Testing").

### Important: in Test mode, the connection expires every 7 days

This is a Google rule, not the application's: for an external application whose publishing
status is **"Testing"**, the **refresh token expires after 7 days**. In practice, about
once a week, the application will display "The Google session has expired or was revoked.
Reconnect your account." and you will need to click **"Connect my Google account"** again
(30 seconds, no data is lost: the inventory and progress are kept locally).

You have two options:

| Option | How | Consequence |
|---|---|---|
| **Stay in Test mode** | Do nothing more | Reconnect required every 7 days; sufficient for occasional use |
| **Go to production** | On the consent screen, click **"Publish app"** (status "In production") | The refresh token no longer expires after 7 days; recommended for regular use |

An honest note about going to production: the Google Photos scopes are classified as
"sensitive" by Google, and an "In production" application not verified by Google will
display, at sign-in, a **"Google hasn't verified this app"** warning. For strictly personal
use (you are both the developer of the Cloud project and the only user), this is expected
and harmless: click **"Advanced"** then **"Go to [application name]"** to continue. The full
Google verification process is only useful if you intend to distribute your OAuth client to
other people, which is not the case here.

## Step 4 — Create the "Desktop app" OAuth credential

1. Open **"APIs & Services" → "Credentials"**
   (<https://console.cloud.google.com/apis/credentials>).
2. Click **"Create credentials" → "OAuth client ID"**.
3. **Application type**: choose **"Desktop app"**. This is essential: this client type
   allows the local redirect `http://127.0.0.1:{port}/` that the application uses to
   receive the authorization (the port is chosen automatically at each connection, there is
   **nothing to declare** in the console about it).
4. Give it a name, for example `Client bureau Uploader`, then click **"Create"**.

## Step 5 — Retrieve the Client ID and Client Secret

Upon creation, Google shows a window containing:

- the **Client ID** — a long string ending in `.apps.googleusercontent.com`;
- the **Client Secret** — a string usually starting with `GOCSPX-`.

Copy these two values (copy button, or download the offered JSON file and keep it in a safe
place). You can always find them again later in **"APIs & Services" → "Credentials"**, by
clicking the name of the created client.

> Good to know: for a desktop application, Google itself considers that this "secret"
> cannot truly remain confidential. The application nevertheless treats it as a secret: it
> is stored encrypted in the Windows Credential Manager, never in plain text on disk (see
> [Security](#where-your-credentials-and-secrets-are-stored)).

## Step 6 — Enter the credentials in the application

1. Launch **Google Photos Local Uploader**.
2. Open the **"Settings"** tab, **"Google Authentication"** section.
3. Paste the Client ID into the **"OAuth Client ID:"** field.
4. Paste the secret into the **"OAuth Client Secret:"** field (the field masks input; the
   secret is not saved with the other settings, it is only kept — encrypted — after a
   successful connection).
5. Click **"Save settings"**.
6. At the top of the window, in the **"Configuration"** area, click
   **"Connect my Google account"**.
7. Your browser opens: choose your account, accept the requested permissions (in unverified
   Test mode, go through "Advanced" → "Go to…" if the warning appears). You have **5
   minutes** before the timeout ("Authorization timed out (5 minutes). Try again.").
8. The page displays **"Connection successful — You can close this window and return to
   Google Photos Local Uploader."**; in the application, the "Google account:" line changes
   to **"Connected: your-address@gmail.com"**.

To disconnect later: **"Disconnect"** button (the refresh token is revoked with Google and
removed from the Windows Credential Manager).

---

## Permissions (scopes) requested by the application

At sign-in, the application requests exactly four permissions, no more, no less:

| Scope | What it is for |
|---|---|
| `https://www.googleapis.com/auth/photoslibrary.appendonly` | **Add** photos to your library (upload). This is an add-only right: it does not allow reading, modifying, or deleting your existing media. |
| `https://www.googleapis.com/auth/photoslibrary.readonly.appcreateddata` | **Read only the media created by this application** — that is all the API allows since 31 March 2025. Used to check what the application has already sent. |
| `openid` | Standard identification, required to obtain the identity token. |
| `email` | Display the connected account's address in the interface ("Connected: …"). |

The application requests **no** scope granting read or modify access to your entire library:
such access no longer exists in the API for third-party applications anyway.

## Photos Library API quotas

- The Photos Library API default quota is **10,000 requests per day per project**. For
  personal use, this is more than enough in most cases: each photo consumes one upload
  request, plus one creation request (`mediaItems:batchCreate`) shared per batch of 20
  files (default setting, maximum 50) — that is, roughly **about 9,500 photos per day**.
- Where to check your quotas: Google Cloud console → **"APIs & Services"** →
  **"Enabled APIs & services"** → **"Photos Library API"** → **"Quotas & System Limits"**
  tab. This is also where you can request a possible increase from Google.
- If the quota or the rate limit is reached, the application loses nothing: it logs, for
  example, "Google Photos rate limit reached (429). Retrying automatically.", respects the
  delay requested by Google (`Retry-After` header), retries with a progressive backoff
  (capped at 60 s) and, if needed, the affected files will be resumed in the next session.

## Where your credentials and secrets are stored

| Data | Location | Sensitive? |
|---|---|---|
| OAuth Client ID | Local SQLite database (`%APPDATA%\MisterGPhotos\app.db`) | No (the Client ID is not a secret) |
| OAuth Client Secret | Windows Credential Manager, entry `MisterGPhotos/OAuthClientSecret` | Yes — never written in plain text |
| Refresh token (connection token) | Windows Credential Manager, entry `MisterGPhotos/RefreshToken` | Yes — never written in plain text |

The **"Delete the application's local data"** button ("Settings" tab, "Local data" section)
erases the inventory, the logs, the settings, and these two secrets. Your local photos and
your Google Photos media are not touched.

## Quick troubleshooting

| Symptom | Likely cause | Solution |
|---|---|---|
| "Access blocked: this app has not completed the verification process" | Your address is not in the **test users** (Test mode) | Step 3, point 5: add your address, then try again |
| "Google hasn't verified this app" warning | "In production" application not verified by Google | Normal for personal use: "Advanced" → "Go to…" |
| "The Google session has expired or was revoked. Reconnect your account." after a week | Test mode: refresh token expired after 7 days | Reconnect, or publish the application "In production" (step 3) |
| "The Client ID is missing or invalid (it must end with ...), or the Client Secret is missing. Do you want to open the step-by-step setup wizard?" | Empty fields, mis-copied Client ID, or settings not saved | Answer "Yes" to open the built-in wizard, or check both fields (the Client ID ends in `.apps.googleusercontent.com`) then "Save settings" |
| "OAuth code exchange refused by Google (401)." | Wrong Client Secret or client deleted in the console | Re-copy the secret from "APIs & Services" → "Credentials" |
| "Google did not provide a refresh token. Try connecting again." | Incomplete Google response (rare) | Restart "Connect my Google account"; the application forces a fresh consent request at each connection, a new refresh token will be issued |
| 403 error "Access refused by Google Photos: …" during upload | "Photos Library API" not enabled on the project | Step 2: enable the API, wait a few minutes, try again |
| "Authorization timed out (5 minutes). Try again." | The browser page stayed unresponsive for more than 5 minutes | Click "Connect my Google account" again |
| "Access to Google Photos was not granted. On the Google consent screen, check all the requested permissions then try connecting again." | Granular consent: the "Google Photos" boxes were not checked on the authorization screen | Restart the connection and check **all** the permissions offered by Google |
