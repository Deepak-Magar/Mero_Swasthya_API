# Backend progress

Session 3 of 4 done (≈ 75 %). Session 1: foundation, Auth, Catalog, Patients. Session 2: Grants,
Clinical (visits + documents) and Audit. Session 3: **Maternal** (pregnancies, ANC contacts with
server-side triage, deliveries) and **Reminders** (scheduling, delivery worker, mock SMS outbox).
Session 4: Sync push/pull and app integration by URL (§5). Sync is a registered placeholder.

## 1. Module status

| Module | Schema | Status | Endpoints (under `/api/v1`) |
|---|---|---|---|
| Host / BuildingBlocks | — | done | `GET /health`, `POST /dev/seed` (Development), Swagger `/swagger` |
| Catalog | `catalog` | done | `GET /codelists`, `/rules`, `/config`, `/facilities`, `/facilities/nearby` |
| Auth | `auth` | done | `POST /auth/otp/request`, `/otp/verify`, `/pin/set`, `/pin/login`, `/refresh`, `/provider/activate`, `GET /me` |
| Patients | `patients` | done | `GET/POST /patients`, `GET/PATCH /patients/:id`, `GET /patients/:id/timeline` |
| Audit | `audit` | done (S2) | `GET /patients/:id/audit` |
| Grants | `grants` | done (S2) | `POST /grants`, `POST /grants/redeem`, `POST /grants/:id/revoke` |
| Clinical | `clinical` | done (S2) | `POST/GET /patients/:id/visits`, `POST /documents/presign`, `POST /documents/:id/complete`, `GET /documents/:id`, `POST /documents/:id/summarize`, `PUT /documents/:id/upload` (signed), `GET /documents/:id/file` (signed), `POST /documents/:id/upload` (multipart, **dev only**) |
| **Maternal** | `maternal` | **done (S3)** | `POST/GET /patients/:id/pregnancies` (GET additive), `GET/PATCH /pregnancies/:id`, `GET /pregnancies/:id/contacts` (additive), `PUT /pregnancies/:id/contacts/:contactNo`, `POST /pregnancies/:id/delivery` |
| **Reminders** | `reminders` | **done (S3)** | `GET /patients/:id/reminders`, `POST /reminders/:id/done`, `POST /reminders/:id/cancel` (both additive), `GET /dev/sms`, `GET /dev/sms.json`, `GET /demo/sms`, `GET /demo/sms.html` (all four: Development + `SmsMode=mock` only) |
| Sync | — | placeholder (S4) | — |

### Session 1 behaviour (unchanged)

OTP demo `123456` (5 / phone / 10 min); Argon2id PIN with 5-strike 15-min lockout; access JWT 12 h,
rotating refresh 30 d with reuse detection; invite activation; `/rules` = the app's `rules.json`
verbatim; codelists seeded from the app's `codelists.json`; idempotent patient create; PATCH with
`version` → `409 VERSION_CONFLICT` + `details.current`.

### Session 2 behaviour

**Grants (A.4 "Access grants (QR)", A.7)**
* `POST /grants` — owner only; `scope` default `append` (`read` for long-lived); `ttlMinutes` 1…525 600
  (default 10); optional `sections` (addendum §4). 20 per patient per hour → `429`. The row stores a `jti`;
  the token is HS256 with the **grant** key, claims `{ typ:"grant", gid, pid, scope, iat, exp, jti }`,
  exp = iat + ttl·60. Response `{ grant, token, qrPayload: "SWC1:" + token }`; `grant.token` is present
  only here. Audit `grant_created`.
* `POST /grants/redeem` — provider/fchv only (`403`). `SWC1:` prefix + signature/issuer/audience/`typ`
  check (an access token, a foreign QR or a bad prefix → `400 VALIDATION_ERROR details.qrPayload`).
  Revoked or expired (10 min) → `403 GRANT_EXPIRED`; used by another provider → `409 ALREADY_REDEEMED`;
  same provider again → idempotent while the 24 h window lasts. Concurrent scans: a conditional
  UPDATE lets exactly one provider win. Sets `redeemedByUserId`, `redeemedAt`, `accessUntil = +24 h`,
  audit `grant_redeemed`. Bundle `{ grant, patient, summary, timeline (≤ 50), pregnancy, ancContacts }`
  built through the Patients read models, filtered by `sections` server-side (patient row always
  included, withheld summary = the empty shape). `pregnancy`/`ancContacts` come from
  `IActivePregnancySource` — `null`/`[]` until Maternal registers one.
* Long-lived card (ttl ≥ 1 day, scope `read`): redeem also needs `pin` = the owner's PIN
  → else `403 FORBIDDEN` with `details.pin = "required" | "invalid"` (as mock_api.dart). Shares the
  login lockout (5 wrong → 429).
