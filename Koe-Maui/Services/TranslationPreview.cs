using System.Text;
using System.Text.Json;

namespace Koe.Services;

public sealed record TranslationPreview(string Text, string ImageText)
{
    // Only expose JSON string values, never syntax or an unfinished escape sequence.
    public static TranslationPreview Read(string json) => new(ReadField(json, "translation"), ReadField(json, "image_translation"));

    static string ReadField(string json, string field)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals(field)) continue;
                if (reader.Read()) return reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                var start = (int)reader.BytesConsumed;
                while (start < bytes.Length && char.IsWhiteSpace((char)bytes[start])) start++;
                if (start >= bytes.Length || bytes[start] != '"') return "";
                var partial = Encoding.UTF8.GetString(bytes.AsSpan(start));
                // Trim only the unfinished tail, including a split Unicode surrogate pair.
                for (var trim = 0; trim <= 12 && trim < partial.Length; trim++)
                {
                    try { return JsonSerializer.Deserialize<string>(partial[..(partial.Length - trim)] + "\"") ?? ""; }
                    catch (JsonException) { }
                    catch (InvalidOperationException) { }
                }
                return "";
            }
        }
        catch (JsonException) { }
        return "";
    }
}
