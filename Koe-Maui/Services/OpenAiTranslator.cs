using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Koe.Services;

public sealed class OpenAiTranslator(HttpClient http, ChatGptAuth auth)
{
    readonly ModelSelectionCache models = new();

    public async Task<TranslationResult> TranslateAsync(string text, ConversationLanguage source, ConversationLanguage target, CancellationToken ct, ImageAttachment? image = null, Action<TranslationPreview>? onPreview = null, bool includeTransliteration = true)
    {
        var token = await auth.ValidAccessTokenAsync(ct);
        var credentialKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var model = await models.GetAsync(credentialKey, () => LoadModelAsync(token, ct), ct);
        var payload = TranslationRequest.Build(model, text, source, target, image, includeTransliteration);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        { Content = new StringContent(Encoding.UTF8.GetString(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode is 401 or 403 or 404) models.Clear();
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var last = new TranslationPreview("", "");
        var json = await ResponseStream.ReadAsync(reader, ct, partial =>
        {
            if (onPreview is null) return;
            var preview = TranslationPreview.Read(partial);
            if (preview == last) return;
            last = preview; onPreview(preview);
        });
        return TranslationResult.Parse(json, image is not null, !string.IsNullOrWhiteSpace(text), includeTransliteration);
    }

    async Task<string> LoadModelAsync(string token, CancellationToken ct)
    {
        using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        catalogRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var catalogResponse = await http.SendAsync(catalogRequest, ct);
        EnsureSuccess(catalogResponse);
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync(ct));
        var model = catalog.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m =>
            m.TryGetProperty("visibility", out var visibility) && visibility.GetString() == "list");
        if (model.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("No available model was found for this account.");
        return model.GetProperty("slug").GetString()!;
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = (int)response.StatusCode switch
        {
            401 => "Your sign-in has expired. Please sign out and reconnect.",
            403 => "Your account does not have access to this request.",
            429 => "Usage limit reached. Try again later or check your ChatGPT usage.",
            _ => $"The translation request failed (HTTP {(int)response.StatusCode})."
        };
        throw new InvalidOperationException(message);
    }
}
