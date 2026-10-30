namespace Koe.Services;

public interface IAudioPlayback
{
    bool IsPlaying { get; }
    void Play(byte[] wav);
    void Stop();
}

public sealed class UnsupportedAudioPlayback : IAudioPlayback
{
    public bool IsPlaying => false;
    public void Play(byte[] wav) => throw new NotSupportedException("OpenAI voice playback is available in the iPhone app.");
    public void Stop() { }
}
