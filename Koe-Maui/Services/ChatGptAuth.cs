using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Koe.Services;

public sealed record Credential(string ClientId, string AccessToken, string RefreshToken, string IdToken,
    string Subject, string? Email, string[] Scopes, DateTimeOffset ExpiresAt);
internal sealed record Registration(string ClientId, string Subject);

public sealed class ChatGptAuth(HttpClient http)
{
    const string StorageKey = "koe.oauth.v1";
    const string RegistrationKey = "koe.registration.v1";
    const string Resource = "https://api.openai.com/v1";
    Credential? current;
    bool loaded;
    public bool IsConnected => current?.Scopes.Contains("chatgpt.tokens.use.direct") == true;
    public string AccountLabel => current?.Email ?? "ChatGPT";

    public async Task LoadAsync()
    {
        if (loaded) return;
        var saved = await SecureStorage.Default.GetAsync(StorageKey);
        current = saved is null ? null : JsonSerializer.Deserialize(saved, KoeJsonContext.Default.Credential);
        loaded = true;
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        await LoadAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        var previous = current;
        var registration = previous is null ? await SecureStorage.Default.GetAsync(RegistrationKey) : null;
        var known = registration is null ? null : JsonSerializer.Deserialize(registration, KoeJsonContext.Default.Registration);
        var oldClientId = previous?.ClientId ?? known?.ClientId;
        var oldSubject = previous?.Subject ?? known?.Subject;
        var hostId = Preferences.Default.Get("koe.host-id", "");
        if (hostId.Length == 0)
        {
            hostId = "urn:uuid:" + Guid.NewGuid().ToString("D");
            Preferences.Default.Set("koe.host-id", hostId);
        }
        var state = RandomValue(); var nonce = RandomValue(); var verifier = RandomValue(48);
        using var listener = new LoopbackCallback(state);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = oldClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = listener.RedirectUri,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)))
        };
        if (oldClientId is null) query["agent_name_hint"] = "Koe";
        if (previous is not null) query["id_token_hint"] = previous.IdToken;
        var uri = new Uri("https://auth.openai.com/api/accounts/authorize?" + Encode(query));
        var callbackTask = listener.WaitAsync(ct);
        Dictionary<string, string> callback;
        try
        {
#if IOS
            // The loopback endpoint redirects into ASWebAuthenticationSession's custom callback.
            var browserTask = WebAuthenticator.Default.AuthenticateAsync(uri, new Uri("koeapp://oauth"));
            var first = await Task.WhenAny(callbackTask, browserTask).WaitAsync(ct);
            if (first == browserTask)
            {
                var result = await browserTask;
                callback = new Dictionary<string, string>(result.Properties);
            }
            else
            {
                callback = await callbackTask;
                // Consume completion/cancellation of the native session without losing validated loopback data.
                _ = ObserveAsync(browserTask);
            }
#else
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            callback = await callbackTask;
#endif
        }
        finally { listener.Dispose(); }
        if (callback.GetValueOrDefault("state") != state) throw new InvalidOperationException("Die Anmeldeantwort konnte nicht sicher zugeordnet werden.");
        if (callback.ContainsKey("error")) throw new InvalidOperationException("Die Anmeldung wurde abgelehnt oder abgebrochen.");
        var clientId = callback.GetValueOrDefault("client_id") ?? oldClientId;
        var code = callback.GetValueOrDefault("code");
        if (string.IsNullOrEmpty(clientId) || clientId == "dynamic_agent_client" || string.IsNullOrEmpty(code))
            throw new InvalidOperationException("OpenAI hat keine vollständige Registrierung geliefert.");
        if (oldClientId is not null && oldClientId != clientId) throw new InvalidOperationException("Die Anmeldung gehört zu einer anderen Registrierung.");
        var tokens = await PostTokensAsync(new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = listener.RedirectUri, ["resource"] = Resource
        }, ct);
        var identity = await IdTokenValidator.ValidateAsync(http, tokens.IdToken, clientId, nonce, ct);
        if (oldSubject is not null && identity.Subject != oldSubject) throw new InvalidOperationException("Die Anmeldung gehört zu einem anderen Konto.");
        await SaveAsync(ToCredential(clientId, tokens, identity, previous));
    }

    public async Task<string> ValidAccessTokenAsync(CancellationToken ct)
    {
        await LoadAsync();
        var credential = current ?? throw new InvalidOperationException("Bitte zuerst mit ChatGPT verbinden.");
        if (!IsConnected) throw new InvalidOperationException("Die Freigabe für ChatGPT-Plan-Nutzung fehlt.");
        if (credential.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(90)) return credential.AccessToken;
        var tokens = await PostTokensAsync(new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = credential.ClientId,
            ["refresh_token"] = credential.RefreshToken, ["resource"] = Resource
        }, ct);
        var identity = await IdTokenValidator.ValidateAsync(http, tokens.IdToken, credential.ClientId, null, ct);
        if (identity.Subject != credential.Subject) throw new InvalidOperationException("Das erneuerte Token gehört zu einem anderen Konto.");
        var refreshed = ToCredential(credential.ClientId, tokens, identity, credential);
        await SaveAsync(refreshed);
        return refreshed.AccessToken;
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        var credential = current;
        try
        {
            if (credential is not null)
            {
                using var discovery = JsonDocument.Parse(await http.GetStringAsync("https://auth.openai.com/.well-known/openid-configuration", ct));
                var endpoint = new Uri(discovery.RootElement.GetProperty("revocation_endpoint").GetString()!);
                if (endpoint.Scheme != "https" || endpoint.Host != "auth.openai.com") throw new InvalidOperationException();
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = credential.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = credential.ClientId
                });
                using var response = await http.PostAsync(endpoint, form, ct);
                response.EnsureSuccessStatusCode();
            }
        }
        catch
        {
            throw new InvalidOperationException("Die Sitzung konnte online nicht widerrufen werden. Bitte erneut versuchen oder den Zugriff in ChatGPT unter Nutzung verwalten entfernen. Die lokale Anmeldung wurde gelöscht.");
        }
        finally { SecureStorage.Default.Remove(StorageKey); current = null; }
    }

    async Task SaveAsync(Credential credential)
    {
        if (!credential.Scopes.Contains("chatgpt.tokens.use.direct")) throw new InvalidOperationException("Die Freigabe für ChatGPT-Plan-Nutzung fehlt.");
        await SecureStorage.Default.SetAsync(RegistrationKey, JsonSerializer.Serialize(new Registration(credential.ClientId, credential.Subject), KoeJsonContext.Default.Registration));
        await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(credential, KoeJsonContext.Default.Credential));
        current = credential;
    }

    static Credential ToCredential(string clientId, Tokens tokens, VerifiedIdentity identity, Credential? old)
    {
        var refresh = tokens.RefreshToken ?? old?.RefreshToken;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("OpenAI hat kein erneuerbares Token geliefert.");
        return new(clientId, tokens.AccessToken, refresh, tokens.IdToken, identity.Subject, identity.Email,
            tokens.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? old?.Scopes ?? [],
            DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn));
    }

    async Task<Tokens> PostTokensAsync(Dictionary<string, string> values, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(values);
        using var response = await http.PostAsync("https://auth.openai.com/api/accounts/oauth/token", form, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Anmeldung konnte nicht erneuert werden (HTTP {(int)response.StatusCode}). Bitte erneut anmelden.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = body.RootElement;
        return new(root.GetProperty("access_token").GetString()!, root.GetProperty("id_token").GetString()!,
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("scope", out var s) ? s.GetString() : null, root.GetProperty("expires_in").GetInt32());
    }

    static async Task ObserveAsync(Task task) { try { await task; } catch { /* Loopback result is authoritative. */ } }
    static string RandomValue(int count = 32) => Base64Url(RandomNumberGenerator.GetBytes(count));
    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string Encode(Dictionary<string, string> values) => string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    sealed record Tokens(string AccessToken, string IdToken, string? RefreshToken, string? Scope, int ExpiresIn);
}

