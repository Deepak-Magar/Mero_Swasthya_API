# Backend progress

Session 1 of ~4 (≈ 25 %): foundation, **Auth**, **Catalog**, **Patients**. The remaining modules are
registered placeholders so the solution structure is final.

## 1. What exists

| Area | Status | Endpoints (under `/api/v1`) |
|---|---|---|
| Host / BuildingBlocks | done | `GET /health` (additive), `POST /dev/seed` (Development only), Swagger at `/swagger` |
| Auth | done | `POST /auth/otp/request`, `/auth/otp/verify`, `/auth/pin/set`, `/auth/pin/login`, `/auth/refresh`, `/auth/provider/activate`, `GET /me` |
| Catalog | done | `GET /codelists[?kind=]`, `GET /rules`, `GET /config`, `GET /facilities/nearby`, `GET /facilities` (additive) |
| Patients | done | `GET /patients`, `POST /patients`, `GET /patients/:id`, `PATCH /patients/:id`, `GET /patients/:id/timeline` |
| Grants, Clinical, Maternal, Reminders, Sync | placeholder `IModule` | — |

Behaviour worth knowing:

* **OTP**: demo mode (`Features:OtpDemo=true`) always issues `123456`; `demoOtp` is in the response only
  when `Features:SmsMode=mock`. 5 requests per phone per 10 min → `429 RATE_LIMITED`. A code must have
  been requested, is single-use, expires in 300 s, and dies after 5 wrong tries.
* **PIN**: Argon2id (m = 19 MiB, t = 2, p = 1) as a PHC string. 5 wrong PINs → `429` for 15 min
  (the fifth wrong attempt already answers 429; the right PIN is refused while locked).
* **Tokens**: access JWT 12 h; opaque refresh token 30 d, stored as SHA-256, rotated on every use;
  presenting an already-rotated token revokes its whole family (theft detection).
* **Activation**: `HA-GHORAHI-01` → provider, `FCHV-W5-01` → fchv, both at `f_0001 Ghorahi Health Post`;
  case-insensitive. The same access token sees the new role immediately.
* **/rules** is the app's `assets/rules.json` embedded byte-for-byte; the contract test deep-equals it
  against the file in the app checkout. `/codelists` is seeded from the app's `assets/codelists.json`
  (32 complaints, 42 diagnoses, 34 drugs, 10 danger signs, 8 risk factors — same codes, labels, order).
* **Patients**: client ids, idempotent create (same id → `200` with the stored row, unchanged),
  PATCH owner-only with `version` → `409 VERSION_CONFLICT` + `details.current` (also under a real race:
  EF concurrency token). Summary has the full A.4 shape; timeline is `{ items, nextBefore }`.

### Decisions where Part A was silent or the mock differs (all additive / non-breaking)

| Topic | Decision | Why |
|---|---|---|
| Wrong OTP | `400 VALIDATION_ERROR`, `details.otp = "Wrong code"` | identical to `mock_api.dart` |
| Wrong PIN / unknown phone on `/auth/pin/login` | `400 VALIDATION_ERROR`, `details.pin`, same text for both | mirrors the mock; a `401` would make the app's `AuthInterceptor` try refresh-and-replay on the login screen; no account enumeration |
| Unknown invite code | `400 VALIDATION_ERROR`, `details.inviteCode = "Unknown invite code"` | identical to the mock |
| `/auth/pin/set` for an existing account | resets the PIN (OTP proves the phone); `name` optional then, required for a new account | forgot-PIN path |
| `GET /patients` for provider/fchv | owned profiles **∪** actively granted patients | A.4 says granted (provider) / owned (patient); a health worker can also own family profiles. Granted part lights up when Grants registers an `IPatientGrantSource` |
| `POST /patients` with an id owned by another account | `403 FORBIDDEN` | returning someone else's row would leak it |
| Create status code | `200` for both the first create and the idempotent replay | A.4 shows only the envelope; mock returns 200 |
| `summary.activeProblems[].since` | `null` until Clinical can date the first diagnosing visit | no data yet |
| `summary.lastVitals` | `{ bpSys, bpDia, weightKg, at }` (the A.4 example keys; the app model has exactly these) | |
| Seeded provider `+9779801000002` | PIN `1234` like the patient | brief did not specify; saves a set-PIN step in demos |
| DB unreachable on `/health` | `500 INTERNAL`, `details.database = "down"` | A.3 has no 503 |
| `mock_api.dart` summary (`pregnancyActive`, labels = code) | **not** copied — Part A shape served | contract is law; the app's `PatientSummary` model follows Part A |

## 2. How to run

Prerequisites: .NET SDK 8.0.417, and either Docker **or** PostgreSQL ≥ 16 binaries installed.

```powershell
# database up (compose, or a private cluster in .localpg\ on :5433) → migrate → seed → run on :5000
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1

# in a second terminal, while the API runs
powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1
```

