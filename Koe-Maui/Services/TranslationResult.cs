using System.Text.Json;

namespace Koe.Services;

public sealed record ImageTextTranslation(bool FromGerman, string Original, string Translation, string Romaji);

public sealed record TranslationResult(string Translation, string Romaji, ImageTextTranslation? ImageText = null)
{
    internal const string ImageSchema = """
        {
          "type":"object",
          "properties":{
            "translation":{"type":"string"}, "romaji":{"type":"string"},
            "image_text_found":{"type":"boolean"}, "image_from_german":{"type":"boolean"},
            "image_original":{"type":"string"}, "image_translation":{"type":"string"}, "image_romaji":{"type":"string"}
          },
          "required":["translation","romaji","image_text_found","image_from_german","image_original","image_translation","image_romaji"],
          "additionalProperties":false
        }
        """;
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

    public static TranslationResult Parse(string json, bool hasImage = false, bool hasText = true)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("translation", out var translation) || translation.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("romaji", out var romaji) || romaji.ValueKind != JsonValueKind.String ||
                (hasText && (string.IsNullOrWhiteSpace(translation.GetString()) || string.IsNullOrWhiteSpace(romaji.GetString()))))
                throw InvalidResponse();
            ImageTextTranslation? imageText = null;
            if (hasImage)
            {
                if (!root.TryGetProperty("image_text_found", out var found) || found.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !root.TryGetProperty("image_from_german", out var fromGerman) || fromGerman.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
                if (found.GetBoolean())
                    imageText = new(fromGerman.GetBoolean(), ReadText(root, "image_original"), ReadText(root, "image_translation"), ReadText(root, "image_romaji"));
            }
            return new(translation.GetString()!.Trim(), romaji.GetString()!.Trim(), imageText);
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    static string ReadText(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw InvalidResponse();
        return value.GetString()!.Trim();
    }

    static InvalidOperationException InvalidResponse() => new("Die Übersetzung mit Romaji konnte nicht vollständig gelesen werden. Bitte erneut versuchen.");
}
