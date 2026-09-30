using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Endpoints;
using MeroSwasthya.Shared.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.UnitTests.Reminders;

/// <summary>GET /dev/sms: where it is mapped, and the HTML it renders.</summary>
public sealed class DevSmsPageTests
{
    private static IServiceProvider Services(bool mockOutbox)
    {
        var services = new ServiceCollection();
        if (mockOutbox) services.AddSingleton(new MockSmsSender(new SystemClock(), NullLogger<MockSmsSender>.Instance));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Testing", true, true)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", false, false)] // Features:SmsMode is not mock: there is no outbox to show
    public void The_outbox_is_mapped_only_in_development_with_mock_sms(string environment, bool mockOutbox, bool expected)
    {
        IHostEnvironment env = new HostingEnvironment { EnvironmentName = environment };
        DevSmsEndpoints.Enabled(env, Services(mockOutbox)).Should().Be(expected);
    }

    [Fact]
    public void The_page_shows_nepal_time_and_escapes_what_it_prints()
    {
        var html = DevSmsEndpoints.Page(
        [
            new SentSms("+9779801000009", "सीता चौधरीको ४ औं गर्भ जाँच २०८३-०६-०२ मा घोराही स्वास्थ्य चौकीमा छ।", new DateTime(2026, 9, 17, 3, 15, 5, DateTimeKind.Utc)),
            new SentSms("+9779801000001", "<script>alert(1)</script>", new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc)),
        ]);

        html.Should().Contain("<meta charset=\"utf-8\">");
        html.Should().Contain("<p>2 message(s), newest first.");
        html.Should().Contain("<tr><td class=\"when\">2026-09-17 09:00:05</td><td class=\"to\">+9779801000009</td>" +
                              "<td class=\"text\">सीता चौधरीको ४ औं गर्भ जाँच २०८३-०६-०२ मा घोराही स्वास्थ्य चौकीमा छ।</td></tr>");
        html.Should().Contain("<td class=\"when\">2026-09-17 01:45:00</td>", "20:00 UTC is 01:45 the next day in Nepal");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;").And.NotContain("<script>");
    }

    [Fact]
    public void An_empty_outbox_says_so()
    {
        DevSmsEndpoints.Page([]).Should().Contain("<p>0 message(s)").And.Contain("<tr><td colspan=\"3\">No messages yet.</td></tr>");
    }
}
