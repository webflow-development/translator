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
