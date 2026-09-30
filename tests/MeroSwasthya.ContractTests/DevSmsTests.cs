using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.Api.Hosting;
using MeroSwasthya.ContractTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.ContractTests;

/// <summary>
/// The mock SMS outbox: <c>GET /dev/sms</c> (HTML) and <c>GET /dev/sms.json</c>, and the same two under
/// the A.4 names the app calls (<c>/demo/sms</c>, <c>/demo/sms.html</c>). No auth; Development / Testing only.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DevSmsTests
{
    private const string SitaFamilyPhone = "+9779801000009";

    private readonly ApiFactory _factory;
    private readonly ApiClient _api;
    private readonly Scenario _s;

    public DevSmsTests(ApiFactory factory)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    /// <summary>A family whose follow-up reminder has just been delivered to the mock outbox.</summary>
    private async Task<(Family Family, string Phone)> FamilyWithDeliveredReminder(string name = "Maya Tharu")
    {
        var family = await _s.NewFamily(name: name);
        var body = Scenario.VisitBody();
        body["followUpAt"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", body, family.Owner.AccessToken)).Data();
        await _s.MakeRemindersDue(family.PatientId, TimeSpan.FromMinutes(5));
        await _s.DispatchReminders();
        return (family, family.Owner.User["phone"]!.GetValue<string>());
    }

    private async Task<List<JsonObject>> Items(string path = "/dev/sms.json")
    {
        var data = (await _api.Get(path)).Data(); // no bearer token
        JsonAssert.HasExactKeys(data, "items");
        return data["items"]!.AsArray().Select(i => i!.AsObject()).ToList();
    }

    [Fact]
    public async Task Json_is_the_part_A_demo_sms_shape_newest_first_without_auth()
    {
        var (_, phone) = await FamilyWithDeliveredReminder();

        var items = await Items();
        foreach (var item in items)
        {
            JsonAssert.HasExactKeys(item, "to", "text", "sentAt");
            JsonAssert.IsIsoTimestamp(item["sentAt"]);
        }
        items.Select(i => i["sentAt"]!.GetValue<string>()).Should().BeInDescendingOrder(StringComparer.Ordinal);
        items[0]["to"]!.GetValue<string>().Should().Be(phone, "the message just delivered is the newest");
        items[0]["text"]!.GetValue<string>().Should().StartWith("Maya Tharu को फलो-अप जाँच २०", "the Nepali text is what is sent");
    }

    [Fact]
    public async Task The_seeded_anc_due_messages_are_in_the_outbox_once_even_after_a_reseed()
    {
        await DatabaseBootstrap.SeedAsync(_factory.Services, NullLogger.Instance);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var seeded = (await Items()).Where(i => i["text"]!.GetValue<string>().StartsWith("Sita Chaudhary को ४ औं गर्भ जाँच ")).ToList();
        seeded.Select(i => i["to"]!.GetValue<string>()).Should().BeEquivalentTo([TestUsers.PatientPhone, SitaFamilyPhone]);
        seeded.Should().OnlyContain(i => i["sentAt"]!.GetValue<string>() == $"{yesterday:yyyy-MM-dd}T03:15:05.000Z");
    }

    [Fact]
    public async Task The_part_A_path_serves_the_same_list()
    {
        await FamilyWithDeliveredReminder();
        JsonAssert.DeepEqual((await _api.Get("/demo/sms")).Data(), (await _api.Get("/dev/sms.json")).Data());
    }

    [Theory]
    [InlineData("/dev/sms")]
    [InlineData("/demo/sms.html")]
    public async Task Html_page_is_a_self_refreshing_table_with_the_text_escaped(string path)
    {
        var (_, phone) = await FamilyWithDeliveredReminder(name: "<b>Maya</b> & Co");

        using var response = await _factory.CreateClient().GetAsync(ApiClient.Prefix + path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be("text/html; charset=utf-8");
        var html = await response.Content.ReadAsStringAsync();

        html.Should().StartWith("<!doctype html>");
        html.Should().Contain("<meta http-equiv=\"refresh\" content=\"5\">");
        html.Should().Contain("<th>Sent at</th><th>To</th><th>Text</th>");
        html.Should().Contain($"<td class=\"to\">{phone}</td>");
        html.Should().Contain("&lt;b&gt;Maya&lt;/b&gt; &amp; Co को फलो-अप जाँच");
        html.Should().NotContain("<b>Maya</b>");
    }
}
