using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace MeroSwasthya.Modules.Auth.Application;

/// <summary>
/// Argon2id with the OWASP baseline (19 MiB, t=2, p=1), stored as a PHC string:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>. Parameters are read back from the
/// string on verify, so they can be raised later without invalidating existing PINs.
/// </summary>
internal sealed class PinHasher
{
    private const int MemoryKiB = 19_456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(pin, salt, MemoryKiB, Iterations, Parallelism, HashBytes);
        return $"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";
    }

    public bool Verify(string pin, string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return false;
        var parts = encoded.Split('$'); // "", "argon2id", "v=19", "m=…,t=…,p=…", salt, hash
        if (parts.Length != 6 || parts[1] != "argon2id") return false;

        var settings = parts[3].Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        var salt = FromBase64(parts[4]);
        var expected = FromBase64(parts[5]);
        var actual = Compute(pin, salt, settings["m"], settings["t"], settings["p"], expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string pin, byte[] salt, int memoryKiB, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(pin))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    private static byte[] FromBase64(string unpadded) =>
        Convert.FromBase64String(unpadded.PadRight(unpadded.Length + (4 - unpadded.Length % 4) % 4, '='));
}
