using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Koe.Services;

public sealed class GuestLimitException(string message) : InvalidOperationException(message);

public sealed class GuestClient(HttpClient http, Uri? baseAddress, Func<Guid> installationId)
{
    readonly Lazy<Guid> guestId = new(installationId);
    public bool IsConfigured => baseAddress is not null;
    public GuestUsage? Usage { get; private set; }
    public GuestUsage? CurrentUsage => Usage is { } usage && usage.ResetsAt > DateTimeOffset.UtcNow ? usage : null;
    public bool LimitReached => CurrentUsage is { Remaining: 0 };

    public static Uri? ParseAddress(string? value, bool allowLoopback = false)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0 || string.IsNullOrEmpty(uri.Host)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowLoopback && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            return null;
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task RefreshUsageAsync(CancellationToken ct)
    {
        if (!IsConfigured) return;
        using var request = CreateRequest(HttpMethod.Get, "usage");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode)
            ReadUsage(response);
    }

    public Task<HttpResponseMessage> TranslateAsync(string text, ConversationLanguage source, ConversationLanguage target,
        ImageAttachment? image, bool transliteration, CancellationToken ct) =>
        SendAsync("translate", new GuestRequest(text, source.Code, target.Code, transliteration, image?.Bytes), ct);

    public Task<HttpResponseMessage> AskAsync(string question, ConversationLanguage first, ConversationLanguage second,
        IEnumerable<AiContextMessage> history, ImageAttachment? image, bool transliteration, CancellationToken ct)
    {
        var context = AiChatRequest.RecentContext(history)
            .Select(message => new GuestMessage(message.Role, message.Text, message.Image?.Bytes)).ToArray();
        return SendAsync("ask", new GuestRequest(question, first.Code, second.Code, transliteration, image?.Bytes, context), ct);
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string operation)
    {
        if (baseAddress is null) throw new InvalidOperationException("Connect ChatGPT to use Koe.");
        var request = new HttpRequestMessage(method, new Uri(baseAddress, "v1/guest/" + operation));
        request.Headers.Add(GuestProtocol.InstallationHeader, guestId.Value.ToString("N"));
        return request;
    }

    async Task<HttpResponseMessage> SendAsync(string operation, GuestRequest payload, CancellationToken ct)
    {
        if (payload.Text.Length > GuestProtocol.MaximumMessageCharacters)
            throw new InvalidOperationException("This message is too long for guest mode. Shorten it or connect ChatGPT.");
        using var request = CreateRequest(HttpMethod.Post, operation);
        request.Content = JsonContent.Create(payload, GuestJsonContext.Default.GuestRequest);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        ReadUsage(response);
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            return response;

        using (response)
        {
            GuestError? error = null;
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                try { error = await response.Content.ReadFromJsonAsync(GuestJsonContext.Default.GuestError, ct); }
                catch (JsonException) { }
            }
            if (error?.Code is GuestProtocol.LimitReached or GuestProtocol.CapacityReached)
                throw new GuestLimitException(error.Message);
            throw new InvalidOperationException(error?.Message ?? "The guest service is temporarily unavailable. Try again or connect ChatGPT.");
        }
    }

    void ReadUsage(HttpResponseMessage response)
    {
        string? Header(string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        if (int.TryParse(Header(GuestProtocol.LimitHeader), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) &&
            int.TryParse(Header(GuestProtocol.RemainingHeader), NumberStyles.None, CultureInfo.InvariantCulture, out var remaining) &&
            long.TryParse(Header(GuestProtocol.ResetHeader), NumberStyles.None, CultureInfo.InvariantCulture, out var reset) &&
            reset is > 0 and <= 253402300799 && limit > 0 && remaining <= limit && Header(GuestProtocol.ModelHeader) is { } model)
        {
            var resetsAt = DateTimeOffset.FromUnixTimeSeconds(reset);
            if (Usage is { } previous && previous.ResetsAt > resetsAt) return;
            if (Usage is { } sameDay && sameDay.ResetsAt == resetsAt && sameDay.DailyLimit == limit)
                remaining = Math.Min(remaining, sameDay.Remaining);
            Usage = new GuestUsage(limit, remaining, resetsAt, model);
        }
    }
}
