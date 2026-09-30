namespace MeroSwasthya.Shared.Sms;

/// <summary>
/// Outbound SMS. Implemented by the Reminders module: the in-memory mock outbox when
/// <c>Features:SmsMode = mock</c>, a gateway otherwise. Lives in Shared so that any module can send
/// (e.g. Auth's OTP) without referencing Reminders.
/// </summary>
public interface ISmsSender
{
    /// <summary>Throws when the message could not be handed over; the caller decides about retries.</summary>
    Task SendAsync(string to, string text, CancellationToken ct = default);
}
