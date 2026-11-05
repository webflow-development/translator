using Koe.Services;

namespace Koe;

public sealed class App(MainPage page, SpeechOutput playback) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(page));
        window.Deactivated += async (_, _) => { playback.Stop(); await page.SuspendAndSaveAsync(); };
        window.Stopped += async (_, _) => await page.SuspendAndSaveAsync();
        return window;
    }
}
