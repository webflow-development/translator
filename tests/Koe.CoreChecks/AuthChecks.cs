using System.Net;
using System.Text.Json;
using Koe.Services;

internal static class AuthChecks
{
    const string SessionKey = "koe.oauth.v1";
    const string LegacyKey = "koe.registration.v1";

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("Auth migration removes remembered accounts without interrupting the active session", async () =>
        {
            SeedSession();
            using var http = NoNetwork();
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            if (SecureStorage.Default.Values.ContainsKey(LegacyKey) ||
                !SecureStorage.Default.Values.ContainsKey(SessionKey) || !auth.IsConnected ||
                await auth.ValidAccessTokenAsync(default) != "active-access") throw new Exception("Active session migration changed sign-in.");
        });
        await check("A remembered account alone cannot restore a signed-out session", async () =>
        {
            SecureStorage.Default.Reset();
            SecureStorage.Default.Values[LegacyKey] = "legacy registration";
            using var http = NoNetwork();
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            if (auth.IsConnected || SecureStorage.Default.Values.Count != 0) throw new Exception("Legacy account survived migration.");
            await MustFail<InvalidOperationException>(() => auth.ValidAccessTokenAsync(default));
        });
        await check("Logout loads and revokes the active issued client before clearing local sign-in", async () =>
        {
            SeedSession();
            var requests = 0;
            using var http = new HttpClient(new AuthHandler(async (request, ct) =>
            {
                requests++;
                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/.well-known/openid-configuration")
                    return Discovery();
                if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != "https://auth.openai.com/revoke")
                    throw new Exception("Unexpected revocation request.");
                var fields = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
                if (fields.GetValueOrDefault("client_id") != "oaiapp_active" ||
                    fields.GetValueOrDefault("token") != "active-refresh" ||
                    fields.GetValueOrDefault("token_type_hint") != "refresh_token") throw new Exception("Wrong session revoked.");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            var auth = new ChatGptAuth(http);
            await auth.SignOutAsync(default);
            if (requests != 2) throw new Exception("Logout did not revoke the stored session.");
            await AssertSignedOut(auth);
        });
        await check("A failed online revocation still forgets tokens and remembered accounts", async () =>
        {
            SeedSession();
            using var http = new HttpClient(new AuthHandler((request, ct) => Task.FromResult(
                request.Method == HttpMethod.Get ? Discovery() : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            SecureStorage.Default.Values[LegacyKey] = "legacy registration";
            await MustFail<InvalidOperationException>(() => auth.SignOutAsync(default));
            await AssertSignedOut(auth);
        });
        await check("Cancelled logout still clears both stored and in-memory sign-in", async () =>
        {
            SeedSession();
            using var http = new HttpClient(new AuthHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct)));
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            SecureStorage.Default.Values[LegacyKey] = "legacy registration";
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await MustFail<InvalidOperationException>(() => auth.SignOutAsync(cancellation.Token));
            await AssertSignedOut(auth);
        });
        await check("A session-storage cleanup failure still clears memory and attempts legacy cleanup", async () =>
        {
            SeedSession();
            using var http = RevocationSucceeds();
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            SecureStorage.Default.Values[LegacyKey] = "legacy registration";
            SecureStorage.Default.FailingRemoveKey = SessionKey;
            await MustFail<IOException>(() => auth.SignOutAsync(default));
            if (auth.IsConnected || SecureStorage.Default.Values.ContainsKey(LegacyKey) ||
                !SecureStorage.Default.Removed.Contains(SessionKey)) throw new Exception("Cleanup stopped after the first storage failure.");
            SecureStorage.Default.Reset();
        });
        await check("A legacy-storage cleanup failure cannot retain the active session", async () =>
        {
            SeedSession();
            using var http = RevocationSucceeds();
            var auth = new ChatGptAuth(http);
            await auth.LoadAsync();
            SecureStorage.Default.Values[LegacyKey] = "legacy registration";
            SecureStorage.Default.FailingRemoveKey = LegacyKey;
            await MustFail<IOException>(() => auth.SignOutAsync(default));
            if (auth.IsConnected || SecureStorage.Default.Values.ContainsKey(SessionKey) ||
                !SecureStorage.Default.Removed.Contains(LegacyKey)) throw new Exception("Session survived legacy cleanup failure.");
            SecureStorage.Default.Reset();
        });
    }

    static void SeedSession()
    {
        SecureStorage.Default.Reset();
        SecureStorage.Default.Values[SessionKey] = JsonSerializer.Serialize(new Credential("oaiapp_active", "active-access",
            "active-refresh", "active-id-token", "active-user", "active@example.invalid", ["chatgpt.tokens.use.direct"], DateTimeOffset.UtcNow.AddHours(1)));
        SecureStorage.Default.Values[LegacyKey] = "legacy registration";
    }

    static async Task AssertSignedOut(ChatGptAuth auth)
    {
        if (auth.IsConnected || auth.AccountLabel != "ChatGPT" || auth.ConnectionKey is not null ||
            SecureStorage.Default.Values.ContainsKey(SessionKey) || SecureStorage.Default.Values.ContainsKey(LegacyKey))
            throw new Exception("Logout retained sign-in data.");
        await MustFail<InvalidOperationException>(() => auth.ValidAccessTokenAsync(default));
        using var http = NoNetwork();
        var restarted = new ChatGptAuth(http);
        await restarted.LoadAsync();
        if (restarted.IsConnected) throw new Exception("Session returned after restart.");
    }

    static async Task MustFail<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    static HttpResponseMessage Discovery() => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"revocation_endpoint\":\"https://auth.openai.com/revoke\"}") };
    static HttpClient NoNetwork() => new(new AuthHandler((_, _) => throw new Exception("Unexpected network request.")));
    static HttpClient RevocationSucceeds() => new(new AuthHandler((request, _) =>
        Task.FromResult(request.Method == HttpMethod.Get ? Discovery() : new HttpResponseMessage(HttpStatusCode.OK))));

    sealed class AuthHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
