# Mero Swasthya API — architecture

ASP.NET Core 8 **modular monolith** serving the Mero Swasthya Flutter app. One process, one
PostgreSQL database, one schema per module. The HTTP contract is `FRONTEND_SPEC.md` **Part A**
(plus `CONTRACT_ADDENDUM.md`) in the app repository, and it is law: field names, enum strings,
paths, status codes and envelopes are copied from it, never renamed.

## 1. Module map

```
MeroSwasthya.sln
├─ src/MeroSwasthya.Api                     host: Program.cs, ModuleCatalog, Serilog, Swagger,
│                                           /api/v1/health, DB bootstrap (--migrate / --seed)
├─ src/BuildingBlocks/MeroSwasthya.Shared   envelope, AppException + ErrorCode (A.3), JSON conventions,
│                                           IClock, uuid v5 ids, cursors, IModule / IModuleInitializer,
│                                           ICurrentUser, Authz, JwtSettings + SigningKeys, FeatureFlags,
│                                           FluentValidation filter, ModuleDb (DbContext-per-schema),
│                                           JsonColumn (jsonb value objects), in-process domain events
├─ src/Modules/Catalog    schema catalog    facilities (+ nearby), codelists + version, /rules, /config
├─ src/Modules/Auth       schema auth       OTP, PIN (Argon2id), JWT access + temp, refresh rotation,
│                                           invite activation, /me, ICurrentUser implementation
├─ src/Modules/Patients   schema patients   patients CRUD, access policy, summary + timeline builders
├─ src/Modules/Audit      schema audit      AuditEntry, IAuditWriter, record_viewed observer, GET …/audit
├─ src/Modules/Grants     schema grants     QR share codes (grant-key JWT), redeem bundle, revoke,
│                                           IPatientGrantSource + IGrantAuthorization
├─ src/Modules/Clinical   schema clinical   visits (+ prescriptions), documents (MinIO / local disk),
│                                           summary + timeline contributions
├─ src/Modules/Maternal   (placeholder)     pregnancies, ANC contacts, triage, deliveries,
│                                           immunisations, growth
├─ src/Modules/Reminders  (placeholder)     reminders, mock SMS outbox
├─ src/Modules/Sync       (placeholder)     /sync/push, /sync/pull
├─ tests/MeroSwasthya.ContractTests         real HTTP (WebApplicationFactory) + real PostgreSQL
└─ tests/MeroSwasthya.UnitTests             helpers, JSON conventions, validators, architecture rules
```

Dependency direction (project references) — arrows point at what a module may *use*:

```
            Shared  ◄──────────── every project
Catalog ◄── Patients ◄── Audit ◄── Grants, Clinical      (Maternal will use Audit too)
   ▲            ▲  ◄──────────── Maternal, Reminders, Sync
   └── Clinical, Maternal (labels, facilities, rules)
Auth    (depends on Shared only; reached through Shared.ICurrentUser, Auth.Contracts.IUserDirectory,
         Auth.Contracts.IPinVerifier — Grants uses the last one for the printed card)
```

`Patients` never references the modules that extend it (enforced by `ArchitectureTests`). They plug
in through interfaces that Patients owns.

## 2. Inside a module

```
MeroSwasthya.Modules.<Name>/
  <Name>Module.cs      public, implements IModule — the only public entry point
  Contracts/           public interfaces + DTOs other modules may use
  Domain/              entities (internal) and the Part A enums (public, [WireName("…")])
  Application/         services, request DTOs, FluentValidation validators (internal)
  Infrastructure/      <Name>DbContext (internal), Migrations/, <Name>ModuleInitializer (migrate + seed)
  Endpoints/           minimal-API route groups under /api/v1
```

Everything except `<Name>Module`, `Contracts/*` and the public enums is `internal`.
`ArchitectureTests.Public_module_types_live_in_Contracts_Domain_enums_or_the_module_root` fails the
build's tests if an implementation type leaks.

### Cross-module contracts in use today