internal sealed class LoopbackCallback : IDisposable
{
    readonly TcpListener listener;
    readonly string expectedState;
    public string RedirectUri { get; }

    public LoopbackCallback(string state)
    {
        expectedState = state;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/auth/callback";
    }

    public async Task<Dictionary<string, string>> WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(ct);
            perRequest.CancelAfter(TimeSpan.FromSeconds(5));
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var line = new StringBuilder();
            var buffer = new char[1];
            try
            {
                while (line.Length < 16384 && await reader.ReadAsync(buffer.AsMemory(), perRequest.Token) > 0)
                { if (buffer[0] == '\n') break; line.Append(buffer[0]); }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            var pieces = line.ToString().Trim().Split(' ');
            if (line.Length >= 16384 || pieces.Length < 2 || pieces[0] != "GET" || !pieces[1].StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
            var query = ParseQuery(new Uri("http://127.0.0.1" + pieces[1]).Query);
            if (query.GetValueOrDefault("state") != expectedState) continue;
#if IOS
            var location = "koeapp://oauth?" + ChatGptAuth.Encode(query);
            var response = "HTTP/1.1 302 Found\r\nLocation: " + location + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
#else
            var response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: 24\r\nConnection: close\r\n\r\nReturn to Koe to continue";
#endif
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
            return query;
        }
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (!values.TryAdd(key, value)) throw new InvalidOperationException("Mehrdeutige Anmeldeantwort.");
        }
        return values;
    }

    public void Dispose() => listener.Stop();
}
