# Mero Swasthya API

ASP.NET Core 8 modular-monolith backend for the Mero Swasthya Flutter app. The HTTP contract is
Part A of `FRONTEND_SPEC.md` in the app repository.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1     # DB → migrate → seed → http://0.0.0.0:5000
powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1   # curl walk over the running API
dotnet test                                                  # unit + contract tests (real PostgreSQL)
```

* [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — module map, adding a module, DbContext-per-schema, envelope rules
* [docs/BACKEND_PROGRESS.md](docs/BACKEND_PROGRESS.md) — what exists, how to run, tests, what is next
* [docs/APP_INTEGRATION.md](docs/APP_INTEGRATION.md) — pointing the phone at this API
