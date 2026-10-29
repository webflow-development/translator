using System.Text.Json;

namespace Koe.Services;

public sealed record ImageTextTranslation(string Source, string Target, string Original, string Translation, string SourceTransliteration, string TargetTransliteration);

public sealed record TranslationResult(string Translation, string SourceTransliteration, string TargetTransliteration, ImageTextTranslation? ImageText = null)
{
    internal const string ImageSchema = """
    {
      "type":"object",
      "properties":{
        "translation":{"type":"string"},
        "source_transliteration":{"type":"string"}, "target_transliteration":{"type":"string"},
        "image_text_found":{"type":"boolean"}, "image_source":{"type":"string"}, "image_target":{"type":"string"},
        "image_original":{"type":"string"}, "image_translation":{"type":"string"},
        "image_source_transliteration":{"type":"string"}, "image_target_transliteration":{"type":"string"}
      },
      "required":["translation","source_transliteration","target_transliteration","image_text_found","image_source","image_target","image_original","image_translation","image_source_transliteration","image_target_transliteration"],
      "additionalProperties":false
    }
    """;
    internal const string Schema = """
    {
      "type":"object",
      "properties":{
        "translation":{"type":"string"},
        "source_transliteration":{"type":"string"},
        "target_transliteration":{"type":"string"}
      },
      "required":["translation","source_transliteration","target_transliteration"],
      "additionalProperties":false
    }
    """;

    public static TranslationResult Parse(string json, bool hasImage = false, bool hasText = true, bool includeTransliteration = true)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            var translation = ReadText(root, "translation", hasText);
            var sourceReading = ReadText(root, "source_transliteration", false);
            var targetReading = ReadText(root, "target_transliteration", false);
            ImageTextTranslation? imageText = null;
            if (hasImage)
            {
                if (!root.TryGetProperty("image_text_found", out var found) || found.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
                if (found.GetBoolean())
                {
                    var source = ReadText(root, "image_source");
                    var target = ReadText(root, "image_target");
                    if (!ConversationLanguage.All.Any(x => x.Code == target) || source == target) throw InvalidResponse();
                    var originalReading = ReadText(root, "image_source_transliteration", false);
                    var translatedReading = ReadText(root, "image_target_transliteration", false);
                    imageText = new(source, target, ReadText(root, "image_original"), ReadText(root, "image_translation"), includeTransliteration ? originalReading : "", includeTransliteration ? translatedReading : "");
                }
            }
            return new(translation, includeTransliteration ? sourceReading : "", includeTransliteration ? targetReading : "", imageText);
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    static string ReadText(JsonElement root, string key, bool required = true)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || (required && string.IsNullOrWhiteSpace(value.GetString()))) throw InvalidResponse();
        return value.GetString()!.Trim();
    }
    static InvalidOperationException InvalidResponse() => new("The translation could not be read completely. Please try again.");
}
