using System.Globalization;
using AudioToolbox;
using AVFoundation;
using Foundation;
using Koe.Services;
using NaturalLanguage;
using Speech;

namespace Koe.Platforms.iOS;

// One temporary recording, then two sequential native recognition passes. No
// extra OpenAI audio endpoint, API key or parallel Apple recognition sessions.
public sealed class IosAutomaticSpeechCapture
{
    AVAudioRecorder? recorder;
    string? path;
    CultureInfo[] cultures = [];
    CancellationTokenSource? lifetime;
    bool ownsSession;
    int generation;
    public bool IsRecording => recorder?.Recording == true;
    public event Action<string?>? Stopped;

    public IosAutomaticSpeechCapture()
    {
        // Remove this feature's cache files left by a force-quit or prior crash.
        try
        {
            foreach (var audioPath in Directory.EnumerateFiles(FileSystem.CacheDirectory, "koe-ai-speech-*.wav"))
                DeleteRecording(audioPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task StartAsync(IReadOnlyList<CultureInfo> languages)
    {
        Stop();
        if (languages.Count is < 1 or > 2) throw new ArgumentException("Choose one or two recognition languages.", nameof(languages));
        cultures = languages.ToArray();
        lifetime = new CancellationTokenSource();
        var ct = lifetime.Token;
        var version = generation;
        try
        {
            var authorization = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            SFSpeechRecognizer.RequestAuthorization(value => authorization.TrySetResult(value));
            if (await authorization.Task.WaitAsync(ct) != SFSpeechRecognizerAuthorizationStatus.Authorized)
                throw new InvalidOperationException("Please allow speech recognition for Koe in iPhone Settings.");
            if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
                throw new InvalidOperationException("Please allow microphone access for Koe in iPhone Settings.");
            ct.ThrowIfCancellationRequested();
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DuckOthers, out var categoryError);
            if (categoryError is not null) throw new InvalidOperationException(categoryError.LocalizedDescription);
            session.SetMode(AVAudioSessionMode.Measurement, out var modeError);
            if (modeError is not null) throw new InvalidOperationException(modeError.LocalizedDescription);
            ownsSession = true;
            session.SetActive(true, out var activeError);
            if (activeError is not null) throw new InvalidOperationException(activeError.LocalizedDescription);
            path = Path.Combine(FileSystem.CacheDirectory, "koe-ai-speech-" + Guid.NewGuid().ToString("N") + ".wav");
            using var url = NSUrl.FromFilename(path);
            var settings = new AudioSettings
            {
                Format = AudioFormatType.LinearPCM, SampleRate = 16000, NumberChannels = 1,
                LinearPcmBitDepth = 16, LinearPcmFloat = false, LinearPcmBigEndian = false
            };
            recorder = AVAudioRecorder.Create(url, settings, out var recordingError);
            if (recordingError is not null || recorder is null)
                throw new InvalidOperationException(recordingError?.LocalizedDescription ?? "The microphone could not be started.");
            recorder.FinishedRecording += (_, _) => QueueFailure(version, "Recording was interrupted. Please try again.");
            recorder.EncoderError += (_, _) => QueueFailure(version, "Audio recording failed. Please try again.");
            if (!recorder.PrepareToRecord() || !recorder.Record())
                throw new InvalidOperationException("The microphone could not be started.");
        }
        catch { if (version == generation) Stop(); throw; }
    }

    void QueueFailure(int version, string message)
    {
        // Never dispose a recorder from within one of its native callbacks.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (version != generation || recorder is null) return;
            Stop(); Stopped?.Invoke(message);
        });
    }

    public async Task<string> FinishAsync(CancellationToken ct)
    {
        if (path is null || lifetime is null) return "";
        var version = generation;
        var audioPath = path;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        try
        {
            EndRecording();
            var result = await AutomaticSpeechRecognition.RecognizeAsync(cultures,
                (culture, token) => RecognizeFileAsync(audioPath, culture, token), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return result.Text;
        }
        finally { if (version == generation) Stop(); }
    }

    static async Task<SpeechRecognitionCandidate?> RecognizeFileAsync(string audioPath, CultureInfo culture, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var locale = NSLocale.FromLocaleIdentifier(culture.Name);
        using var recognizer = new SFSpeechRecognizer(locale);
        if (!recognizer.Available) throw new InvalidOperationException("Recognition is unavailable for this language.");
        using var url = NSUrl.FromFilename(audioPath);
        using var request = new SFSpeechUrlRecognitionRequest(url) { ShouldReportPartialResults = false };
        if (recognizer.SupportsOnDeviceRecognition) request.RequiresOnDeviceRecognition = true;
        var completion = new TaskCompletionSource<SpeechRecognitionCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var task = recognizer.GetRecognitionTask(request, (result, error) =>
        {
            if (ct.IsCancellationRequested) return;
            if (result?.Final == true)
            {
                var text = result.BestTranscription.FormattedString;
                var segments = result.BestTranscription.Segments;
                var confidence = segments.Length == 0 ? 0 : segments.Average(x => (double)x.Confidence);
                completion.TrySetResult(new(culture, text, confidence, 0));
            }
            else if (error is not null) completion.TrySetException(new InvalidOperationException("Speech could not be recognized in this language."));
        });
        try
        {
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            return result is null ? null : result with { LanguageConfidence = LanguageProbability(result.Text, culture) };
        }
        finally { task.Cancel(); }
    }

    static double LanguageProbability(string text, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        using var recognizer = new NLLanguageRecognizer();
        recognizer.Process(text);
        using var probabilities = recognizer.GetNativeLanguageHypotheses(10);
        foreach (var key in probabilities.Keys)
        {
            if (key.ToString().Split('-')[0].Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                return probabilities[key].DoubleValue;
        }
        return 0;
    }

    void EndRecording()
    {
        var recording = recorder;
        recorder = null;
        recording?.Stop(); recording?.Dispose();
        if (ownsSession)
        {
            ownsSession = false;
            AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }
    }

    public void Stop()
    {
        generation++;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
        EndRecording();
        var audioPath = path;
        path = null;
        if (audioPath is not null) DeleteRecording(audioPath);
        Stopped?.Invoke(null);
    }

    static void DeleteRecording(string audioPath)
    {
        try { File.Delete(audioPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
