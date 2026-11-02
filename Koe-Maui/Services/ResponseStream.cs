using System.Text;
using System.Text.Json;

namespace Koe.Services;

internal static class ResponseStream
{
    public static async Task<string> ReadAsync(TextReader reader, CancellationToken ct, Action<string>? onPartial = null)
        => (await ReadCompletionAsync(reader, ct, onPartial)).Text;

    public static async Task<ResponseCompletion> ReadCompletionAsync(TextReader reader, CancellationToken ct,
        Action<string>? onPartial = null, Action? onSearching = null)
    {
        var output = new StringBuilder();
        var data = new List<string>();
        var annotations = new List<WebCitation>();
        var searched = false;
        var notified = false;
        ResponseCompletion? completed = null;
        void Searching()
        {
            if (notified) return;
            notified = true; onSearching?.Invoke();
        }
        bool HandleEvent()
        {
            if (data.Count == 0) return false;
            var json = string.Join("\n", data); data.Clear();
            if (json == "[DONE]") return false;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "response.output_text.delta":
                    output.Append(root.GetProperty("delta").GetString());
                    onPartial?.Invoke(output.ToString());
                    break;
                case "response.web_search_call.in_progress": case "response.web_search_call.searching":
                    Searching(); break;
                case "response.web_search_call.completed":
                    searched = true; break;
                case "response.output_item.added":
                    if (root.TryGetProperty("item", out var added))
                    {
                        if (IsType(added, "web_search_call")) Searching();
                        else if (IsType(added, "message")) { output.Clear(); annotations.Clear(); }
                    }
                    break;
                case "response.output_item.done":
                    if (root.TryGetProperty("item", out var done) && IsType(done, "web_search_call") &&
                        done.TryGetProperty("status", out var status) && status.GetString() == "completed") searched = true;
                    break;
                case "response.output_text.annotation.added":
                    if (root.TryGetProperty("annotation", out var annotation))
                    {
                        using var array = JsonDocument.Parse("[" + annotation.GetRawText() + "]");
                        WebCitation.Collect(array.RootElement, annotations);
                    }
                    break;
                case "response.completed":
                    completed = Finish(root, output.ToString(), searched, annotations);
                    return true;
                case "response.failed": case "response.incomplete": case "error":
                    throw new InvalidOperationException("The response could not be completed. Please try again.");
            }
            return false;
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (HandleEvent()) return completed!;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line[5..].TrimStart(' '));
        }
        if (HandleEvent()) return completed!;
        throw new InvalidOperationException("The response was interrupted before completion.");
    }

    static bool IsType(JsonElement item, string expected) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == expected;

    static ResponseCompletion Finish(JsonElement root, string deltas, bool searched, List<WebCitation> eventCitations)
    {
        var text = deltas;
        IReadOnlyList<WebCitation> citations = eventCitations.ToArray();
        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            if (response.TryGetProperty("status", out var status) && status.GetString() != "completed")
                throw new InvalidOperationException("The response could not be completed. Please try again.");
            if (response.TryGetProperty("output", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                JsonElement lastMessage = default;
                foreach (var item in items.EnumerateArray())
                {
                    if (IsType(item, "web_search_call") && item.TryGetProperty("status", out var state) && state.GetString() == "completed") searched = true;
                    if (IsType(item, "message")) lastMessage = item;
                }
                if (lastMessage.ValueKind == JsonValueKind.Object && lastMessage.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var final = new StringBuilder();
                    var finalCitations = new List<WebCitation>();
                    foreach (var part in content.EnumerateArray())
                    {
                        if (!IsType(part, "output_text")) continue;
                        if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String) final.Append(value.GetString());
                        if (part.TryGetProperty("annotations", out var links)) WebCitation.Collect(links, finalCitations);
                    }
                    text = final.ToString(); citations = finalCitations;
                }
            }
        }
        text = text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("The response contains no text.");
        return new(text, searched, citations);
    }
}
