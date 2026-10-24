namespace Koe.Services;

public sealed record ConversationLanguage(string Code, string Name, string Culture)
{
    public static readonly ConversationLanguage[] All =
    [
        new("en", "English", "en-US"), new("es", "Spanish", "es-ES"),
        new("de", "German", "de-DE"), new("ja", "Japanese", "ja-JP"),
        new("zh", "Mandarin", "zh-CN")
    ];
    public static ConversationLanguage Get(string code) => All.FirstOrDefault(x => x.Code == code) ?? All[0];
}
