using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koe.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

namespace Koe.GuestServer;

internal sealed class GuestApi(GuestOptions options, GuestQuota quota, IHttpClientFactory clients,
    string apiKey, ILogger<GuestApi> logger) : IDisposable
{
    internal const string OpenAiClient = "KoeGuestOpenAI";
    const int TranslationOutputTokens = 8192;
    const int AiOutputTokens = 4096;
    readonly SemaphoreSlim concurrentRequests = new(options.ConcurrentRequests);

    public static void Configure(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Guest").Get<GuestOptions>() ?? new GuestOptions();
        options.Validate();
        var apiKey = builder.Configuration["OPENAI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Set OPENAI_API_KEY in the server's secret store.");

        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = GuestProtocol.MaximumRequestBytes);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<GuestQuota>();
        builder.Services.AddHttpClient(OpenAiClient, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton(services => new GuestApi(options, services.GetRequiredService<GuestQuota>(),
            services.GetRequiredService<IHttpClientFactory>(), apiKey, services.GetRequiredService<ILogger<GuestApi>>()));
    }

    public static void Map(WebApplication app)
    {
        var api = app.Services.GetRequiredService<GuestApi>();
        if (app.Services.GetRequiredService<GuestOptions>().TrustedProxyIp is { } address)
        {
            var forwarding = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
            forwarding.KnownIPNetworks.Clear();
            forwarding.KnownProxies.Clear();
            forwarding.KnownProxies.Add(IPAddress.Parse(address));
            app.UseForwardedHeaders(forwarding);
        }

        app.MapGet("/health", () => Results.Text("ok"));
        app.MapGet("/v1/guest/usage", api.GetUsageAsync);
        app.MapPost("/v1/guest/translate", (HttpContext context) => api.HandleAsync(context, ask: false));
        app.MapPost("/v1/guest/ask", (HttpContext context) => api.HandleAsync(context, ask: true));
    }

    async Task GetUsageAsync(HttpContext context)
    {
        if (!TryGuest(context, out var id))
        {
            await WriteErrorAsync(context, 400, "invalid_guest", "A valid Koe installation identifier is required.");
            return;
        }

        var usage = quota.GetUsage(id);
        WriteUsageHeaders(context, usage);
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, usage, GuestJsonContext.Default.GuestUsage, context.RequestAborted);
    }

    async Task HandleAsync(HttpContext context, bool ask)
    {
        if (!TryGuest(context, out var id))
        {
            await WriteErrorAsync(context, 400, "invalid_guest", "A valid Koe installation identifier is required.");
            return;
        }
        if (!context.Request.HasJsonContentType())
        {
            await WriteErrorAsync(context, 415, "invalid_content_type", "Send the message as JSON.");
            return;
        }
        if (context.Request.ContentLength > GuestProtocol.MaximumRequestBytes)
        {
            await WriteErrorAsync(context, 413, "message_too_large", "This message is too large. Use a smaller photo or connect ChatGPT.");
            return;
        }
        if (!concurrentRequests.Wait(0))
        {
            await WriteErrorAsync(context, 503, "guest_busy", "The guest service is busy. Try again in a moment.");
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var ct = deadline.Token;
        try
        {
            byte[] payload;
            try
            {
                var request = await JsonSerializer.DeserializeAsync(context.Request.Body, GuestJsonContext.Default.GuestRequest, ct);
                payload = BuildPayload(request, ask);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
            {
                await WriteErrorAsync(context, 413, "message_too_large", "This message is too large. Use a smaller photo or connect ChatGPT.");
                return;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                await WriteErrorAsync(context, 400, "invalid_message", "Koe could not read this message or its photo. Edit it and try again.");
                return;
            }

            ct.ThrowIfCancellationRequested();
            var reservation = quota.Reserve(id, context.Connection.RemoteIpAddress);
            WriteUsageHeaders(context, reservation.Usage);
            if (reservation.Error is { } error)
            {
                await WriteErrorAsync(context, 429, error.Code, error.Message);
                return;
            }

            using var client = clients.CreateClient(OpenAiClient);
            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            upstreamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            upstreamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            upstreamRequest.Content = new ByteArrayContent(payload);
            upstreamRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var upstream = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!upstream.IsSuccessStatusCode || upstream.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                logger.LogWarning("OpenAI did not start a guest response. HTTP {StatusCode}.", (int)upstream.StatusCode);
                await WriteErrorAsync(context, 502, "guest_unavailable", "The guest service could not complete the request. Try again or connect ChatGPT.");
                return;
            }

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await using var stream = await upstream.Content.ReadAsStreamAsync(ct);
            await context.Response.StartAsync(ct);
            await stream.CopyToAsync(context.Response.Body, ct);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            await FailAsync(context, 504, "The guest request timed out. Your message can be retried.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("OpenAI guest connection failed. Error {Error}.", ex.HttpRequestError);
            await FailAsync(context, 502, "The guest service could not reach OpenAI. Try again or connect ChatGPT.");
        }
        catch (IOException)
        {
            logger.LogWarning("The guest service could not persist usage or stream the reply.");
            await FailAsync(context, 503, "The guest service is temporarily unavailable. Try again or connect ChatGPT.");
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("The guest usage directory is not writable.");
            await FailAsync(context, 503, "The guest service is temporarily unavailable. Try again or connect ChatGPT.");
        }
        finally { concurrentRequests.Release(); }
    }

    byte[] BuildPayload(GuestRequest? request, bool ask)
    {
        if (request is null || request.Text is null || request.Text.Length > GuestProtocol.MaximumMessageCharacters)
            throw new ArgumentException("Invalid message.");
        var first = ReadLanguage(request.First);
        var second = ReadLanguage(request.Second);
        var image = ReadImage(request.Image);

        if (!ask)
        {
            if (request.History is { Length: > 0 }) throw new ArgumentException("Unexpected translation history.");
            return TranslationRequest.Build(options.Model, request.Text, first, second, image, request.Transliteration, TranslationOutputTokens);
        }

        var history = request.History ?? [];
        if (history.Length > AiChatRequest.MaximumHistoryMessages) throw new ArgumentException("Too many previous messages.");
        var context = new List<AiContextMessage>(history.Length);
        var characters = 0;
        var hasHistoryImage = false;
        foreach (var previous in history)
        {
            if (previous is null || previous.Text is null || previous.Role is not ("user" or "assistant"))
                throw new ArgumentException("Invalid previous message.");
            characters += previous.Text.Length;
            if (characters > AiChatRequest.MaximumHistoryCharacters) throw new ArgumentException("Previous messages are too long.");
            if (previous.Image is not null)
            {
                if (hasHistoryImage || previous.Role != "user") throw new ArgumentException("Invalid previous photo.");
                hasHistoryImage = true;
            }
            context.Add(new AiContextMessage(previous.Role, previous.Text, ReadImage(previous.Image)));
        }
        return AiChatRequest.Build(options.Model, request.Text, first, second, context, image, request.Transliteration, AiOutputTokens, maxToolCalls: 1);
    }

    static ConversationLanguage ReadLanguage(string? code)
    {
        if (code is null || (code != "de-CH" && (code.Length is < 2 or > 3 || code.Any(character => character is < 'a' or > 'z'))))
            throw new ArgumentException("Invalid language code.");
        return ConversationLanguage.All.FirstOrDefault(language => language.Code == code)
            ?? new ConversationLanguage(code, CultureInfo.GetCultureInfo(code).EnglishName, code);
    }

    static ImageAttachment? ReadImage(byte[]? bytes) => bytes is null ? null : new ImageAttachment("photo", bytes);

    static bool TryGuest(HttpContext context, out Guid id)
    {
        id = Guid.Empty;
        return context.Request.Headers.TryGetValue(GuestProtocol.InstallationHeader, out var header) && header.Count == 1 &&
            Guid.TryParseExact(header[0], "N", out id) && id != Guid.Empty;
    }

    static void WriteUsageHeaders(HttpContext context, GuestUsage usage)
    {
        var headers = context.Response.Headers;
        headers.CacheControl = "no-store";
        headers[GuestProtocol.LimitHeader] = usage.DailyLimit.ToString(CultureInfo.InvariantCulture);
        headers[GuestProtocol.RemainingHeader] = usage.Remaining.ToString(CultureInfo.InvariantCulture);
        headers[GuestProtocol.ResetHeader] = usage.ResetsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        headers[GuestProtocol.ModelHeader] = usage.Model;
    }

    static async Task WriteErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new GuestError(code, message), GuestJsonContext.Default.GuestError, context.RequestAborted);
    }

    static Task FailAsync(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
            return Task.CompletedTask;
        }
        return WriteErrorAsync(context, status, "guest_unavailable", message);
    }

    public void Dispose() => concurrentRequests.Dispose();
}
