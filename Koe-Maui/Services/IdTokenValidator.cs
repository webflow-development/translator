using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Koe.Services;

internal sealed record VerifiedIdentity(string Subject, string? Email);

internal static class IdTokenValidator
{
    public static async Task<VerifiedIdentity> ValidateAsync(HttpClient http, string token, string clientId, string? nonce, CancellationToken ct)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw InvalidToken();
        using var header = JsonDocument.Parse(Decode(parts[0]));
        if (header.RootElement.GetProperty("alg").GetString() != "RS256") throw InvalidToken();
        var kid = header.RootElement.GetProperty("kid").GetString();
        using var keys = JsonDocument.Parse(await http.GetStringAsync("https://auth.openai.com/.well-known/jwks.json", ct));
        var jwk = keys.RootElement.GetProperty("keys").EnumerateArray().FirstOrDefault(k =>
            k.GetProperty("kid").GetString() == kid && k.GetProperty("kty").GetString() == "RSA");
        if (jwk.ValueKind == JsonValueKind.Undefined) throw InvalidToken();
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = Decode(jwk.GetProperty("n").GetString()!), Exponent = Decode(jwk.GetProperty("e").GetString()!) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw InvalidToken();
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        var root = claims.RootElement;
        var audience = root.GetProperty("aud");
        var correctAudience = audience.ValueKind == JsonValueKind.String ? audience.GetString() == clientId :
            audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(a => a.GetString() == clientId);
        if (root.GetProperty("iss").GetString() != "https://auth.openai.com" || !correctAudience ||
            root.GetProperty("exp").GetDouble() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            (root.TryGetProperty("nbf", out var nbf) && nbf.GetDouble() > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ||
            (nonce is not null && (!root.TryGetProperty("nonce", out var n) || n.GetString() != nonce))) throw InvalidToken();
        var subject = root.GetProperty("sub").GetString();
        if (string.IsNullOrEmpty(subject)) throw InvalidToken();
        return new(subject, root.TryGetProperty("email", out var email) ? email.GetString() : null);
    }

    internal static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
    static InvalidOperationException InvalidToken() => new("Could not verify the ChatGPT account identity.");
}
