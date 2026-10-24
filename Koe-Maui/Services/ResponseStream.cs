using System.Text;
using System.Text.Json;

namespace Koe.Services;

internal static class ResponseStream
{
    public static async Task<string> ReadAsync(TextReader reader, CancellationToken ct)
    {
        var output = new StringBuilder();
        var data = new List<string>();
        bool HandleEvent()
        {
            if (data.Count == 0) return false;
            var json = string.Join("\n", data); data.Clear();
            if (json == "[DONE]") return false;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "response.output_text.delta": output.Append(root.GetProperty("delta").GetString()); break;
                case "response.completed": return true;
                case "response.failed": case "response.incomplete": case "error":
                    throw new InvalidOperationException("The translation could not be completed. Please try again.");
            }
            return false;
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (HandleEvent()) return Finish(output);
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line[5..].TrimStart(' '));
        }
        if (HandleEvent()) return Finish(output);
        throw new InvalidOperationException("Die Übersetzung wurde vor dem Abschluss unterbrochen.");
    }

    static string Finish(StringBuilder output)
    {
        var text = output.ToString().Trim();
        if (text.Length == 0) throw new InvalidOperationException("The response contains no translation.");
        return text;
    }
}
