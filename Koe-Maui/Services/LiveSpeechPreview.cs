namespace Koe.Services;

// Recognition may restart after a silence or replay earlier audio when the
// detected language changes. Both cases must update one editable utterance.
public sealed class LiveSpeechPreview(TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    static readonly TimeSpan SilenceCheckpoint = TimeSpan.FromSeconds(2);
    int revision;
    string prefix = "";
    string checkpoint = "", currentText = "";
    DateTimeOffset? lastChange;
    bool replaying;
    public string Text { get; private set; } = "";
    public bool HasRetainedText => prefix.Length > 0 || checkpoint.Length > 0;

    public int BeginSegment(bool replaysUtterance)
    {
        prefix = replaysUtterance ? "" : Text.TrimEnd();
        checkpoint = currentText = ""; lastChange = null;
        replaying = replaysUtterance;
        return ++revision;
    }

    public bool Update(int segment, string text)
    {
        if (segment != revision || string.IsNullOrWhiteSpace(text)) return false;
        var now = time.GetUtcNow();
        // Some native recognizers start a fresh partial after silence without
        // first finishing their task. Retain the earlier speech independently.
        RetainAfterSilence();
        var current = Merge(checkpoint, text.Trim());
        var next = Join(prefix, current);
        // A replay first reports the opening words again. Keep the complete
        // visible sentence until replay catches up or corrects its wording.
        if (replaying && Text.StartsWith(next, StringComparison.Ordinal) && next.Length < Text.Length) return false;
        if (next == Text) return false;
        Text = next; currentText = current; lastChange = now;
        return true;
    }

    // A final file pass may contain only the last phrase of a long recording.
    // Once earlier phrases are retained, use the completed live utterance.
    public string FinalText(string final, bool preserveRetained = true)
        => (preserveRetained && HasRetainedText) || string.IsNullOrWhiteSpace(final) ? Text : final.Trim();

    public void RetainAfterSilence()
    {
        if (lastChange is { } last && time.GetUtcNow() - last >= SilenceCheckpoint && currentText.Length > 0)
            checkpoint = currentText;
    }

    static string Merge(string saved, string latest)
    {
        if (saved.Length == 0) return latest;
        var oldWords = saved.TrimEnd(' ', '.', ',', '!', '?', '。', '、', '！', '？', '…');
        var newWords = latest.TrimEnd(' ', '.', ',', '!', '?', '。', '、', '！', '？', '…');
        if (oldWords.Length > 0 && Continues(latest, oldWords)) return latest;
        if (newWords.Length > 0 && Continues(saved, newWords)) return saved;
        // A reset can repeat the last few words. Merge an overlapping tail,
        // respecting Latin word boundaries and continuous Japanese/Chinese text.
        for (var length = Math.Min(64, Math.Min(saved.Length, latest.Length)); length >= 2; length--)
        {
            var start = saved.Length - length;
            if (!saved.AsSpan(start).Equals(latest.AsSpan(0, length), StringComparison.OrdinalIgnoreCase)) continue;
            var continuousScript = false;
            foreach (var character in latest.AsSpan(0, length))
                if (character is >= '\u3040' and <= '\u9fff') { continuousScript = true; break; }
            if (!continuousScript && start > 0 && char.IsLetterOrDigit(saved[start - 1])) continue;
            if (!continuousScript && length < latest.Length && char.IsLetterOrDigit(latest[length])) continue;
            return saved + latest[length..];
        }
        return Join(saved, latest);
    }

    static bool Continues(string full, string beginning)
        => full.StartsWith(beginning, StringComparison.OrdinalIgnoreCase) &&
            (full.Length == beginning.Length || !char.IsLetterOrDigit(full[beginning.Length]) ||
             !char.IsLetterOrDigit(beginning[^1]) || beginning[^1] is >= '\u3040' and <= '\u9fff');

    static string Join(string first, string second) => string.IsNullOrWhiteSpace(first) ? second : first + " " + second;
    public void Invalidate() => revision++;
    public void Clear() { revision++; prefix = checkpoint = currentText = Text = ""; lastChange = null; }
}
