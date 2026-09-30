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

Sign-in, set PIN, PIN login, token refresh, provider activation, `/me`, the family list, code lists,
rules refresh, config flags, nearest facilities, and — since session 2 — **QR share → scan → redeem**
across two phones (patient phone draws the code, provider phone scans it and receives the bundle),
revoke, the provider's "recent patients" (granted patients in `GET /patients`), **document capture**
(presign → upload → complete, then viewing the photo), the patient summary with problems / medicines /
last vitals, the unified timeline, the owner's **access log** (S16) and — since session 3 — the
**reminders list** (S15). Creating / editing profiles and recording **visits** (provider with an append
grant, or the owner as "Self-reported") work on the server, but the app sends them through its sync
outbox — see "Still needs Session 4" below.

Printed card (A.7): a share with `ttlMinutes` ≥ 1440 is `longLived`; redeeming it needs the patient's
PIN in the request (`403 FORBIDDEN` with `details.pin = required | invalid` otherwise).

**Since session 3 the server side of maternal care and reminders is complete** (§5.1), so what the app
*reads* carries it: the redeem bundle has `pregnancy` + `ancContacts` for a woman with an active pregnancy,
`GET /patients/:id` has `summary.activePregnancy`, the timeline has `pregnancy_registered` / `anc_contact`
(badge = triage) / `delivery`, and `GET /patients/:id/reminders` (S15) returns real rows. With the seeded
account, Sita's profile shows a week-30 pregnancy with contacts 1–3 recorded and 30 reminders (2 sent).

**Still needs Session 4 (Sync) — read this before a demo with Demo data mode off.** The app does not call
the write endpoints for profiles, visits, pregnancies, ANC contacts or deliveries. Every write on those
tables goes through `SyncableRepo.writeAndEnqueue` (local row + outbox op) and reaches the server only by
`POST /sync/push`; `PatientsApi.addVisit` and the whole `PregnanciesApi` have no caller in `lib/` (searched
2026-09-30). Until `/sync/push` exists the push gets a `404 NOT_FOUND` envelope and those records stay on
the phone — a pregnancy registered on the phone gets no server triage and no reminders yet. What the app
does call directly, and what therefore works today: auth, `GET /patients`, timeline, audit, reminders,
grants (create / redeem / revoke), document presign / upload / complete, code lists, rules, config,
facilities. The endpoints themselves are exercised by `scripts\smoke.ps1` and the contract tests.

Not yet: immunisation / growth (addendum §1–2), sync push/pull. AI summaries answer `501 NOT_IMPLEMENTED`
and `/config` reports `aiSummaryEnabled: false`, so the app hides the button.

### 5.1 Maternal and reminders — shapes worth knowing on the Flutter side

* **Triage is the server's.** `PUT /pregnancies/:id/contacts/:contactNo` returns `ancContact.triageLevel` /
  `triageReasons` (English texts, same wording and order as `triage.dart`) and `nearestReferral`
  (a Facility with `distanceKm`, or `null` when green or when no facility can be worked out).
  `contactNo` outside 1..8 is `422 RULE_VIOLATION`, not `404`.
* **`nextContact`** is the earliest contact with `doneAt = null` (A.2), so for a pregnancy registered
  late it is an overdue contact 1, not the next one by date.
* **After a delivery** (or `status: ended`) the contacts that were never recorded come back `deleted: true`
  and are left out of `GET /pregnancies/:id`; the pregnancy is `delivered` and rejects further contacts
  (`422`). `PATCH /pregnancies/:id` accepts `birthPlan`, `riskFactors` and `status: "ended"` only.
* **Audit:** every maternal write (register, patch, contact, delivery) appears in S16 as `contact_recorded`.
* **Reminders** (`GET /patients/:id/reminders`, earliest first, default 50, `?limit=` up to 200):
  * `dueAt` is always `…T03:15:00.000Z` (09:00 Nepal time) — the same instant `reminders_preview.dart`
    computes, so a local preview and the server row line up.
  * One row per recipient: `recipientRole: "patient"` is the phone of the account that owns the profile,
    `"family"` is `emergencyContactPhone` (ANC reminders only; none when it is unset or the same number).
  * `messageNp` carries the BS date in Nepali digits; a name or facility stored in Latin letters stays
    in Latin letters ("Sita Chaudhary को ४ औं गर्भ जाँच २०८३-०६-१४ मा Ghorahi Health Post मा छ।").
  * Only messages still ahead at registration exist. Recording a contact, a delivery, or ending the
    pregnancy makes the pending ones disappear from the list (there is no "cancelled" status);
    `status` is only ever `pending`, `sent` or `failed`.
  * A pregnancy's rows are also in `GET /pregnancies/:id` → `reminders`.
  * Additive, not used by the app today: `POST /reminders/:id/done`, `POST /reminders/:id/cancel`,
    `GET /patients/:id/pregnancies`, `GET /pregnancies/:id/contacts`.
* **SMS panel.** `GET /api/v1/demo/sms` (the A.4 path, `{ items: [{ to, text, sentAt }] }`, no auth) and the
  projector page `http://127.0.0.1:5000/api/v1/dev/sms` exist only when the API runs in Development with
  `Features:SmsMode=mock` (what `scripts\dev.ps1` starts); otherwise they are `404`. The outbox is in
  memory: it restarts with Sita's two seeded messages. Reminders are delivered by a poll every 60 s.

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
