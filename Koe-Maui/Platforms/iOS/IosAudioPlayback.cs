using AVFoundation;
using CoreFoundation;
using Foundation;
using Koe.Services;

namespace Koe.Platforms.iOS;

public sealed class IosAudioPlayback : IAudioPlayback
{
    AVAudioPlayer? player;
    NSData? audioData;
    PlaybackDelegate? playerDelegate;
    long generation;
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
            // Keep both the data and the weak native delegate rooted for the full playback.
            audioData = NSData.FromArray(wav);
            player = AVAudioPlayer.FromData(audioData, out var error);
            if (player is null || error is not null) throw new InvalidOperationException("Could not play the generated voice.");
            playerDelegate = new PlaybackDelegate(this, generation);
            player.Delegate = playerDelegate;
            player.PrepareToPlay();
            if (!player.Play()) throw new InvalidOperationException("Could not start voice playback.");
        }
        catch { Stop(); throw; }
    }

    void ScheduleCompletion(long completedGeneration)
    {
        // Always enqueue, even on the main thread: native callbacks must return before
        // their player/delegate are detached and disposed. MainThread.BeginInvoke can inline.
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (completedGeneration != generation) return;
            Cleanup(stopPlayer: false);
        });
    }

    public void Stop()
    {
        if (!MainThread.IsMainThread)
        {
            DispatchQueue.MainQueue.DispatchAsync(Stop);
            return;
        }
        Cleanup(stopPlayer: true);
    }

    void Cleanup(bool stopPlayer)
    {
        ++generation; // A queued completion from an older player cannot stop its replacement.
        var previousPlayer = player;
        var previousDelegate = playerDelegate;
        var previousData = audioData;
        player = null; playerDelegate = null; audioData = null;
        if (previousPlayer is not null)
        {
            previousPlayer.Delegate = null;
            if (stopPlayer) previousPlayer.Stop();
            previousPlayer.Dispose();
        }
        previousDelegate?.Dispose();
        previousData?.Dispose();
        if (ownsSession)
        {
            ownsSession = false;
            AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }
    }

    sealed class PlaybackDelegate(IosAudioPlayback owner, long generation) : AVAudioPlayerDelegate
    {
        readonly WeakReference<IosAudioPlayback> ownerReference = new(owner);

        void Complete()
        {
            if (ownerReference.TryGetTarget(out var playback)) playback.ScheduleCompletion(generation);
        }

        public override void FinishedPlaying(AVAudioPlayer player, bool flag) => Complete();
        public override void DecoderError(AVAudioPlayer player, NSError? error) => Complete();
    }
}
