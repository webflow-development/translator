using System.Text.Json;

namespace Koe.Services;

public sealed record TranslationResult(string Translation, string Romaji)
{
    // Both values are produced together so the reading refers to this exact Japanese sentence.
    internal const string Schema = """
        {
          "type": "object",
          "properties": {
            "translation": { "type": "string", "description": "The translated sentence, with no explanations." },
            "romaji": { "type": "string", "description": "Hepburn romanization of the Japanese sentence: the translation for German input, or the original for Japanese input." }
          },
          "required": ["translation", "romaji"],
          "additionalProperties": false
        }
        """;

    public static TranslationResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("translation", out var translation) || translation.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("romaji", out var romaji) || romaji.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(translation.GetString()) || string.IsNullOrWhiteSpace(romaji.GetString()))
                throw InvalidResponse();
            return new(translation.GetString()!.Trim(), romaji.GetString()!.Trim());
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    static InvalidOperationException InvalidResponse() => new("Die Übersetzung mit Romaji konnte nicht vollständig gelesen werden. Bitte erneut versuchen.");
}
