using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MeroSwasthya.Shared.Validation;

/// <summary>Runs the FluentValidation validator for <typeparamref name="T"/> before the handler.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var argument = context.Arguments.OfType<T>().FirstOrDefault()
                ?? throw AppException.Validation("body", "A JSON request body is required");
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var details = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var failure in result.Errors)
                    details.TryAdd(string.IsNullOrEmpty(failure.PropertyName) ? "body" : failure.PropertyName, failure.ErrorMessage);
                throw AppException.Validation(details);
            }
        }
        return await next(context);
    }
}

public static partial class ValidationExtensions
{
    /// <summary>A.3: every endpoint with a body validates it → 400 VALIDATION_ERROR, details { field: message }.</summary>
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();

    /// <summary>
    /// Field names in details are the JSON (camelCase) names, e.g. <c>emergencyContactPhone</c>, and
    /// messages say "'name' must not be empty" rather than "'Name' …".
    /// </summary>
    public static void ConfigureGlobal()
    {
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
            member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
        ValidatorOptions.Global.LanguageManager.Enabled = false; // English messages regardless of server culture
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop; // one message per field
    }

    /// <summary>A.1: phones are E.164 without spaces, e.g. <c>+9779812345678</c>.</summary>
    public static IRuleBuilderOptions<T, string?> E164<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(p => p is null || E164Pattern().IsMatch(p)).WithMessage("Must be an E.164 phone number, e.g. +9779812345678");

    public static IRuleBuilderOptions<T, string?> ClientId<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Ids.Ids.IsValidClientId).WithMessage("Must be a client-generated id (letters, digits, '_' or '-', max 64)");

    /// <summary>The value must be one of the Part A strings of <typeparamref name="TEnum"/>.</summary>
    public static IRuleBuilderOptions<T, string?> WireEnum<T, TEnum>(this IRuleBuilder<T, string?> rule)
        where TEnum : struct, Enum =>
        rule.Must(v => v is null || Json.WireEnum.TryParse<TEnum>(v, out _))
            .WithMessage($"Must be one of: {string.Join(", ", Json.WireEnum.Names<TEnum>())}");

    public static bool IsE164(string? phone) => phone is not null && E164Pattern().IsMatch(phone);

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Pattern();
}

public static partial class Phones
{
    /// <summary>
    /// Same normalisation the app applies (A.1): strip spaces, dashes and brackets; a 10-digit number
    /// starting with 9 gets +977; 977XXXXXXXXXX gets a leading +.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = Strip().Replace(input.Trim(), "");
        if (s.StartsWith("00", StringComparison.Ordinal)) s = "+" + s[2..];
        if (s.Length == 10 && s[0] == '9' && s.All(char.IsAsciiDigit)) return "+977" + s;
        if (s.Length == 13 && s.StartsWith("977", StringComparison.Ordinal) && s.All(char.IsAsciiDigit)) return "+" + s;
        return s;
    }

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex Strip();
}