| Interface | Owner | Implemented by | Used for |
|---|---|---|---|
| `ICurrentUser` | Shared | Auth | caller id from the JWT, role/facility from `auth.users` (per request) |
| `IUserDirectory` | Auth | Auth | actor names for audit, owner phones for reminders (later) |
| `ICodeListLookup` | Catalog | Catalog | activeProblems labels, drug names |
| `IFacilityDirectory` | Catalog | Catalog | nearestReferral, visit facility names |
| `IRulesProvider` | Catalog | Catalog | the A.5 document (Maternal parses it) |
| `IPatientAccess` | Patients | Patients | owner / read / append checks → 404, 403 FORBIDDEN, 403 GRANT_EXPIRED |
| `IPatientDirectory` | Patients | Patients | server-side jobs that bypass access checks |
| `IPatientGrantSource` | Patients | Grants | "does this health worker hold an active grant?" (level + expired flag) |
| `IPatientSummaryContributor` | Patients | Clinical (Maternal next) | currentMedicines, lastVitals, activeProblems.since, … |
| `ITimelineContributor` | Patients | Clinical (Maternal next) | timeline items of their own kinds |
| `IPatientReadObserver` | Patients | Audit | `record_viewed` when a non-owner reads a patient |
| `IActivePregnancySource` | Patients | **Maternal (next)** | `pregnancy` + `ancContacts` in the redeem bundle |
| `IAuditWriter` | Audit | Audit | grant_created / redeemed / revoked, visit_added, document_added (contact_recorded next) |
| `IGrantAuthorization` | Grants | Grants | `CanReadPatientAsync` / `CanAppendPatientAsync` booleans |
| `IPinVerifier` | Auth | Auth | patient PIN check for the A.7 printed card (shares the lockout) |
| `IDomainEventPublisher` | Shared | Shared | in-process events, e.g. Clinical's `FollowUpScheduled` → Reminders (next) |
| `IPatientSummaryService`, `IPatientTimelineService` | Patients | Patients | redeem bundle (Grants) |

Multiple implementations of a contribution interface are all resolved (`IEnumerable<T>`); with none
registered the feature degrades to the empty shape (e.g. an empty timeline page), never to an error.

## 3. How to add a module

1. `dotnet new classlib -o src/Modules/Foo/MeroSwasthya.Modules.Foo -n MeroSwasthya.Modules.Foo`,
   copy a placeholder `.csproj` (FrameworkReference `Microsoft.AspNetCore.App`, reference Shared and
   only the modules whose **Contracts** you need, `InternalsVisibleTo` the test projects).
2. `FooModule : IModule` with a lower-case `Name`.
3. `FooDbContext` (internal) with `public const string Schema = "foo"` and `b.HasDefaultSchema(Schema)`.
   Register with `services.AddModuleDbContext<FooDbContext>(config, FooDbContext.Schema)`.
4. `FooModuleInitializer : IModuleInitializer` (`Order` after the modules it seeds on top of;
   Catalog 10, Auth 20, Patients 30) — `MigrateAsync` = `db.Database.MigrateAsync`, `SeedAsync`
   insert-if-missing.
5. Validators: `services.AddValidatorsFromAssemblyContaining<FooModule>(includeInternalTypes: true)`,
   and `.Validate<TRequest>()` on every endpoint that has a body.
6. Add one line to `src/MeroSwasthya.Api/Hosting/ModuleCatalog.cs`, and a project reference in the Api.
7. First migration:
   `dotnet ef migrations add InitialFoo -p src/Modules/Foo/MeroSwasthya.Modules.Foo -s src/MeroSwasthya.Api -c FooDbContext -o Infrastructure/Migrations`
8. Contract tests in `tests/MeroSwasthya.ContractTests` (collection `api`), asserting the Part A JSON.

## 4. The DbContext-per-schema rule

* Each module owns exactly one `DbContext`, mapped to exactly one PostgreSQL schema
  (`auth`, `catalog`, `patients`, later `grants`, `clinical`, `maternal`, `reminders`, `sync`).
* The migrations history lives in that schema too (`<schema>.__ef_migrations_history`), so modules
  migrate independently.
* **No module opens another module's DbContext, joins across schemas, or holds a foreign key into
  another schema.** Cross-module data goes through a `Contracts` interface. Ids referring to another
  module (e.g. `patients.owner_user_id`) are plain columns.
* DbContexts are `internal`; `ArchitectureTests.No_DbContext_is_visible_outside_its_module` enforces it.
* Columns are `snake_case` (EFCore.NamingConventions), JSON is `camelCase`. Enums are stored as their
  Part A strings (`'health_post'`, `'provider'`), lists as `text[]`, free-form objects as `jsonb`.
