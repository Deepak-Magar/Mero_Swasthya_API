using System.Text.Json;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Paging;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Http;

namespace MeroSwasthya.UnitTests.Shared;

public sealed class HelperTests
{
    [Theory]
    [InlineData("9801000001", "+9779801000001")]
    [InlineData("980 100 0001", "+9779801000001")]
    [InlineData("+977-9801000001", "+9779801000001")]
    [InlineData("9779801000001", "+9779801000001")]
    [InlineData("009779801000001", "+9779801000001")]
    [InlineData("+9779801000001", "+9779801000001")]
    public void Phones_normalise_like_the_app(string input, string expected)
    {
        Phones.Normalize(input).Should().Be(expected);
        ValidationExtensions.IsE164(Phones.Normalize(input)).Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+0123456789")]
    [InlineData("abc")]
    public void Invalid_phones_fail_e164(string input)
    {
        ValidationExtensions.IsE164(Phones.Normalize(input)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ErrorCode.ValidationError, 400, "VALIDATION_ERROR")]
    [InlineData(ErrorCode.Unauthenticated, 401, "UNAUTHENTICATED")]
    [InlineData(ErrorCode.Forbidden, 403, "FORBIDDEN")]
    [InlineData(ErrorCode.GrantExpired, 403, "GRANT_EXPIRED")]
    [InlineData(ErrorCode.NotFound, 404, "NOT_FOUND")]
    [InlineData(ErrorCode.VersionConflict, 409, "VERSION_CONFLICT")]
    [InlineData(ErrorCode.AlreadyRedeemed, 409, "ALREADY_REDEEMED")]
    [InlineData(ErrorCode.RuleViolation, 422, "RULE_VIOLATION")]
    [InlineData(ErrorCode.RateLimited, 429, "RATE_LIMITED")]
    [InlineData(ErrorCode.Internal, 500, "INTERNAL")]
    [InlineData(ErrorCode.NotImplemented, 501, "NOT_IMPLEMENTED")]
    public void Error_codes_mirror_table_A3(ErrorCode code, int status, string wire)
    {
        code.HttpStatus().Should().Be(status);
        code.Wire().Should().Be(wire);
    }

    [Fact]
    public void Error_envelope_serialises_exactly()
    {
        var (status, error) = GlobalExceptionHandler.Map(AppException.Validation("pin", "Must be 4 digits"));
        status.Should().Be(400);
        JsonSerializer.Serialize(new ApiErrorEnvelope(error), JsonDefaults.Options).Should().Be(
            "{\"ok\":false,\"error\":{\"code\":\"VALIDATION_ERROR\",\"message\":\"Must be 4 digits\",\"details\":{\"pin\":\"Must be 4 digits\"}}}");
    }

    [Fact]
    public void Unexpected_exceptions_become_INTERNAL_without_leaking_the_message()
    {
        var (status, error) = GlobalExceptionHandler.Map(new InvalidOperationException("connection string secret"));
        status.Should().Be(500);
        error.Code.Should().Be("INTERNAL");
        error.Message.Should().NotContain("secret");
    }

    [Fact]
    public void Malformed_json_field_becomes_a_validation_detail()
    {
        var inner = new JsonException("bad", "$.ward", 1, 10);
        var (status, error) = GlobalExceptionHandler.Map(new BadHttpRequestException("Failed to read", inner));
        status.Should().Be(400);
        error.Details.Should().BeEquivalentTo(new Dictionary<string, string> { ["ward"] = "Invalid value" });
    }

    [Fact]
    public void Success_envelope_serialises_ok_first()
    {
        JsonSerializer.Serialize(new ApiEnvelope<object>(new { items = Array.Empty<int>() }), JsonDefaults.Options)
            .Should().Be("{\"ok\":true,\"data\":{\"items\":[]}}");
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData("", 50)]
    [InlineData("1", 1)]
    [InlineData("200", 200)]
    public void Limit_parses_within_bounds(string? raw, int expected)
    {
        Cursors.Limit(raw, 50, 200).Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("201")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void Limit_out_of_bounds_is_a_validation_error(string raw)
    {
        var act = () => Cursors.Limit(raw, 50, 200);
        act.Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.ValidationError);
    }

    [Fact]
    public void Timestamp_cursor_round_trips()
    {
        var at = Cursors.Timestamp("2026-06-01T00:00:00.000Z", "before");
        Cursors.ToWire(at).Should().Be("2026-06-01T00:00:00.000Z");
        Cursors.Timestamp(null, "before").Should().BeNull();
        var act = () => Cursors.Timestamp("June", "before");
        act.Should().Throw<AppException>();
    }

    [Fact]
    public void PageDescending_returns_next_cursor_only_when_more_remain()
    {
        var baseAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
        var items = Enumerable.Range(0, 5).Select(i => baseAt.AddMinutes(i)).ToList();

        var (page1, next1) = Cursors.PageDescending(items, x => x, null, 3);
        page1.Should().Equal(items[4], items[3], items[2]);
        next1.Should().Be(items[2]);

        var (page2, next2) = Cursors.PageDescending(items, x => x, next1, 3);
        page2.Should().Equal(items[1], items[0]);
        next2.Should().BeNull();
    }

    [Fact]
    public void Clock_truncates_to_milliseconds()
    {
        var now = new SystemClock().UtcNow;
        (now.Ticks % TimeSpan.TicksPerMillisecond).Should().Be(0);
        now.Kind.Should().Be(DateTimeKind.Utc);
    }
}
