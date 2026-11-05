using Koe.Services;

namespace Koe;

public sealed class App(ChatsPage page, MainPage conversation, SpeechOutput playback) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(page));
        window.Deactivated += async (_, _) => { playback.Stop(); await conversation.SuspendAndSaveAsync(); };
        window.Stopped += async (_, _) => await conversation.SuspendAndSaveAsync();
        return window;
    }
}
