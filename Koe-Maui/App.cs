using Koe.Services;

namespace Koe;

public sealed class App(MainPage page, SpeechOutput playback) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page);
        window.Deactivated += (_, _) => playback.Stop();
        return window;
    }
}
