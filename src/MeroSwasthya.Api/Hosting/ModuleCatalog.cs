using MeroSwasthya.Modules.Audit;
using MeroSwasthya.Modules.Auth;
using MeroSwasthya.Modules.Catalog;
using MeroSwasthya.Modules.Clinical;
using MeroSwasthya.Modules.Grants;
using MeroSwasthya.Modules.Maternal;
using MeroSwasthya.Modules.Patients;
using MeroSwasthya.Modules.Reminders;
using MeroSwasthya.Modules.Sync;
using MeroSwasthya.Shared.Modules;

namespace MeroSwasthya.Api.Hosting;

/// <summary>The one place a module is plugged into the host. Adding a module = one line here.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new CatalogModule(),
        new AuthModule(),
        new PatientsModule(),
        new AuditModule(),
        new GrantsModule(),
        new ClinicalModule(),
        new MaternalModule(),
        new RemindersModule(),
        new SyncModule(),
    ];
}
