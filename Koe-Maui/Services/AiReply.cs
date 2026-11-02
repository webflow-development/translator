using System.Text.Json;

namespace Koe.Services;

public sealed record AiReply(string FirstAnswer, string SecondAnswer, string FirstTransliteration, string SecondTransliteration,
    string FirstLanguageCode, string InputLanguageCode)
{
    public bool WebSearchUsed { get; init; }
    public IReadOnlyList<WebCitation> Citations { get; init; } = [];
    internal static string Schema(ConversationLanguage first, ConversationLanguage second) => $$"""
    {
      "type":"object",
      "properties":{
        "input_language":{"type":"string"},
        "first_language":{"type":"string","enum":[{{JsonSerializer.Serialize(first.Code)}},{{JsonSerializer.Serialize(second.Code)}}]},
        "first_answer":{"type":"string"}, "second_answer":{"type":"string"},
        "first_transliteration":{"type":"string"}, "second_transliteration":{"type":"string"}
      },
      "required":["input_language","first_language","first_answer","second_answer","first_transliteration","second_transliteration"],
      "additionalProperties":false
    }
    """;

    public static AiReply Parse(string json, bool includeTransliteration, ConversationLanguage first, ConversationLanguage second)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("The AI answer is incomplete. Please try again.");
            string Read(string key, bool required)
            {
                if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String ||
                    (required && string.IsNullOrWhiteSpace(value.GetString())))
                    throw new InvalidOperationException("The AI answer is incomplete. Please try again.");
                return value.GetString()!.Trim();
            }
            var firstLanguage = Read("first_language", true);
            if (firstLanguage != first.Code && firstLanguage != second.Code)
                throw new InvalidOperationException("The AI answer contains an unexpected language. Please try again.");
            var inputLanguage = ConversationLanguage.ResolveSelectedCode(Read("input_language", false), first, second);
            var expectedFirst = inputLanguage == second.Code ? second.Code : first.Code;
            if (firstLanguage != expectedFirst)
                throw new InvalidOperationException("The AI answer language order could not be verified. Please try again.");
            var firstAnswer = Read("first_answer", true); var secondAnswer = Read("second_answer", true);
            var firstReading = Read("first_transliteration", false); var secondReading = Read("second_transliteration", false);
            return new(firstAnswer, secondAnswer, includeTransliteration ? firstReading : "", includeTransliteration ? secondReading : "", firstLanguage, inputLanguage);
        }
        catch (JsonException) { throw new InvalidOperationException("The AI answer could not be read. Please try again."); }
    }
}
