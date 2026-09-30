using System.Net;
using System.Text;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Shared.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeroSwasthya.Modules.Reminders.Endpoints;

/// <summary>
/// The mock SMS outbox for the projector: <c>GET /dev/sms</c> (HTML table, refreshes itself) and
/// <c>GET /dev/sms.json</c>. Also served under the names A.4 gives them and the app calls —
/// <c>GET /demo/sms</c> (JSON) and <c>GET /demo/sms.html</c>. No auth; mapped only in Development
/// (and the contract tests' Testing environment) while <c>Features:SmsMode = mock</c>.
/// </summary>
internal static class DevSmsEndpoints
{
    public const int RefreshSeconds = 5;

    /// <summary>Asia/Kathmandu, for the "sent at" column.</summary>
    private static readonly TimeSpan KathmanduOffset = ReminderPlanner.KathmanduOffset;

    public static bool Enabled(IHostEnvironment env, IServiceProvider services) =>
        (env.IsDevelopment() || env.IsEnvironment("Testing")) && services.GetService<MockSmsSender>() is not null;

    public static void Map(IEndpointRouteBuilder api)
    {
        if (!Enabled(api.ServiceProvider.GetRequiredService<IHostEnvironment>(), api.ServiceProvider)) return;

        foreach (var path in new[] { "/dev/sms.json", "/demo/sms" })
            api.MapGet(path, (MockSmsSender outbox) => ApiResults.Ok(new ItemsResponse<SentSms>(outbox.Sent())))
                .WithTags("Dev")
                .AllowAnonymous();

        foreach (var path in new[] { "/dev/sms", "/demo/sms.html" })
            api.MapGet(path, (MockSmsSender outbox) => Results.Content(Page(outbox.Sent()), "text/html; charset=utf-8"))
                .WithTags("Dev")
                .AllowAnonymous()
                .ExcludeFromDescription();
    }

    internal static string Page(IReadOnlyList<SentSms> messages)
    {
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="refresh" content="{{RefreshSeconds}}">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Mock SMS outbox</title>
            <style>
              body { font-family: system-ui, "Noto Sans Devanagari", sans-serif; margin: 2rem; color: #1b1b1b; }
              h1 { font-size: 1.4rem; margin-bottom: .25rem; }
              p { color: #555; margin-top: 0; }
              table { border-collapse: collapse; width: 100%; }
              th, td { text-align: left; padding: .6rem .8rem; border-bottom: 1px solid #ddd; vertical-align: top; }
              th { background: #f4f4f4; }
              td.when, td.to { white-space: nowrap; font-variant-numeric: tabular-nums; }
              td.text { font-size: 1.15rem; }
            </style>
            </head>
            <body>
            <h1>Mock SMS outbox</h1>
            <p>{{messages.Count}} message(s), newest first. Kept in memory (cleared on restart); refreshes every {{RefreshSeconds}} s. Times are Nepal time.</p>
            <table>
            <thead><tr><th>Sent at</th><th>To</th><th>Text</th></tr></thead>
            <tbody>

            """);

        foreach (var m in messages)
            html.Append("<tr><td class=\"when\">").Append((m.SentAt + KathmanduOffset).ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("</td><td class=\"to\">").Append(WebUtility.HtmlEncode(m.To))
                .Append("</td><td class=\"text\">").Append(WebUtility.HtmlEncode(m.Text))
                .Append("</td></tr>\n");
        if (messages.Count == 0)
            html.Append("<tr><td colspan=\"3\">No messages yet.</td></tr>\n");

        html.Append("</tbody>\n</table>\n</body>\n</html>\n");
        return html.ToString();
    }
}
