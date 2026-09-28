using System.Reflection;
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
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.UnitTests;

/// <summary>Guards the modular-monolith rules in docs/ARCHITECTURE.md.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(AuthModule).Assembly, typeof(CatalogModule).Assembly, typeof(PatientsModule).Assembly,
        typeof(GrantsModule).Assembly, typeof(ClinicalModule).Assembly, typeof(MaternalModule).Assembly,
        typeof(RemindersModule).Assembly, typeof(SyncModule).Assembly, typeof(AuditModule).Assembly,
    ];

    [Fact]
    public void Every_module_assembly_exposes_exactly_one_IModule()
    {
        foreach (var assembly in Modules)
            assembly.GetExportedTypes().Count(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract)
                .Should().Be(1, assembly.GetName().Name);
    }

    [Fact]
    public void No_DbContext_is_visible_outside_its_module()
    {
        foreach (var assembly in Modules)
            assembly.GetExportedTypes().Where(t => typeof(DbContext).IsAssignableFrom(t))
                .Should().BeEmpty($"{assembly.GetName().Name} must keep its DbContext internal");
    }

    [Fact]
    public void Public_module_types_live_in_Contracts_Domain_enums_or_the_module_root()
    {
        foreach (var assembly in Modules)
        {
            var root = assembly.GetName().Name!;
            var leaks = assembly.GetExportedTypes()
                .Where(t => t.Namespace != root
                            && !t.Namespace!.EndsWith(".Contracts", StringComparison.Ordinal)
                            && !(t.Namespace.EndsWith(".Domain", StringComparison.Ordinal) && t.IsEnum)
                            && !IsAllowedPublicHelper(t))
                .Select(t => t.FullName);
            leaks.Should().BeEmpty($"{root}: implementation types must be internal");
        }
    }

    // Response records that are also contract shapes, and EF migrations (generated public).
    private static bool IsAllowedPublicHelper(Type t) =>
        t.Namespace!.EndsWith(".Migrations", StringComparison.Ordinal)
        || t.FullName == "MeroSwasthya.Modules.Auth.Application.UserDto"
        || t.FullName == "MeroSwasthya.Modules.Catalog.Application.Geo";

    [Fact]
    public void Patients_never_depends_on_the_modules_that_extend_it()
    {
        var references = typeof(PatientsModule).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        references.Should().NotContain(["MeroSwasthya.Modules.Grants", "MeroSwasthya.Modules.Clinical",
            "MeroSwasthya.Modules.Maternal", "MeroSwasthya.Modules.Reminders", "MeroSwasthya.Modules.Sync",
            "MeroSwasthya.Modules.Auth", "MeroSwasthya.Modules.Audit"]);
    }

    [Fact]
    public void Module_names_are_unique_and_lower_case()
    {
        var names = Modules.Select(a => ((IModule)Activator.CreateInstance(
            a.GetExportedTypes().Single(t => typeof(IModule).IsAssignableFrom(t)))!).Name).ToList();
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n == n.ToLowerInvariant());
    }
}
