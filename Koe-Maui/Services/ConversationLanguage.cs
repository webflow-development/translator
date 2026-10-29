using System.Globalization;

namespace Koe.Services;

public sealed record ConversationLanguage(string Code, string Name, string Culture)
{
    public string Greeting => Code switch
    {
        "en" => "Hello!", "de" => "Hallo!", "es" => "¡Hola!", "fr" => "Bonjour !",
        "it" => "Ciao!", "pt" => "Olá!", "nl" => "Hallo!", "ja" => "こんにちは！",
        "zh" => "你好！", "yue" => "你好！", "ko" => "안녕하세요!", "vi" => "Xin chào!",
        "th" => "สวัสดี!", "id" => "Halo!", "ms" => "Helo!", "fil" or "tl" => "Kumusta!",
        "ar" => "مرحبًا!", "he" => "שלום!", "fa" => "سلام!", "ur" => "سلام!",
        "hi" => "नमस्ते!", "bn" => "নমস্কার!", "ta" => "வணக்கம்!", "te" => "నమస్కారం!",
        "kn" => "ನಮಸ್ಕಾರ!", "ml" => "നമസ്കാരം!", "mr" => "नमस्कार!", "gu" => "નમસ્તે!",
        "pa" => "ਸਤ ਸ੍ਰੀ ਅਕਾਲ!", "ne" => "नमस्ते!", "si" => "ආයුබෝවන්!",
        "ru" => "Здравствуйте!", "uk" => "Вітаю!", "pl" => "Cześć!", "cs" => "Ahoj!",
        "sk" => "Ahoj!", "sl" => "Živjo!", "hr" or "bs" => "Bok!", "sr" => "Здраво!",
        "bg" => "Здравейте!", "mk" => "Здраво!", "ro" => "Salut!", "hu" => "Szia!",
        "el" => "Γεια σας!", "tr" => "Merhaba!", "az" => "Salam!", "uz" => "Salom!",
        "kk" => "Сәлеметсіз бе!", "sv" or "da" => "Hej!", "nb" or "nn" or "no" => "Hei!",
        "fi" => "Hei!", "is" => "Halló!", "et" => "Tere!", "lv" => "Sveiki!",
        "lt" => "Labas!", "ca" => "Hola!", "eu" => "Kaixo!", "gl" => "Ola!",
        "cy" => "Helo!", "ga" => "Dia duit!", "af" => "Hallo!", "sw" => "Habari!",
        "hy" => "Բարև!", "ka" => "გამარჯობა!", "sq" => "Përshëndetje!",
        "km" => "សួស្តី!", "lo" => "ສະບາຍດີ!", "my" => "မင်္ဂလာပါ!",
        "mn" => "Сайн байна уу!", "be" => "Вітаю!", "am" => "ሰላም!",
        "so" => "Salaan!", "zu" => "Sawubona!", "jv" => "Sugeng rawuh!", "su" => "Sampurasun!",
        _ => "👋 " + CultureInfo.GetCultureInfo(Culture).NativeName
    };

    static readonly ConversationLanguage[] Defaults =
    [
        new("en", "English", "en-US"), new("es", "Spanish", "es-ES"),
        new("de", "German", "de-DE"), new("ja", "Japanese", "ja-JP"),
        new("zh", "Mandarin", "zh-CN")
    ];
    public static ConversationLanguage[] All { get; private set; } = Defaults;

    public static void ConfigureSpeechLocales(IEnumerable<string> locales)
    {
        var supported = FromSpeechLocales(locales);
        if (supported.Length >= 2) All = supported;
    }

    public static ConversationLanguage[] FromSpeechLocales(IEnumerable<string> locales)
    {
        var candidates = new List<ConversationLanguage>();
        foreach (var locale in locales.OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(locale.Replace('_', '-'));
                var code = culture.TwoLetterISOLanguageName;
                if (code is "" or "iv" || culture.IsNeutralCulture) continue;
                var known = Defaults.FirstOrDefault(x => x.Code == code);
                var name = known?.Name ?? CultureInfo.GetCultureInfo(code).EnglishName;
                candidates.Add(new(code, name, culture.Name));
            }
            catch (CultureNotFoundException) { /* Ignore locales .NET cannot recognize for speech capture. */ }
        }
        return candidates.GroupBy(x => x.Code).Select(group =>
        {
            var preferred = Defaults.FirstOrDefault(x => x.Code == group.Key);
            return group.FirstOrDefault(x => string.Equals(x.Culture, preferred?.Culture, StringComparison.OrdinalIgnoreCase)) ?? group.First();
        }).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static ConversationLanguage Get(string code) => All.FirstOrDefault(x => x.Code == code) ?? All[0];
}
