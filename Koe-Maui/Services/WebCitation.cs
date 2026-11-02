using System.Text.Json;
using System.Text.RegularExpressions;

namespace Koe.Services;

public sealed record WebCitation(string Title, string Url)
{
    static readonly Regex ToolMarker = new("\uE200cite\uE202[^\uE201]*\uE201", RegexOptions.CultureInvariant);
    static readonly Regex MarkdownLink = new(@"\[([^\]\r\n]+)\]\((https?://[^\s)]+)\)", RegexOptions.CultureInvariant);

    public static WebCitation? Create(string? title, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 8192 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0) return null;
        var label = string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim();
        return new(label.Length <= 240 ? label : label[..240], uri.AbsoluteUri);
    }

    internal static void Collect(JsonElement annotations, List<WebCitation> target)
    {
        if (annotations.ValueKind != JsonValueKind.Array) return;
        foreach (var annotation in annotations.EnumerateArray())
        {
            if (annotation.ValueKind != JsonValueKind.Object ||
                !annotation.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "url_citation") continue;
            var source = annotation.TryGetProperty("url_citation", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : annotation;
            var title = source.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            var url = source.TryGetProperty("url", out var address) && address.ValueKind == JsonValueKind.String ? address.GetString() : null;
            if (Create(title, url) is { } citation && !target.Any(x => x.Url == citation.Url)) target.Add(citation);
        }
    }

    public static string CleanAnswer(string text, IReadOnlyList<WebCitation> citations)
    {
        text = ToolMarker.Replace(text, "");
        return MarkdownLink.Replace(text, match =>
            Create(null, match.Groups[2].Value) is { } link && citations.Any(x => x.Url == link.Url)
                ? match.Groups[1].Value : match.Value).Trim();
    }
}

public sealed record ResponseCompletion(string Text, bool WebSearchUsed, IReadOnlyList<WebCitation> Citations);
