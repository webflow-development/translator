using System.Globalization;

namespace Koe.Services;

public sealed record PlaybackVoice(string Id, string Name, string Culture)
{
    public bool Matches(string languageCode)
    {
        try
        {
            var voiceCulture = CultureInfo.GetCultureInfo(Culture.Replace('_', '-'));
            var requestedCulture = CultureInfo.GetCultureInfo(languageCode.Replace('_', '-'));
            return requestedCulture.IsNeutralCulture
                ? voiceCulture.TwoLetterISOLanguageName == requestedCulture.TwoLetterISOLanguageName
                : voiceCulture.Name.Equals(requestedCulture.Name, StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException) { return false; }
    }
    public string DisplayName => $"{Name} · {Culture}";
}

public interface ISpeechPlayback
{
    IReadOnlyList<PlaybackVoice> GetVoices(string languageCode);
    void Speak(string text, string languageCode, string? voiceId);
    void Stop();
}

public sealed class UnsupportedSpeechPlayback : ISpeechPlayback
{
    public IReadOnlyList<PlaybackVoice> GetVoices(string languageCode) => [];
    public void Speak(string text, string languageCode, string? voiceId) => throw new NotSupportedException("Read aloud is available in the iPhone app.");
    public void Stop() { }
}
