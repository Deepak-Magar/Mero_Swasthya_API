using FluentValidation;
using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.Modules.Auth.Application;

// ---- Requests (A.4 "Auth"). Strings are nullable so a missing field is a validation error, not a crash.

internal sealed class OtpRequest
{
    public string? Phone { get; init; }
}

internal sealed class OtpVerifyRequest
{
    public string? Phone { get; init; }
    public string? Otp { get; init; }
}

internal sealed class PinSetRequest
{
    public string? Pin { get; init; }
    public string? Name { get; init; }
}

internal sealed class PinLoginRequest
{
    public string? Phone { get; init; }
    public string? Pin { get; init; }
}

internal sealed class RefreshRequest
{
    public string? RefreshToken { get; init; }
}

internal sealed class ActivateRequest
{
    public string? InviteCode { get; init; }
}

// ---- Responses: exactly the Part A shapes.

/// <summary>A.2 User.</summary>
public sealed record UserDto(
    string Id,
    string Phone,
    UserRole Role,
    string Name,
    string? FacilityId,
    string? FacilityName,
    DateTime CreatedAt)
{
    internal static UserDto From(User u) => new(u.Id, u.Phone, u.Role, u.Name, u.FacilityId, u.FacilityName, u.CreatedAt);
}

internal sealed record OtpRequestResponse(
    string OtpSentTo,
    int ExpiresInSec,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? DemoOtp);

internal sealed record OtpVerifyResponse(string TempToken, bool HasPin, bool IsNewUser);

internal sealed record SessionResponse(string AccessToken, string RefreshToken, UserDto User);

internal sealed record RefreshResponse(string AccessToken, string RefreshToken);

internal sealed record UserResponse(UserDto User);

// ---- Validators (A.3: 400 VALIDATION_ERROR, details { field: message }).

internal sealed class OtpRequestValidator : AbstractValidator<OtpRequest>
{
    public OtpRequestValidator() => RuleFor(x => x.Phone).ValidPhone();
}

internal sealed class OtpVerifyRequestValidator : AbstractValidator<OtpVerifyRequest>
{
    public OtpVerifyRequestValidator()
    {
        RuleFor(x => x.Phone).ValidPhone();
        RuleFor(x => x.Otp).NotEmpty().Matches("^[0-9]{6}$").WithMessage("OTP must be 6 digits");
    }
}

internal sealed class PinSetRequestValidator : AbstractValidator<PinSetRequest>
{
    public PinSetRequestValidator()
    {
        RuleFor(x => x.Pin).ValidPin();
        RuleFor(x => x.Name).MaximumLength(100).Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("'name' must not be blank");
    }
}

internal sealed class PinLoginRequestValidator : AbstractValidator<PinLoginRequest>
{
    public PinLoginRequestValidator()
    {
        RuleFor(x => x.Phone).ValidPhone();
        RuleFor(x => x.Pin).ValidPin();
    }
}

internal sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class ActivateRequestValidator : AbstractValidator<ActivateRequest>
{
    public ActivateRequestValidator() => RuleFor(x => x.InviteCode).NotEmpty().MaximumLength(64);
}

internal static class AuthRules
{
    public static IRuleBuilderOptions<T, string?> ValidPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().Must(p => ValidationExtensions.IsE164(Phones.Normalize(p)))
            .WithMessage("Must be a phone number in E.164 form, e.g. +9779812345678");

    public static IRuleBuilderOptions<T, string?> ValidPin<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().Matches("^[0-9]{4}$").WithMessage("PIN must be 4 digits");
}
