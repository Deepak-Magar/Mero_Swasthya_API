using System.Text.Json;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Catalog.Infrastructure;

/// <summary>
/// Migrates schema <c>catalog</c> and upserts facilities + code lists from the embedded copies of the
/// app's assets, so codes match the app exactly. Idempotent: re-running converges on the files.
/// </summary>
internal sealed class CatalogModuleInitializer(CatalogDbContext db) : IModuleInitializer
{
    public string Module => "catalog";
    public int Order => 10;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedFacilitiesAsync(ct);
        await SeedCodeListsAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFacilitiesAsync(CancellationToken ct)
    {
        using var doc = CatalogResources.Parse(CatalogResources.Facilities);
        var existing = await db.Facilities.ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            if (!existing.TryGetValue(id, out var facility))
            {
                facility = new Facility { Id = id, Name = "", Municipality = "" };
                db.Facilities.Add(facility);
            }
            facility.Name = item.GetProperty("name").GetString()!;
            facility.Type = WireEnum.Parse<FacilityType>(item.GetProperty("type").GetString()!);
            facility.HasBirthingCentre = item.GetProperty("hasBirthingCentre").GetBoolean();
            facility.Phone = item.GetProperty("phone").GetString();
            facility.Lat = item.GetProperty("lat").GetDouble();
            facility.Lng = item.GetProperty("lng").GetDouble();
            facility.Municipality = item.GetProperty("municipality").GetString()!;
        }
    }

    private async Task SeedCodeListsAsync(CancellationToken ct)
    {
        using var doc = CatalogResources.Parse(CatalogResources.CodeLists);
        var existing = await db.CodeListItems.ToDictionaryAsync(c => (c.Kind, c.Code), ct);

        var order = 0;
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var kind = WireEnum.Parse<CodeListKind>(item.GetProperty("kind").GetString()!);
            var code = item.GetProperty("code").GetString()!;
            if (!existing.TryGetValue((kind, code), out var row))
            {
                row = new CodeListItem { Kind = kind, Code = code, LabelEn = "", LabelNp = "" };
                db.CodeListItems.Add(row);
            }
            row.LabelEn = item.GetProperty("labelEn").GetString()!;
            row.LabelNp = item.GetProperty("labelNp").GetString()!;
            row.MetaJson = item.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                ? meta.GetRawText()
                : null;
            row.SortOrder = order++;
        }

        var version = doc.RootElement.GetProperty("version").GetString()!;
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == CatalogSetting.CodeListVersion, ct);
        if (setting is null)
            db.Settings.Add(new CatalogSetting { Key = CatalogSetting.CodeListVersion, Value = version });
        else
            setting.Value = version;
    }
}
