// Only the platform storage/browser boundary is replaced; AuthChecks uses the real auth service.
internal static class SecureStorage
{
    public static MemorySecureStorage Default { get; } = new();
}

internal sealed class MemorySecureStorage
{
    public Dictionary<string, string> Values { get; } = new();
    public List<string> Removed { get; } = new();
    public string? FailingRemoveKey { get; set; }
    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
    public bool Remove(string key)
    {
        Removed.Add(key);
        if (key == FailingRemoveKey) throw new IOException("Simulated secure-storage removal failure.");
        return Values.Remove(key);
    }
    public void Reset() { Values.Clear(); Removed.Clear(); FailingRemoveKey = null; }
}

internal static class Preferences
{
    public static MemoryPreferences Default { get; } = new();
}

internal sealed class MemoryPreferences
{
    readonly Dictionary<string, string> values = new();
    public string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
    public void Set(string key, string value) => values[key] = value;
}

internal enum BrowserLaunchMode { SystemPreferred }
internal static class Browser
{
    public static UnavailableBrowser Default { get; } = new();
}

internal sealed class UnavailableBrowser
{
    public Task OpenAsync(Uri uri, BrowserLaunchMode mode) =>
        throw new InvalidOperationException("Auth regression checks must not open a real browser.");
}
