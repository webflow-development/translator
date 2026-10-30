using System.Text.Json;

namespace Koe.Services;

public sealed record TranslationModel(string Id, string Name)
{
    public static TranslationModel[] ParseCatalog(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var result = new List<TranslationModel>();
            foreach (var item in document.RootElement.GetProperty("models").EnumerateArray())
            {
                if (!item.TryGetProperty("visibility", out var visibility) || visibility.GetString() != "list" ||
                    !item.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(slug.GetString())) continue;
                var id = slug.GetString()!;
                var name = item.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String ? display.GetString() : null;
                if (!result.Any(x => x.Id == id)) result.Add(new(id, string.IsNullOrWhiteSpace(name) ? id : name));
            }
            if (result.Count == 0) throw new InvalidOperationException("No available model was found for this account.");
            return result.ToArray();
        }
        catch (JsonException) { throw new InvalidOperationException("The model list could not be read. Please try again."); }
        catch (KeyNotFoundException) { throw new InvalidOperationException("The model list could not be read. Please try again."); }
    }

    public static TranslationModel Resolve(IReadOnlyList<TranslationModel> models, string? selectedId)
    {
        if (models.Count == 0) throw new InvalidOperationException("No available model was found for this account.");
        if (string.IsNullOrWhiteSpace(selectedId)) return models[0];
        return models.FirstOrDefault(x => x.Id == selectedId) ?? throw new InvalidOperationException("This model is no longer available. Please choose another model from the model menu.");
    }
}
