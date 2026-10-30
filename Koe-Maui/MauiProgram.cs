using Koe.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Koe;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
        builder.Services.AddSingleton<ChatGptAuth>();
        builder.Services.AddSingleton<OpenAiTranslator>();
        builder.Services.AddSingleton<OpenAiSpeechClient>();
        builder.Services.AddSingleton<OpenAiVoiceSettings>();
        builder.Services.AddSingleton<SpeechOutput>();
#if IOS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<ConversationEditor, Platforms.iOS.ConversationEditorHandler>());
        var speechLocales = new List<string>();
        foreach (Foundation.NSLocale locale in Speech.SFSpeechRecognizer.SupportedLocales)
            speechLocales.Add(locale.LocaleIdentifier);
        ConversationLanguage.ConfigureSpeechLocales(speechLocales);
        builder.Services.AddSingleton<ISpeechCapture, Platforms.iOS.IosSpeechCapture>();
        builder.Services.AddSingleton<ISpeechPlayback, Platforms.iOS.IosSpeechPlayback>();
        builder.Services.AddSingleton<IAudioPlayback, Platforms.iOS.IosAudioPlayback>();
#else
        builder.Services.AddSingleton<ISpeechCapture, UnsupportedSpeechCapture>();
        builder.Services.AddSingleton<ISpeechPlayback, UnsupportedSpeechPlayback>();
        builder.Services.AddSingleton<IAudioPlayback, UnsupportedAudioPlayback>();
#endif
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
