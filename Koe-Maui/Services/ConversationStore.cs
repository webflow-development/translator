using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koe.Services;

public enum ConversationTurnKind { Translation, AiQuestion, AiAnswer }

public sealed record ConversationMessage(string Source, string Target, string Original, string Translation,
    string SourceTransliteration, string TargetTransliteration, ImageAttachment? Attachment = null,
    bool FromFirst = true, ConversationTurnKind Kind = ConversationTurnKind.Translation,
    IReadOnlyList<WebCitation>? Citations = null, bool WebSearchUsed = false);

public sealed record ConversationSnapshot(Guid Id, DateTimeOffset CreatedAt, string FirstLanguageCode,
    string SecondLanguageCode, IReadOnlyList<ConversationMessage> Messages)
{
    public string Draft { get; init; } = "";
    public ImageAttachment? DraftImage { get; init; }
    public bool IsAi { get; init; }
    public bool FromFirst { get; init; } = true;
    public string? LastSpeakerCode { get; init; }
    public bool HasContent => Messages.Count > 0 || !string.IsNullOrWhiteSpace(Draft) || DraftImage is not null;
}

public sealed record ConversationSummary(Guid Id, string Title, string Preview, string FirstLanguageCode,
    string SecondLanguageCode, DateTimeOffset UpdatedAt, int MessageCount, bool HasDraft);

