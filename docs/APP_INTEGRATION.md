# Pointing the Flutter app at this API

The app needs **no code change** — only its Settings screen (S23). Everything below was checked
against the app source in `D:\mero_swasthya` (`lib/core/config/app_config.dart`,
`lib/core/net/interceptors.dart`, `lib/features/settings/settings_screen.dart`).

## 1. Start the API

```powershell
cd D:\mero_swasthya_api
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1     # DB up, migrate, seed, listen on 0.0.0.0:5000
```

Check from the laptop: `curl http://127.0.0.1:5000/api/v1/health` → `{"ok":true,"data":{"status":"healthy",…}}`.

## 2. Make the phone reach the laptop

**USB (recommended for the demo) — `adb reverse`:**

```powershell
adb reverse tcp:5000 tcp:5000        # repeat per phone; re-run after every reconnect
adb reverse --list                   # should show  tcp:5000 tcp:5000
```

The phone's `127.0.0.1:5000` is now the laptop's `:5000`. Base URL: **`http://127.0.0.1:5000/api/v1`**.
Two phones on two USB ports can share one server this way — which is what the cross-phone sync /
QR-redeem moment needs (`docs/QR_LIVE_SCAN.md` §4 in the app repo).

**Android emulator:** `http://10.0.2.2:5000/api/v1` (no adb reverse needed).

**Same Wi-Fi (LAN IP):** find the laptop address (`ipconfig` → IPv4, e.g. `192.168.1.20`) and use
`http://192.168.1.20:5000/api/v1`. Allow inbound TCP 5000 once in Windows Firewall:

```powershell
New-NetFirewallRule -DisplayName "Mero Swasthya API 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

**Over the internet (A.1 hackathon setup):** `cloudflared tunnel --url http://localhost:5000` (or
`ngrok http 5000`) and use `https://<generated-host>/api/v1`.

## 3. Settings in the app (S23)

1. Open **Settings**.
2. **Server address** → `http://127.0.0.1:5000/api/v1` (or one of the alternatives above), save.
   The app reads the URL fresh on every request — no restart needed.
3. **Demo data mode** (the mock switch) → **off**. The choice is persisted, so it survives relaunches.
4. Sign out and sign in again if the device previously ran on the mock: mock tokens (`mock_access`)
   are not JWTs and the server answers them with `401 UNAUTHENTICATED`.

The app's compiled-in default is `http://10.0.2.2:3000/api/v1` (port 3000) — so the Server address **must** be set, unless you start this API on port 3000 instead
(`$env:ASPNETCORE_URLS="http://0.0.0.0:3000"` before `dotnet run`), or build the app with
`--dart-define=API_BASE_URL=http://127.0.0.1:5000/api/v1`.

## 4. Signing in (OTP demo mode)

`Features:OtpDemo=true` (default in Development): the OTP is **always `123456`**, no SMS is sent, and
because `Features:SmsMode=mock` the `/auth/otp/request` response also carries `"demoOtp": "123456"`.

| Phone | Role | PIN | Notes |
|---|---|---|---|
| `9801000001` / `+9779801000001` | patient — Sita Chaudhary | 1234 | owns Sita, Ram, Aarav |
| `9801000002` / `+9779801000002` | provider — Ramesh Thapa (HA) @ Ghorahi HP | 1234 | |
| any other number | new account | you choose | OTP → Set PIN + name |

Become a health worker from any account in S18 with invite **`HA-GHORAHI-01`** (provider) or
**`FCHV-W5-01`** (FCHV). The server remembers the role; there is no need for the mock's
"activated role" workaround.

Limits you may hit while testing: 5 OTP requests per phone per 10 min, and 5 wrong PINs lock the
number for 15 min (both answer `429 RATE_LIMITED`; the phone-number screen shows its rate-limit message
for the OTP case, while the unlock screen just reports a failed unlock). Restarting the API does not clear them (they are in the database); use another number.

## 5. What works against this backend today

Sign-in, set PIN, PIN login, token refresh, provider activation, `/me`, the family list, creating
and editing profiles (online), code lists, rules refresh, config flags, nearest facilities, and — since
session 2 — **QR share → scan → redeem** across two phones (patient phone draws the code, provider phone
scans it and receives the bundle), revoke, the provider's "recent patients" (granted patients in
`GET /patients`), **visits** (provider with an append grant, or the owner as "Self-reported"),
**document capture** (presign → upload → complete, then viewing the photo), the patient summary with
problems / medicines / last vitals, the unified timeline, and the owner's **access log** (S16).

Printed card (A.7): a share with `ttlMinutes` ≥ 1440 is `longLived`; redeeming it needs the patient's
PIN in the request (`403 FORBIDDEN` with `details.pin = required | invalid` otherwise).

