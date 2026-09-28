namespace MeroSwasthya.Shared.Errors;

/// <summary>
/// Thrown anywhere in a request; the global handler turns it into the A.1 error envelope with the
/// A.3 status. <see cref="Details"/> must serialise to a JSON object.
/// </summary>
public sealed class AppException : Exception
{
    public AppException(ErrorCode code, string message, object? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public ErrorCode Code { get; }
    public object? Details { get; }

    public static AppException Validation(string field, string message) =>
        new(ErrorCode.ValidationError, message, new Dictionary<string, string> { [field] = message });

    public static AppException Validation(IDictionary<string, string> fields) =>
        new(ErrorCode.ValidationError, "Request failed validation", fields);

    public static AppException NotFound(string what = "Resource") => new(ErrorCode.NotFound, $"{what} not found");

    public static AppException Forbidden(string message = "Not allowed") => new(ErrorCode.Forbidden, message);

    public static AppException Unauthenticated(string message = "Missing or invalid access token") =>
        new(ErrorCode.Unauthenticated, message);

    public static AppException RateLimited(string message) => new(ErrorCode.RateLimited, message);

    /// <summary>A.3: <c>details = { current: &lt;entity&gt; }</c>.</summary>
    public static AppException VersionConflict(object current) =>
        new(ErrorCode.VersionConflict, "Version does not match the server copy", new { current });

    public static AppException RuleViolation(string message, object? details = null) =>
        new(ErrorCode.RuleViolation, message, details);
}
