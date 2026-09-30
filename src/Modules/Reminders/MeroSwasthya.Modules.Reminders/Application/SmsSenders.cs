using MeroSwasthya.Shared.Sms;
using MeroSwasthya.Shared.Time;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Modules.Reminders.Application;

/// <summary>One message in the mock outbox — the A.4 GET /demo/sms item.</summary>
internal sealed record SentSms(string To, string Text, DateTime SentAt);

/// <summary>
/// <c>Features:SmsMode = mock</c>: nothing leaves the machine. Every message is logged and kept in
/// memory (newest <see cref="Capacity"/>, lost on restart) for the demo SMS panel.
/// </summary>
internal sealed class MockSmsSender(IClock clock, ILogger<MockSmsSender> logger) : ISmsSender
{
    public const int Capacity = 200;

    private readonly object _gate = new();
    private readonly LinkedList<SentSms> _sent = new();

    public Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        var message = new SentSms(to, text, clock.UtcNow);
        lock (_gate)
        {
            _sent.AddFirst(message);
            if (_sent.Count > Capacity) _sent.RemoveLast();
        }
        logger.LogInformation("Mock SMS to {To}: {Text}", to, text);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seed only: shows a message that the seed stored as already sent (it never went through
    /// <see cref="SendAsync"/>). Adding the same message again — a re-run seed — changes nothing.
    /// </summary>
    public void Remember(SentSms message)
    {
        lock (_gate)
        {
            if (_sent.Count >= Capacity || _sent.Contains(message)) return;
            _sent.AddLast(message);
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SentSms> Sent()
    {
        lock (_gate) return _sent.OrderByDescending(m => m.SentAt).ToList();
    }
}

/// <summary>
/// <c>Features:SmsMode</c> is not <c>mock</c> but no gateway is wired in yet: every send fails, so the
/// reminder ends up <c>failed</c> with this reason instead of being reported as sent.
/// </summary>
internal sealed class UnconfiguredSmsSender : ISmsSender
{
    public const string Reason = "No SMS gateway is configured (Features:SmsMode is not \"mock\")";

    public Task SendAsync(string to, string text, CancellationToken ct = default) =>
        Task.FromException(new InvalidOperationException(Reason));
}
