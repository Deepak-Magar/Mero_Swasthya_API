namespace MeroSwasthya.Api.Hosting;

public sealed record CommandLineFlags(bool Migrate, bool Seed)
{
    /// <summary>With either flag the process prepares the database and exits instead of serving.</summary>
    public bool ExitAfterDatabase => Migrate || Seed;

    /// <summary>Strips our own flags so the configuration command-line provider never sees them.</summary>
    public static (string[] HostArgs, CommandLineFlags Flags) Parse(string[] args)
    {
        var migrate = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
        var seed = args.Contains("--seed", StringComparer.OrdinalIgnoreCase);
        var rest = args.Where(a => !a.Equals("--migrate", StringComparison.OrdinalIgnoreCase)
                                   && !a.Equals("--seed", StringComparison.OrdinalIgnoreCase)).ToArray();
        return (rest, new CommandLineFlags(migrate, seed));
    }
}