* Syncable tables carry `version` (EF concurrency token), `updated_at` (indexed — the pull cursor)
  and `deleted` (soft delete; a deleted row is `404 NOT_FOUND` on read, `deleted: true` on pull).

## 5. Envelope and error rules

* Success: `{ "ok": true, "data": { … } }` — always through `ApiResults.Ok(dto)`. `data` is always an
  object; lists are `{ "items": [...] }` (`ItemsResponse<T>`).
* Error: `{ "ok": false, "error": { "code", "message", "details": { … } } }` — `details` is always an
  object (`{}` when empty). Produced only by `GlobalExceptionHandler` (exceptions), the JWT challenge
  handler (401/403) and `UseEnvelopeStatusCodes` (routing 404/405/415). Nothing else writes a body.
* Throw `AppException` with an `ErrorCode`; status and wire string come from the A.3 table
  (`ErrorCodes.HttpStatus()/Wire()`). Unknown exceptions become `500 INTERNAL` without leaking the
  message.
* Validation: FluentValidation → `400 VALIDATION_ERROR`, `details = { "<jsonField>": "<message>" }`
  (first failure per field). Malformed JSON / wrong JSON types become the same error keyed by the JSON
  path (`"ward": "Invalid value"`); query parameters are parsed by hand for the same reason.
* `409 VERSION_CONFLICT` always carries `details.current` = the full current entity DTO.
* Never return EF entities: every entity has one DTO (`PatientDto`, `UserDto`, `FacilityDto`, …).
* Nullable fields are always present (`JsonIgnoreCondition.Never`). The only omitted key in the whole
  contract is `demoOtp`, which A.4 says is present *only* when `SMS_MODE=mock`.

## 6. Cross-cutting conventions

* **Time**: `IClock.UtcNow` is UTC truncated to milliseconds, so the stored value equals the wire
  value and a cursor echoed back compares exactly. Timestamps serialise as
  `2026-09-18T04:05:00.000Z`; incoming timestamps must carry `Z` or an offset. Calendar dates are
  `DateOnly` ↔ `YYYY-MM-DD`.
