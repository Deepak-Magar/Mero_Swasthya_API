using System.Text.Json;

namespace MeroSwasthya.Modules.Catalog.Infrastructure;

/// <summary>The app's own asset files, embedded byte-for-byte (see Resources/).</summary>
internal static class CatalogResources
{
    public const string Rules = "MeroSwasthya.Catalog.rules.json";
    public const string CodeLists = "MeroSwasthya.Catalog.codelists.json";
    public const string Facilities = "MeroSwasthya.Catalog.facilities.json";

    public static byte[] ReadBytes(string name)
    {
        using var stream = typeof(CatalogResources).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static JsonDocument Parse(string name) => JsonDocument.Parse(ReadBytes(name));
}
