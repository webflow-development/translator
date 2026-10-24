using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koe.Services;

var passed = 0;
async Task Check(string name, Func<Task> test)
{
    await test(); passed++; Console.WriteLine("PASS " + name);
}
async Task Reject(Func<Task> test)
{
    try { await test(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected rejection.");
}
Task<string> Read(string s, CancellationToken ct = default) => ResponseStream.ReadAsync(new StringReader(s), ct);
await Check("SSE combines Unicode deltas and waits for completion", async () =>
{
    var result = await Read(": keepalive\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"こん\"}\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"にちは\"}\n\ndata: {\"type\":\"response.completed\"}\n\n");
    if (result != "こんにちは") throw new Exception(result);
});
await Check("SSE rejects truncated output", () => Reject(async () => { await Read("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"); }));
await Check("SSE rejects incomplete response", () => Reject(async () => { await Read("data: {\"type\":\"response.incomplete\"}\n\n"); }));
await Check("SSE rejects empty translation", () => Reject(async () => { await Read("data: {\"type\":\"response.completed\"}\n\n"); }));
await Check("SSE processes final event without blank terminator", async () =>
{
    if (await Read("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hallo\"}\n\ndata: {\"type\":\"response.completed\"}") != "Hallo") throw new Exception();
});
using var rsa = RSA.Create(2048);
var parameters = rsa.ExportParameters(false);
string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
using var http = new HttpClient(new KeyHandler(JsonSerializer.Serialize(new { keys = new[] { new { kid = "test-key", kty = "RSA", n = B64(parameters.Modulus!), e = B64(parameters.Exponent!) } } })));
string Token(string aud = "koe-client", string nonce = "nonce", long? exp = null, string iss = "https://auth.openai.com")
{
    var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "test-key" }));
    var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new { iss, aud, sub = "subject", email = "test@example.invalid", nonce, exp = exp ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }));
    var signed = header + "." + claims;
    return signed + "." + B64(rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
await Check("OIDC accepts valid signed identity", async () =>
{
    var identity = await IdTokenValidator.ValidateAsync(http, Token(), "koe-client", "nonce", default);
    if (identity.Subject != "subject") throw new Exception();
});
await Check("OIDC rejects wrong audience", () => Reject(async () => { await IdTokenValidator.ValidateAsync(http, Token(aud: "other"), "koe-client", "nonce", default); }));
await Check("OIDC rejects wrong nonce", () => Reject(async () => { await IdTokenValidator.ValidateAsync(http, Token(nonce: "other"), "koe-client", "nonce", default); }));
await Check("OIDC rejects expired token", () => Reject(async () => { await IdTokenValidator.ValidateAsync(http, Token(exp: 1), "koe-client", "nonce", default); }));
await Check("OIDC rejects wrong issuer", () => Reject(async () => { await IdTokenValidator.ValidateAsync(http, Token(iss: "https://invalid.example"), "koe-client", "nonce", default); }));
await Check("OIDC rejects modified signature", () => Reject(async () =>
{
    var token = Token(); var pieces = token.Split('.');
    var signature = IdTokenValidator.Decode(pieces[2]); signature[0] ^= 1;
    await IdTokenValidator.ValidateAsync(http, pieces[0] + "." + pieces[1] + "." + B64(signature), "koe-client", "nonce", default);
}));
await Check("Translation preserves Japanese, romaji and escaped newlines separately", () =>
{
    var result = TranslationResult.Parse("{\"translation\":\"こんにちは。\\nお元気ですか？\",\"romaji\":\"Konnichiwa. Ogenki desu ka?\"}");
    if (result.Translation != "こんにちは。\nお元気ですか？" || result.Romaji != "Konnichiwa. Ogenki desu ka?") throw new Exception();
    return Task.CompletedTask;
});
await Check("Japanese input can return German translation with original romaji", () =>
{
    var result = TranslationResult.Parse("{\"translation\":\"Danke.\",\"romaji\":\"Arigatō.\"}");
    if (result.Translation != "Danke." || result.Romaji != "Arigatō.") throw new Exception();
    return Task.CompletedTask;
});
foreach (var invalid in new[] { "not JSON", "{\"translation\":\"こんにちは\"}", "{\"translation\":\"\",\"romaji\":\"konnichiwa\"}", "{\"translation\":\"こんにちは\",\"romaji\":42}", "[]" })
    await Check("Malformed or incomplete translation is rejected", () => Reject(() => { TranslationResult.Parse(invalid); return Task.CompletedTask; }));
await Check("Translation JSON can be reconstructed from split streaming deltas", async () =>
{
    var fragments = new[] { "{\"translation\":\"こん", "にちは\",\"romaji\":\"Konnichiwa\"}" };
    var events = string.Concat(fragments.Select(delta => "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta }) + "\n\n"));
    events += "data: {\"type\":\"response.completed\"}\n\n";
    if (TranslationResult.Parse(await Read(events)).Translation != "こんにちは") throw new Exception();
});
await Check("Pause retains the complete sentence and stops recording", async () =>
{
    var speech = new FakeSpeech(); var draft = new SpeechDraft(speech);
    await draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    speech.Emit("Ich möchte"); speech.FinalText = "Ich möchte bestellen.";
    await draft.PauseAsync();
    if (draft.State != RecordingState.Paused || draft.Text != "Ich möchte bestellen." || speech.IsRecording) throw new Exception();
});
await Check("Resume appends to corrected text and finishing does not restart capture", async () =>
{
    var speech = new FakeSpeech(); var draft = new SpeechDraft(speech);
    var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
    await draft.StartAsync(culture); speech.Emit("Wein"); await draft.PauseAsync();
    draft.Edit("Wasser bitte."); await draft.StartAsync(culture); speech.Emit("Ohne Eis.");
    var text = await draft.FinishAsync();
    if (text != "Wasser bitte. Ohne Eis." || draft.State != RecordingState.Ready || speech.IsRecording || speech.StartCount != 2) throw new Exception(text);
});
await Check("A late final transcript cannot undo a manual correction", async () =>
{
    var speech = new FakeSpeech { FinalGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var draft = new SpeechDraft(speech);
    await draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE")); speech.Emit("Falscher Text");
    var pausing = draft.PauseAsync();
    draft.Edit("Korrigierter Text"); speech.FinalGate.SetResult("Falscher Text mit Satzende");
    await pausing;
    if (draft.Text != "Korrigierter Text") throw new Exception(draft.Text);
});
await Check("Recognition callbacks while paused cannot resurrect deleted text", async () =>
{
    var speech = new FakeSpeech(); var draft = new SpeechDraft(speech);
    var culture = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
    await draft.StartAsync(culture); speech.Emit("古い文"); await draft.PauseAsync();
    draft.Edit(""); speech.Emit("古い文が復活");
    if (draft.Text != "") throw new Exception();
    await draft.StartAsync(culture); speech.Emit("新しい文");
    if (await draft.FinishAsync() != "新しい文") throw new Exception();
});
await Check("Editing before pause also protects corrections from final recognition", async () =>
{
    var speech = new FakeSpeech(); var draft = new SpeechDraft(speech);
    await draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    speech.Emit("Falsch"); draft.Edit("Richtig");
    await draft.PauseAsync();
    if (draft.Text != "Richtig") throw new Exception(draft.Text);
});
await Check("Permission or start failure preserves the editable draft", async () =>
{
    var speech = new FakeSpeech { FailStart = true }; var draft = new SpeechDraft(speech);
    draft.Edit("Bitte behalten.");
    await Reject(() => draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
    if (draft.HasTurn || speech.IsRecording || draft.Text != "Bitte behalten.") throw new Exception();
});
await Check("Cancelling a pause preserves partial text and releases the microphone", async () =>
{
    var speech = new FakeSpeech { FinalGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var draft = new SpeechDraft(speech);
    await draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE")); speech.Emit("Behalten");
    using var cancellation = new CancellationTokenSource();
    var pausing = draft.PauseAsync(cancellation.Token); cancellation.Cancel();
    try { await pausing; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
    if (draft.Text != "Behalten" || draft.State != RecordingState.Paused || speech.IsRecording) throw new Exception();
});
await Check("Repeated start during recording never starts a second microphone session", async () =>
{
    var speech = new FakeSpeech(); var draft = new SpeechDraft(speech);
    var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
    await draft.StartAsync(culture); await draft.StartAsync(culture);
    if (speech.StartCount != 1) throw new Exception();
    await draft.FinishAsync();
    if (draft.HasTurn || speech.StartCount != 1) throw new Exception();
});
var photoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==");
var photo = new ImageAttachment("menu.jpg", photoBytes);
await Check("Image media type comes from bytes, not an untrusted filename", () =>
{
    if (photo.MediaType != "image/png" || !photo.DataUrl.StartsWith("data:image/png;base64,")) throw new Exception();
    return Task.CompletedTask;
});
await Check("Image and spoken text are sent in the same Responses message", () =>
{
    using var request = JsonDocument.Parse(TranslationRequest.Build("test-model", "Welches ist vegetarisch?", ConversationLanguage.Get("de"), ConversationLanguage.Get("ja"), photo));
    var root = request.RootElement;
    var content = root.GetProperty("input")[0].GetProperty("content");
    if (content.GetArrayLength() != 2 || content[0].GetProperty("text").GetString() != "Welches ist vegetarisch?" ||
        content[1].GetProperty("type").GetString() != "input_image" || content[1].GetProperty("image_url").GetString() != photo.DataUrl ||
        root.GetProperty("store").GetBoolean() || !root.GetProperty("stream").GetBoolean()) throw new Exception();
    return Task.CompletedTask;
});
await Check("Image-only request does not fabricate a spoken message", () =>
{
    using var request = JsonDocument.Parse(TranslationRequest.Build("test-model", "", ConversationLanguage.Get("ja"), ConversationLanguage.Get("de"), photo));
    var content = request.RootElement.GetProperty("input")[0].GetProperty("content");
    if (content.GetArrayLength() != 1 || content[0].GetProperty("type").GetString() != "input_image") throw new Exception();
    return Task.CompletedTask;
});
await Check("Text-only requests contain no image payload", () =>
{
    using var request = JsonDocument.Parse(TranslationRequest.Build("test-model", "Hallo", ConversationLanguage.Get("de"), ConversationLanguage.Get("ja")));
    if (request.RootElement.GetProperty("input")[0].GetProperty("content").GetArrayLength() != 1) throw new Exception();
    return Task.CompletedTask;
});
await Check("Empty requests are rejected", () => Reject(() => { TranslationRequest.Build("test-model", " ", ConversationLanguage.Get("de"), ConversationLanguage.Get("ja")); return Task.CompletedTask; }));
await Check("Renamed non-image files are rejected", () => Reject(() => { new ImageAttachment("fake.jpg", Encoding.UTF8.GetBytes("%PDF-1.7 document")); return Task.CompletedTask; }));
await Check("Empty image files are rejected", () => Reject(() => { new ImageAttachment("empty.png", []); return Task.CompletedTask; }));
await Check("Oversized image uploads are rejected before sending", () => Reject(() => { new ImageAttachment("large.png", new byte[ImageAttachment.MaxImageBytes + 1]); return Task.CompletedTask; }));
await Check("Source reading honors its size bound", async () =>
{
    using var source = new MemoryStream(new byte[ImageAttachment.MaxSourceBytes + 1]);
    await Reject(async () => { await ImageAttachment.ReadSourceAsync(source); });
});
await Check("Image OCR and speech translations retain independent directions", () =>
{
    var json = """
        {"translation":"どれがベジタリアン料理ですか？","romaji":"Dore ga bejitarian ryōri desu ka?","image_text_found":true,"image_source":"ja","image_target":"de","image_original":"野菜カレー","image_translation":"Gemüsecurry","image_romaji":"Yasai karē"}
        """;
    var result = TranslationResult.Parse(json, hasImage: true);
    if (result.ImageText is not { Source: "ja", Target: "de", Translation: "Gemüsecurry", Romaji: "Yasai karē" } || result.Translation != "どれがベジタリアン料理ですか？") throw new Exception();
    return Task.CompletedTask;
});
await Check("An image without legible text is represented without invented OCR", () =>
{
    var result = TranslationResult.Parse("{\"translation\":\"\",\"romaji\":\"\",\"image_text_found\":false,\"image_source\":\"ja\",\"image_target\":\"de\"}", hasImage: true, hasText: false);
    if (result.ImageText is not null) throw new Exception();
    return Task.CompletedTask;
});
await Check("Missing image reading is rejected rather than dropping source text", () => Reject(() =>
{
    TranslationResult.Parse("{\"translation\":\"Danke\",\"romaji\":\"Arigatō\",\"image_text_found\":true,\"image_source\":\"ja\",\"image_target\":\"de\",\"image_original\":\"猫\",\"image_translation\":\"Katze\"}", hasImage: true);
    return Task.CompletedTask;
}));
foreach (var source in ConversationLanguage.All)
foreach (var target in ConversationLanguage.All.Where(x => x.Code != source.Code))
await Check($"Language pair {source.Code} to {target.Code} configures translation", () =>
{
    using var request = JsonDocument.Parse(TranslationRequest.Build("test", "hello", source, target));
    var instructions = request.RootElement.GetProperty("instructions").GetString()!;
    if (!instructions.Contains($"from {source.Name} to {target.Name}")) throw new Exception();
    if (string.IsNullOrWhiteSpace(System.Globalization.CultureInfo.GetCultureInfo(source.Culture).Name)) throw new Exception();
    return Task.CompletedTask;
});
await Check("Non-Japanese translation accepts empty romaji", () =>
{
    var result = TranslationResult.Parse("{\"translation\":\"Hola\",\"romaji\":\"\"}", requiresRomaji: false);
    if (result.Translation != "Hola" || result.Romaji != "") throw new Exception();
    return Task.CompletedTask;
});
await Check("Same-language pairs are rejected", () => Reject(() =>
{
    TranslationRequest.Build("test", "hello", ConversationLanguage.Get("en"), ConversationLanguage.Get("en"));
    return Task.CompletedTask;
}));
await Check("Mandarin image OCR accepts empty romaji", () =>
{
    var result = TranslationResult.Parse("""
    {"translation":"","romaji":"","image_text_found":true,"image_source":"zh","image_target":"en","image_original":"你好","image_translation":"Hello","image_romaji":""}
    """, hasImage: true, hasText: false, requiresRomaji: false);
    if (result.ImageText is not { Source: "zh", Target: "en", Translation: "Hello" }) throw new Exception();
    return Task.CompletedTask;
});
await Check("Sending during recording waits for final words and leaves microphone off", async () =>
{
    var speech = new FakeSpeech { FinalGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var draft = new SpeechDraft(speech);
    await draft.StartAsync(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    speech.Emit("Ein Wasser");
    var finishing = draft.FinishAsync();
    if (finishing.IsCompleted) throw new Exception("Final words were not awaited.");
    speech.FinalGate.SetResult("Ein Wasser ohne Eis bitte.");
    if (await finishing != "Ein Wasser ohne Eis bitte." || draft.HasTurn || speech.IsRecording || speech.StartCount != 1) throw new Exception();
});
Console.WriteLine($"{passed} checks passed.");

sealed class KeyHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
}

sealed class FakeSpeech : ISpeechCapture
{
    public bool IsRecording { get; private set; }
    public int StartCount { get; private set; }
    public bool FailStart { get; init; }
    public string FinalText { get; set; } = "";
    public TaskCompletionSource<string>? FinalGate { get; init; }
    public event Action<string>? TranscriptChanged;
    public event Action<string?>? Stopped;
    public Task StartAsync(System.Globalization.CultureInfo culture)
    {
        if (FailStart) throw new InvalidOperationException("Microphone unavailable");
        StartCount++; IsRecording = true; FinalText = "";
        return Task.CompletedTask;
    }
    public void Emit(string text) { FinalText = text; TranscriptChanged?.Invoke(text); }
    public async Task<string> FinishAsync(CancellationToken ct = default)
    {
        var text = FinalGate is null ? FinalText : await FinalGate.Task.WaitAsync(ct);
        Stop(); return text;
    }
    public void Stop() { IsRecording = false; Stopped?.Invoke(null); }
}
