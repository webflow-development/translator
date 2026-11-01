namespace Koe.Services;

// Recognition may restart after a silence or replay earlier audio when the
// detected language changes. Both cases must update one editable utterance.
public sealed class LiveSpeechPreview
{
    int revision;
    string prefix = "";
    bool replaying;
    public string Text { get; private set; } = "";

    public int BeginSegment(bool replaysUtterance)
    {
        prefix = replaysUtterance ? "" : Text.TrimEnd();
        replaying = replaysUtterance;
        return ++revision;
    }

    public bool Update(int segment, string text)
    {
        if (segment != revision || string.IsNullOrWhiteSpace(text)) return false;
        var next = string.IsNullOrWhiteSpace(prefix) ? text.Trim() : prefix + " " + text.Trim();
        // A replay first reports the opening words again. Keep the complete
        // visible sentence until replay catches up or corrects its wording.
        if (replaying && Text.StartsWith(next, StringComparison.Ordinal) && next.Length < Text.Length) return false;
        if (next == Text) return false;
        Text = next;
        return true;
    }

    public void Invalidate() => revision++;
    public void Clear() { revision++; prefix = Text = ""; }
}
