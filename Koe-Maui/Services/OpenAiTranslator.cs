using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Koe.Services;

public sealed class OpenAiTranslator(HttpClient http, ChatGptAuth auth)
{
    public async Task<TranslationResult> TranslateAsync(string text, bool fromGerman, CancellationToken ct, ImageAttachment? image = null)
    {
        var token = await auth.ValidAccessTokenAsync(ct);
        using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        catalogRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var catalogResponse = await http.SendAsync(catalogRequest, ct);
        EnsureSuccess(catalogResponse);
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync(ct));
        var model = catalog.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m =>
            m.TryGetProperty("visibility", out var visibility) && visibility.GetString() == "list");
        if (model.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Für dieses Konto wurde kein verfügbares Modell gefunden.");
        var payload = TranslationRequest.Build(model.GetProperty("slug").GetString()!, text, fromGerman, image);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        { Content = new StringContent(Encoding.UTF8.GetString(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        return TranslationResult.Parse(await ResponseStream.ReadAsync(reader, ct), image is not null, !string.IsNullOrWhiteSpace(text));
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = (int)response.StatusCode switch
        {
            401 => "Die Anmeldung ist abgelaufen. Bitte abmelden und erneut verbinden.",
            403 => "Dein Konto hat keine Freigabe für diese Anfrage.",
            429 => "Das Nutzungslimit wurde erreicht. Bitte später erneut versuchen oder die Nutzung in ChatGPT prüfen.",
            _ => $"Die Übersetzungsanfrage ist fehlgeschlagen (HTTP {(int)response.StatusCode})."
        };
        throw new InvalidOperationException(message);
    }
}
