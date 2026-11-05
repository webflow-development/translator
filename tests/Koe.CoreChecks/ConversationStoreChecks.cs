using System.Text.Json.Nodes;
using System.Text;
using Koe.Services;

static class ConversationStoreChecks
{
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aFVEAAAAASUVORK5CYII=");
    static ConversationSnapshot Chat(string text = "Hello!", Guid? id = null) => new(id ?? Guid.NewGuid(), DateTimeOffset.UtcNow,
        "en", "ja", [new("English", "Japanese", text, "こんにちは！", "", "Konnichiwa!")]);

    static async Task WithStore(Func<ConversationStore, string, ChatClock, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "koe-conversation-check-" + Guid.NewGuid().ToString("N"));
        var clock = new ChatClock();
        try { await test(new(directory, clock), directory, clock); }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(absolute) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(absolute).StartsWith("koe-conversation-check-", StringComparison.Ordinal))
                throw new Exception("Unsafe test cleanup path.");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
        }
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("Chats: a fresh installation and an unused + chat stay empty", () => WithStore(async (store, root, _) =>
        {
            if ((await store.ListAsync()).Count != 0) throw new Exception();
            await store.SaveAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "en", "ja", []));
            if (Directory.Exists(root)) throw new Exception("Empty chats must not create files.");
        }));
        await check("Chats: Unicode, readings and corrected text survive an app restart", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat("Corrected grammar.\nこんにちは。 Grüezi 👋");
            await store.SaveAsync(chat);
            var restored = await new ConversationStore(root).LoadAsync(chat.Id) ?? throw new Exception();
            if (restored.Messages[0] != chat.Messages[0] || restored.CreatedAt != chat.CreatedAt) throw new Exception();
        }));
        await check("Chats: language pair, AI selection and active speaker are restored", () => WithStore(async (store, _, _) =>
        {
            var chat = Chat() with { FirstLanguageCode = "de-CH", SecondLanguageCode = "es", IsAi = true, FromFirst = false, LastSpeakerCode = "es" };
            await store.SaveAsync(chat);
            var restored = await store.LoadAsync(chat.Id) ?? throw new Exception();
            var mode = new ConversationMode(); mode.Restore(restored.IsAi, restored.FromFirst, restored.LastSpeakerCode);
            if (restored.FirstLanguageCode != "de-CH" || restored.SecondLanguageCode != "es" || !mode.IsAi || mode.FromFirst || mode.LastSpeakerCode != "es") throw new Exception();
        }));
        await check("Chats: a draft-only conversation and selected photo survive restart", () => WithStore(async (store, root, _) =>
        {
            var chat = new ConversationSnapshot(Guid.NewGuid(), DateTimeOffset.UtcNow, "en", "ja", [])
                { Draft = "Not sent yet", DraftImage = new("menu.png", Png) };
            await store.SaveAsync(chat);
            var restored = await new ConversationStore(root).LoadAsync(chat.Id) ?? throw new Exception();
            if (restored.Draft != chat.Draft || !restored.DraftImage!.Bytes.SequenceEqual(Png)) throw new Exception();
            var summary = (await store.ListAsync()).Single();
            if (!summary.HasDraft || summary.Preview != "Draft: Not sent yet") throw new Exception();
        }));
        await check("Chats: repeated photos are stored once and hydrated in both messages", () => WithStore(async (store, root, _) =>
        {
            var image = new ImageAttachment("menu.png", Png);
            var turn = new ConversationMessage("English", "Japanese", "Photo", "写真", "", "Shashin", image);
            var chat = Chat() with { Messages = new[] { turn, turn }, DraftImage = image };
            await store.SaveAsync(chat);
            var restored = await new ConversationStore(root).LoadAsync(chat.Id) ?? throw new Exception();
            if (Directory.GetFiles(Path.Combine(root, chat.Id.ToString("N")), "*.img").Length != 1 ||
                !restored.Messages[0].Attachment!.Bytes.SequenceEqual(Png) ||
                !ReferenceEquals(restored.Messages[0].Attachment, restored.Messages[1].Attachment)) throw new Exception();
        }));
        await check("Chats: bilingual AI answers retain citations and follow-up context", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat() with { Messages = new ConversationMessage[]
            {
                new("English", "", "Where is Kyoto?", "", "", "", Kind: ConversationTurnKind.AiQuestion),
                new("English", "Japanese", "Kyoto is in Japan.", "京都は日本にあります。", "", "Kyōto wa Nihon ni arimasu.",
                    Kind: ConversationTurnKind.AiAnswer, Citations: new[] { new WebCitation("Source", "https://example.org/kyoto") }, WebSearchUsed: true)
            }};
            await store.SaveAsync(chat);
            var restored = await new ConversationStore(root).LoadAsync(chat.Id) ?? throw new Exception();
            var answer = restored.Messages[1];
            if (answer.Kind != ConversationTurnKind.AiAnswer || !answer.WebSearchUsed || answer.Citations?.Single().Url != "https://example.org/kyoto") throw new Exception();
            var context = AiChatRequest.RecentContext(restored.Messages.Select(x => new AiContextMessage(x.Kind == ConversationTurnKind.AiAnswer ? "assistant" : "user", x.Original)));
            if (context.Count != 2 || context[1].Text != "Kyoto is in Japan.") throw new Exception();
        }));
        await check("Chats: conversations remain isolated and sort by their latest saved change", () => WithStore(async (store, _, clock) =>
        {
            var one = Chat("First conversation"); var two = Chat("Second conversation") with { FirstLanguageCode = "es", Draft = "Only in second" };
            await store.SaveAsync(one); clock.Advance(); await store.SaveAsync(two);
            if ((await store.ListAsync())[0].Id != two.Id) throw new Exception();
            clock.Advance(); await store.SaveAsync(one with { Draft = "Only in first" });
            var list = await store.ListAsync();
            if (list.Count != 2 || list[0].Id != one.Id || (await store.LoadAsync(two.Id))!.Draft != "Only in second") throw new Exception();
        }));
        await check("Chats: discarding the only draft removes its saved text and photos", () => WithStore(async (store, root, _) =>
        {
            var chat = new ConversationSnapshot(Guid.NewGuid(), DateTimeOffset.UtcNow, "en", "ja", []) { Draft = "Discard this", DraftImage = new("photo.png", Png) };
            await store.SaveAsync(chat);
            await store.SaveAsync(chat with { Draft = "", DraftImage = null });
            if ((await store.ListAsync()).Count != 0 || Directory.Exists(Path.Combine(root, chat.Id.ToString("N")))) throw new Exception();
        }));
        await check("Chats: delete removes messages, backups and photos without touching another chat", () => WithStore(async (store, root, _) =>
        {
            var one = Chat() with { DraftImage = new("photo.png", Png) }; var two = Chat("Keep me");
            await store.SaveAsync(one); await store.SaveAsync(one with { Draft = "A backup exists" }); await store.SaveAsync(two);
            await store.DeleteAsync(one.Id);
            if (Directory.Exists(Path.Combine(root, one.Id.ToString("N"))) || (await store.ListAsync()).Single().Id != two.Id) throw new Exception();
            await store.SaveAsync(one); // A queued pre-delete snapshot must not resurrect the chat.
            if (await store.LoadAsync(one.Id) is not null || (await store.ListAsync()).Count != 1) throw new Exception();
        }));
        await check("Chats: concurrent saves and a delete cannot resurrect deleted data", () => WithStore(async (store, _, _) =>
        {
            var chat = Chat(); await store.SaveAsync(chat);
            await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.SaveAsync(chat with { Draft = i.ToString() })).Append(store.DeleteAsync(chat.Id)));
            if ((await store.ListAsync()).Count != 0) throw new Exception();
        }));
        await check("Chats: a corrupt primary manifest recovers the last committed backup", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat("Last valid version"); await store.SaveAsync(chat); await store.SaveAsync(chat with { Draft = "Newer" });
            var path = Path.Combine(root, chat.Id.ToString("N"), "conversation.json");
            await File.WriteAllTextAsync(path, "{interrupted");
            var restarted = new ConversationStore(root);
            if ((await restarted.LoadAsync(chat.Id))!.Messages[0].Original != "Last valid version") throw new Exception();
            await restarted.SaveAsync(chat with { Draft = "Recovered safely" });
            if ((await restarted.LoadAsync(chat.Id))!.Draft != "Recovered safely") throw new Exception();
        }));
        await check("Chats: an incomplete temporary write does not replace committed messages", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat(); await store.SaveAsync(chat);
            await File.WriteAllTextAsync(Path.Combine(root, chat.Id.ToString("N"), "conversation.json.tmp"), "{incomplete");
            if ((await new ConversationStore(root).LoadAsync(chat.Id))!.Messages[0].Original != "Hello!") throw new Exception();
        }));
        await check("Chats: corrupt and future-version chats are left intact while healthy chats load", () => WithStore(async (store, root, _) =>
        {
            var bad = Chat(); var good = Chat("Keep visible"); await store.SaveAsync(bad); await store.SaveAsync(good);
            var path = Path.Combine(root, bad.Id.ToString("N"), "conversation.json");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["Version"] = 99;
            await File.WriteAllTextAsync(path, json.ToJsonString());
            if ((await store.ListAsync()).Single().Id != good.Id || store.UnreadableChats != 1 || !File.Exists(path)) throw new Exception();
        }));
        await check("Chats: unsafe source links are removed when loading a saved answer", () => WithStore(async (store, _, _) =>
        {
            var chat = Chat() with { Messages = new[] { new ConversationMessage("English", "Japanese", "Answer", "回答", "", "", Kind: ConversationTurnKind.AiAnswer,
                Citations: new[] { new WebCitation("Bad", "file:///etc/passwd"), new WebCitation("Good", "https://example.org") }) } };
            await store.SaveAsync(chat);
            if ((await store.LoadAsync(chat.Id))!.Messages[0].Citations!.Count != 1) throw new Exception();
        }));
        await check("Chats: a newer format cannot be overwritten by recovering an older backup", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat(); await store.SaveAsync(chat); await store.SaveAsync(chat with { Draft = "Backup" });
            var path = Path.Combine(root, chat.Id.ToString("N"), "conversation.json");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["Version"] = 99;
            var original = json.ToJsonString(); await File.WriteAllTextAsync(path, original);
            if ((await store.ListAsync()).Count != 0) throw new Exception();
            try { await store.SaveAsync(chat); throw new Exception("Newer data was overwritten."); }
            catch (NotSupportedException) { }
            if (await File.ReadAllTextAsync(path) != original) throw new Exception();
        }));
        await check("Chats: image references cannot escape their conversation folder", () => WithStore(async (store, root, _) =>
        {
            var chat = Chat() with { DraftImage = new("image.png", Png) }; await store.SaveAsync(chat);
            var path = Path.Combine(root, chat.Id.ToString("N"), "conversation.json");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["DraftImage"]!["FileName"] = "../outside.png";
            await File.WriteAllTextAsync(path, json.ToJsonString());
            try { await store.LoadAsync(chat.Id); throw new Exception("Unsafe image accepted."); }
            catch (InvalidDataException) { }
        }));
        await check("Chats: summaries collapse whitespace and keep emoji surrogate pairs intact", () => WithStore(async (store, _, _) =>
        {
            var text = string.Concat(Enumerable.Repeat("👋", 70)); var chat = Chat(text + "\n  Next line"); await store.SaveAsync(chat);
            var summary = (await store.ListAsync()).Single();
            if (summary.Title.EnumerateRunes().Count() != 60 || summary.Title.Contains('\n') || summary.Title.Contains('�')) throw new Exception();
        }));
    }

    sealed class ChatClock : TimeProvider
    {
        DateTimeOffset now = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now += TimeSpan.FromMinutes(1);
    }
}
