using System.Text.Json;

namespace Koe.Services;

public sealed record AiReply(string FirstAnswer, string SecondAnswer, string FirstTransliteration, string SecondTransliteration)
{
    internal const string Schema = """
    {
      "type":"object",
      "properties":{
        "first_answer":{"type":"string"}, "second_answer":{"type":"string"},
        "first_transliteration":{"type":"string"}, "second_transliteration":{"type":"string"}
      },
      "required":["first_answer","second_answer","first_transliteration","second_transliteration"],
      "additionalProperties":false
    }
    """;

    public static AiReply Parse(string json, bool includeTransliteration)
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
            var first = Read("first_answer", true); var second = Read("second_answer", true);
            var firstReading = Read("first_transliteration", false); var secondReading = Read("second_transliteration", false);
            return new(first, second, includeTransliteration ? firstReading : "", includeTransliteration ? secondReading : "");
        }
        catch (JsonException) { throw new InvalidOperationException("The AI answer could not be read. Please try again."); }
    }
}
