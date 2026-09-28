namespace MeroSwasthya.Shared;

/// <summary>Bound from <c>Features</c>; drives GET /config and demo behaviour (A.4, contract addendum §6).</summary>
public sealed class FeatureFlags
{
    public const string Section = "Features";

    /// <summary>"mock" | "live". In mock mode SMS goes to the demo outbox and OTP responses carry <c>demoOtp</c>.</summary>
    public string SmsMode { get; set; } = "mock";

    /// <summary>"off" | anything else. <c>aiSummaryEnabled</c> = AiMode != "off".</summary>
    public string AiMode { get; set; } = "off";

    /// <summary>When true the OTP is always 123456 (A.4 demo build).</summary>
    public bool OtpDemo { get; set; } = true;

    public bool NidEnabled { get; set; }
    public bool HmisExportEnabled { get; set; }
    public bool CouncilVerifyEnabled { get; set; }

    public bool SmsIsMock => string.Equals(SmsMode, "mock", StringComparison.OrdinalIgnoreCase);
    public bool AiSummaryEnabled => !string.Equals(AiMode, "off", StringComparison.OrdinalIgnoreCase);
}
