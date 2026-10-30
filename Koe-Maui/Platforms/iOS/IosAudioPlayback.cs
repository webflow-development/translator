using AVFoundation;
using Foundation;
using Koe.Services;

namespace Koe.Platforms.iOS;

public sealed class IosAudioPlayback : IAudioPlayback
{
    AVAudioPlayer? player;
    bool ownsSession;
    public bool IsPlaying => player?.Playing == true;

    public void Play(byte[] wav)
    {
        Stop();
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Playback, (AVAudioSessionCategoryOptions)0, out var categoryError);
            if (categoryError is not null) throw new InvalidOperationException(categoryError.LocalizedDescription);
            session.SetMode(AVAudioSessionMode.Default, out var modeError);
            if (modeError is not null) throw new InvalidOperationException(modeError.LocalizedDescription);
            session.SetActive(true, out var activeError);
            if (activeError is not null) throw new InvalidOperationException(activeError.LocalizedDescription);
            ownsSession = true;
            using var data = NSData.FromArray(wav);
            player = AVAudioPlayer.FromData(data, out var error);
            if (player is null || error is not null) throw new InvalidOperationException("Could not play the generated voice.");
            player.FinishedPlaying += Finished;
            player.PrepareToPlay();
            if (!player.Play()) throw new InvalidOperationException("Could not start voice playback.");
        }
        catch { Stop(); throw; }
    }

    void Finished(object? sender, AVStatusEventArgs e) => Stop();

    public void Stop()
    {
        if (player is not null)
        {
            player.FinishedPlaying -= Finished;
            player.Stop(); player.Dispose(); player = null;
        }
        if (ownsSession)
        {
            ownsSession = false;
            AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }
    }
}
