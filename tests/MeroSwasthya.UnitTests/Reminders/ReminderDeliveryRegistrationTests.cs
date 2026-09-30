using MeroSwasthya.Modules.Reminders;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Shared.Sms;
using MeroSwasthya.Shared.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.UnitTests.Reminders;

/// <summary>What the Reminders module registers for delivery: the worker, its interval, and the SMS sender per <c>Features:SmsMode</c>.</summary>
public sealed class ReminderDeliveryRegistrationTests
{
    private static ServiceCollection Register(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings
                .Append(("ConnectionStrings:Default", "Host=127.0.0.1;Database=unused"))
                .ToDictionary(s => s.Item1, s => (string?)s.Item2))
            .Build();
        var services = new ServiceCollection();
        new RemindersModule().AddModule(services, config);
        return services;
    }

    private static bool HasWorker(ServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ReminderDeliveryWorker));

    [Fact]
    public void By_default_the_worker_runs_every_sixty_seconds_into_the_mock_outbox()
    {
        var services = Register();

        HasWorker(services).Should().BeTrue();
        services.Single(d => d.ServiceType == typeof(ReminderDeliveryOptions)).ImplementationInstance
            .Should().BeEquivalentTo(new ReminderDeliveryOptions { WorkerEnabled = true, PollInterval = TimeSpan.FromSeconds(60) });
        services.Should().Contain(d => d.ServiceType == typeof(MockSmsSender), "Features:SmsMode defaults to mock");
    }

    [Fact]
    public void The_worker_and_its_interval_are_configurable()
    {
        var services = Register(("Reminders:WorkerEnabled", "false"), ("Reminders:PollInterval", "00:00:05"));

        HasWorker(services).Should().BeFalse();
        ((ReminderDeliveryOptions)services.Single(d => d.ServiceType == typeof(ReminderDeliveryOptions)).ImplementationInstance!)
            .PollInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Outside_mock_mode_every_send_fails_until_a_gateway_exists()
    {
        var services = Register(("Features:SmsMode", "live"));

        services.Should().NotContain(d => d.ServiceType == typeof(MockSmsSender));
        var sender = services.Single(d => d.ServiceType == typeof(ISmsSender)).ImplementationType;
        sender.Should().Be(typeof(UnconfiguredSmsSender));
        await new UnconfiguredSmsSender().Invoking(s => s.SendAsync("+9779801000001", "x"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(UnconfiguredSmsSender.Reason);
    }

    [Fact]
    public async Task The_mock_outbox_keeps_the_newest_messages_first_and_is_bounded()
    {
        var outbox = new MockSmsSender(new SystemClock(), NullLogger<MockSmsSender>.Instance);
        for (var i = 1; i <= MockSmsSender.Capacity + 5; i++)
            await outbox.SendAsync("+9779801000001", $"message {i}");

        var sent = outbox.Sent();
        sent.Should().HaveCount(MockSmsSender.Capacity);
        sent[0].Text.Should().Be($"message {MockSmsSender.Capacity + 5}");
        sent[^1].Text.Should().Be("message 6");
        sent[0].To.Should().Be("+9779801000001");
    }
}
