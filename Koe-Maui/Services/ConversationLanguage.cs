using System.Globalization;

namespace Koe.Services;

public sealed record ConversationLanguage(string Code, string Name, string Culture)
{
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
