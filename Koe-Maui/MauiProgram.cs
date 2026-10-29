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
        ConversationLanguage.ConfigureSpeechLocales(Speech.SFSpeechRecognizer.SupportedLocales.Select(locale => locale.LocaleIdentifier));
        builder.Services.AddSingleton<ISpeechCapture, Platforms.iOS.IosSpeechCapture>();
#else
        builder.Services.AddSingleton<ISpeechCapture, UnsupportedSpeechCapture>();
#endif
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