* **Ids**: client-generated ids (A.1) are accepted if they match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`
  (UUID v4 and the Part A prefixed examples). Server ids are `prefix_uuid` (`u_…`). Deterministic ids
  use `Ids.AncContactId` / `Ids.ImmunisationId` = uuid v5 in namespace
  `6ba7b810-9dad-11d1-80b4-00c04fd430c8` (A.8.15, addendum §1), unit-tested against Python's `uuid5`.
* **PATCH** bodies use `Optional<T>` so omitted ≠ explicit `null` (A.1).
* **Auth**: two HS256 keys. Access key signs access tokens (aud `mero-swasthya-api`, `typ=access`,
  12 h) and OTP temp tokens (aud `mero-swasthya-otp`, `typ=temp`, 10 min, only accepted by
  `/auth/pin/set`). The grant key (`Jwt:GrantSigningKey`, A.7 GRANT_SECRET) is registered in
  `SigningKeys.Grant` for the Grants module and is never a valid bearer key. Authorisation reads the
  role from the database on every request, because `/auth/provider/activate` changes it mid-session.
* **Config**: `appsettings.json` → `appsettings.{Environment}.json` → environment (`Section__Key`,
  plus the spec aliases `JWT_SECRET`, `GRANT_SECRET`, `SMS_MODE`, `AI_MODE`, `OTP_DEMO`,
  `DATABASE_URL`) → command line. See `.env.example`.

## 7. Patient access, grants and audit

**One decision point.** Every endpoint that touches a patient's data asks
`Patients.Contracts.IPatientAccess` (`RequireReadAsync` / `RequireAppendAsync` / `RequireOwnerAsync`).
It loads the patient (404 when missing or soft-deleted), grants `Owner` to the owner, and for a
provider/FCHV asks every registered `IPatientGrantSource`. It maps the result to the A.3 errors:
none → `403 FORBIDDEN`; a redeemed grant whose 24 h window ran out → `403 GRANT_EXPIRED`.

**Grants plug in, Patients does not reach out.** The Grants module registers `PatientGrantSource`
(`IPatientGrantSource`). An *active* grant is redeemed by the caller, not revoked, and has
`accessUntil > now`; scope `append` → `Append`, `read` → `Read`. That single registration is what makes
granted patients appear in a provider's `GET /patients`, opens `GET /patients/:id` and the timeline,
and lets Clinical's visit/document endpoints work — without Patients or Clinical referencing Grants.

**`Grants.Contracts.IGrantAuthorization`** exposes the same rule as booleans for modules that only
need yes/no (e.g. a background job):

```csharp
Task<bool> CanReadPatientAsync(string userId, string patientId);   // owner OR active grant (any scope)
Task<bool> CanAppendPatientAsync(string userId, string patientId); // owner OR active grant with scope append
```

Endpoints should keep using `IPatientAccess`, because only it can distinguish FORBIDDEN from
GRANT_EXPIRED. Role rules on top of access stay in the owning module: for example Clinical forbids an
FCHV (not the owner) from recording a visit even with an append grant.

**Grant tokens** are signed with `SigningKeys.Grant`, a different key from access tokens. They carry
`typ:"grant"` and audience `mero-swasthya-grant`, and the row's `jti` must match. The stored row decides
expiry, so a re-scan by the same provider is idempotent.

**Audit writer.** `Audit.Contracts.IAuditWriter.WriteAsync(patientId, action)` appends an A.2 AuditEntry
for the *current user*, with actor name and facility denormalised at write time. Callers write after
their own `SaveChanges` succeeded, so a failed action never leaves an audit line behind. (The two
writes are separate transactions: a crash between them can lose an audit line, never invent one.)
`record_viewed` is driven by Patients' `IPatientReadObserver` hook, throttled to once per 10 minutes per
(actor, patient). Entries are ordered by `at` desc, then an identity `seq` for same-millisecond ties.

## 8. The summary / timeline contribution pattern

Patients owns the *shapes* (`PatientSummaryDto`, `TimelineItemDto`, `TimelinePageDto`) and the
*builders* (`IPatientSummaryService`, `IPatientTimelineService`). Modules that own clinical data
contribute:

```csharp
// Clinical registers:
services.AddScoped<IPatientSummaryContributor, ClinicalSummaryContributor>(); // Order = 10
services.AddScoped<ITimelineContributor, ClinicalTimelineContributor>();
```

* **Summary**: the Patients base fills `allergies` and `activeProblems` from `chronicConditions`
  (labelled via Catalog). Contributors then run in `Order` against a mutable `PatientSummaryBuilder`.
  Clinical dates the problems (`since`), adds visit diagnoses, and sets `currentMedicines`, `lastVitals`,
  `lastVisitAt` and `visitCount`. Maternal (Order 20) will set `activePregnancy`.
* **Timeline**: each contributor returns its newest items strictly before the `before` cursor, at most
  `limit` of them. The service asks each for `limit + 1`, merges newest-first, cuts the page and
  returns `nextBefore` (null on the last page). Payload = the entity's own DTO, so no second call is
  needed.
* The same builders feed `GET /patients/:id`, `GET /patients/:id/timeline` and the grant redeem bundle.
  Grants then filters the bundle by `sections` (kinds → sections map in `GrantService.BundleAsync`).

A new kind is one contributor class plus one enum value in `TimelineKind`; the Patients module does
not change.

## 9. Documents storage

`Clinical.Application.DocumentStorage` decides where bytes go:

* `Storage:Mode=auto` uses MinIO (AWSSDK.S3, path-style, SigV4) when `S3:Endpoint` answers. Reachability
  is probed and cached for 30 s, and the bucket is created if missing. Otherwise bytes go to local disk
  under `Storage:LocalPath`. The row remembers which (`storage` = `s3` | `local`).
* `Storage:UploadMode=proxy` (default) hands the phone a signed `PUT /api/v1/documents/:id/upload` URL,
  and the API writes the bytes to MinIO or disk. `presigned` hands out a MinIO presigned PUT signed for
  `S3:PublicEndpoint` instead.
* Download URLs are MinIO presigned GETs for the public endpoint, or signed `GET …/documents/:id/file`
  URLs for local bytes (1 h). Both work without a bearer token. Signed API URLs are
  HMAC-SHA256(`purpose:id:exp`) with a key derived from the access signing key.
