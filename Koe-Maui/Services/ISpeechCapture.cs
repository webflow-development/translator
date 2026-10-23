using System.Globalization;

namespace Koe.Services;

public interface ISpeechCapture
{
    bool IsRecording { get; }
    event Action<string>? TranscriptChanged;
    event Action<string?>? Stopped;
    Task StartAsync(CultureInfo culture);
    Task<string> FinishAsync(CancellationToken cancellationToken = default);
    void Stop();
}

public sealed class UnsupportedSpeechCapture : ISpeechCapture
{
    public bool IsRecording => false;
    public event Action<string>? TranscriptChanged { add { } remove { } }
    public event Action<string?>? Stopped { add { } remove { } }
    public Task StartAsync(CultureInfo culture) => throw new NotSupportedException("Diktat ist in dieser Version auf dem iPhone verfügbar. Bitte Text eingeben.");
    public Task<string> FinishAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
    public void Stop() { }
}
