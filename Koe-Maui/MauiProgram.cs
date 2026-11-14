using System.Reflection;
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
        var guestUrl = typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "KoeGuestApiUrl")?.Value;
#if DEBUG && WINDOWS
        var guestAddress = GuestClient.ParseAddress(guestUrl, allowLoopback: true);
#else
        var guestAddress = GuestClient.ParseAddress(guestUrl);
#endif
        builder.Services.AddKeyedSingleton<HttpClient>("guest", (_, _) => new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        }) { Timeout = TimeSpan.FromMinutes(3) });
        builder.Services.AddSingleton(services => new GuestClient(services.GetRequiredKeyedService<HttpClient>("guest"), guestAddress, GuestInstallationId));
        builder.Services.AddSingleton<OpenAiTranslator>();
        builder.Services.AddSingleton<OpenAiSpeechClient>();
        builder.Services.AddSingleton<OpenAiVoiceSettings>();
        builder.Services.AddSingleton<SpeechOutput>();
        builder.Services.AddSingleton(new ConversationStore(Path.Combine(FileSystem.Current.AppDataDirectory, "conversations")));
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

    static Guid GuestInstallationId()
    {
        const string preference = "koe.guest-installation";
        if (Guid.TryParseExact(Preferences.Default.Get(preference, ""), "N", out var saved) && saved != Guid.Empty)
            return saved;
        var id = Guid.NewGuid();
        Preferences.Default.Set(preference, id.ToString("N"));
        return id;
    }
}
