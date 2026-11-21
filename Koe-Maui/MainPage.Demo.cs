using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed partial class MainPage
{
    readonly ConversationStore demoConversationStore = new(System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, "demo-conversations"));
    readonly ConversationListView demoConversationList;
    readonly Border demoControls;
    readonly Button demoExample = new() { Text = "Example", FontSize = 14, BackgroundColor = Colors.White, TextColor = Ink, CornerRadius = 16, HeightRequest = 38, Padding = new Thickness(12, 0) };
    readonly Button demoExit = new() { Text = "Exit", FontSize = 14, BackgroundColor = Colors.Transparent, TextColor = Ink, HeightRequest = 38, Padding = new Thickness(10, 0) };
    readonly Label demoInstruction = new() { TextColor = Color.FromArgb("#205C3B"), FontSize = 12 };
    readonly Dictionary<string, string> demoVoices = [];
    ConversationSnapshot? normalConversation;
    bool normalShowingConversations;
    string normalTextSize = "";
    bool normalTransliteration;
    ImageAttachment? demoMenu;
    string? demoPrefilledText;

    public bool IsDemo { get; private set; }
    ConversationStore conversationStore => IsDemo ? demoConversationStore : normalConversationStore;
    ConversationListView conversationList => IsDemo ? demoConversationList : normalConversationList;
    string SelectedVoice(string language) => IsDemo ? demoVoices.GetValueOrDefault(language, "") : Preferences.Default.Get("koe.voice." + language, "");

    Border CreateDemoControls()
    {
        var row = new Grid { ColumnSpacing = 4, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        row.Add(new Label { Text = "Demo · sample responses", TextColor = Ink, FontSize = 14, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        row.Add(demoExample, 1, 0);
        row.Add(demoExit, 2, 0);
        demoExample.Clicked += async (_, _) => await ChooseDemoExampleAsync();
        demoExit.Clicked += async (_, _) => await ExitDemoAsync();
        SemanticProperties.SetDescription(demoExample, "Choose a prepared demo phrase, menu image or AI question");
        SemanticProperties.SetDescription(demoExit, "Exit demo and return to your conversations");
        return new Border
        {
            IsVisible = false, Margin = new Thickness(14, 4), Padding = new Thickness(10, 6),
            BackgroundColor = Color.FromArgb("#DDF1DF"), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout { Spacing = 2, Children = { row, demoInstruction } }
        };
    }

    public async Task StartDemoAsync()
    {
        if (IsDemo || busy || leavingConversation) return;
        leavingConversation = true;
        UpdateControls();
        try
        {
            draft.Unfocus();
            if (recording.IsRecording) await recording.PauseAsync();
            playback.Stop();
            await FlushConversationAsync();
            normalConversation = CaptureConversation();
            normalShowingConversations = showingConversations;
            normalTextSize = TextSizeLabel;
            normalTransliteration = showTransliteration;
            IsDemo = true;
            ApplyConversation(null);
            showingConversations = false;
            SetDemoExampleDraft(DemoConversation.HotelPhrases[0].English);
            SetStatus("Prepared English–Japanese example. Edit it or read it aloud, then tap ↑. Choose Example for more.");
            await FlushConversationAsync();
        }
        catch (Exception)
        {
            if (IsDemo) RestoreNormalConversation();
            SetStatus("Could not open the demo. Your conversation has been kept. Please try again.");
        }
        finally { leavingConversation = false; UpdateControls(); }
    }

    public async Task ExitDemoAsync()
    {
        if (!IsDemo || busy || leavingConversation) return;
        leavingConversation = true;
        UpdateControls();
        try
        {
            draft.Unfocus();
            if (recording.IsRecording) await recording.PauseAsync();
            playback.Stop();
            await FlushConversationAsync();
            RestoreNormalConversation();
            if (showingConversations) await normalConversationList.RefreshAsync();
        }
        catch (Exception) { SetStatus("Could not save the demo chat. Please try exiting again."); }
        finally { leavingConversation = false; UpdateControls(); }
        if (!IsDemo) await RefreshModelsAsync();
    }

    void RestoreNormalConversation()
    {
        IsDemo = false;
        ApplyConversation(normalConversation);
        showingConversations = normalShowingConversations;
        normalConversation = null;
        SetTextSize(normalTextSize);
        SetTransliteration(normalTransliteration);
    }

    async Task ChooseDemoExampleAsync(string? example = null)
    {
        if (!IsDemo || busy || leavingConversation) return;
        leavingConversation = true;
        UpdateControls();
        try
        {
            draft.Unfocus();
            if (recording.IsRecording) await recording.PauseAsync();
            playback.Stop();
            await FlushConversationAsync();
            example ??= await DisplayActionSheetAsync("Prepared demo examples", "Cancel", null, "Hotel conversation", "Menu photo", "Ask AI", "AI follow-up");
            string text;
            ImageAttachment? image = null;
            var source = SourceLanguage;
            switch (example)
            {
                case "Hotel conversation":
                    var phrases = DemoConversation.HotelPhrases.Select(phrase => source.Code == "ja" ? phrase.Japanese : phrase.English).ToArray();
                    var choice = await DisplayActionSheetAsync(source.Name + " · read or send an example", "Cancel", null, phrases);
                    if (!phrases.Contains(choice)) return;
                    text = choice;
                    break;
                case "Menu photo":
                    image = await LoadDemoMenuAsync();
                    text = "";
                    break;
                case "Ask AI":
                    text = source.Code == "ja" ? DemoConversation.AiQuestionJapanese : DemoConversation.AiQuestionEnglish;
                    break;
                case "AI follow-up":
                    text = source.Code == "ja" ? DemoConversation.AiFollowUpJapanese : DemoConversation.AiFollowUpEnglish;
                    break;
                default:
                    return;
            }
            recording.Clear();
            if (example is "Ask AI" or "AI follow-up") mode.SelectAi();
            else mode.SelectSpeaker(source.Code == first.Code, source.Code);
            SetDemoExampleDraft(text);
            SetAttachment(image);
            ShowConversationArea();
            SetStatus(image is null ? "Prepared example loaded. Edit or record it, then tap ↑." : "Bundled menu sample. Tap ↑ for the prepared translation.");
            await FlushConversationAsync();
        }
        catch (Exception) { SetStatus("Could not load the demo example. Please try again."); }
        finally { leavingConversation = false; UpdateControls(); }
    }

    async Task<ImageAttachment> LoadDemoMenuAsync()
    {
        if (demoMenu is not null) return demoMenu;
        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync("demo-menu.png");
        demoMenu = new ImageAttachment("Demo menu", await ImageAttachment.ReadSourceAsync(stream));
        return demoMenu;
    }

    void SetDemoExampleDraft(string text)
    {
        demoPrefilledText = text;
        demoInstruction.Text = string.IsNullOrEmpty(text) ? "Send the bundled menu to see its prepared translation." : "Say or send “" + text + "”";
        recording.Edit(text);
    }

    async Task<bool> IsDemoMenuAsync(ImageAttachment? image)
    {
        if (image is null) return false;
        var sample = await LoadDemoMenuAsync();
        if (!image.Bytes.AsSpan().SequenceEqual(sample.Bytes))
            throw new InvalidOperationException("Choose Example → Menu photo to use the prepared demo image.");
        return true;
    }
}
