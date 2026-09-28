namespace MeroSwasthya.Shared.Errors;

/// <summary>Mirrors A.3 one-to-one. The wire string and HTTP status come from <see cref="ErrorCodes"/>.</summary>
public enum ErrorCode
{
    ValidationError,
    Unauthenticated,
    Forbidden,
    GrantExpired,
    NotFound,
    VersionConflict,
    AlreadyRedeemed,
    RuleViolation,
    RateLimited,
    Internal,

    /// <summary>Not in A.3; A.4 allows 501 for POST /documents/:id/summarize when AI is off.</summary>
    NotImplemented,
}

public static class ErrorCodes
{
    public static string Wire(this ErrorCode code) => code switch
    {
        ErrorCode.ValidationError => "VALIDATION_ERROR",
        ErrorCode.Unauthenticated => "UNAUTHENTICATED",
        ErrorCode.Forbidden => "FORBIDDEN",
        ErrorCode.GrantExpired => "GRANT_EXPIRED",
        ErrorCode.NotFound => "NOT_FOUND",
        ErrorCode.VersionConflict => "VERSION_CONFLICT",
        ErrorCode.AlreadyRedeemed => "ALREADY_REDEEMED",
        ErrorCode.RuleViolation => "RULE_VIOLATION",
        ErrorCode.RateLimited => "RATE_LIMITED",
        ErrorCode.NotImplemented => "NOT_IMPLEMENTED",
        _ => "INTERNAL",
    };

    public static int HttpStatus(this ErrorCode code) => code switch
    {
        ErrorCode.ValidationError => 400,
        ErrorCode.Unauthenticated => 401,
        ErrorCode.Forbidden => 403,
        ErrorCode.GrantExpired => 403,
        ErrorCode.NotFound => 404,
        ErrorCode.VersionConflict => 409,
        ErrorCode.AlreadyRedeemed => 409,
        ErrorCode.RuleViolation => 422,
        ErrorCode.RateLimited => 429,
        ErrorCode.NotImplemented => 501,
        _ => 500,
    };
}
