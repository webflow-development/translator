using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Koe.GuestServer;
using Koe.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var temporaryDirectory = Directory.CreateTempSubdirectory("koe-guest-checks-");
var root = temporaryDirectory.FullName;
var clock = new TestClock(new DateTimeOffset(DateTime.UtcNow.Year + 1, 1, 30, 15, 0, 0, TimeSpan.Zero));
var id = Guid.NewGuid();
var network = IPAddress.Loopback;

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}

try
{
    var options = new GuestOptions
    {
        DataDirectory = Path.Combine(root, "quota"), RequestsPerGuestPerDay = 2,
        RequestsPerNetworkPerDay = 3, RequestsPerDay = 4, RequestsPerMonth = 5
    };
    using (var quota = new GuestQuota(options, clock))
    {
        var simultaneous = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => quota.Reserve(id, network))));
        Check(simultaneous.Count(result => result.Error is null) == 2, "Concurrent requests cannot overrun the installation limit");
        Check(quota.Reserve(Guid.NewGuid(), network).Error is null &&
              quota.Reserve(Guid.NewGuid(), network).Error?.Code == GuestProtocol.CapacityReached,
              "Changing the installation ID does not evade the network allowance");
        Check(quota.Reserve(Guid.NewGuid(), IPAddress.IPv6Loopback).Error is null &&
              quota.Reserve(Guid.NewGuid(), IPAddress.Parse("192.0.2.1")).Error?.Code == GuestProtocol.CapacityReached,
              "The service-wide daily cap spans different networks");
    }
    using (var quota = new GuestQuota(options, clock))
    {
        Check(quota.Reserve(id, network).Error?.Code == GuestProtocol.LimitReached, "Counters survive a server restart");
        clock.Now = clock.Now.AddDays(1);
        Check(quota.GetUsage(id).Remaining == 2 && quota.Reserve(id, network).Error is null &&
              quota.Reserve(id, network).Error?.Code == GuestProtocol.CapacityReached,
              "A UTC day resets the guest quota while retaining the monthly cap");
        clock.Now = clock.Now.AddDays(1);
        Check(quota.Reserve(id, network).Error is null, "A new UTC month resets the service allowance");
    }

    var api = new SimulatedOpenAi();
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["OPENAI_API_KEY"] = "local-test-fixture",
        ["Guest:DataDirectory"] = Path.Combine(root, "http"),
        ["Guest:RequestsPerGuestPerDay"] = "2",
        ["Guest:RequestsPerNetworkPerDay"] = "4"
    });
    GuestApi.Configure(builder);
    builder.Services.AddSingleton<TimeProvider>(clock);
    builder.Services.AddHttpClient(GuestApi.OpenAiClient)
        .ConfigurePrimaryHttpMessageHandler(() => api);
    await using var app = builder.Build();
    GuestApi.Map(app);
    await app.StartAsync();
    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    var guest = new GuestClient(http, http.BaseAddress, () => id);
    var english = ConversationLanguage.Get("en");
    var german = ConversationLanguage.Get("de");
    var image = new ImageAttachment("sample.png", Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0ZsAAAAASUVORK5CYII="));
    await guest.RefreshUsageAsync(default);
    Check(guest.CurrentUsage is { Remaining: 2 } && api.Calls == 0, "Reading the allowance never calls OpenAI");

    using (var bad = new HttpRequestMessage(HttpMethod.Post, "v1/guest/translate"))
    {
        bad.Headers.Add(GuestProtocol.InstallationHeader, id.ToString("N"));
        bad.Content = JsonContent.Create(new { text = "Hello", first = "en", second = "de", transliteration = false, model = "other-model" });
        using var rejected = await http.SendAsync(bad);
        Check(rejected.StatusCode == HttpStatusCode.BadRequest && api.Calls == 0, "Client-supplied model overrides are rejected before the upstream call");
    }

    using (var response = await guest.TranslateAsync("Hello", english, german, image, false, default))
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var completion = await ResponseStream.ReadCompletionAsync(reader, default);
        var translation = TranslationResult.Parse(completion.Text, hasImage: true, sourceLanguage: english, targetLanguage: german);
        Check(translation.Translation == "Hallo" && translation.ImageText?.Translation == "Eingang" &&
              guest.CurrentUsage is { Remaining: 1 } && api.ImageReceived && api.OutputLimit == 8192 && !api.SearchEnabled,
              "Guest translation carries photo text and consumes one request");
    }
    var searchStarted = false;
    using (var response = await guest.AskAsync("What did I ask?", english, german,
        [new AiContextMessage("user", "Earlier question")], null, true, default))
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var completion = await ResponseStream.ReadCompletionAsync(reader, default, onSearching: () => searchStarted = true);
        var answer = AiReply.Parse(completion.Text, true, english, german);
        Check(answer.SecondAnswer == "Antwort" && completion.WebSearchUsed && searchStarted &&
              completion.Citations.Count == 1 && api.HistoryReceived && api.SearchEnabled && api.OutputLimit == 4096 &&
              api.ToolLimit == 1 && guest.LimitReached, "Guest AI keeps history and streams bilingual web results within its limits");
    }
    var prompted = false;
    try { using var ignored = await guest.TranslateAsync("Again", english, german, null, false, default); }
    catch (GuestLimitException) { prompted = true; }
    Check(prompted && api.Calls == 2, "Quota exhaustion reaches the app's sign-in path without an extra API call");

    api.FailNextRequest = true;
    var secondGuest = new GuestClient(http, http.BaseAddress, Guid.NewGuid);
    var sanitizedError = false;
    try { using var ignored = await secondGuest.TranslateAsync("Retry later", english, german, null, false, default); }
    catch (InvalidOperationException ex)
    {
        sanitizedError = ex.Message == "The guest service could not complete the request. Try again or connect ChatGPT.";
    }
    Check(sanitizedError && secondGuest.CurrentUsage is { Remaining: 1 },
        "Upstream errors are redacted and the attempted request stays reserved");
    Check(api.Calls == 3 && api.FixedUpstream, "Only the configured OpenAI Responses endpoint receives the server credential");
    await app.StopAsync();
}
finally
{
    foreach (var directory in temporaryDirectory.EnumerateDirectories())
    {
        foreach (var file in directory.EnumerateFiles()) file.Delete();
        directory.Delete();
    }
    temporaryDirectory.Delete();
}

sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class SimulatedOpenAi : HttpMessageHandler
{
    public int Calls { get; private set; }
    public int OutputLimit { get; private set; }
    public int ToolLimit { get; private set; }
    public bool FixedUpstream { get; private set; } = true;
    public bool ImageReceived { get; private set; }
    public bool SearchEnabled { get; private set; }
    public bool HistoryReceived { get; private set; }
    public bool FailNextRequest { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        FixedUpstream &= request.RequestUri?.AbsoluteUri == "https://api.openai.com/v1/responses" &&
            request.Headers.Authorization?.Parameter == "local-test-fixture";
        using var document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct));
        var payload = document.RootElement;
        FixedUpstream &= payload.GetProperty("model").GetString() == "gpt-6-luna" && !payload.GetProperty("store").GetBoolean();
        OutputLimit = payload.GetProperty("max_output_tokens").GetInt32();
        ToolLimit = payload.TryGetProperty("max_tool_calls", out var tools) ? tools.GetInt32() : 0;
        ImageReceived = payload.GetRawText().Contains("data:image/png;base64,", StringComparison.Ordinal);
        HistoryReceived = payload.GetProperty("input").GetArrayLength() == 2;
        SearchEnabled = payload.TryGetProperty("tools", out _);
        if (FailNextRequest)
        {
            FailNextRequest = false;
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("upstream-secret-error") };
        }

        var text = SearchEnabled
            ? """{"input_language":"en","first_language":"en","first_answer":"Answer","second_answer":"Antwort","first_transliteration":"","second_transliteration":""}"""
            : """{"corrected_original":"Hello","translation":"Hallo","source_transliteration":"","target_transliteration":"","image_text_found":true,"image_source":"en","image_target":"de","image_original":"Entrance","image_translation":"Eingang","image_source_transliteration":"","image_target_transliteration":""}""";
        var annotations = SearchEnabled ? new[] { new { type = "url_citation", title = "Example source", url = "https://example.org/" } } : [];
        var completed = new
        {
            type = "response.completed",
            response = new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text, annotations } } } } }
        };
        var events = new StringBuilder();
        if (SearchEnabled)
            events.Append("data: {\"type\":\"response.web_search_call.searching\"}\n\ndata: {\"type\":\"response.web_search_call.completed\"}\n\n");
        events.Append("data: ").Append(JsonSerializer.Serialize(completed)).Append("\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(events.ToString(), Encoding.UTF8, "text/event-stream") };
    }
}