Not yet — keep **Demo data mode on** for these demos until the module lands: pregnancy / ANC / delivery /
immunisation / growth (Maternal; the redeem bundle carries `pregnancy: null, ancContacts: []`),
**offline sync push/pull** (Sync — the app's background sync gets `404 NOT_FOUND` envelopes from
`/sync/*`) and reminders / `/demo/sms` (Reminders). AI summaries answer `501 NOT_IMPLEMENTED` and
`/config` reports `aiSummaryEnabled: false`, so the app hides the button.

## 6. Document uploads (MinIO or the dev fallback)

The app uploads a photo in three calls: `POST /documents/presign` → `PUT <uploadUrl>` with
`uploadHeaders` → `POST /documents/:id/complete`. It displays `downloadUrl` with `Image.network`,
which sends no token.

**Why `uploadUrl` points at this API by default (`Storage:UploadMode=proxy`).** The app sends the PUT
through the same Dio client as its API calls (`ApiClient.uploadBytes`). That client's `AuthInterceptor`
adds `Authorization: Bearer <accessToken>` to every path not on its allow-list, and an absolute MinIO
URL is not on it. S3 and MinIO reject a request that carries both a presigned query signature and an
Authorization header. So with a raw MinIO presigned URL the upload would fail, and fixing that would
need an app change. In proxy mode the URL is `…/api/v1/documents/:id/upload?exp=…&sig=…`, which
accepts that bearer header (or just the signature). The server then writes the bytes to MinIO when it
is reachable, and to local disk otherwise. (This conclusion comes from reading the app code; there was
no MinIO on the development machine to confirm it end to end.)

**With MinIO** (`docker compose up -d minio`, or the standalone `minio.exe server … --console-address :9001`):

* The API reaches it at `S3:Endpoint` (default `http://127.0.0.1:9000`) and creates bucket `swc-documents`.
* Photos are stored under `patients/{patientId}/{documentId}.jpg`.
* `downloadUrl` is a presigned GET for **`S3:PublicEndpoint`**. It must be an address the phone can
  open: the laptop's LAN IP (`http://192.168.1.20:9000`), or `http://127.0.0.1:9000` together with
  `adb reverse tcp:9000 tcp:9000`.
* Allow inbound TCP 9000 in the firewall for the LAN option.
* `Storage:UploadMode=presigned` switches uploads to direct MinIO PUTs, for a client that sends no
  Authorization header to foreign URLs.

**Without MinIO (dev fallback):** nothing to configure. `Storage:Mode=auto` notices MinIO is
unreachable (it re-checks every 30 s) and stores bytes in `src\MeroSwasthya.Api\.data\documents\`.
`downloadUrl` is then a signed `GET /api/v1/documents/:id/file` URL on the same host the phone already
uses (`127.0.0.1:5000` via `adb reverse`). Documents stored on disk stay there if MinIO appears later.
Behind a tunnel, set `Storage:PublicApiBaseUrl` to the tunnel's `https://…/api/v1` so the URLs point at it.

**Dev-only manual upload** (Development environment only, never mapped in production):

```powershell
curl.exe -H "Authorization: Bearer <accessToken>" -F "file=@photo.jpg;type=image/jpeg" `
  http://127.0.0.1:5000/api/v1/documents/<documentId>/upload
```

After `presign` this stores the bytes, just as the PUT would; then call `complete`.

Limits: 2 MB, `image/jpeg` or `image/png`, upload URL valid 15 min, download URL 1 h.

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| "Network error" immediately | `adb reverse --list` empty (re-run it), wrong port, or API not running. |
| `Cleartext HTTP traffic … not permitted` / "Insecure HTTP is not allowed" | The app's `AndroidManifest.xml` does not set `usesCleartextTraffic`. If a build enforces it, use the HTTPS tunnel URL from §2 (no app change). |
| Every call `401 UNAUTHENTICATED` right after switching off the mock | Mock-issued session; sign out and sign in again (§3.4). |
| `429 RATE_LIMITED` on OTP | 5 requests / 10 min per phone; wait or use another number. |
| Photo upload fails with `400`/`403` from `:9000` | `Storage:UploadMode=presigned` with the current app build — switch back to `proxy` (§6). |
| Photo does not show after upload | `downloadUrl` points at a host the phone cannot reach: set `S3:PublicEndpoint` (MinIO) or `Storage:PublicApiBaseUrl` (tunnel). |
| `403 GRANT_EXPIRED` on a provider phone | The 24 h access window after the scan ended (or the QR was older than 10 min) — scan a fresh code. |
| Seed data missing | `POST http://127.0.0.1:5000/api/v1/dev/seed` (Development only) or re-run `scripts\dev.ps1`. |
