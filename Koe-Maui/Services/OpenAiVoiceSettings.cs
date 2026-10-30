namespace Koe.Services;

public sealed class OpenAiVoiceSettings
{
    const string KeyName = "koe.openai-voice.api-key";
    public bool Enabled => Preferences.Default.Get("koe.openai-voice.enabled", false);
    public string Voice => Preferences.Default.Get("koe.openai-voice.voice", "marin");
    public Task<string?> GetKeyAsync() => SecureStorage.Default.GetAsync(KeyName);

    public async Task SaveAsync(bool enabled, string voice, string? newKey)
    {
        if (!OpenAiSpeechClient.Voices.Contains(voice)) throw new InvalidOperationException("Choose an OpenAI voice.");
        var key = string.IsNullOrWhiteSpace(newKey) ? await GetKeyAsync() : newKey.Trim();
        if (enabled && (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("Paste your OpenAI API key to enable OpenAI voice.");
        if (!string.IsNullOrWhiteSpace(newKey)) await SecureStorage.Default.SetAsync(KeyName, key!);
        Preferences.Default.Set("koe.openai-voice.voice", voice);
        Preferences.Default.Set("koe.openai-voice.enabled", enabled);
    }

    public void RemoveKey()
    {
        Preferences.Default.Set("koe.openai-voice.enabled", false);
        SecureStorage.Default.Remove(KeyName);
    }
}
