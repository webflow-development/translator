using System.Text;

namespace Koe.Services;

public sealed record DemoPhrase(string English, string Japanese, string JapaneseTransliteration);

public static class DemoConversation
{
    public const string AiQuestionEnglish = "How can I politely ask for a table for two?";
    public const string AiQuestionJapanese = "二人分の席を丁寧にお願いするにはどう言えばいいですか？";
    public const string AiFollowUpEnglish = "How do I say thank you?";
    public const string AiFollowUpJapanese = "ありがとうはどう言えばいいですか？";
    public const string MenuOriginal = "お品書き\nおにぎり 300円\n緑茶 200円\n水 無料";
    public const string MenuTranslation = "Menu\nRice ball ¥300\nGreen tea ¥200\nWater Free";
    public const string MenuTransliteration = "Oshinagaki\nOnigiri sanbyaku-en\nRyokucha nihyaku-en\nMizu muryō";
    const string UnsupportedText = "This demo uses prepared examples. Choose a demo phrase or question to continue.";
    const string UnsupportedLanguages = "The demo examples use English and Japanese. Select those two languages to continue.";

    public static IReadOnlyList<DemoPhrase> HotelPhrases { get; } = Array.AsReadOnly<DemoPhrase>(
    [
        new("Hello, I have a reservation.", "こんにちは、予約しています。", "Konnichiwa, yoyaku shite imasu."),
        new("Could I have some water without ice, please?", "氷なしの水をいただけますか？", "Kōri nashi no mizu o itadakemasu ka?"),
        new("Of course. I will bring it right away.", "もちろんです。すぐにお持ちします。", "Mochiron desu. Sugu ni omochi shimasu."),
        new("Thank you.", "ありがとうございます。", "Arigatō gozaimasu.")
    ]);

    public static TranslationResult Translate(string text, ConversationLanguage source, ConversationLanguage target,
        bool includeTransliteration = true, bool hasSampleImage = false)
    {
        ValidateLanguages(source, target);
        var hasText = !string.IsNullOrWhiteSpace(text);
        var phrase = HotelPhrases.FirstOrDefault(example =>
            Normalize(source.Code == "en" ? example.English : example.Japanese) == Normalize(text));
        if ((hasText && phrase is null) || (!hasText && !hasSampleImage))
            throw new InvalidOperationException(UnsupportedText);

        var fromEnglish = source.Code == "en";
        var original = phrase is null ? "" : fromEnglish ? phrase.English : phrase.Japanese;
        var translated = phrase is null ? "" : fromEnglish ? phrase.Japanese : phrase.English;
        var reading = includeTransliteration ? phrase?.JapaneseTransliteration ?? "" : "";
        var imageText = hasSampleImage
            ? new ImageTextTranslation("ja", "en", MenuOriginal, MenuTranslation, includeTransliteration ? MenuTransliteration : "", "")
            : null;
        return new TranslationResult(translated, fromEnglish ? "" : reading, fromEnglish ? reading : "", original, imageText);
    }

    public static AiReply Ask(string question, ConversationLanguage first, ConversationLanguage second,
        bool includeTransliteration = true)
    {
        ValidateLanguages(first, second);
        var input = Normalize(question);
        var japanese = input == Normalize(AiQuestionJapanese) || input == Normalize(AiFollowUpJapanese);
        DemoPhrase answer;
        if (input == Normalize(AiQuestionEnglish) || input == Normalize(AiQuestionJapanese))
            answer = new("You can politely ask, \"Do you have a table for two?\"",
                "「二人ですが、席はありますか？」と丁寧に尋ねられます。",
                "“Futari desu ga, seki wa arimasu ka?” to teinei ni tazuneraremasu.");
        else if (input == Normalize(AiFollowUpEnglish) || input == Normalize(AiFollowUpJapanese))
            answer = new("Say \"Arigatō gozaimasu\" in Japanese to say thank you politely.",
                "日本語では「ありがとうございます」と言うと丁寧です。",
                "Nihongo de wa “Arigatō gozaimasu” to iu to teinei desu.");
        else throw new InvalidOperationException(UnsupportedText);

        var reading = includeTransliteration ? answer.JapaneseTransliteration : "";
        return japanese
            ? new AiReply(answer.Japanese, answer.English, reading, "", "ja", "ja")
            : new AiReply(answer.English, answer.Japanese, "", reading, "en", "en");
    }

    static void ValidateLanguages(ConversationLanguage first, ConversationLanguage second)
    {
        if (first.Code == second.Code || first.Code is not ("en" or "ja") || second.Code is not ("en" or "ja"))
            throw new InvalidOperationException(UnsupportedLanguages);
    }

    static string Normalize(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