Manual equivalents: `docker compose up -d` · `dotnet run --project src/MeroSwasthya.Api -- --seed`
(migrate + seed, exit) · `dotnet run --project src/MeroSwasthya.Api --launch-profile http`.
`--migrate` applies migrations only. In Development, startup also migrates and seeds
(`Database:MigrateOnStartup/SeedOnStartup`). Stop the local cluster with `scripts\dev.ps1 -StopDb`.

Demo accounts: `+9779801000001` (patient "Sita Chaudhary", PIN 1234, owns Sita / Ram / Aarav),
`+9779801000002` (provider "Ramesh Thapa (HA)" @ f_0001, PIN 1234). OTP is always `123456`.

### Environment used in this session

Docker is **not installed** on this machine, so everything ran against a **local PostgreSQL 18.1**
cluster that `scripts/dev.ps1` creates in `.localpg\` on port **5433** (user/password `swc`/`swc`),
separate from the PostgreSQL 18 service already on 5432 (untouched). The contract tests detect Docker
first (Testcontainers `postgres:16`) and otherwise create and drop a throwaway database on
`MS_TEST_PG` (default that same 5433 server); `HealthTests.Reports_which_database_the_contract_tests_use`
prints which one. `docker-compose.yml` is written for postgres:16 / redis:7 / minio but was not
exercised here.

## 3. Tests

```
dotnet test            # 183 tests: 80 unit + 103 contract, all passing
```

* **Contract tests (103)** — real host, real HTTP, real PostgreSQL. Every A.4 Auth endpoint (shapes,
  validation details, rate limits, lockout, rotation + reuse detection, temp-vs-access token), Catalog
  (rules deep-equal to the app asset, codelists deep-equal, config flags, Haversine ordering against
  independently computed distances, query validation), Patients (Part A example round-trip, idempotent
  create, cross-owner id, defaults, 13 validation cases, owner/stranger/health-worker access,
  summary shapes incl. Ram's labelled problems, PATCH apply / stale / concurrent race / null-vs-omitted
  / owner-only, timeline page + cursor validation), health, routing 404 envelope.
* **Unit tests (80)** — uuid v5 against Python reference vectors (A.8.15 + addendum ids), JSON
  conventions (dates, ms, enums, Optional, escaping), error table A.3, cursors, phone normalisation,
  Argon2id, Haversine, rules document, nearby parsing, timeline merge + paging, summary contributors,
  PATCH validator field names, architecture rules.

## 4. Smoke run (scripts/smoke.ps1, this session)

```
Smoke test against http://127.0.0.1:5000/api/v1
----------------------------------------------------------------------------------------------------
[PASS] GET  /health                       HTTP 200  status=healthy database=up modules=8
[PASS] POST /auth/otp/request             HTTP 200  otpSentTo=+9779801000001 expiresInSec=300 demoOtp=123456
[PASS] POST /auth/otp/verify              HTTP 200  hasPin=True isNewUser=False tempToken=eyJhbGciOiJIUzI1...
[PASS] POST /auth/pin/login               HTTP 200  user=u_11111111-1111-4111-8111-111111111111 role=patient refreshToken=43 chars
[PASS] POST /auth/refresh                 HTTP 200  rotated=True
[PASS] GET  /me                           HTTP 200  name=Sita Chaudhary phone=+9779801000001 role=patient
[PASS] GET  /patients                     HTTP 200  items=3: Sita Chaudhary v1, Ram Bahadur Chaudhary v1, Aarav Chaudhary v1
[PASS] GET  /patients/:id (Ram)           HTTP 200  allergies=penicillin problems=E11,I10 visitCount=0
[PASS] GET  /patients/:id/timeline        HTTP 200  items=0 nextBefore=
[PASS] GET  /codelists                    HTTP 200  version=2026-09-18.1 complaint=32 diagnosis=42 drug=34 dangerSign=10 riskFactor=8
[PASS] GET  /rules                        HTTP 200  version=2026-09-18.1 ancWeeks=12,20,26,30,34,36,38,40 dangerSigns=10
[PASS] GET  /config                       HTTP 200  smsMode=mock otpDemo=True aiSummaryEnabled=False rulesVersion=2026-09-18.1
[PASS] GET  /facilities/nearby            HTTP 200  f_0001 0.5km, f_0002 0.6km, f_0004 18.6km
----------------------------------------------------------------------------------------------------
SMOKE PASSED
```

## 5. What is next — in this order

Each module: DbContext in its own schema, migration, idempotent seed (with `updatedAt = now`,
addendum §7), validators, one DTO per entity, contract tests asserting the Part A JSON.

### 5.1 Grants (schema `grants`)
* **A.2** AccessGrant (+ addendum §4 `sections`, echoed), AuditEntry.
* **A.4** `POST /grants` (rate limit 20 / patient / hour → 429; owner only), `POST /grants/redeem`
  (provider/fchv only → else 403; expired → `403 GRANT_EXPIRED`; redeemed by someone else →
  `409 ALREADY_REDEEMED`; bundle = patient + summary + timeline(50) + pregnancy + ancContacts, filtered
  by `sections` server-side, patient row always included, withheld summary = `PatientSummaryDto.Empty`),
  `POST /grants/:id/revoke`, `GET /patients/:id/audit` (owner only).
* **A.7** token `SWC1:<jwt>`, HS256 with `SigningKeys.Grant`, claims `{ typ:"grant", gid, pid, scope,
  iat, exp }`; printed card (ttl ≥ 1 day) requires the patient `pin` on redeem (Tier 2).
* **A.6 #15** redeem after 10 min → GRANT_EXPIRED. Access window 24 h (`accessUntil`).
* Implement `IPatientGrantSource` (Patients picks it up automatically: GET /patients, detail, timeline,
  `IPatientAccess`). Add the `record_viewed` audit in `PatientService.GetAsync` (TODO marker).
* Seed: the three dashboard women `p_…0003–0005` with already-redeemed grants for the seeded provider.

### 5.2 Clinical (schema `clinical`)
* **A.2** Visit, Prescription (embedded), Document. **A.4** `POST/GET /patients/:id/visits`
  (owner or append grant; idempotent; server fills provider/facility from the token; writes
  `visit_added` audit via a Grants contract; follow-up reminder via Reminders later),
  `POST /documents/presign` (≤ 2 MB, MinIO presigned PUT), `/documents/:id/complete`,
  `GET /documents/:id` (fresh 1 h URL), `/documents/:id/summarize` (501 when `AiMode=off`).
* `IPatientSummaryContributor`: currentMedicines (latest visit's prescriptions), lastVitals (newest visit
  with any vitals), lastVisitAt, visitCount, `activeProblems[].since`.
* `ITimelineContributor`: kinds `visit` (title "Visit — <facility> — <codes>", subtitle
  "BP 138/88 · E11 · 1 Rx" as in `mock_api.dart`) and `document`.
* **A.6 #16** provider visit 25 h after redeem → GRANT_EXPIRED.
* Seed: Ram's visit `v_c3c3c3c3-…-0001` and documents `d_e5e5e5e5-…-0001/0002`.

### 5.3 Maternal (schema `maternal`)
* **A.2** Pregnancy (computed `gestationalAgeDays`, `nextContact`), AncContact, Delivery; addendum §1–2
  immunisations (deterministic ids — `Ids.ImmunisationId` already exists and is tested) and
  growth_measurements; §5 `findings.notesText` round-trip (store findings as jsonb).
* **A.4** `POST /patients/:id/pregnancies` (female only → `422 RULE_VIOLATION`; 8 contacts with
  `Ids.AncContactId`), `GET/PATCH /pregnancies/:id`, `PUT /pregnancies/:id/contacts/:contactNo`
  (server triage + `nearestReferral` via `IFacilityDirectory`), `POST /pregnancies/:id/delivery`.
* **A.5 / A.6 #1–#12**: EDD = lmp + 280 d; dueAt = edd − 280 + week×7; triage exactly as the app's
  `lib/domain/rules/triage.dart` (same reason codes and en/np texts — copy them, do not re-word).
* Summary contributor (`activePregnancy`), timeline kinds `pregnancy_registered`, `anc_contact`
  (badge = triage level), `delivery`, `immunisation` (given doses only), `growth`.
* Seed: Sita's week-30 pregnancy (contacts 1–3 done), Aarav's EPI schedule (MR-2 and TCV overdue) and
  three weights (10.4, 11.8, 13.1 kg). Regenerate immunisation `dueAt` on a dob PATCH (TODO marker).

### 5.4 Sync (schema `sync`)
* **A.4** `POST /sync/push` (per-change `applied` / `duplicate` (opId seen) / `conflict` (+ `current`) /
  `rejected` (+ `error {code,message}`); never fails the batch; `serverTime`),
  `GET /sync/pull?since=&deviceId=` (page 200, `hasMore`, `cursor`, deleted rows included).
* **A.8** whole protocol; addendum §3 table whitelist incl. `immunisations`, `growth_measurements`.
  Visibility = `IPatientAccess.VisiblePatientIdsAsync`. **A.6 #13–#14.**
* Each owning module exposes an internal "apply change / changed since" contract; Sync never opens their
  DbContexts. Use an `(updatedAt, table, id)` keyset so rows sharing a millisecond are never skipped.

### 5.5 Reminders (schema `reminders`)
* **A.2** Reminder; **A.4** `GET /patients/:id/reminders`, `GET /demo/sms` + `/demo/sms.html`
  (only when `SmsMode=mock`).
* A.5 `reminders` rules: anc_due 1 day before at 09:00 Asia/Kathmandu (patient + emergency contact),
  anc_missed +3 d and +7 d, follow_up 1 day before; cancel anc_missed on contact recorded.
  Background dispatcher (hosted service; Redis optional). Also takes over sending OTP SMS when
  `SmsMode != mock` (TODO in `AuthService`).
* Seed: the two `anc_due` reminders for Sita that `/demo/sms` shows.