* `POST /grants/:id/revoke` — owner only; sets `revokedAt`, clears `accessUntil`; audit `grant_revoked`.
* Access: an active grant (redeemed by me, not revoked, `accessUntil` > now) makes the patient appear in
  the provider's `GET /patients` and opens `GET /patients/:id`, timeline, visits, documents. A window
  that ran out → `403 GRANT_EXPIRED` (A.6 #16); a revoked grant → `403 FORBIDDEN`.

**Clinical (A.4 "Visits", "Documents")**
* `POST /patients/:id/visits` — owner or append grant; **FCHV forbidden** unless owner. Idempotent on
  `id`. Complaint / diagnosis / drug codes validated against Catalog (`details.chiefComplaintCode`,
  `details.diagnosisCodes`, `details["prescriptions[0].drugCode"]`), vitals ranges, referral, frequency
  enum. Provider name/facility from the caller; the owner's own entries are `"Self-reported"` with no
  facility. `drugName` filled from the codelist. `supersedesId` must reference a visit of the same
  patient. Vitals/referral/prescriptions stored as jsonb. Audit `visit_added`; `followUpAt` publishes the
  `FollowUpScheduled` domain event (logged; Reminders will subscribe). No update/delete routes.
* `GET /patients/:id/visits?limit=` — newest first.
* Documents: presign (append access; ≤ 2 MB; image/jpeg|png; key `patients/{patientId}/{id}.jpg|png`)
  → PUT bytes to `uploadUrl` → complete (HEAD/exists check, else `422 RULE_VIOLATION`; → `uploaded`,
  version+1, audit `document_added`) → `GET /documents/:id` with a fresh 1 h `downloadUrl`.
  `summarize` → `501 NOT_IMPLEMENTED` while `Features:AiMode=off` (default), else `aiSummaryStatus=queued`.
* **Storage:** MinIO via AWSSDK.S3 when reachable (`Storage:Mode=auto`), else local disk
  (`Storage:LocalPath`). **`Storage:UploadMode=proxy` is the default**: `uploadUrl` is this API's signed
  `PUT /documents/:id/upload`, and the server writes the bytes to MinIO (or disk). `presigned` hands out
  a MinIO presigned PUT signed for `S3:PublicEndpoint` instead. Why proxy is the default:
  docs/APP_INTEGRATION.md §6. Download URLs are MinIO presigned GETs (public endpoint) or signed API
  URLs for local files — both work without a bearer (the app shows them with `Image.network`).
* Summary contribution: `activeProblems` = chronicConditions ∪ visit diagnoses, labelled, `since` = first
  visit with the code; `currentMedicines` = prescriptions still inside `durationDays`, newest per drug;
  `lastVitals` = newest visit with BP or weight; `lastVisitAt`; `visitCount`. Superseded visits excluded.
* Timeline contribution: `visit` ("Visit — {facility} — {first diagnosis label}", subtitle
  "{complaint} · BP s/d") and `document` (at = takenAt 00:00Z, subtitle = type label).

**Audit** — `IAuditWriter` (actor = current user, name + facility denormalised); `record_viewed` when a
non-owner reads `GET /patients/:id`, at most once per 10 min per (actor, patient);
`GET /patients/:id/audit` owner-only, newest first (ties broken by insertion order).

### Session 3 behaviour

The feature-by-feature detail is in §6; in short:

**Maternal (A.2, A.4 "Maternal", A.5, A.6 #1–#12, addendum §5)** — registering a pregnancy (female only,
one active per patient, idempotent on the client id) creates the eight ANC contacts with the deterministic
ids the app generates offline; `gestationalAgeDays` and `nextContact` are computed on read. Recording a
contact runs the triage engine (a port of the app's `triage.dart`: same order, reason codes and texts) and
returns the stored level + English reasons and `nearestReferral`. A delivery closes the pregnancy and
soft-deletes the contacts that never happened. Every endpoint goes through `IPatientAccess`; every write is
audited as `contact_recorded`. The pregnancy shows up in the patient summary, the timeline and the grant
redeem bundle.

**Reminders (A.2 Reminder, A.4 "Reminders", A.5 `reminders`)** — scheduled from domain events
(`PregnancyRegistered`, `AncContactRecorded`, `PregnancyClosed`, `FollowUpScheduled`, `VisitSuperseded`),
sent by a background worker every 60 s through `ISmsSender` (in-memory mock outbox while
`Features:SmsMode = mock`), visible at `GET /dev/sms`.

### Decisions where Part A was silent or the brief conflicted with the app

| Topic | Decision | Why |
|---|---|---|
| Upload URL | **proxy by default**, presigned MinIO optional | the app's upload goes through its Dio `AuthInterceptor`, which adds `Authorization: Bearer` to the presigned URL; S3/MinIO reject a request that carries both a query signature and an Authorization header. Verified by reading `lib/core/net/api_client.dart` + `interceptors.dart`; not verified against a live MinIO (none on this machine) |
| Grant check order | revoked → same provider (idempotent) → expired → other provider (409) | mock_api.dart order: an expired code is GRANT_EXPIRED whoever holds it |
| Revoked vs window ended | revoked → FORBIDDEN, window ended → GRANT_EXPIRED | A.3 wording ("access window (24 h) ended") |
| Grant DTO | adds `longLived`, `sections` | the app's `AccessGrant` model reads both (mock_api.dart, addendum §4) |
| Wrong QR | `400 VALIDATION_ERROR details.qrPayload` | mock says NOT_FOUND "Unknown QR code" for unknown tokens; a forged/foreign token is invalid input. Unknown-but-valid grant id → `404` |
| Visit id reused for another patient | `400 details.id` | never leak / overwrite another record |
| Complete before upload | `422 RULE_VIOLATION` | the upload step was skipped — a rule, not a shape error |
| `NOT_IMPLEMENTED` | new error code, `501` | A.4 allows 501 for summarize |
| AI switch | `Features:AiMode` (env alias `AI_MODE`) | single flag that also drives `/config aiSummaryEnabled` |
| Maternal audit action (S3) | every maternal write → `contact_recorded` | the only maternal action in A.2; the app's enum rejects unknown values |
| Contacts after delivery (S3) | never-recorded contacts are soft-deleted | A.2 has no "not applicable" status; a synced phone drops them from the schedule |
| Additive maternal reads (S3) | `GET /patients/:id/pregnancies`, `GET /pregnancies/:id/contacts` | not in A.4; nothing in A.4 lists a patient's past pregnancies |
| Reminder send time (S3) | `03:15:00.000Z` = 09:00 Asia/Kathmandu | A.5 says 09:00 Asia/Kathmandu and the app's `reminders_preview.dart` computes 03:15Z; the A.2 example's `03:00Z` is 08:45 NPT |
| "Patient phone" (S3) | the phone of the account that owns the profile | a Patient has no phone field in A.2 |
| `anc_missed` recipients (S3) | patient phone + `emergencyContactPhone` | A.5 names recipients only for `anc_due`; a missed visit is when the family most needs to know |
| Past send times (S3) | not scheduled | registering at week 28 must not send "contact 1 is due" for a date months ago (the app's preview skips them too) |
| Contact recorded early (S3) | cancels its pending `anc_due` as well as `anc_missed` | A.4 names only `anc_missed`; "due tomorrow" after she already came is wrong |
| Superseded visit (S3) | its pending `follow_up` is cancelled (`VisitSuperseded` event) | the correction schedules its own; two follow-up SMS for one visit otherwise |
| Cancelled reminder (S3) | internal `cancelledAt`, row leaves the lists, `status` unchanged | A.2 `Reminder.status` is pending / sent / failed and the app's enum accepts nothing else |
| Reminder done / cancel (S3) | additive `POST /reminders/:id/done`, `/cancel` | not in A.4; "done" = delivered another way (the FCHV phoned) |
| Late reminder (S3) | found > 24 h after `dueAt` → `failed`, not sent | after downtime, "your check-up is tomorrow" must not arrive days late |
| SMS outside mock mode (S3) | every send fails → reminder `failed` after 3 attempts | there is no gateway yet; reporting `sent` would be false |
| Mock outbox routes (S3) | `/dev/sms` (HTML) + `/dev/sms.json`, and the A.4 names `/demo/sms` (JSON) + `/demo/sms.html`; Development + `SmsMode=mock` only | the session brief asked for `/dev/sms`; the app calls `GET /demo/sms`. A.4 ties the route to `SMS_MODE=mock` only — here it also needs the Development environment (unauthenticated patient names must not be reachable on a deployed server) |

## 2. How to run

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1     # DB → migrate → seed → API on :5000
powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1   # in a second terminal
```

MinIO (optional — without it documents live in `src\MeroSwasthya.Api\.data\documents`):
`docker compose up -d minio` (console http://127.0.0.1:9001, minio/minio123), or the standalone binary
`minio.exe server D:\minio-data --console-address :9001` with `MINIO_ROOT_USER=minio`,
`MINIO_ROOT_PASSWORD=minio123`. The API finds it at `S3:Endpoint` (default `http://127.0.0.1:9000`),
creates bucket `swc-documents`, and re-checks reachability every 30 s. Set `S3:PublicEndpoint` to an
address the phone can reach (LAN IP, or `http://127.0.0.1:9000` with `adb reverse tcp:9000 tcp:9000`).

Demo accounts: `+9779801000001` (patient Sita, PIN 1234, owns Sita / Ram / Aarav),
`+9779801000002` (provider Ramesh Thapa (HA) @ f_0001, PIN 1234). OTP `123456`.

**Seed (idempotent, `updatedAt` = now):** Ram — 4 visits (360 / 240 / 120 / 25 days ago, E11 + I10;
latest = the Part A visit `v_c3c3c3c3-…-0001`, Metformin 500 BD + Amlodipine 5 OD for 30 days,
follow-up +30 days) and 2 uploaded documents (`d_e5e5e5e5-…-0001` lab "Fasting blood sugar",
`…-0002` "Bharatpur Hospital discharge sheet") with a placeholder JPEG in MinIO or on disk; Sita —
fever visit a year ago (R50, paracetamol); Aarav — diarrhoea 6 months ago (A09, ORS + zinc) and cough
2 months ago (J06, paracetamol syrup). **Session 3:** Sita's pregnancy `pg_b2b2b2b2-…-0001` — LMP 210 days
before the seed day (week 30), contacts 1–3 recorded by the seeded provider 18 / 10 / 4 weeks ago (all
green), contact 4 due on the seed day; her reminders — contact 4's two `anc_due` rows (her phone and
`+9779801000009`) already `sent` the day before at 09:00 NPT, 28 more `pending`; Ram's `follow_up` for his
latest visit (`pending`, the day before the follow-up date).

Mock SMS outbox (Development, `SmsMode=mock`): http://127.0.0.1:5000/api/v1/dev/sms — an HTML table that
refreshes every 5 s; JSON at `/api/v1/dev/sms.json` (and `/api/v1/demo/sms`, the path the app calls).

### Environment used in sessions 2 and 3

No Docker on this machine: PostgreSQL = the private cluster in `.localpg\` on :5433; contract tests
use a throwaway database on it and the **local-disk document fallback** (with Docker they would start
Testcontainers `postgres:16` + `minio/minio` and run the same tests against MinIO —
`HealthTests.Reports_which_database_the_contract_tests_use` prints both). No MinIO was running, so the
S3 code path is covered only by offline unit tests (presigned URL host/SigV4/expiry); the smoke run used
the local fallback.

## 3. Tests

```
dotnet test            # 410 tests: 149 unit + 261 contract, all passing
```

New in session 3 (91 contract + 59 unit): Maternal — rules pinned to the `/rules` document, EDD / schedule /
gestational age, the 16 shared triage cases (A.6 #1–#11), pregnancies (Part A shape, idempotency, female
only, one active, validation, PATCH + version conflict), ANC contacts (the A.4 red example, amber by risk level, A.6 #8, owner / FCHV recording, re-recording,
nearestReferral, contactNo 1..8, validation), deliveries (Part A example, idempotency, closed pregnancy,
timeline), access + audit for every endpoint, the seeded pregnancy. Reminders — list shape and access,
scheduling on registration (48 rows for 8 contacts × 3 messages × 2 recipients, exact send times and texts),
past messages skipped, cancel on contact / delivery / ended, follow-up (one row, patient only, idempotent,
self-reported without facility, superseded visit), done / cancel actions, the seeded rows; delivery
(sent once in Nepali, not-yet-due, cancelled, > 24 h late → failed, 3 attempts then failed, the worker
loop); the mock outbox endpoints (shape, order, no auth, seeded messages once after a re-seed, the
`/demo/sms` alias, HTML escaping). Unit: BS calendar against the app's table, message texts (the A.2
example word for word), send-time planning, worker / sender registration per config, the outbox page.

New in session 2 (67 contract + 10 unit): Grants (shape incl. JWT claims and exp−iat = 600, defaults,
owner-only, validation incl. long-lived-must-be-read, 20/h limit, bundle shape + Ram's 4 visits / 2
documents, provider/fchv only, idempotent re-redeem, ALREADY_REDEEMED, A.6 #15 expired, foreign QR +
access token rejected, revoke → access ends, owner-only revoke, provider list/detail include granted
patients, A.6 #16 25 h later → GRANT_EXPIRED on visit + detail, sections filtering, printed card PIN,
read scope cannot append); Visits (Part A example round-trip, idempotency, self-reported, referral +
omitted vitals, 10 code/field validations, FCHV forbidden, no-grant forbidden, supersedes, no edit/delete
routes, newest first, summary contribution, timeline shape + paging, seeded Aarav/Sita); Documents
(presign shape, full round trip with byte-identical download, complete-before-upload 422, idempotent
complete, size/type limits, append access, tampered signature 401, oversize upload, dev multipart,
summarize 501, read access, timeline item, seeded document downloadable); Audit (all six actions newest
first in the A.2 shape, record_viewed throttle, owner-only). Unit: grant token round trip, access-key
forgery and wrong `typ` rejected, SWC prefixes, signed URL purpose/id/expiry, MinIO presign
host + SigV4, object-key layout.

## 4. Smoke run (scripts/smoke.ps1, 2026-09-30, end of session 3)

```
[PASS] GET  /health                       HTTP 200  status=healthy database=up modules=9
[PASS] POST /auth/otp/request             HTTP 200  otpSentTo=+9779801000001 expiresInSec=300 demoOtp=123456
[PASS] POST /auth/otp/verify              HTTP 200  hasPin=True isNewUser=False tempToken=eyJhbGciOiJIUzI1...
[PASS] POST /auth/pin/login               HTTP 200  user=u_11111111-1111-4111-8111-111111111111 role=patient refreshToken=43 chars
[PASS] POST /auth/refresh                 HTTP 200  rotated=True
[PASS] GET  /me                           HTTP 200  name=Sita Chaudhary phone=+9779801000001 role=patient
[PASS] GET  /patients                     HTTP 200  items=3: Sita Chaudhary v1, Ram Bahadur Chaudhary v1, Aarav Chaudhary v1
[PASS] GET  /patients/:id (Ram)           HTTP 200  allergies=penicillin problems=E11,I10 visitCount=6
[PASS] GET  /patients/:id/timeline        HTTP 200  items=10 nextBefore=
[PASS] GET  /codelists                    HTTP 200  version=2026-09-18.1 complaint=32 diagnosis=42 drug=34 dangerSign=10 riskFactor=8
[PASS] GET  /rules                        HTTP 200  version=2026-09-18.1 ancWeeks=12,20,26,30,34,36,38,40 dangerSigns=10
[PASS] GET  /config                       HTTP 200  smsMode=mock otpDemo=True aiSummaryEnabled=False rulesVersion=2026-09-18.1
[PASS] GET  /facilities/nearby            HTTP 200  f_0001 0.5km, f_0002 0.6km, f_0004 18.6km
[PASS] POST /grants (patient shares Ram)  HTTP 200  grant=g_2e7789c3-e4dd-406d-a899-f450829a7dd9 scope=append qr=SWC1:eyJhbGc...
[PASS] POST /auth/pin/login (provider)    HTTP 200  user=Ramesh Thapa (HA) role=provider facility=Ghorahi Health Post
[PASS] POST /grants/redeem                HTTP 200  patient=Ram Bahadur Chaudhary accessUntil=2026-10-01T16:03:27.676Z timeline=10 visits=6
[PASS] POST /patients/:id/visits          HTTP 200  visit=v_b73dd5a4-e46a-4a22-bab6-dc476ac9cac1 by=Ramesh Thapa (HA) drug=Metformin 500 mg
[PASS] POST /documents/presign            HTTP 200  status=pending_upload uploadUrl=http://127.0.0.1:5000/api/v1/documents/d_71b4cd32-e912-4674-9786-553fa28cdd42/upload
[PASS] PUT  uploadUrl                     HTTP 200  2006 bytes
[PASS] POST /documents/:id/complete       HTTP 200  status=uploaded version=2 downloadUrl=True
[PASS] GET  /patients/:id/timeline        HTTP 200  visit: Visit — Ghorahi Health Post — Type 2 diabetes | visit: Visit — Ghorahi Health Post — Type 2 diabetes | document: Smoke test lab report | document: Smoke test lab report | visit: Visit — Ghorahi Health Post — Type 2 diabetes
[PASS] GET  /patients/:id (summary)       HTTP 200  visits=7 meds=METFORMIN_500,AMLODIPINE_5 lastBP=134/84
[PASS] GET  /patients/:id/audit (owner)   HTTP 200  document_added, visit_added, grant_redeemed, grant_created, record_viewed, document_added
[PASS] smoke account (otp + pin)          HTTP 200  user=u_f0c0c97e-d0bc-4ff5-aa8b-f1d5f5b5b7d3 phone=+9779801999001 role=patient
[PASS] POST /patients (smoke mother)      HTTP 200  patient=p_dcc21a81-dded-40ba-bf81-e45225c11fd2 emergencyContactPhone=+9779801999002
[PASS] POST /grants + /grants/redeem      HTTP 200  patient=Smoke Mother scope=append pregnancy=False
[PASS] POST /patients/:id/pregnancies     HTTP 200  pregnancy=pg_3de9e7f6-dd84-4893-95d5-ddd1f33cadb0 edd=2026-12-19 week=28 risk=normal contacts=8 next=#1 due 2026-06-06
[PASS] GET  /patients/:id/reminders       HTTP 200  items=30 (anc_due=10 anc_missed=20) first: 2026-10-09T03:15:00.000Z -> patient | Smoke Mother's ANC contact 4 is due on 2026-10-10 at Ghorahi Health Post.
[PASS] PUT  /pregnancies/:id/contacts/4   HTTP 200  triage=red reasons=[Severe headache with blurred vision; BP ≥ 140/90 with proteinuria or severe headache — possible pre-eclampsia] nearestReferral=Rapti Provincial Hospital (0.1 km)
[PASS] GET  /patients/:id/reminders       HTTP 200  items=24 (was 30; contact 4's pending reminders cancelled)
[PASS] POST /pregnancies/:id/delivery     HTTP 200  delivery=dl_875c3119-f293-4938-ac9d-a36a9d0eddd3 place=hospital outcome=live_birth pregnancy.status=delivered
[PASS] GET  /pregnancies/:id (owner)      HTTP 200  status=delivered contacts=1 (recorded only) delivery=True reminders=0
[PASS] GET  /patients/:id/timeline        HTTP 200  delivery: Delivery — live birth | anc_contact[red]: ANC contact 4 (week 30) | pregnancy_registered: Pregnancy registered
[PASS] GET  /patients/Sita/reminders      HTTP 200  items=30 (sent=2 pending=28) sent: 2026-09-29T03:15:05.000Z -> +9779801000001 | Sita Chaudhary को ४ औं गर्भ जाँच २०८३-०६-१४ मा Ghorahi Health Post मा छ।
[PASS] GET  /patients/Ram/reminders       HTTP 200  items=3 latest: follow_up 2026-10-29T03:15:00.000Z pending | Ram Bahadur Chaudhary's follow-up visit is due on 2026-10-30 at Ghorahi Health Post.
[PASS] GET  /dev/sms.json                 HTTP 200  items=2 newest: 2026-09-29T03:15:05.000Z -> +9779801000009 | Sita Chaudhary को ४ औं गर्भ जाँच २०८३-०६-१४ मा Ghorahi Health Post मा छ।
SMOKE PASSED
```

Each smoke run adds to the dev database: one visit, one document and one pending `follow_up` reminder on
Ram, and — under the separate smoke account `+9779801999001` — one "Smoke Mother" profile with a delivered
pregnancy. Sita's seeded pregnancy is not touched. Re-seeding does not remove any of it.

## 5. What is next — Session 4: Sync push/pull and app integration by URL

### 5.1 Sync (schema `sync`)
* **A.4** `POST /sync/push` (per-change applied / duplicate / conflict + current / rejected + error;
  never fails the batch; `serverTime`), `GET /sync/pull?since=&deviceId=` (page 200, `hasMore`, `cursor`,
  deleted rows). **A.8**, addendum §3 whitelist. **A.6 #13–#14.**
* Visibility = `IPatientAccess.VisiblePatientIdsAsync` (owned + active grants — already grant-aware).
* Each owning module (Patients, Clinical, Maternal) exposes an apply-change / changed-since contract.
  A pushed change must go through the same service as the HTTP endpoint — `VisitService`,
  `PregnancyService`, `AncContactService`, `DeliveryService` — so validation, triage, audit **and the
  domain events** still happen: a pregnancy or contact that arrives by sync must schedule / cancel its
  reminders exactly as one that arrives by `POST` / `PUT`.
* Keyset `(updatedAt, table, id)` so rows sharing a millisecond are never skipped. Maternal rows already
  carry `version`, `updatedAt`, `deleted` (contacts dropped at delivery are `deleted: true`).
* Reminders are server-owned: pull-only for the app (A.2), never accepted in a push.

### 5.2 App integration by URL
Point the Flutter app at this API (Settings → Server address, `docs/APP_INTEGRATION.md`) with Demo data
mode off and walk the demo script on a phone: sign-in, QR share / redeem, visit, document, pregnancy
registration, ANC contact with triage, delivery, reminders list (S15), the SMS panel (`/demo/sms`), then
offline → online with the new sync endpoints. Record every mismatch between what the app sends / expects
and what the API does in `docs/APP_INTEGRATION.md`.

### 5.3 Still open from earlier sessions
* Immunisations and growth measurements (addendum §1–2): entities, endpoints, timeline kinds
  `immunisation` / `growth`, Aarav's seed (MR-2, TCV overdue, three weights); regenerate immunisation
  `dueAt` on a dob PATCH (TODO in `PatientService`).
* Seed: the three dashboard women with grants already redeemed by the seeded provider (TODO in Grants).
* OTP by SMS when `Features:SmsMode` is not `mock` (TODO in `AuthService`): `ISmsSender` now exists in
  Shared, but there is no real gateway behind it — outside mock mode every send fails.
* Reminders do not follow a phone number / emergency contact changed after they were scheduled.
* `summarize` stays `501` until an AI mode exists.

## 6. Session 3 feature log (done 2026-09-30)

Session 3 (Maternal + Reminders) was paused after feature 9 on 2026-09-29 and finished on 2026-09-30.
Each feature is one commit on `main`.

**Features 1–13**
1. Maternal module skeleton (schema `maternal`, DbContext, initializer order 50).
2. Versioned ANC rules service — the A.5 table as in-code data (`AncRules`, version `2026-09-18.1`),
   `IRulesService` (schedule, EDD/LMP, gestational age, risk level); a unit test pins it to the
   document Catalog serves at `GET /rules`.
3. Triage engine mirroring the app's `lib/domain/rules/triage.dart` (same rule order, reason codes,
   en/np texts); A.6 #1–#11 as unit tests.
4. Pregnancy: `POST /patients/:id/pregnancies` (idempotent; female only → 422; one active per patient →
   422; 8 contacts with `Ids.AncContactId`), `GET /pregnancies/:id`, `PATCH /pregnancies/:id`
   (`version` → 409 `VERSION_CONFLICT` + `details.current`; only `status: "ended"` settable),
   additive `GET /patients/:id/pregnancies`.
5. ANC contacts: `PUT /pregnancies/:id/contacts/:contactNo` (server triage stored as level + English
   reasons; `nearestReferral` = nearest other birthing facility from the recorder's facility, else the
   birth-plan facility, else null; contactNo outside 1..8 → 422), additive `GET /pregnancies/:id/contacts`.
   Catalog's `FacilityDto` moved to `Catalog.Contracts` (public) for the referral shape.
6. Delivery: `POST /pregnancies/:id/delivery` closes the pregnancy (`delivered`); contacts never
   recorded are soft-deleted (A.2 has no "not applicable" status). Patients plug-ins: summary
   `activePregnancy`, redeem-bundle `pregnancy` / `ancContacts`, timeline kinds
   `pregnancy_registered`, `anc_contact` (recorded only, badge = triage), `delivery`.
7. Access + audit: every Maternal endpoint goes through `IPatientAccess`; non-owner reads → throttled
   `record_viewed`; every Maternal write (register, patch, contact, delivery) is audited as
   `contact_recorded` — the only maternal action in A.2, and the app's enum rejects unknown values.
8. Contract tests (`PregnanciesTests`, `AncContactsTests`, `DeliveriesTests`, `MaternalAccessTests`,
   `MaternalSeedTests`) and the seed: Sita's `pg_b2b2b2b2-…-0001`, LMP 210 days before today (week 30),
   contacts 1–3 recorded by the seeded provider 18 / 10 / 4 weeks ago, all green (mirrors mock_api.dart).
9. Reminders module skeleton: schema `reminders`, `Reminder` entity (+ internal `sourceKey`,
   `cancelledAt`, `attempts`, `lastError`), `GET /patients/:id/reminders`, additive
   `POST /reminders/:id/done` and `POST /reminders/:id/cancel`.
10. Scheduling (A.5 `reminders`). Reminders subscribes to domain events: Maternal's `PregnancyRegistered`,
    `AncContactRecorded`, `PregnancyClosed` (new, ids only — the schedule is read through
    `Maternal.Contracts.IPregnancyDirectory`) and Clinical's `FollowUpScheduled` + new `VisitSuperseded`.
    * `anc_due` the day before the contact, `anc_missed` 3 and 7 days after it, `follow_up` the day before
      `followUpAt` — all at 09:00 Asia/Kathmandu = `03:15:00.000Z` (as the app's `reminders_preview.dart`;
      the A.2 example's `03:00Z` is not 09:00 NPT).
    * Recipients: "patient phone" = the phone of the account that owns the profile (`recipientRole: patient`);
      ANC reminders also go to `emergencyContactPhone` (`family`) unless it is the same number. `follow_up`
      goes to the patient phone only.
    * Text: `anc_due` is the A.2 example word for word — Nepali with the BS date and Nepali digits
      ("… ४ औं गर्भ जाँच २०८३-०६-०२ मा … छ।"), English with the AD date. BS conversion uses the month table of
      the app's `nepali_utils` (BS 2080–2100). The facility is the one of the health worker who registered the
      pregnancy / recorded the visit; a record the owner entered herself has no facility clause. Names stored
      in Latin letters are set off from the postposition ("Sita Chaudhary को").
    * Only messages whose send time is still ahead are created (no late SMS for a contact already past).
      Idempotent on (`sourceKey`, recipient): a repeated event or a re-run seed adds nothing.
    * Recording a contact cancels its pending `anc_missed` **and** `anc_due`; delivery or `status: ended`
      cancels everything pending for the pregnancy; a visit with `supersedesId` cancels the replaced visit's
      follow-up. Cancelled rows leave the lists (feature 9).
    * Seed: Sita's contact 4 (due on the seed day) has its two `anc_due` rows — her phone and
      `+9779801000009` — stored as `sent` yesterday 09:00 NPT (the A.2 example); contact 4's `anc_missed` and
      everything for contacts 5–8 are `pending`. Ram's `follow_up` comes from the Clinical seed publishing
      `FollowUpScheduled` for his latest visit.
    * Not handled: a phone number or emergency contact changed after scheduling does not move reminders
      that already exist.
11. Delivery. `ReminderDeliveryWorker` (`BackgroundService`) polls every 60 s, first poll right after
    startup, and hands each due reminder (pending, not cancelled, `dueAt` ≤ now) to `ISmsSender`
    (`MeroSwasthya.Shared.Sms`, so Auth can use it for the OTP later). The Nepali text is what goes out.
    * `Features:SmsMode = mock` → `MockSmsSender`: logs every message and keeps the newest 200 in memory
      (lost on restart). Any other mode → `UnconfiguredSmsSender`, which fails every send: there is no
      gateway yet, and a reminder must not be reported `sent` when nothing left the machine.
    * Sent → `status: sent`, `sentAt`. A send that throws is retried on the next polls, 3 attempts in all,
      then `status: failed` (`attempts`, `lastError` kept internally). A reminder found more than 24 h after
      its `dueAt` (server was down) is `failed` unsent — no "check-up tomorrow" arriving days late.
    * Config: `Reminders:WorkerEnabled` (default `true`), `Reminders:PollInterval` (default `00:01:00`).
      The contract tests switch the worker off and run the dispatcher (or their own 50 ms worker) themselves.
      `--migrate` / `--seed` exit before the worker starts.
12. Mock SMS outbox page: `GET /api/v1/dev/sms` (HTML table — sent at in Nepal time, to, text; refreshes
    every 5 s; all text HTML-escaped) and `GET /api/v1/dev/sms.json` (`{ items: [{ to, text, sentAt }] }`,
    newest first). The same two handlers also answer under the A.4 names, because the app calls
    `GET /demo/sms`: `GET /api/v1/demo/sms` (JSON) and `GET /api/v1/demo/sms.html`. No auth. Mapped only in
    Development (and the tests' `Testing` environment) **and** only while `Features:SmsMode = mock`;
    anywhere else all four are `404`.
    * The page shows `MockSmsSender`'s memory. The seed puts Sita's two already-sent `anc_due` messages into
      it on every start (idempotent), so the panel is never empty — as `mock_api.dart`'s `/demo/sms`.
    * Contract tests: outbox shape / order / no auth, seeded messages once even after a re-seed, the A.4
      alias, the HTML page (escaping, refresh), and `POST /reminders/:id/done` / `cancel` (happy path,
      idempotency, cancel-after-sent → 422, append access).
    * `GrantAndStorageTests.MinIO_presigned_urls…` asserted `X-Amz-Expires=900` exactly and failed on a cold
      first run (899: the SDK subtracts the time the call itself took). It now accepts 890–900 / 3590–3600.
13. `scripts/smoke.ps1` walks pregnancy → reminders → red contact → delivery → timeline → seeded reminders →
    mock outbox on a throwaway profile of a separate smoke account (§4); this document;
    `docs/APP_INTEGRATION.md` §5 (what works now, and the maternal / reminder shapes worth knowing on the
    Flutter side); `docs/ARCHITECTURE.md` (module map, dependencies, cross-module contracts).

**Shape decisions made in feature 9**
* *Cancel = internal `cancelledAt`.* A.2 fixes `Reminder.status` to `pending | sent | failed` and the
  app's `ReminderStatus` enum accepts nothing else, so a cancelled reminder keeps its status, gets
  `cancelledAt`, and leaves the patient's list (and `/pregnancies/:id.reminders`). *Done = sent now:*
  `POST /reminders/:id/done` sets `status = sent`, `sentAt = now` (a message delivered another way,
  e.g. the FCHV phoned). Both need append access; cancelling a sent reminder → 422.
* *Reminders → Maternal via `IPregnancyReminderSource`.* Maternal owns the contract
  (`Maternal.Contracts.IPregnancyReminderSource`, returns the A.2 Reminder DTOs for one pregnancy);
  Reminders implements it. Maternal never references Reminders, so the `reminders` list in
  `GET /pregnancies/:id` is `[]` until the Reminders module registers — the same pattern as
  `IActivePregnancySource`.

Test count at the end of session 3: `dotnet test` → 410 tests (149 unit + 261 contract), all passing
against the local PostgreSQL cluster on :5433 (no Docker on this machine). After feature 9: 362; 10: 385;
11: 396; 12: 410.
