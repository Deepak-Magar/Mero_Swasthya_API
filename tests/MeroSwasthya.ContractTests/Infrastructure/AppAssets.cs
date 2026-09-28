using System.Text.Json.Nodes;

namespace MeroSwasthya.ContractTests.Infrastructure;

/// <summary>
/// The Flutter app's own asset files — the ground truth the API must match. Located via
/// <c>MERO_APP_DIR</c> (default <c>D:\mero_swasthya</c>). When the app is not checked out next to the
/// API (e.g. CI), the tests fall back to the embedded copies and say so in the assertion message.
/// </summary>
public static class AppAssets
{
    public static string AppDir => Environment.GetEnvironmentVariable("MERO_APP_DIR") ?? @"D:\mero_swasthya";

    public static (JsonNode Node, string Source) Load(string fileName)
    {
        var appPath = Path.Combine(AppDir, "assets", fileName);
        if (File.Exists(appPath)) return (JsonNode.Parse(File.ReadAllText(appPath))!, appPath);

        var repoCopy = Path.Combine(RepoRoot(), "src", "Modules", "Catalog", "MeroSwasthya.Modules.Catalog", "Resources", fileName);
        return (JsonNode.Parse(File.ReadAllText(repoCopy))!, $"{repoCopy} (app checkout not found at {AppDir})");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MeroSwasthya.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MeroSwasthya.sln not found above the test binaries");
    }
}