// One atomic manifest per chat; photos are deduplicated inside that chat. No tokens,
// API keys or absolute iOS sandbox paths belong in the stored document.
public sealed class ConversationStore(string root, TimeProvider? clock = null)
{
    readonly string root = Path.GetFullPath(root);
    readonly TimeProvider clock = clock ?? TimeProvider.System;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly HashSet<Guid> deleted = [];
    public int UnreadableChats { get; private set; }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!Directory.Exists(root)) return [];
            var result = new List<ConversationSummary>();
            UnreadableChats = 0;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) || deleted.Contains(id)) continue;
                try
                {
                    var stored = await ReadManifestAsync(id);
                    if (stored is not null) result.Add(Summarize(stored));
                }
                catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
                { UnreadableChats++; } // Leave damaged files intact; never replace them with an empty chat.
            }
            return result.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(ConversationSnapshot snapshot)
    {
        if (snapshot.Id == Guid.Empty) throw new ArgumentException("A chat ID is required.");
        await gate.WaitAsync();
        try
        {
            if (deleted.Contains(snapshot.Id)) return; // A late save must never recreate a deleted chat.
            var directory = DirectoryFor(snapshot.Id);
            if (!snapshot.HasContent)
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            Directory.CreateDirectory(directory);
            var stored = new StoredConversation
            {
                Id = snapshot.Id, CreatedAt = snapshot.CreatedAt, UpdatedAt = clock.GetUtcNow(),
                FirstLanguageCode = snapshot.FirstLanguageCode, SecondLanguageCode = snapshot.SecondLanguageCode,
                IsAi = snapshot.IsAi, FromFirst = snapshot.FromFirst, LastSpeakerCode = snapshot.LastSpeakerCode,
                Draft = snapshot.Draft, DraftImage = await StoreImageAsync(directory, snapshot.DraftImage),
                Messages = []
            };
            foreach (var message in snapshot.Messages)
                stored.Messages.Add(new StoredMessage
                {
                    Source = message.Source, Target = message.Target, Original = message.Original, Translation = message.Translation,
                    SourceTransliteration = message.SourceTransliteration, TargetTransliteration = message.TargetTransliteration,
                    Attachment = await StoreImageAsync(directory, message.Attachment), FromFirst = message.FromFirst,
                    Kind = message.Kind, WebSearchUsed = message.WebSearchUsed, Citations = message.Citations?.ToList() ?? []
                });
            Validate(stored, snapshot.Id);
            var path = Path.Combine(directory, "conversation.json");
            var temporary = path + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, stored, ConversationJsonContext.Default.StoredConversation);
                    await stream.FlushAsync();
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path))
                {
                    // Back up only a valid committed manifest, preserving recovery from a corrupt primary.
                    try { _ = await ReadOneAsync(path, snapshot.Id); File.Copy(path, path + ".previous", overwrite: true); }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException) { }
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public async Task<ConversationSnapshot?> LoadAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            if (deleted.Contains(id)) return null;
            var saved = await ReadManifestAsync(id);
            if (saved is null) return null;
            var directory = DirectoryFor(id);
            var images = new Dictionary<string, ImageAttachment>();
            async Task<ImageAttachment?> LoadImage(StoredImage? stored)
            {
                if (stored is null) return null;
                if (stored.FileName.Length != 68 || !stored.FileName.EndsWith(".img", StringComparison.Ordinal) ||
                    stored.FileName[..64].Any(x => !Uri.IsHexDigit(x))) throw new InvalidDataException("Invalid image reference.");
                if (images.TryGetValue(stored.FileName, out var existing)) return existing;
                var imagePath = Path.Combine(directory, stored.FileName);
                if (new FileInfo(imagePath).Length > ImageAttachment.MaxImageBytes) throw new InvalidDataException("Invalid stored image size.");
                var bytes = await File.ReadAllBytesAsync(imagePath);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(stored.FileName[..64], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored image is damaged.");
                var image = new ImageAttachment(stored.Name, bytes);
                images.Add(stored.FileName, image);
                return image;
            }
            var messages = new List<ConversationMessage>();
            foreach (var message in saved.Messages)
                messages.Add(new(message.Source, message.Target, message.Original, message.Translation,
                    message.SourceTransliteration, message.TargetTransliteration, await LoadImage(message.Attachment),
                    message.FromFirst, message.Kind, message.Citations.Count == 0 ? null : message.Citations.ToArray(), message.WebSearchUsed));
            return new(id, saved.CreatedAt, saved.FirstLanguageCode, saved.SecondLanguageCode, messages)
            {
                Draft = saved.Draft, DraftImage = await LoadImage(saved.DraftImage), IsAi = saved.IsAi,
                FromFirst = saved.FromFirst, LastSpeakerCode = saved.LastSpeakerCode
            };
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var directory = DirectoryFor(id);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            deleted.Add(id);
        }
        finally { gate.Release(); }
    }

    string DirectoryFor(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A chat ID is required.");
        return Path.Combine(root, id.ToString("N"));
    }

    static async Task<StoredImage?> StoreImageAsync(string directory, ImageAttachment? image)
    {
        if (image is null) return null;
        var name = Convert.ToHexString(SHA256.HashData(image.Bytes)).ToLowerInvariant() + ".img";
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, image.Bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return new() { Name = image.Name, FileName = name };
    }

    async Task<StoredConversation?> ReadManifestAsync(Guid id)
    {
        var path = Path.Combine(DirectoryFor(id), "conversation.json");
        if (!File.Exists(path) && !File.Exists(path + ".previous")) return null;
        try { return await ReadOneAsync(path, id); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            if (!File.Exists(path + ".previous")) throw;
            return await ReadOneAsync(path + ".previous", id);
        }
    }

    static async Task<StoredConversation> ReadOneAsync(string path, Guid id)
    {
        await using var stream = File.OpenRead(path);
        var saved = await JsonSerializer.DeserializeAsync(stream, ConversationJsonContext.Default.StoredConversation)
            ?? throw new InvalidDataException("Chat file is empty.");
        Validate(saved, id);
        return saved;
    }

    static void Validate(StoredConversation saved, Guid id)
    {
        static void ValidateImage(StoredImage? image)
        {
            if (image is null) return;
            if (image.Name is null || image.FileName is null || image.FileName.Length != 68 ||
                !image.FileName.EndsWith(".img", StringComparison.Ordinal) || image.FileName[..64].Any(x => !Uri.IsHexDigit(x)))
                throw new InvalidDataException("Invalid stored image reference.");
        }
        if (saved.Version != 1) throw new NotSupportedException("This conversation requires a newer version of Koe.");
        if (saved.Id != id || saved.Messages is null || saved.Draft is null ||
            string.IsNullOrWhiteSpace(saved.FirstLanguageCode) || string.IsNullOrWhiteSpace(saved.SecondLanguageCode) ||
            saved.FirstLanguageCode == saved.SecondLanguageCode)
            throw new InvalidDataException("Unsupported or invalid chat file.");
        ValidateImage(saved.DraftImage);
        foreach (var message in saved.Messages)
        {
            if (message is null || message.Source is null || message.Target is null || message.Original is null ||
                message.Translation is null || message.SourceTransliteration is null || message.TargetTransliteration is null ||
                !Enum.IsDefined(message.Kind) || message.Citations is null)
                throw new InvalidDataException("Invalid stored message.");
            ValidateImage(message.Attachment);
            message.Citations = message.Citations.Select(x => x is null ? null : WebCitation.Create(x.Title, x.Url))
                .OfType<WebCitation>().ToList();
        }
    }

    static ConversationSummary Summarize(StoredConversation saved)
    {
        static string Compact(string value, int maximum) => string.Concat(
            string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).EnumerateRunes().Take(maximum).Select(x => x.ToString()));
        var first = saved.Messages.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Original));
        var last = saved.Messages.LastOrDefault();
        var hasDraft = !string.IsNullOrWhiteSpace(saved.Draft) || saved.DraftImage is not null;
        var title = first?.Original ?? (saved.Messages.Count > 0 ? "Photo conversation" : "New conversation");
        var preview = hasDraft ? "Draft: " + (string.IsNullOrWhiteSpace(saved.Draft) ? "Photo" : saved.Draft)
            : last is null ? "" : !string.IsNullOrWhiteSpace(last.Translation) ? last.Translation : last.Original;
        return new(saved.Id, Compact(title, 60), Compact(preview, 110), saved.FirstLanguageCode,
            saved.SecondLanguageCode, saved.UpdatedAt, saved.Messages.Count, hasDraft);
    }
}

internal sealed class StoredConversation
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string FirstLanguageCode { get; set; } = "";
    public string SecondLanguageCode { get; set; } = "";
    public List<StoredMessage> Messages { get; set; } = [];
    public string Draft { get; set; } = "";
    public StoredImage? DraftImage { get; set; }
    public bool IsAi { get; set; }
    public bool FromFirst { get; set; } = true;
    public string? LastSpeakerCode { get; set; }
}

internal sealed class StoredMessage
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";
    public string SourceTransliteration { get; set; } = "";
    public string TargetTransliteration { get; set; } = "";
    public StoredImage? Attachment { get; set; }
    public bool FromFirst { get; set; } = true;
    public ConversationTurnKind Kind { get; set; }
    public List<WebCitation> Citations { get; set; } = [];
    public bool WebSearchUsed { get; set; }
}

internal sealed class StoredImage
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
}

[JsonSerializable(typeof(StoredConversation))]
internal partial class ConversationJsonContext : JsonSerializerContext { }
