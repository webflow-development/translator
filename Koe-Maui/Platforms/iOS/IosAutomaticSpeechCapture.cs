using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using AudioToolbox;
using AVFoundation;
using Foundation;
using Koe.Services;
using NaturalLanguage;
using Speech;

namespace Koe.Platforms.iOS;

// A single microphone feeds a live recognizer and a temporary recording. A
// short, sequential language check can replay the beginning in the other locale.
public sealed class IosAutomaticSpeechCapture
{
    readonly object audioGate = new();
    readonly Queue<AVAudioPcmBuffer> replay = new();
    readonly LiveSpeechPreview preview = new();
    AVAudioEngine? engine;
    AVAudioFormat? format;
    AVAudioFile? audioFile;
    SFSpeechRecognizer? liveRecognizer;
    SFSpeechAudioBufferRecognitionRequest? liveRequest;
    SFSpeechAudioBufferRecognitionRequest? liveFeed;
    SFSpeechRecognitionTask? liveTask;
    TaskCompletionSource? liveCompletion;
    SpeechRecognitionCandidate? liveCandidate;
    CultureInfo[] cultures = [];
    CultureInfo? initialCulture;
    string? path, probePath;
    CancellationTokenSource? lifetime, probeCancellation;
    Task? probeTask;
    long replayFrames;
    bool capturing, tapInstalled, retainReplay, ownsSession, finishing, comparing, detectionFinished;
    int generation, liveRevision;
    public bool IsRecording => capturing;
    public event Action<string>? TranscriptChanged;
    public event Action<string?>? Stopped;

    public IosAutomaticSpeechCapture()
    {
        try
        {
            foreach (var pattern in new[] { "koe-ai-speech-*.wav", "koe-ai-speech-*.caf" })
                foreach (var audioPath in Directory.EnumerateFiles(FileSystem.CacheDirectory, pattern)) DeleteRecording(audioPath);
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
            engine = new AVAudioEngine();
            var input = engine.InputNode;
            format = input.GetBusOutputFormat(0);
            if (format.SampleRate <= 0 || format.ChannelCount == 0) throw new InvalidOperationException("No microphone input is available.");
            path = NewRecordingPath();
            audioFile = OpenFile(path, format);
            retainReplay = true; finishing = comparing = detectionFinished = false;
            var preferred = AutomaticSpeechRecognition.InitialCulture(cultures, CultureInfo.CurrentCulture);
            foreach (var culture in new[] { preferred }.Concat(cultures).DistinctBy(x => x.Name))
            {
                try { StartLive(culture, true, true); initialCulture = culture; break; }
                catch (InvalidOperationException) { StopLive(); }
            }
            if (initialCulture is null) throw new InvalidOperationException("Speech recognition is unavailable for the selected languages.");
            capturing = true;
            input.InstallTapOnBus(0, 1024, format, (buffer, _) => Capture(buffer, version));
            tapInstalled = true;
            engine.Prepare(); engine.StartAndReturnError(out var startError);
            if (startError is not null) throw new InvalidOperationException(startError.LocalizedDescription);
            probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeTask = DetectLanguageAsync(version, probeCancellation.Token);
        }
        catch { if (version == generation) Stop(); throw; }
    }

    void Capture(AVAudioPcmBuffer buffer, int version)
    {
        try
        {
            lock (audioGate)
            {
                if (version != generation || !capturing || buffer.FrameLength == 0) return;
                if (audioFile is not null && !audioFile.WriteFromBuffer(buffer, out _))
                    throw new InvalidOperationException("The recording could not be saved.");
                if (retainReplay || liveFeed is null)
                {
                    replay.Enqueue(CopyBuffer(buffer)); replayFrames += buffer.FrameLength;
                    // Bound native memory even if recognition is slow or speech starts late.
                    while (replayFrames > buffer.Format.SampleRate * 12 && replay.Count > 1)
                    { var old = replay.Dequeue(); replayFrames -= old.FrameLength; old.Dispose(); }
                }
                liveFeed?.Append(buffer);
            }
        }
        catch (Exception) { QueueFailure(version, "Audio recording failed. Please try again."); }
    }

