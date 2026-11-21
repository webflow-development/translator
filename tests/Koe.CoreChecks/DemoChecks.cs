using Koe.Services;

internal static class DemoChecks
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var english = new ConversationLanguage("en", "English", "en-US");
        var japanese = new ConversationLanguage("ja", "Japanese", "ja-JP");
        await check("Demo translation preserves each direction and its independent reading", () =>
        {
            foreach (var phrase in DemoConversation.HotelPhrases)
            {
                var outgoing = DemoConversation.Translate(phrase.English, english, japanese);
                var incoming = DemoConversation.Translate(phrase.Japanese, japanese, english);
                if (outgoing.Translation != phrase.Japanese || outgoing.CorrectedOriginal != phrase.English ||
                    outgoing.SourceTransliteration != "" || outgoing.TargetTransliteration != phrase.JapaneseTransliteration ||
                    incoming.Translation != phrase.English || incoming.SourceTransliteration != phrase.JapaneseTransliteration ||
                    incoming.TargetTransliteration != "") throw new Exception("Demo language direction changed.");
            }
            return Task.CompletedTask;
        });
        await check("Demo phrase matching tolerates dictation punctuation and Unicode width", () =>
        {
            var result = DemoConversation.Translate("  ＨＥＬＬＯ,\n I have a reservation!  ", english, japanese);
            var incoming = DemoConversation.Translate("氷なしの水をいただけますか。", japanese, english);
            if (result.CorrectedOriginal != DemoConversation.HotelPhrases[0].English ||
                incoming.Translation != DemoConversation.HotelPhrases[1].English) throw new Exception();
            return Task.CompletedTask;
        });
        await check("Demo menu text is translated independently from the typed message direction", () =>
        {
            var photo = DemoConversation.Translate("", english, japanese, hasSampleImage: true);
            var mixed = DemoConversation.Translate(DemoConversation.HotelPhrases[3].Japanese, japanese, english, hasSampleImage: true);
            if (photo.Translation != "" || photo.CorrectedOriginal != "" || photo.ImageText?.Original != DemoConversation.MenuOriginal ||
                photo.ImageText.Source != "ja" || photo.ImageText.Target != "en" ||
                mixed.Translation != DemoConversation.HotelPhrases[3].English ||
                mixed.ImageText?.Translation != DemoConversation.MenuTranslation) throw new Exception();
            return Task.CompletedTask;
        });
        await check("Demo respects disabled transliteration in messages, photos and AI replies", () =>
        {
            var phrase = DemoConversation.Translate(DemoConversation.HotelPhrases[0].English, english, japanese, false, true);
            var ai = DemoConversation.Ask(DemoConversation.AiQuestionJapanese, english, japanese, false);
            if (phrase.SourceTransliteration != "" || phrase.TargetTransliteration != "" ||
                phrase.ImageText?.SourceTransliteration != "" || phrase.ImageText.TargetTransliteration != "" ||
                ai.FirstTransliteration != "" || ai.SecondTransliteration != "") throw new Exception();
            return Task.CompletedTask;
        });
        await check("Demo AI orders its bilingual answer from the latest question language", () =>
        {
            var outgoing = DemoConversation.Ask(DemoConversation.AiQuestionEnglish, japanese, english);
            var incoming = DemoConversation.Ask(DemoConversation.AiQuestionJapanese, english, japanese);
            var followUp = DemoConversation.Ask(DemoConversation.AiFollowUpEnglish, english, japanese);
            if (outgoing.FirstLanguageCode != "en" || outgoing.InputLanguageCode != "en" || outgoing.FirstTransliteration != "" ||
                incoming.FirstLanguageCode != "ja" || incoming.InputLanguageCode != "ja" || incoming.SecondTransliteration != "" ||
                outgoing.FirstAnswer != incoming.SecondAnswer || outgoing.SecondAnswer != incoming.FirstAnswer ||
                followUp.FirstAnswer == outgoing.FirstAnswer) throw new Exception();
            if (outgoing.WebSearchUsed || incoming.WebSearchUsed || outgoing.Citations.Count != 0 || incoming.Citations.Count != 0)
                throw new Exception("Prepared demo claimed a live web search.");
            return Task.CompletedTask;
        });
        await check("Demo rejects unsupported text, mismatched directions and language pairs", () =>
        {
            Reject(() => DemoConversation.Translate("Please give me your bank account.", english, japanese));
            Reject(() => DemoConversation.Translate(DemoConversation.HotelPhrases[0].English, japanese, english));
            Reject(() => DemoConversation.Translate("", english, japanese));
            Reject(() => DemoConversation.Ask("What is today's exchange rate?", english, japanese));
            Reject(() => DemoConversation.Translate("Thank you.", english, english));
            Reject(() => DemoConversation.Ask(DemoConversation.AiQuestionEnglish, english, new("de", "German", "de-DE")));
            return Task.CompletedTask;
        });
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("demo", StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("The demo accepted an unsupported request.");
    }
}
