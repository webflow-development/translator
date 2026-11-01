using System.Globalization;

namespace Koe.Services;

public sealed record SpeechRecognitionCandidate(CultureInfo Culture, string Text, double Confidence, double LanguageConfidence)
{
    static double Probability(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    public double Score => Probability(Confidence) * .85 + Probability(LanguageConfidence) * .15;
}

public static class AutomaticSpeechRecognition
{
    // Native recognizers need an explicit locale. Reusing one recording in
    // sequential passes keeps task lifetimes and cleanup isolated.
    public static async Task<SpeechRecognitionCandidate> RecognizeAsync(IReadOnlyList<CultureInfo> cultures,
        Func<CultureInfo, CancellationToken, Task<SpeechRecognitionCandidate?>> recognize, CancellationToken ct)
    {
        if (cultures.Count is < 1 or > 2) throw new ArgumentException("Choose one or two recognition languages.", nameof(cultures));
        SpeechRecognitionCandidate? best = null;
        foreach (var culture in cultures.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            SpeechRecognitionCandidate? result;
            try { result = await recognize(culture, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { continue; }
            ct.ThrowIfCancellationRequested();
            if (result is null || string.IsNullOrWhiteSpace(result.Text) || result.Score <= 0 ||
                !result.Culture.Name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || result.Score > best.Score) best = result;
        }
        ct.ThrowIfCancellationRequested();
        return best is not null ? best with { Text = best.Text.Trim() } :
            throw new InvalidOperationException("No clear speech could be recognized. Try again, or select a language button to record in one language.");
    }
}
