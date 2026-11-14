using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Koe.Services;

public sealed class OpenAiTranslator(HttpClient http, ChatGptAuth auth)
{
    readonly ModelSelectionCache models = new();

    public async Task<TranslationResult> TranslateAsync(string text, ConversationLanguage source, ConversationLanguage target, CancellationToken ct, ImageAttachment? image = null, Action<TranslationPreview>? onPreview = null, bool includeTransliteration = true, string? modelId = null)
    {
        var token = await auth.ValidAccessTokenAsync(ct);
        var catalog = await GetModelsAsync(token, ct);
        var model = TranslationModel.Resolve(catalog, modelId);
        var payload = TranslationRequest.Build(model.Id, text, source, target, image, includeTransliteration);
        var json = await CompleteAsync(token, payload, ct, onPreview);
        return TranslationResult.Parse(json.Text, image is not null, !string.IsNullOrWhiteSpace(text), includeTransliteration, source, target);
    }

    public async Task<AiReply> AskAsync(string question, ConversationLanguage first, ConversationLanguage second,
        IEnumerable<AiContextMessage> history, CancellationToken ct, ImageAttachment? image = null,
        Action<TranslationPreview>? onPreview = null, bool includeTransliteration = true, string? modelId = null, Action? onSearching = null)
    {
        var token = await auth.ValidAccessTokenAsync(ct);
        var catalog = await GetModelsAsync(token, ct);
        var model = TranslationModel.Resolve(catalog, modelId);
        var payload = AiChatRequest.Build(model.Id, question, first, second, history, image, includeTransliteration);
        var result = await CompleteAsync(token, payload, ct, onPreview, "first_answer", "second_answer", onSearching, usesWebSearch: true);
        var reply = AiReply.Parse(result.Text, includeTransliteration, first, second);
        return reply with
        {
            WebSearchUsed = result.WebSearchUsed, Citations = result.Citations,
            FirstAnswer = WebCitation.CleanAnswer(reply.FirstAnswer, result.Citations),
            SecondAnswer = WebCitation.CleanAnswer(reply.SecondAnswer, result.Citations)
        };
    }

    async Task<ResponseCompletion> CompleteAsync(string token, byte[] payload, CancellationToken ct,
        Action<TranslationPreview>? onPreview, string firstField = "translation", string secondField = "image_translation", Action? onSearching = null, bool usesWebSearch = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        { Content = new StringContent(Encoding.UTF8.GetString(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode is 401 or 403 or 404) models.Clear();
        EnsureSuccess(response, usesWebSearch);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var last = new TranslationPreview("", "");
        var result = await ResponseStream.ReadCompletionAsync(reader, ct, partial =>
        {
            if (onPreview is null) return;
            var preview = TranslationPreview.Read(partial, firstField, secondField);
            if (preview == last) return;
            last = preview; onPreview(preview);
        }, onSearching);
        return result;
    }

    public async Task<TranslationModel[]> GetModelsAsync(CancellationToken ct)
        => await GetModelsAsync(await auth.ValidAccessTokenAsync(ct), ct);

    async Task<TranslationModel[]> GetModelsAsync(string token, CancellationToken ct)
    {
        var credentialKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return TranslationModel.ParseCatalog(await models.GetAsync(credentialKey, () => LoadCatalogAsync(token, ct), ct));
    }

    public void ClearModels() => models.Clear();

    async Task<string> LoadCatalogAsync(string token, CancellationToken ct)
    {
        using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        catalogRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var catalogResponse = await http.SendAsync(catalogRequest, ct);
        EnsureSuccess(catalogResponse);
        var json = await catalogResponse.Content.ReadAsStringAsync(ct);
        _ = TranslationModel.ParseCatalog(json);
        return json;
    }

    static void EnsureSuccess(HttpResponseMessage response, bool usesWebSearch = false)
    {
        if (response.IsSuccessStatusCode) return;
        var message = (int)response.StatusCode switch
        {
            401 => "Your sign-in has expired. Please sign out and reconnect.",
            400 when usesWebSearch => "The AI request with web search was rejected. Try another model.",
            403 when usesWebSearch => "Your selected model or account does not allow this request with web search. Try another model or check account access.",
            403 => "Your account does not have access to this request.",
            429 => "Usage limit reached. Try again later or check your ChatGPT usage.",
            _ => $"The ChatGPT request failed (HTTP {(int)response.StatusCode})."
        };
        throw new InvalidOperationException(message);
    }
}
