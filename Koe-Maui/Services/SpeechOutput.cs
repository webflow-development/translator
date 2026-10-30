namespace Koe.Services;

public sealed class SpeechOutput(ISpeechPlayback native, IAudioPlayback audio, OpenAiSpeechClient client, OpenAiVoiceSettings settings)
{
    CancellationTokenSource? pending;
    string? lastText, lastLanguage;
    public bool UsesOpenAi => settings.Enabled;
    public IReadOnlyList<PlaybackVoice> GetVoices(string code) => native.GetVoices(code);

    public async Task<bool> SpeakAsync(string text, string code, string? nativeVoice)
    {
        if (!settings.Enabled) { native.Speak(text, code, nativeVoice); return true; }
        var stopOnly = lastText == text && lastLanguage == code && (pending is not null || audio.IsPlaying);
        Stop();
        if (stopOnly || string.IsNullOrWhiteSpace(text)) return false;
        if (DeviceInfo.Platform != DevicePlatform.iOS) throw new NotSupportedException("OpenAI voice playback is available in the iPhone app.");
        using var request = new CancellationTokenSource();
        pending = request; lastText = text; lastLanguage = code;
        try
        {
            var key = await settings.GetKeyAsync();
            request.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add your API key in Settings → OpenAI voice.");
            var wav = await client.SynthesizeAsync(key, text, code, settings.Voice, request.Token);
            request.Token.ThrowIfCancellationRequested();
            // Capture the UI context so native playback cannot race a stop/record action.
            audio.Play(wav);
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        finally { if (ReferenceEquals(pending, request)) pending = null; }
    }

    public void Stop()
    {
        pending?.Cancel(); pending = null;
        native.Stop(); audio.Stop(); lastText = lastLanguage = null;
    }
}