    static AVAudioPcmBuffer CopyBuffer(AVAudioPcmBuffer buffer)
    {
        var copy = new AVAudioPcmBuffer(buffer.Format, buffer.FrameLength) { FrameLength = buffer.FrameLength };
        try
        {
            using var source = buffer.AudioBufferList;
            using var destination = copy.MutableAudioBufferList;
            for (var index = 0; index < source.Count; index++)
            {
                var count = source[index].DataByteSize;
                if (count > destination[index].DataByteSize) throw new InvalidOperationException("Invalid audio buffer size.");
                var bytes = ArrayPool<byte>.Shared.Rent(count);
                try { Marshal.Copy(source[index].Data, bytes, 0, count); Marshal.Copy(bytes, 0, destination[index].Data, count); }
                finally { ArrayPool<byte>.Shared.Return(bytes); }
            }
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    void StartLive(CultureInfo culture, bool replaysUtterance, bool keepForDetection)
    {
        StopLive();
        var version = generation;
        var revision = ++liveRevision;
        var segment = preview.BeginSegment(replaysUtterance);
        using var locale = NSLocale.FromLocaleIdentifier(culture.Name);
        liveRecognizer = new SFSpeechRecognizer(locale);
        if (!liveRecognizer.Available) throw new InvalidOperationException("Speech recognition is unavailable for this language.");
        var request = new SFSpeechAudioBufferRecognitionRequest { ShouldReportPartialResults = true };
        if (liveRecognizer.SupportsOnDeviceRecognition) request.RequiresOnDeviceRecognition = true;
        liveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        liveCandidate = null;
        lock (audioGate)
        {
            foreach (var buffer in replay) request.Append(buffer);
            if (!keepForDetection) ClearReplay();
            retainReplay = keepForDetection;
            liveRequest = liveFeed = request;
        }
        liveTask = liveRecognizer.GetRecognitionTask(request, (result, error) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (version != generation || revision != liveRevision) return;
            if (result is not null && !string.IsNullOrWhiteSpace(result.BestTranscription.FormattedString))
            {
                var text = result.BestTranscription.FormattedString;
                var segments = result.BestTranscription.Segments;
                liveCandidate = new(culture, text, segments.Length == 0 ? 0 : segments.Average(x => (double)x.Confidence), 0);
                if (preview.Update(segment, text) && !finishing) TranscriptChanged?.Invoke(preview.Text);
            }
            if (error is null && result?.Final != true) return;
            lock (audioGate) liveFeed = null;
            liveCompletion?.TrySetResult();
            if (finishing || comparing || !detectionFinished) return;
            // Native recognition may end after a silence. Keep the utterance and
            // microphone running; only the user pauses or sends the draft.
            CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (version != generation || revision != liveRevision || !capturing || finishing) return;
                if (error is not null && liveCandidate is null)
                { QueueFailure(version, "Live speech recognition stopped. Please try again."); return; }
                try { StartLive(culture, false, false); }
                catch (Exception) { QueueFailure(version, "Live speech recognition stopped. Please try again."); }
            });
        }));
    }

    async Task EndLiveAsync(CancellationToken ct)
    {
        var completion = liveCompletion;
        lock (audioGate)
        {
            var request = liveFeed;
            liveFeed = null;
            request?.EndAudio();
        }
        if (completion is null) return;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), ct); }
        catch (TimeoutException) { }
    }

    async Task DetectLanguageAsync(int version, CancellationToken ct)
    {
        string? snapshot = null;
        var preferred = initialCulture!;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
            comparing = true;
            await EndLiveAsync(ct);
            var first = liveCandidate;
            if (first is not null) first = first with { LanguageConfidence = LanguageProbability(first.Text, first.Culture) };
            StopLive();
            snapshot = NewRecordingPath(); probePath = snapshot;
            lock (audioGate)
            {
                using var file = OpenFile(snapshot, format!);
                foreach (var buffer in replay)
                    if (!file.WriteFromBuffer(buffer, out _)) throw new InvalidOperationException("Audio could not be checked.");
            }
            var best = await AutomaticSpeechRecognition.RecognizeAsync(cultures, (culture, token) =>
                culture.Name == preferred.Name ? Task.FromResult(first) : RecognizeFileAsync(snapshot, culture, token, TimeSpan.FromSeconds(4), true), ct);
            ct.ThrowIfCancellationRequested();
            if (version == generation && capturing && !finishing)
            {
                comparing = false; detectionFinished = true;
                StartLive(best.Culture, true, false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (version == generation && capturing && !finishing)
            {
                comparing = false; detectionFinished = true;
                try { StartLive(preferred, true, false); }
                catch (Exception) { QueueFailure(version, "Live speech recognition is unavailable. Please try again."); }
            }
        }
        finally
        {
            if (snapshot is not null) DeleteRecording(snapshot);
            if (version == generation) probePath = null;
        }
    }

    public async Task<string> FinishAsync(CancellationToken ct)
    {
        if (path is null || lifetime is null) return "";
        var version = generation;
        var audioPath = path;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        try
        {
            finishing = true;
            probeCancellation?.Cancel();
            EndRecording();
            if (probeTask is not null) await probeTask;
            await EndLiveAsync(linked.Token);
            preview.RetainAfterSilence();
            StopLive();
            linked.Token.ThrowIfCancellationRequested();
            if (detectionFinished && preview.HasRetainedText && !string.IsNullOrWhiteSpace(preview.Text))
                return preview.FinalText("");
            var result = await AutomaticSpeechRecognition.RecognizeAsync(cultures,
                (culture, token) => RecognizeFileAsync(audioPath, culture, token, TimeSpan.FromSeconds(15), false), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return preview.FinalText(result.Text, preserveRetained: detectionFinished);
        }
        finally { if (version == generation) Stop(); }
    }

    static async Task<SpeechRecognitionCandidate?> RecognizeFileAsync(string audioPath, CultureInfo culture, CancellationToken ct, TimeSpan timeout, bool allowPartial)
    {
        ct.ThrowIfCancellationRequested();
        using var locale = NSLocale.FromLocaleIdentifier(culture.Name);
        using var recognizer = new SFSpeechRecognizer(locale);
        if (!recognizer.Available) throw new InvalidOperationException("Recognition is unavailable for this language.");
        using var url = NSUrl.FromFilename(audioPath);
        using var request = new SFSpeechUrlRecognitionRequest(url) { ShouldReportPartialResults = allowPartial };
        if (recognizer.SupportsOnDeviceRecognition) request.RequiresOnDeviceRecognition = true;
        var completion = new TaskCompletionSource<SpeechRecognitionCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        SpeechRecognitionCandidate? partial = null;
        var accepting = 1;
        using var task = recognizer.GetRecognitionTask(request, (result, error) =>
        {
            if (Volatile.Read(ref accepting) == 0 || ct.IsCancellationRequested) return;
            if (result is not null && !string.IsNullOrWhiteSpace(result.BestTranscription.FormattedString))
            {
                var segments = result.BestTranscription.Segments;
                Volatile.Write(ref partial, new(culture, result.BestTranscription.FormattedString, segments.Length == 0 ? 0 : segments.Average(x => (double)x.Confidence), 0));
            }
            if (result?.Final == true) completion.TrySetResult(Volatile.Read(ref partial));
            else if (error is not null) completion.TrySetException(new InvalidOperationException("Speech could not be recognized in this language."));
        });
        try
        {
            SpeechRecognitionCandidate? result;
            try { result = await completion.Task.WaitAsync(timeout, ct); }
            catch (TimeoutException) when (allowPartial && Volatile.Read(ref partial) is not null) { result = Volatile.Read(ref partial); }
            ct.ThrowIfCancellationRequested();
            return result is null ? null : result with { LanguageConfidence = LanguageProbability(result.Text, culture) };
        }
        finally { Volatile.Write(ref accepting, 0); task.Cancel(); }
    }

    static double LanguageProbability(string text, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        using var recognizer = new NLLanguageRecognizer(); recognizer.Process(text);
        using var probabilities = recognizer.GetNativeLanguageHypotheses(10);
        foreach (var key in probabilities.Keys)
            if (key.ToString().Split('-')[0].Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)) return probabilities[key].DoubleValue;
        return 0;
    }

    static string NewRecordingPath() => Path.Combine(FileSystem.CacheDirectory, "koe-ai-speech-" + Guid.NewGuid().ToString("N") + ".caf");
    static AVAudioFile OpenFile(string audioPath, AVAudioFormat audioFormat)
    {
        using var url = NSUrl.FromFilename(audioPath);
        var file = new AVAudioFile(url, audioFormat.Settings, audioFormat.CommonFormat, audioFormat.Interleaved, out var error);
        if (error is null) return file;
        file.Dispose(); throw new InvalidOperationException(error.LocalizedDescription);
    }

    void QueueFailure(int version, string message) => CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
    {
        if (version != generation) return;
        Stop(); Stopped?.Invoke(message);
    });

    void StopLive()
    {
        liveRevision++; preview.Invalidate();
        SFSpeechAudioBufferRecognitionRequest? request;
        bool needsEnd;
        lock (audioGate) { request = liveRequest; needsEnd = liveFeed is not null; liveRequest = liveFeed = null; }
        if (needsEnd) request?.EndAudio();
        liveTask?.Cancel(); liveTask?.Dispose(); liveTask = null;
        request?.Dispose();
        liveRecognizer?.Dispose(); liveRecognizer = null;
        liveCompletion = null;
    }

    void ClearReplay()
    {
        while (replay.TryDequeue(out var buffer)) buffer.Dispose();
        replayFrames = 0;
    }

    void EndRecording()
    {
        lock (audioGate) capturing = false;
        if (engine?.Running == true) engine.Stop();
        if (tapInstalled) { engine?.InputNode.RemoveTapOnBus(0); tapInstalled = false; }
        engine?.Dispose(); engine = null;
        lock (audioGate) { audioFile?.Dispose(); audioFile = null; }
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
        probeCancellation?.Cancel(); probeCancellation?.Dispose(); probeCancellation = null;
        EndRecording(); StopLive();
        lock (audioGate) ClearReplay();
        format?.Dispose(); format = null;
        preview.Clear(); initialCulture = null;
        var audioPath = path; path = null;
        var snapshot = probePath; probePath = null;
        if (audioPath is not null) DeleteRecording(audioPath);
        if (snapshot is not null) DeleteRecording(snapshot);
        Stopped?.Invoke(null);
    }

    static void DeleteRecording(string audioPath)
    {
        try { File.Delete(audioPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
