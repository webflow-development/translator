namespace Koe.Services;

// Memory-only and scoped to the current credential; never persists credentials or model access.
public sealed class ModelSelectionCache
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Func<DateTimeOffset> now;
    string? owner, model;
    DateTimeOffset expires;

    public ModelSelectionCache(Func<DateTimeOffset>? now = null) => this.now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<string> GetAsync(string credentialKey, Func<Task<string>> load, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (owner == credentialKey && model is not null && now() < expires) return model;
            var selected = await load();
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(selected)) throw new InvalidOperationException("No available model was found for this account.");
            owner = credentialKey; model = selected; expires = now().AddMinutes(30);
            return selected;
        }
        finally { gate.Release(); }
    }

    public void Clear() { owner = model = null; }
}
