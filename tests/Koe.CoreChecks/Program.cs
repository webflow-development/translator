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
Console.WriteLine($"{passed} checks passed.");

sealed class KeyHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
}
