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
and editing profiles (online), the provider summary for owned patients, the empty timeline,
code lists, rules refresh, config flags, and nearest facilities.

Not yet (later sessions — keep **Demo data mode on** for these demos until then): QR share / redeem
(Grants), visits and documents (Clinical), pregnancy / ANC / delivery / immunisation / growth
(Maternal), **offline sync push/pull** (Sync) and reminders / `/demo/sms` (Reminders). The app's
background sync will get `404 NOT_FOUND` envelopes from `/sync/*` until the Sync module lands.

## 6. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| "Network error" immediately | `adb reverse --list` empty (re-run it), wrong port, or API not running. |
| `Cleartext HTTP traffic … not permitted` / "Insecure HTTP is not allowed" | The app's `AndroidManifest.xml` does not set `usesCleartextTraffic`. If a build enforces it, use the HTTPS tunnel URL from §2 (no app change). |
| Every call `401 UNAUTHENTICATED` right after switching off the mock | Mock-issued session; sign out and sign in again (§3.4). |
| `429 RATE_LIMITED` on OTP | 5 requests / 10 min per phone; wait or use another number. |
| Seed data missing | `POST http://127.0.0.1:5000/api/v1/dev/seed` (Development only) or re-run `scripts\dev.ps1`. |
