using AVFoundation;
using Koe.Services;

namespace Koe.Platforms.iOS;

public sealed class IosSpeechPlayback : ISpeechPlayback
{
    readonly AVSpeechSynthesizer synthesizer = new() { UsesApplicationAudioSession = false };
    string? lastText, lastLanguage;

    public IReadOnlyList<PlaybackVoice> GetVoices(string languageCode)
    {
        var voices = new List<PlaybackVoice>();
        foreach (var voice in AVSpeechSynthesisVoice.GetSpeechVoices())
        {
            var item = new PlaybackVoice(voice.Identifier, voice.Name, voice.Language);
            if (item.Matches(languageCode)) voices.Add(item);
        }
        return voices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Culture).ToArray();
    }

    public void Speak(string text, string languageCode, string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // Tapping the same speaker again stops it; another speaker replaces playback.
        var stopOnly = synthesizer.Speaking && lastText == text && lastLanguage == languageCode;
        Stop();
        if (stopOnly) return;
        var choices = GetVoices(languageCode);
        var selected = choices.FirstOrDefault(x => x.Id == voiceId) ?? choices.FirstOrDefault();
        var voice = selected is null ? null : AVSpeechSynthesisVoice.FromIdentifier(selected.Id);
        if (voice is null) throw new InvalidOperationException("No speech voice is available for this language. Download a voice in iPhone Settings under Accessibility → Spoken Content (or Read & Speak) → Voices.");
        var utterance = new AVSpeechUtterance(text) { Voice = voice, Rate = AVSpeechUtterance.DefaultSpeechRate };
        lastText = text; lastLanguage = languageCode;
        synthesizer.SpeakUtterance(utterance);
    }

    public void Stop()
    {
        synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);
        lastText = lastLanguage = null;
    }
}
