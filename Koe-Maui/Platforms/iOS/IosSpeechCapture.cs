using System.Globalization;
using AVFoundation;
using Foundation;
using Koe.Services;
using Speech;

namespace Koe.Platforms.iOS;

public sealed class IosSpeechCapture : ISpeechCapture
{
    AVAudioEngine? engine;
    SFSpeechRecognizer? recognizer;
    SFSpeechAudioBufferRecognitionRequest? request;
    SFSpeechRecognitionTask? task;
    bool tapInstalled;
    bool starting;
    int generation;
    string transcript = "";
    TaskCompletionSource? finalResult;
    bool finishing;
    public bool IsRecording { get; private set; }
    public event Action<string>? TranscriptChanged;
    public event Action<string?>? Stopped;

    public async Task StartAsync(CultureInfo culture)
    {
        if (starting) return;
        starting = true;
        try
        {
            Stop();
            transcript = "";
            var authorization = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
            SFSpeechRecognizer.RequestAuthorization(value => authorization.TrySetResult(value));
            if (await authorization.Task != SFSpeechRecognizerAuthorizationStatus.Authorized)
                throw new InvalidOperationException("Please allow speech recognition for Koe in iPhone Settings.");
            if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
                throw new InvalidOperationException("Please allow microphone access for Koe in iPhone Settings.");
            recognizer = new SFSpeechRecognizer(NSLocale.FromLocaleIdentifier(culture.Name));
            if (!recognizer.Available) throw new InvalidOperationException("Speech recognition is currently unavailable for this language.");
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DuckOthers, out var categoryError);
            if (categoryError is not null) throw new InvalidOperationException(categoryError.LocalizedDescription);
            session.SetMode(AVAudioSession.ModeMeasurement, out var modeError);
            if (modeError is not null) throw new InvalidOperationException(modeError.LocalizedDescription);
            session.SetActive(true, out var activeError);
            if (activeError is not null) throw new InvalidOperationException(activeError.LocalizedDescription);
            engine = new AVAudioEngine();
            var audioRequest = new SFSpeechAudioBufferRecognitionRequest { ShouldReportPartialResults = true };
            if (recognizer.SupportsOnDeviceRecognition) audioRequest.RequiresOnDeviceRecognition = true;
            request = audioRequest;
            var input = engine.InputNode;
            using var format = input.GetBusOutputFormat(0);
            if (format.SampleRate <= 0 || format.ChannelCount == 0) throw new InvalidOperationException("No microphone input is available.");
            input.InstallTapOnBus(0, 1024, format, (buffer, _) => audioRequest.Append(buffer));
            tapInstalled = true;
            engine.Prepare();
            engine.StartAndReturnError(out var startError);
            if (startError is not null) throw new InvalidOperationException(startError.LocalizedDescription);
            IsRecording = true;
            var currentGeneration = generation;
            task = recognizer.GetRecognitionTask(audioRequest, (result, error) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (currentGeneration != generation) return;
                if (result is not null)
                {
                    transcript = result.BestTranscription.FormattedString;
                    TranscriptChanged?.Invoke(transcript);
                }
                if (error is not null || result?.Final == true)
                {
                    finalResult?.TrySetResult();
                    if (finishing) return;
                    var message = error?.LocalizedDescription;
                    Stop();
                    if (message is not null) Stopped?.Invoke(message);
                }
            }));
        }
        catch { Stop(); throw; }
        finally { starting = false; }
    }

    public async Task<string> FinishAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRecording) return transcript;
        finishing = true;
        finalResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (engine?.Running == true) engine.Stop();
            if (tapInstalled) { engine?.InputNode.RemoveTapOnBus(0); tapInstalled = false; }
            request?.EndAudio();
            // Wait for the final words before cancelling recognition.
            try { await finalResult.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
            catch (TimeoutException) { /* Keep the latest partial transcript. */ }
            cancellationToken.ThrowIfCancellationRequested();
            return transcript;
        }
        finally { finishing = false; Stop(); finalResult = null; }
    }

    public void Stop()
    {
        generation++;
        var hadSession = engine is not null;
        if (engine?.Running == true) engine.Stop();
        if (tapInstalled) { engine?.InputNode.RemoveTapOnBus(0); tapInstalled = false; }
        request?.EndAudio();
        task?.Cancel(); task?.Dispose(); task = null;
        request?.Dispose(); request = null;
        recognizer?.Dispose(); recognizer = null;
        engine?.Dispose(); engine = null;
        IsRecording = false;
        if (hadSession) AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        Stopped?.Invoke(null);
    }
}
