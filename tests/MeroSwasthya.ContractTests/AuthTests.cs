using System.Net;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 "Auth" + GET /me, over real HTTP against PostgreSQL.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    private readonly ApiClient _api = new(factory.CreateClient());

    // ---- POST /auth/otp/request

    [Fact]
    public async Task Otp_request_returns_exact_shape_with_demo_otp()
    {
        var phone = TestUsers.UniquePhone();
        var data = (await _api.Post("/auth/otp/request", new { phone })).Data();

        JsonAssert.DeepEqual(data, Json.Obj($$"""{ "otpSentTo": "{{phone}}", "expiresInSec": 300, "demoOtp": "123456" }"""));
    }

    [Fact]
    public async Task Otp_request_normalises_a_local_number_to_e164()
    {
        var phone = TestUsers.UniquePhone();
        var data = (await _api.Post("/auth/otp/request", new { phone = phone[4..] })).Data(); // "98xxxxxxxx"
        data["otpSentTo"]!.GetValue<string>().Should().Be(phone);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"phone\":\"12\"}")]
    [InlineData("{\"phone\":\"not a phone\"}")]
    public async Task Otp_request_rejects_bad_phone_with_field_details(string body)
    {
        var details = (await _api.PostRaw("/auth/otp/request", body)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.Select(d => d.Key).Should().Equal("phone");
    }

    [Fact]
    public async Task Otp_request_is_rate_limited_to_5_per_phone_per_10_minutes()
    {
        var phone = TestUsers.UniquePhone();
        for (var i = 0; i < 5; i++)
            (await _api.Post("/auth/otp/request", new { phone })).Data();

        (await _api.Post("/auth/otp/request", new { phone })).Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    [Fact]
    public async Task Malformed_json_is_a_validation_error_envelope()
    {
        (await _api.PostRaw("/auth/otp/request", "{ not json")).Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var details = (await _api.PostRaw("/auth/otp/request", "{\"phone\": 9801000001}"))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.ContainsKey("phone").Should().BeTrue();
    }

    // ---- POST /auth/otp/verify

    [Fact]
    public async Task Otp_verify_for_a_new_phone_says_new_user_without_pin()
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        var data = (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data();

        JsonAssert.HasExactKeys(data, "tempToken", "hasPin", "isNewUser");
        data["hasPin"]!.GetValue<bool>().Should().BeFalse();
        data["isNewUser"]!.GetValue<bool>().Should().BeTrue();
        data["tempToken"]!.GetValue<string>().Split('.').Should().HaveCount(3, "tempToken is a JWT");
    }

    [Fact]
    public async Task Otp_verify_for_the_seeded_patient_says_existing_user_with_pin()
    {
        await _api.Post("/auth/otp/request", new { phone = TestUsers.PatientPhone });
        var data = (await _api.Post("/auth/otp/verify", new { phone = TestUsers.PatientPhone, otp = "123456" })).Data();

        data["hasPin"]!.GetValue<bool>().Should().BeTrue();
        data["isNewUser"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Otp_verify_rejects_a_wrong_code()
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        var details = (await _api.Post("/auth/otp/verify", new { phone, otp = "000000" }))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details["otp"]!.GetValue<string>().Should().Be("Wrong code");
    }

    [Fact]
    public async Task Otp_verify_without_a_request_is_rejected()
    {
        (await _api.Post("/auth/otp/verify", new { phone = TestUsers.UniquePhone(), otp = "123456" }))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey("otp").Should().BeTrue();
    }

    [Fact]
    public async Task Otp_code_is_single_use()
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data();
        (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---- POST /auth/pin/set

    [Fact]
    public async Task Pin_set_creates_the_account_and_returns_a_session()
    {
        var session = await TestUsers.NewAccount(_api, name: "Sita Chaudhary");

        JsonAssert.HasExactKeys(session.User, TestUsers.UserKeys);
        session.User["role"]!.GetValue<string>().Should().Be("patient");
        session.User["name"]!.GetValue<string>().Should().Be("Sita Chaudhary");
        session.User["facilityId"].Should().BeNull();
        session.User["facilityName"].Should().BeNull();
        session.UserId.Should().StartWith("u_");
        JsonAssert.IsIsoTimestamp(session.User["createdAt"]);
        session.AccessToken.Split('.').Should().HaveCount(3);
        session.RefreshToken.Should().NotContain(".", "refresh token is opaque");
    }

    [Fact]
    public async Task Pin_set_response_has_exactly_accessToken_refreshToken_user()
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        var temp = (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data()["tempToken"]!.GetValue<string>();
        var data = (await _api.Post("/auth/pin/set", new { pin = "4321", name = "Maya" }, temp)).Data();
        JsonAssert.HasExactKeys(data, "accessToken", "refreshToken", "user");
        data["user"]!["phone"]!.GetValue<string>().Should().Be(phone);
    }

    [Fact]
    public async Task Pin_set_requires_the_temp_token()
    {
        (await _api.Post("/auth/pin/set", new { pin = "4321", name = "X" }))
            .Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");

        var session = await TestUsers.NewAccount(_api);
        (await _api.Post("/auth/pin/set", new { pin = "4321", name = "X" }, session.AccessToken))
            .Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Theory]
    [InlineData("{\"pin\":\"12a4\",\"name\":\"X\"}", "pin")]
    [InlineData("{\"pin\":\"12345\",\"name\":\"X\"}", "pin")]
    [InlineData("{\"name\":\"X\"}", "pin")]
    [InlineData("{\"pin\":\"1234\"}", "name")]
    [InlineData("{\"pin\":\"1234\",\"name\":\"   \"}", "name")]
    public async Task Pin_set_validates_pin_and_name(string body, string field)
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        var temp = (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data()["tempToken"]!.GetValue<string>();

        var details = (await _api.PostRaw("/auth/pin/set", body, temp)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.ContainsKey(field).Should().BeTrue(details.ToJsonString());
    }

    [Fact]
    public async Task Pin_set_for_an_existing_account_resets_the_pin()
    {
        var phone = TestUsers.UniquePhone();
        async Task<string> Temp()
        {
            await _api.Post("/auth/otp/request", new { phone });
            return (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data()["tempToken"]!.GetValue<string>();
        }

        var first = TestUsers.ToSession((await _api.Post("/auth/pin/set", new { pin = "1111", name = "A" }, await Temp())).Data());
        var second = TestUsers.ToSession((await _api.Post("/auth/pin/set", new { pin = "2222" }, await Temp())).Data());

        second.UserId.Should().Be(first.UserId);
        second.User["name"]!.GetValue<string>().Should().Be("A");
        (await _api.Post("/auth/pin/login", new { phone, pin = "2222" })).Data();
        (await _api.Post("/auth/pin/login", new { phone, pin = "1111" })).Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---- POST /auth/pin/login

    [Fact]
    public async Task Pin_login_for_the_seeded_provider_matches_the_part_A_user_example()
    {
        var data = (await _api.Post("/auth/pin/login", new { phone = TestUsers.ProviderPhone, pin = "1234" })).Data();

        JsonAssert.HasExactKeys(data, "accessToken", "refreshToken", "user");
        JsonAssert.DeepEqual(data["user"], Json.Obj("""
            {
              "id": "u_22222222-2222-4222-8222-222222222222",
              "phone": "+9779801000002",
              "role": "provider",
              "name": "Ramesh Thapa (HA)",
              "facilityId": "f_0001",
              "facilityName": "Ghorahi Health Post",
              "createdAt": "2026-09-18T03:10:00.000Z"
            }
            """), "createdAt");
        JsonAssert.IsIsoTimestamp(data["user"]!["createdAt"]);
    }

    [Fact]
    public async Task Pin_login_with_wrong_pin_or_unknown_phone_gives_the_same_answer()
    {
        var wrongPin = (await _api.Post("/auth/pin/login", new { phone = TestUsers.PatientPhone, pin = "9999" }))
            .Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var unknown = (await _api.Post("/auth/pin/login", new { phone = TestUsers.UniquePhone(), pin = "1234" }))
            .Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        wrongPin.ToJsonString().Should().Be(unknown.ToJsonString());
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_account_for_15_minutes()
    {
        var session = await TestUsers.NewAccount(_api, pin: "4321");
        var phone = session.User["phone"]!.GetValue<string>();

        for (var i = 0; i < 4; i++)
            (await _api.Post("/auth/pin/login", new { phone, pin = "0000" })).Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await _api.Post("/auth/pin/login", new { phone, pin = "0000" })).Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED");

        // Even the right PIN is refused while locked.
        (await _api.Post("/auth/pin/login", new { phone, pin = "4321" })).Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    [Fact]
    public async Task Pin_login_validates_pin_format()
    {
        (await _api.Post("/auth/pin/login", new { phone = TestUsers.PatientPhone, pin = "12" }))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")["pin"]!.GetValue<string>().Should().Be("PIN must be 4 digits");
    }

    // ---- POST /auth/refresh

    [Fact]
    public async Task Refresh_rotates_and_returns_exactly_two_tokens()
    {
        var session = await TestUsers.NewAccount(_api);
        var data = (await _api.Post("/auth/refresh", new { refreshToken = session.RefreshToken })).Data();

        JsonAssert.HasExactKeys(data, "accessToken", "refreshToken");
        data["refreshToken"]!.GetValue<string>().Should().NotBe(session.RefreshToken);
        (await _api.Get("/me", data["accessToken"]!.GetValue<string>())).Data()["user"]!["id"]!.GetValue<string>()
            .Should().Be(session.UserId);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        var session = await TestUsers.NewAccount(_api);
        var rotated = (await _api.Post("/auth/refresh", new { refreshToken = session.RefreshToken })).Data()["refreshToken"]!
            .GetValue<string>();

        (await _api.Post("/auth/refresh", new { refreshToken = session.RefreshToken })).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Post("/auth/refresh", new { refreshToken = rotated })).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Refresh_with_an_unknown_token_is_unauthenticated()
    {
        (await _api.Post("/auth/refresh", new { refreshToken = "nope" })).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Post("/auth/refresh", new { })).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey("refreshToken").Should().BeTrue();
    }

    // ---- POST /auth/provider/activate

    [Theory]
    [InlineData("HA-GHORAHI-01", "provider")]
    [InlineData("FCHV-W5-01", "fchv")]
    [InlineData("ha-ghorahi-01", "provider")]
    public async Task Activate_upgrades_the_account(string inviteCode, string role)
    {
        var session = await TestUsers.NewAccount(_api, name: "Health Worker");
        var data = (await _api.Post("/auth/provider/activate", new { inviteCode }, session.AccessToken)).Data();

        JsonAssert.HasExactKeys(data, "user");
        JsonAssert.DeepEqual(data["user"], Json.Obj($$"""
            {
              "id": "{{session.UserId}}",
              "phone": "{{session.User["phone"]}}",
              "role": "{{role}}",
              "name": "Health Worker",
              "facilityId": "f_0001",
              "facilityName": "Ghorahi Health Post",
              "createdAt": "{{session.User["createdAt"]}}"
            }
            """));

        // The same access token now sees the new role: roles are read from storage, not frozen in the JWT.
        (await _api.Get("/me", session.AccessToken)).Data()["user"]!["role"]!.GetValue<string>().Should().Be(role);
    }

    [Fact]
    public async Task Activate_rejects_unknown_codes_and_anonymous_callers()
    {
        var session = await TestUsers.NewAccount(_api);
        (await _api.Post("/auth/provider/activate", new { inviteCode = "NOPE-00" }, session.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")["inviteCode"]!.GetValue<string>()
            .Should().Be("Unknown invite code");
        (await _api.Post("/auth/provider/activate", new { inviteCode = "HA-GHORAHI-01" }))
            .Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    // ---- GET /me

    [Fact]
    public async Task Me_returns_the_seeded_patient()
    {
        var session = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var data = (await _api.Get("/me", session.AccessToken)).Data();

        JsonAssert.HasExactKeys(data, "user");
        JsonAssert.DeepEqual(data["user"], Json.Obj("""
            {
              "id": "u_11111111-1111-4111-8111-111111111111",
              "phone": "+9779801000001",
              "role": "patient",
              "name": "Sita Chaudhary",
              "facilityId": null,
              "facilityName": null,
              "createdAt": "<server>"
            }
            """), "createdAt");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl")]
    public async Task Me_without_a_valid_access_token_is_unauthenticated(string? token)
    {
        (await _api.Get("/me", token)).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Temp_token_is_not_an_access_token()
    {
        var phone = TestUsers.UniquePhone();
        await _api.Post("/auth/otp/request", new { phone });
        var temp = (await _api.Post("/auth/otp/verify", new { phone, otp = "123456" })).Data()["tempToken"]!.GetValue<string>();

        (await _api.Get("/me", temp)).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }
}
