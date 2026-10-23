using System.Globalization;

namespace Koe.Services;

public enum RecordingState { Ready, Recording, Paused }

// Owns the editable draft across separate recognition segments and deliberate pauses.
public sealed class SpeechDraft
{
    readonly ISpeechCapture speech;
    string prefix = "";
    bool acceptingTranscript;
    bool transitioning;
    bool segmentEdited;
    int editRevision;
    public string Text { get; private set; } = "";
    public RecordingState State { get; private set; }
    public bool HasTurn => State != RecordingState.Ready;
    public bool IsRecording => State == RecordingState.Recording;
    public event Action? Changed;
    public event Action<string>? Error;

    public SpeechDraft(ISpeechCapture speech)
    {
        this.speech = speech;
        speech.TranscriptChanged += transcript =>
        {
            if (!acceptingTranscript || !IsRecording) return;
            Text = Join(prefix, transcript);
            Changed?.Invoke();
        };
        speech.Stopped += error =>
        {
            if (transitioning) return;
            if (IsRecording) { acceptingTranscript = false; State = RecordingState.Paused; Changed?.Invoke(); }
            if (error is not null) Error?.Invoke(error);
        };
    }

    public void Edit(string text)
    {
        if (text == Text) return;
        editRevision++;
        segmentEdited = true;
        acceptingTranscript = false;
        Text = text;
        Changed?.Invoke();
    }

    public async Task StartAsync(CultureInfo culture, CancellationToken ct = default)
    {
        if (transitioning || IsRecording) return;
        ct.ThrowIfCancellationRequested();
        var previousState = State;
        transitioning = true;
        prefix = Text.TrimEnd();
        segmentEdited = false;
        acceptingTranscript = true;
        State = RecordingState.Recording;
        try
        {
            await speech.StartAsync(culture);
            ct.ThrowIfCancellationRequested();
            if (!speech.IsRecording) { acceptingTranscript = false; State = RecordingState.Paused; }
        }
        catch
        {
            acceptingTranscript = false;
            speech.Stop();
            State = previousState;
            throw;
        }
        finally { transitioning = false; Changed?.Invoke(); }
    }

    public async Task PauseAsync(CancellationToken ct = default)
    {
        if (transitioning || !IsRecording) return;
        transitioning = true;
        acceptingTranscript = false;
        var revision = editRevision;
        try
        {
            var final = await speech.FinishAsync(ct);
            // A final recognizer callback must never undo a correction made in the editor.
            if (!segmentEdited && revision == editRevision) Text = Join(prefix, final);
        }
        finally
        {
            speech.Stop();
            State = RecordingState.Paused;
            transitioning = false;
            Changed?.Invoke();
        }
    }

    public async Task<string> FinishAsync(CancellationToken ct = default)
    {
        if (IsRecording) await PauseAsync(ct);
        ct.ThrowIfCancellationRequested();
        acceptingTranscript = false;
        State = RecordingState.Ready;
        Changed?.Invoke();
        return Text;
    }

    public void Suspend()
    {
        acceptingTranscript = false;
        var wasActive = HasTurn;
        speech.Stop();
        State = wasActive ? RecordingState.Paused : RecordingState.Ready;
        Changed?.Invoke();
    }

    public void Clear()
    {
        acceptingTranscript = false;
        Text = prefix = "";
        segmentEdited = false;
        State = RecordingState.Ready;
        Changed?.Invoke();
    }

    static string Join(string prefix, string segment) => string.IsNullOrWhiteSpace(prefix) ? segment.Trim() :
        string.IsNullOrWhiteSpace(segment) ? prefix : prefix + " " + segment.Trim();
}
