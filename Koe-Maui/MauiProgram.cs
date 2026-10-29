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
#if IOS
        var speechLocales = new List<string>();
        foreach (Foundation.NSLocale locale in Speech.SFSpeechRecognizer.SupportedLocales)
            speechLocales.Add(locale.LocaleIdentifier);
        ConversationLanguage.ConfigureSpeechLocales(speechLocales);
        builder.Services.AddSingleton<ISpeechCapture, Platforms.iOS.IosSpeechCapture>();
#else
        builder.Services.AddSingleton<ISpeechCapture, UnsupportedSpeechCapture>();
#endif
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
