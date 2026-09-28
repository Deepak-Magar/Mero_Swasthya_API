using System.Text.Json.Nodes;

namespace MeroSwasthya.ContractTests.Infrastructure;

public sealed record Session(string AccessToken, string RefreshToken, JsonObject User)
{
    public string UserId => User["id"]!.GetValue<string>();
}

/// <summary>Seeded data (SEED section of the brief) and helpers to create fresh accounts through the real API.</summary>
public static class TestUsers
{
    public const string PatientPhone = "+9779801000001";
    public const string PatientId = "u_11111111-1111-4111-8111-111111111111";
    public const string ProviderPhone = "+9779801000002";
    public const string ProviderId = "u_22222222-2222-4222-8222-222222222222";
    public const string DemoPin = "1234";
    public const string DemoOtp = "123456";

    public static readonly string[] UserKeys = ["id", "phone", "role", "name", "facilityId", "facilityName", "createdAt"];

    private static int _counter = Random.Shared.Next(1_000_000, 9_000_000);

    /// <summary>A phone no other test uses: +97798 + 8 digits.</summary>
    public static string UniquePhone() => $"+97798{Interlocked.Increment(ref _counter):D8}";

    /// <summary>otp/request → otp/verify → pin/set for a brand-new account.</summary>
    public static async Task<Session> NewAccount(ApiClient api, string name = "Test User", string pin = "4321")
    {
        var phone = UniquePhone();
        (await api.Post("/auth/otp/request", new { phone })).Data();
        var temp = (await api.Post("/auth/otp/verify", new { phone, otp = DemoOtp })).Data()["tempToken"]!.GetValue<string>();
        return ToSession((await api.Post("/auth/pin/set", new { pin, name }, temp)).Data());
    }

    /// <summary>A fresh account upgraded with an invite code (HA-GHORAHI-01 → provider, FCHV-W5-01 → fchv).</summary>
    public static async Task<Session> NewHealthWorker(ApiClient api, string inviteCode = "HA-GHORAHI-01")
    {
        var session = await NewAccount(api, "Test Health Worker");
        var user = (await api.Post("/auth/provider/activate", new { inviteCode }, session.AccessToken)).Data()["user"]!.AsObject();
        return session with { User = user };
    }

    public static async Task<Session> Login(ApiClient api, string phone, string pin = DemoPin) =>
        ToSession((await api.Post("/auth/pin/login", new { phone, pin })).Data());

    public static Session ToSession(JsonObject data) => new(
        data["accessToken"]!.GetValue<string>(),
        data["refreshToken"]!.GetValue<string>(),
        data["user"]!.AsObject());
}
