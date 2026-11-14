using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed record ConversationTurn(string Source, string Target, string Original, string Translation, string SourceTransliteration, string TargetTransliteration, ImageAttachment? Attachment = null, bool FromFirst = true, ConversationTurnKind Kind = ConversationTurnKind.Translation, IReadOnlyList<WebCitation>? Citations = null, bool WebSearchUsed = false)
{
    public string Direction => Kind == ConversationTurnKind.AiAnswer ? (WebSearchUsed ? "AI · Web search" : "AI") : Kind == ConversationTurnKind.AiQuestion ? $"You → AI · {Source}" : string.IsNullOrWhiteSpace(Target) ? Source : $"{Source}  →  {Target}";
    public bool IsAi => Kind != ConversationTurnKind.Translation;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);
    public string TranslatedReadingCaption => "Transliteration · " + TargetTransliteration;
    public string OriginalReadingCaption => "Transliteration · " + SourceTransliteration;
    public bool TranslationHasReading => !string.IsNullOrWhiteSpace(TargetTransliteration);
    public bool OriginalHasReading => !string.IsNullOrWhiteSpace(SourceTransliteration);
    public ImageSource? Picture => Attachment is null ? null : ImageSource.FromStream(() => new MemoryStream(Attachment.Bytes, writable: false));
    public bool HasPicture => Attachment is not null;
    public bool HasOriginal => !string.IsNullOrWhiteSpace(Original);
}

sealed class ConversationTemplateSelector(DataTemplate translation, DataTemplate question, DataTemplate answer) : DataTemplateSelector
{
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item is ConversationTurn turn
        ? turn.Kind switch { ConversationTurnKind.AiQuestion => question, ConversationTurnKind.AiAnswer => answer, _ => translation }
        : translation;
}

public sealed class MainPage : ContentPage
{
    static readonly Color Ink = Color.FromArgb("#19384E"), Muted = Color.FromArgb("#638295"), Blue = Color.FromArgb("#568FBE");
    readonly ChatGptAuth auth;
    readonly OpenAiTranslator translator;
    readonly SpeechDraft recording;
    readonly SpeechOutput playback;
    readonly OpenAiVoiceSettings voiceSettings;
    readonly ConversationStore conversationStore;
    readonly ConversationListView conversationList;
    readonly VerticalStackLayout composer;
    readonly ImageButton chatBack = new() { Source = "back.png", WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 11, BackgroundColor = Color.FromArgb("#E8F2F9") };
    readonly ImageButton newConversation = new() { Source = "compose.png", WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 10, BackgroundColor = Color.FromArgb("#E8F2F9") };
    bool showingConversations = true, initializedConversations;
    Guid conversationId = Guid.NewGuid();
    DateTimeOffset conversationCreatedAt = DateTimeOffset.UtcNow;
    bool conversationDirty, restoringConversation, leavingConversation;
    int saveRevision;
    Task pendingSave = Task.CompletedTask;
    INavigation? settingsNavigation;
    int playbackRevision;
    readonly ObservableCollection<ConversationTurn> turns = [];
    readonly Label liveCaption = new() { FontSize = 11, TextColor = Muted };
    readonly Label welcomeHint = new() { FontSize = 15, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.2 };
    readonly Label attachmentHint = new() { FontSize = 11, TextColor = Muted };
    readonly Label liveTranslation = new() { TextColor = Ink, FontSize = 18, LineHeight = 1.15 };
    readonly Border livePreview;
    readonly CollectionView messages;
    readonly Label firstGreeting = new() { FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Blue, HorizontalTextAlignment = TextAlignment.Center };
    readonly Label secondGreeting = new() { FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Blue, HorizontalTextAlignment = TextAlignment.Center };
    int scrollRevision;
    string textSize = Preferences.Default.Get("koe.text-size", "Mittel");
    bool showTransliteration = Preferences.Default.Get("koe.show-transliteration", Preferences.Default.Get("koe.show-romaji", true));
    string TextSizeLabel => textSize == "Klein" ? "Small" : textSize == "Groß" ? "Large" : "Medium";
    public bool ShowTransliteration => showTransliteration;
    public double MessageFontSize => textSize == "Klein" ? 16 : textSize == "Groß" ? 23 : 18;
    public double OriginalFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 19 : 14;
    public double ReadingFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 18 : 14;
    readonly ConversationEditor draft = new() { Placeholder = "Type a message …", PlaceholderColor = Muted, TextColor = Ink, BackgroundColor = Colors.Transparent, FontSize = 16, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44, MaximumHeightRequest = 104 };
    readonly Button attach = new() { Text = "+", FontSize = 28, WidthRequest = 40, HeightRequest = 44, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Blue, VerticalOptions = LayoutOptions.Start };
    readonly Image attachmentPreview = new() { WidthRequest = 70, HeightRequest = 70, Aspect = Aspect.AspectFill };
    readonly Label attachmentName = new() { FontSize = 12, TextColor = Ink, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    readonly Button removeAttachment = new() { Text = "×", FontSize = 24, WidthRequest = 40, HeightRequest = 40, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Muted };
    readonly Border attachmentCard;
    ImageAttachment? pendingImage;
    bool pickingAttachment;
    readonly Button connect = new() { Text = "Continue with ChatGPT", BackgroundColor = Blue, TextColor = Colors.White, CornerRadius = 18, FontSize = 14 };
    const string SignInExplanation = "Sign in with ChatGPT to enable recording, translations and AI replies. No separate Koe account is needed.";
    readonly Label signInExplanation = new() { Text = SignInExplanation, TextColor = Muted, FontSize = 13 };
    bool promptingForSignIn;
    readonly Button send = new() { Text = "↑", FontSize = 30, WidthRequest = 56, HeightRequest = 56, CornerRadius = 28, Padding = 0, BackgroundColor = Blue, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
    readonly Button shutter = new() { ImageSource = "microphone.png", FontSize = 28, WidthRequest = 78, HeightRequest = 78, CornerRadius = 39, Padding = 0, BackgroundColor = Color.FromArgb("#CF4F55"), TextColor = Colors.White };
    readonly Button cancel = new() { Text = "×  Discard input", FontSize = 14, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#B63E46"), IsVisible = false, MinimumHeightRequest = 44, Padding = new Thickness(12, 6) };
    readonly Button firstSpeaker = LanguageButton("English"), secondSpeaker = LanguageButton("Japanese");
    readonly Button aiSpeaker = new() { Text = "AI", FontSize = 14, FontAttributes = FontAttributes.Bold, WidthRequest = 52, HeightRequest = 36, CornerRadius = 18, Padding = 0, TextColor = Color.FromArgb("#205C3B"), BackgroundColor = Colors.White, BorderColor = Color.FromArgb("#9ACCAF"), BorderWidth = 1 };
    readonly Label status = new() { FontSize = 12, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
    TranslationModel[] availableModels = [];
    bool updatingModels, loadingModels;
    string selectedModelId = Preferences.Default.Get("koe.model", "");
    readonly Label prompt = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ink };
    readonly Label hint = new() { FontSize = 12, TextColor = Muted };
    readonly ActivityIndicator progress = new() { Color = Colors.White, WidthRequest = 42, HeightRequest = 42, IsVisible = false, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true, ZIndex = 1 };
    readonly Border signInCard;
    readonly Border shutterRing;
    CancellationTokenSource? operation;
    bool busy, pageVisible, syncingDraft, pausingForEdit, recognizingSpeech;
    bool discardingInput;
    readonly ConversationMode mode = new();
    ConversationLanguage first = ConversationLanguage.Get(Preferences.Default.Get("koe.language.first", "en"));
    ConversationLanguage second = ConversationLanguage.Get(Preferences.Default.Get("koe.language.second", "ja"));
    ConversationLanguage SourceLanguage => mode.FromFirst ? first : second;
    ConversationLanguage TargetLanguage => mode.FromFirst ? second : first;

    public MainPage(ChatGptAuth auth, OpenAiTranslator translator, ISpeechCapture speech, SpeechOutput playback, OpenAiVoiceSettings voiceSettings, ConversationStore conversationStore)
    {
        if (first.Code == second.Code) second = ConversationLanguage.All.First(x => x.Code != first.Code);
        this.auth = auth; this.translator = translator; this.playback = playback; this.voiceSettings = voiceSettings;
        this.conversationStore = conversationStore; recording = new SpeechDraft(speech);
        NavigationPage.SetHasNavigationBar(this, false);
        Title = "Koe"; BackgroundColor = Color.FromArgb("#F0F6FA");
        // Resize the whole chat above the keyboard, including the composer and send button.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        var menu = new ImageButton { Source = "more.png", BackgroundColor = Color.FromArgb("#E8F2F9"), WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 10 };
        SemanticProperties.SetDescription(menu, "Settings");
        menu.Clicked += async (_, _) => { if (!busy && !leavingConversation && !recording.HasTurn) await ShowSettingsAsync(); };
        newConversation.Clicked += async (_, _) => await StartNewConversationAsync();
        SemanticProperties.SetDescription(newConversation, "Start a new conversation");
        var header = new Grid
        {
            Padding = new Thickness(18, 6, 18, 8), ColumnSpacing = 0, BackgroundColor = Colors.White,
            ColumnDefinitions = { new(44), new(GridLength.Star), new(44) }
        };
        SemanticProperties.SetDescription(chatBack, "Show saved conversations");
        chatBack.Clicked += async (_, _) => await BackToChatsAsync();
        // Equal side slots keep the title centered as the left button changes.
        header.Add(chatBack, 0, 0);
        header.Add(newConversation, 0, 0);
        header.Add(new Label { Text = "Koe", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Ink, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, MaxLines = 1 }, 1, 0);
        header.Add(menu, 2, 0);
        signInCard = new Border
        {
            Margin = new Thickness(16, 10), Padding = 14, StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E1EEF8"), StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new VerticalStackLayout { Spacing = 9, Children = { new Label { Text = "ChatGPT sign-in required", TextColor = Ink, FontSize = 17, FontAttributes = FontAttributes.Bold }, signInExplanation, connect } }
        };
        messages = new CollectionView
        {
            Margin = new Thickness(14, 0), ItemsSource = turns, SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            EmptyView = new VerticalStackLayout { Padding = new Thickness(30, 42), Spacing = 12, Children =
            {
                firstGreeting, secondGreeting,
                welcomeHint
            } },
            ItemTemplate = new ConversationTemplateSelector(new DataTemplate(() => CreateBubble(ConversationTurnKind.Translation)), new DataTemplate(() => CreateBubble(ConversationTurnKind.AiQuestion)), new DataTemplate(() => CreateBubble(ConversationTurnKind.AiAnswer)))
        };
        conversationList = new ConversationListView(conversationStore, OpenConversationInlineAsync);
        var conversationArea = new Grid { Children = { messages, conversationList } };
        var languages = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            }
        };
        languages.Add(firstSpeaker, 0, 0);
        languages.Add(aiSpeaker, 1, 0);
        languages.Add(secondSpeaker, 2, 0);
        var input = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        input.Add(attach, 0, 0); input.Add(draft, 1, 0);
        var attachmentRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        attachmentRow.Add(attachmentPreview, 0, 0);
        attachmentRow.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 4, Children = { attachmentName, attachmentHint } }, 1, 0);
        attachmentRow.Add(removeAttachment, 2, 0);
        attachmentCard = new Border { IsVisible = false, Padding = 8, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE8F0"), StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = attachmentRow };
        var inputBorder = new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE8F0"), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Padding = new Thickness(12, 4, 5, 4), Content = input };
        shutterRing = new Border { WidthRequest = 94, HeightRequest = 94, Padding = 6, StrokeThickness = 2, Stroke = Blue, StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = shutter };
        prompt.HorizontalTextAlignment = TextAlignment.Center;
        hint.HorizontalTextAlignment = TextAlignment.Center;
        var turnCaption = new VerticalStackLayout { Spacing = 4, Children = { prompt, hint } };
        var turnControls = new Grid { HorizontalOptions = LayoutOptions.Center, ColumnSpacing = 24, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        turnControls.Add(new Grid { WidthRequest = 94, HeightRequest = 94, Children = { shutterRing, progress } }, 0, 0);
        turnControls.Add(send, 1, 0);
        cancel.HorizontalOptions = LayoutOptions.Center;
        composer = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 14), Spacing = 10, BackgroundColor = Color.FromArgb("#E8F2F9"), Children = { languages, attachmentCard, inputBorder, turnCaption, turnControls, cancel, status } };
        liveTranslation.SetBinding(Label.FontSizeProperty, new Binding(nameof(MessageFontSize), source: this));
        livePreview = new Border
        {
            IsVisible = false, Margin = new Thickness(18, 4), Padding = 12, StrokeThickness = 0,
            BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout { Spacing = 4, Children =
            {
                liveCaption,
                new ScrollView { MaximumHeightRequest = 120, Content = liveTranslation }
            } }
        };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        root.Add(header, 0, 0); root.Add(signInCard, 0, 1); root.Add(conversationArea, 0, 2); root.Add(livePreview, 0, 3); root.Add(composer, 0, 4);
        root.SizeChanged += (_, _) => ScrollToLatest();
        Content = root;
        SemanticProperties.SetDescription(send, "Send message, translate and switch speakers");
        SemanticProperties.SetDescription(attach, "Choose a photo, camera or image file");
        SemanticProperties.SetDescription(removeAttachment, "Remove image");
        attach.Clicked += async (_, _) => await PickAttachmentAsync();
        removeAttachment.Clicked += (_, _) => { if (!busy) SetAttachment(null); };
        connect.Clicked += async (_, _) => await ConnectAsync();
        shutter.Clicked += async (_, _) => await TogglePauseAsync();
        send.Clicked += async (_, _) => await SendTextAsync();
        SemanticProperties.SetDescription(cancel, "Stop and delete the current input and attached image");
        cancel.Clicked += (_, _) =>
        {
            if (recording.HasTurn || !string.IsNullOrWhiteSpace(recording.Text) || pendingImage is not null) DiscardInput();
            else operation?.Cancel();
        };
        firstSpeaker.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { mode.SelectSpeaker(true, first.Code); SetStatus(null); UpdateControls(); MarkConversationChanged(); } };
        secondSpeaker.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { mode.SelectSpeaker(false, second.Code); SetStatus(null); UpdateControls(); MarkConversationChanged(); } };
        aiSpeaker.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { mode.SelectAi(); SetStatus(null); UpdateControls(); MarkConversationChanged(); } };
        draft.TextChanged += (_, _) => { if (!syncingDraft) recording.Edit(draft.Text ?? ""); UpdateControls(); };
        draft.Focused += async (_, _) =>
        {
            if (leavingConversation) return;
            ShowConversationArea();
            if (busy || !recording.IsRecording) return;
            pausingForEdit = true;
            try { await TogglePauseAsync(); }
            finally { pausingForEdit = false; UpdateControls(); }
        };
        recording.Changed += () => { if (MainThread.IsMainThread) SyncDraft(); else MainThread.BeginInvokeOnMainThread(SyncDraft); };
        recording.Error += error => MainThread.BeginInvokeOnMainThread(() => { if (!busy) SetStatus(error); });
        turns.CollectionChanged += (_, _) => { ScrollToLatest(); MarkConversationChanged(); };
        messages.SizeChanged += (_, _) => ScrollToLatest();
        messages.Loaded += (_, _) => ScrollToLatest();
        draft.FontSize = OriginalFontSize + 2;
        draft.HandlerChanged += (_, _) => AlignComposerText();
        draft.Loaded += (_, _) => AlignComposerText();
        UpdateControls();
    }

    public async Task LoadAccountAsync()
    {
        try { await auth.LoadAsync(); }
        catch { SetStatus("Could not load your sign-in. Please reconnect."); }
        UpdateControls();
    }

    ConversationSnapshot CaptureConversation() => new(conversationId, conversationCreatedAt, first.Code, second.Code,
        turns.Select(turn => new ConversationMessage(turn.Source, turn.Target, turn.Original, turn.Translation,
            turn.SourceTransliteration, turn.TargetTransliteration, turn.Attachment, turn.FromFirst, turn.Kind,
            turn.Citations?.ToArray(), turn.WebSearchUsed)).ToArray())
    {
        Draft = recording.Text, DraftImage = pendingImage, IsAi = mode.IsAi,
        FromFirst = mode.FromFirst, LastSpeakerCode = mode.LastSpeakerCode
    };

    void MarkConversationChanged()
    {
        if (restoringConversation) return;
        conversationDirty = true;
        var revision = ++saveRevision;
        var id = conversationId;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(650), () =>
        {
            if (revision == saveRevision && id == conversationId && !restoringConversation)
                _ = FlushConversationSafelyAsync();
        });
    }

    public Task FlushConversationAsync()
    {
        ++saveRevision;
        if (restoringConversation || !conversationDirty) return pendingSave;
        var snapshot = CaptureConversation(); // Capture before any await or a chat switch.
        conversationDirty = false;
        var previous = pendingSave;
        pendingSave = SaveAfterAsync(previous, snapshot);
        return pendingSave;
    }

    async Task SaveAfterAsync(Task previous, ConversationSnapshot snapshot)
    {
        try { await previous; }
        catch { /* Retry the latest complete snapshot after an earlier failed save. */ }
        try { await conversationStore.SaveAsync(snapshot); }
        catch (Exception ex)
        {
            if (snapshot.Id == conversationId) conversationDirty = true;
            throw new IOException("Could not save this conversation. Please check the available storage and try again.", ex);
        }
    }

    async Task FlushConversationSafelyAsync()
    {
        try { await FlushConversationAsync(); }
        catch { if (pageVisible) SetStatus("Could not save this chat. Please check the available storage and try again."); }
    }

    public async Task SuspendAndSaveAsync()
    {
        playback.Stop();
        if (recording.IsRecording) recording.Suspend();
        await FlushConversationSafelyAsync();
    }

    public async Task PrepareNewConversationAsync()
    {
        if (busy) throw new InvalidOperationException("Please wait for the current request to finish.");
        await FlushConversationAsync();
        ApplyConversation(null);
    }

    public async Task OpenConversationAsync(Guid id)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current request to finish.");
        await FlushConversationAsync();
        var snapshot = await conversationStore.LoadAsync(id) ?? throw new IOException("This conversation is no longer available.");
        ApplyConversation(snapshot);
    }

    static ConversationLanguage SavedLanguage(string code)
    {
        var known = ConversationLanguage.All.FirstOrDefault(x => x.Code == code);
        if (known is not null) return known;
        var culture = CultureInfo.GetCultureInfo(code);
        return new(code, culture.EnglishName, culture.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(code).Name : culture.Name);
    }

    void ApplyConversation(ConversationSnapshot? snapshot)
    {
        restoringConversation = true; ++saveRevision;
        try
        {
            ++playbackRevision; playback.Stop(); recording.Clear();
            conversationId = snapshot?.Id ?? Guid.NewGuid();
            conversationCreatedAt = snapshot?.CreatedAt ?? DateTimeOffset.UtcNow;
            first = SavedLanguage(snapshot?.FirstLanguageCode ?? Preferences.Default.Get("koe.language.first", "en"));
            second = SavedLanguage(snapshot?.SecondLanguageCode ?? Preferences.Default.Get("koe.language.second", "ja"));
            if (first.Code == second.Code) second = ConversationLanguage.All.First(x => x.Code != first.Code);
            turns.Clear();
            if (snapshot is not null)
                foreach (var message in snapshot.Messages)
                    turns.Add(new(message.Source, message.Target, message.Original, message.Translation,
                        message.SourceTransliteration, message.TargetTransliteration, message.Attachment, message.FromFirst,
                        message.Kind, message.Citations, message.WebSearchUsed));
            mode.Restore(snapshot?.IsAi ?? false, snapshot?.FromFirst ?? true, snapshot?.LastSpeakerCode);
            recording.Edit(snapshot?.Draft ?? "");
            SyncDraft(); SetAttachment(snapshot?.DraftImage);
            livePreview.IsVisible = false; liveTranslation.Text = ""; SetStatus(null);
            conversationDirty = false;
            UpdateControls();
        }
        finally { restoringConversation = false; }
        ScrollToLatest();
    }

    async Task BackToChatsAsync()
    {
        if (leavingConversation || busy) return;
        leavingConversation = true; UpdateControls();
        try
        {
            draft.Unfocus();
            if (recording.IsRecording) await recording.PauseAsync();
            playback.Stop();
            await FlushConversationAsync();
            ApplyConversation(null);
            showingConversations = true;
            UpdateControls();
            await conversationList.RefreshAsync();
        }
        catch { await DisplayAlertAsync("Could not save conversation", "Please check the available storage and try again. Your chat is still open.", "OK"); }
        finally { leavingConversation = false; UpdateControls(); }
    }

    void ShowConversationArea()
    {
        if (!showingConversations) return;
        showingConversations = false;
        UpdateControls(); ScrollToLatest();
    }

    async Task StartNewConversationAsync()
    {
        if (leavingConversation || busy) return;
        leavingConversation = true; UpdateControls();
        try
        {
            draft.Unfocus();
            if (recording.IsRecording) await recording.PauseAsync();
            await PrepareNewConversationAsync();
            ShowConversationArea();
        }
        catch { await DisplayAlertAsync("Could not start a new conversation", "Your current chat has been kept. Please try again.", "OK"); }
        finally { leavingConversation = false; UpdateControls(); }
    }

    async Task OpenConversationInlineAsync(Guid id)
    {
        if (leavingConversation || busy) return;
        leavingConversation = true; UpdateControls();
        try { await OpenConversationAsync(id); ShowConversationArea(); }
        finally { leavingConversation = false; UpdateControls(); }
    }

    protected override bool OnBackButtonPressed()
    {
        if (showingConversations) return base.OnBackButtonPressed();
        _ = BackToChatsAsync();
        return true;
    }

    void AlignComposerText()
    {
        // Align the first native text line with the 44-point attachment button, even for multiline drafts.
#if IOS
        if (draft.Handler?.PlatformView is UIKit.UITextView textView)
        {
            var top = Math.Max(6, (44 - (double)(textView.Font?.LineHeight ?? 20)) / 2);
            textView.TextContainerInset = new UIKit.UIEdgeInsets((System.Runtime.InteropServices.NFloat)top, 0, 8, 0);
            textView.TextContainer.LineFragmentPadding = 0;
        }
#elif WINDOWS
        if (draft.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
            textBox.Padding = new Microsoft.UI.Xaml.Thickness(0, Math.Max(6, (44 - draft.FontSize * 1.25) / 2), 0, 8);
#endif
    }

    static Button LanguageButton(string text) => new() { Text = text, FontSize = 14, CornerRadius = 16, HeightRequest = 36, Padding = new Thickness(12, 0), TextColor = Muted, BackgroundColor = Colors.Transparent };

    View CreateBubble(ConversationTurnKind kind)
    {
        var isAi = kind != ConversationTurnKind.Translation;
        var direction = new Label { FontSize = 11, TextColor = Muted };
        direction.SetBinding(Label.TextProperty, nameof(ConversationTurn.Direction));
        var original = new Label { FontSize = 14, TextColor = isAi ? Ink : Muted, LineHeight = 1.15 };
        if (kind != ConversationTurnKind.AiAnswer) original.SetBinding(Label.TextProperty, nameof(ConversationTurn.Original));
        original.SetBinding(Label.FontSizeProperty, new Binding(isAi ? nameof(MessageFontSize) : nameof(OriginalFontSize), source: this));
        var translation = new Label { FontSize = 18, TextColor = Ink, LineHeight = 1.15 };
        if (kind != ConversationTurnKind.AiAnswer) translation.SetBinding(Label.TextProperty, nameof(ConversationTurn.Translation));
        translation.SetBinding(Label.FontSizeProperty, new Binding(nameof(MessageFontSize), source: this));
        var translatedReadingLabel = new Label { TextColor = Blue, LineHeight = 1.15 };
        translatedReadingLabel.SetBinding(Label.TextProperty, nameof(ConversationTurn.TranslatedReadingCaption));
        translatedReadingLabel.SetBinding(Label.IsVisibleProperty, nameof(ConversationTurn.TranslationHasReading));
        translatedReadingLabel.SetBinding(Label.FontSizeProperty, new Binding(nameof(ReadingFontSize), source: this));
        var originalReadingLabel = new Label { TextColor = Blue, LineHeight = 1.15 };
        originalReadingLabel.SetBinding(Label.TextProperty, nameof(ConversationTurn.OriginalReadingCaption));
        originalReadingLabel.SetBinding(Label.IsVisibleProperty, nameof(ConversationTurn.OriginalHasReading));
        originalReadingLabel.SetBinding(Label.FontSizeProperty, new Binding(nameof(ReadingFontSize), source: this));
        var translatedReading = new VerticalStackLayout { Children = { translatedReadingLabel } };
        translatedReading.SetBinding(IsVisibleProperty, new Binding(nameof(ShowTransliteration), source: this));
        var originalReading = new VerticalStackLayout { Children = { originalReadingLabel } };
        originalReading.SetBinding(IsVisibleProperty, new Binding(nameof(ShowTransliteration), source: this));
        var copyTranslation = CopyButton(isAi ? "Copy second AI answer" : "Copy translation");
        copyTranslation.Clicked += async (_, _) =>
        {
            if (copyTranslation.BindingContext is ConversationTurn turn) await CopyTextAsync(turn.Translation);
        };
        var copyOriginal = CopyButton(kind == ConversationTurnKind.AiAnswer ? "Copy first AI answer" : isAi ? "Copy AI question" : "Copy original");
        copyOriginal.Clicked += async (_, _) =>
        {
            if (copyOriginal.BindingContext is ConversationTurn turn) await CopyTextAsync(turn.Original);
        };
        var speakTranslation = SpeakerButton(isAi ? "Read second AI answer aloud" : "Read translation aloud");
        speakTranslation.Clicked += async (_, _) => { if (speakTranslation.BindingContext is ConversationTurn turn) await SpeakTextAsync(turn.Translation, turn.Target); };
        var speakOriginal = SpeakerButton(kind == ConversationTurnKind.AiAnswer ? "Read first AI answer aloud" : isAi ? "Read AI question aloud" : "Read original aloud");
        speakOriginal.Clicked += async (_, _) => { if (speakOriginal.BindingContext is ConversationTurn turn) await SpeakTextAsync(turn.Original, turn.Source); };
        var translatedRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        translatedRow.SetBinding(IsVisibleProperty, nameof(ConversationTurn.HasTranslation));
        translatedRow.Add(translation, 0, 0); translatedRow.Add(copyTranslation, 1, 0);
        translatedRow.Add(speakTranslation, 2, 0);
        var originalRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        originalRow.SetBinding(IsVisibleProperty, nameof(ConversationTurn.HasOriginal));
        originalRow.Add(original, 0, 0); originalRow.Add(copyOriginal, 1, 0);
        originalRow.Add(speakOriginal, 2, 0);
        var picture = new Image { HeightRequest = 150, Aspect = Aspect.AspectFit };
        picture.SetBinding(Image.SourceProperty, nameof(ConversationTurn.Picture));
        picture.SetBinding(IsVisibleProperty, nameof(ConversationTurn.HasPicture));
        var sources = new VerticalStackLayout { Spacing = 5, IsVisible = false };
        var content = new VerticalStackLayout { Spacing = 7 };
        content.Children.Add(direction); content.Children.Add(picture);
        if (kind == ConversationTurnKind.AiAnswer)
        {
            var firstLanguage = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#205C3B") };
            firstLanguage.SetBinding(Label.TextProperty, nameof(ConversationTurn.Source));
            var secondLanguage = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#205C3B") };
            secondLanguage.SetBinding(Label.TextProperty, nameof(ConversationTurn.Target));
            content.Children.Add(firstLanguage); content.Children.Add(originalRow); content.Children.Add(originalReading);
            content.Children.Add(secondLanguage); content.Children.Add(translatedRow); content.Children.Add(translatedReading);
            content.Children.Add(sources);
        }
        else if (kind == ConversationTurnKind.AiQuestion)
        {
            content.Children.Add(originalRow); content.Children.Add(originalReading);
        }
        else
        {
            content.Children.Add(translatedRow); content.Children.Add(translatedReading);
            content.Children.Add(originalRow); content.Children.Add(originalReading);
        }
        var bubble = new Border
        {
            Margin = new Thickness(4, 6, 34, 6), Padding = new Thickness(14, 11), StrokeThickness = 0, BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17, 17, 4, 17) }, HorizontalOptions = LayoutOptions.Start, MaximumWidthRequest = 330,
            Content = content
        };
        bubble.BindingContextChanged += (_, _) =>
        {
            if (bubble.BindingContext is not ConversationTurn turn) return;
            speakTranslation.IsVisible = !string.IsNullOrWhiteSpace(turn.Target) && !string.IsNullOrWhiteSpace(turn.Translation);
            if (turn.IsAi)
            {
                if (turn.Kind == ConversationTurnKind.AiAnswer)
                {
                    original.FormattedText = CitedAnswer(turn.Original, turn.Citations);
                    translation.FormattedText = CitedAnswer(turn.Translation, turn.Citations);
                    sources.Children.Clear();
                    sources.IsVisible = turn.WebSearchUsed || turn.Citations is { Count: > 0 };
                    if (turn.Citations is { Count: > 0 } citations)
                    {
                        sources.Children.Add(new Label { Text = "Sources", TextColor = Ink, FontSize = 12, FontAttributes = FontAttributes.Bold });
                        for (var index = 0; index < citations.Count; index++)
                        {
                            var citation = citations[index];
                            var formatted = new FormattedString();
                            formatted.Spans.Add(SourceSpan($"[{index + 1}] {citation.Title}", citation));
                            var label = new Label { FormattedText = formatted, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation };
                            label.SetBinding(Label.FontSizeProperty, new Binding(nameof(ReadingFontSize), source: this));
                            sources.Children.Add(label);
                        }
                    }
                    else if (turn.WebSearchUsed)
                        sources.Children.Add(new Label { Text = "Web search used. No source links were returned.", TextColor = Muted, FontSize = 12 });
                }
                bubble.HorizontalOptions = LayoutOptions.Center;
                bubble.Margin = new Thickness(8, 6);
                bubble.BackgroundColor = Color.FromArgb(turn.Kind == ConversationTurnKind.AiAnswer ? "#DDF1DF" : "#ECF6ED");
                bubble.StrokeShape = new RoundRectangle { CornerRadius = 17 };
                return;
            }
            bubble.HorizontalOptions = turn.FromFirst ? LayoutOptions.End : LayoutOptions.Start;
            bubble.Margin = turn.FromFirst ? new Thickness(34, 6, 4, 6) : new Thickness(4, 6, 34, 6);
            bubble.BackgroundColor = turn.FromFirst ? Color.FromArgb("#D3E8F8") : Colors.White;
            bubble.StrokeShape = new RoundRectangle { CornerRadius = turn.FromFirst ? new CornerRadius(17, 17, 17, 4) : new CornerRadius(17, 17, 4, 17) };
        };
        bubble.SizeChanged += (_, _) =>
        {
            if (turns.Count > 0 && ReferenceEquals(bubble.BindingContext, turns[^1])) ScrollToLatest();
        };
        return new Grid { Children = { bubble } };
    }

    static ImageButton CopyButton(string description)
    {
        var button = new ImageButton { Source = "copy.png", BackgroundColor = Colors.Transparent, WidthRequest = 40, HeightRequest = 40, Padding = 10, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    FormattedString CitedAnswer(string text, IReadOnlyList<WebCitation>? citations)
    {
        var formatted = new FormattedString();
        formatted.Spans.Add(new Span { Text = text });
        if (citations is not null)
            for (var index = 0; index < citations.Count; index++)
                formatted.Spans.Add(SourceSpan($" [{index + 1}]", citations[index]));
        return formatted;
    }

    Span SourceSpan(string text, WebCitation citation)
    {
        var span = new Span { Text = text, TextColor = Color.FromArgb("#245C86"), TextDecorations = TextDecorations.Underline };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (WebCitation.Create(citation.Title, citation.Url) is not { } source) return;
            try
            {
                if (!await Launcher.Default.OpenAsync(new Uri(source.Url))) SetStatus("Could not open this source.");
            }
            catch { SetStatus("Could not open this source."); }
        };
        span.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(span, "Open source: " + citation.Title);
        return span;
    }

    static ImageButton SpeakerButton(string description)
    {
        var button = new ImageButton { Source = "speaker.png", BackgroundColor = Colors.Transparent, WidthRequest = 40, HeightRequest = 40, Padding = 9, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    async Task SpeakTextAsync(string text, string languageName)
    {
        if (busy) return;
        if (recording.IsRecording) { SetStatus("Pause recording before playing a message."); return; }
        var language = ConversationLanguage.All.FirstOrDefault(x => x.Name == languageName);
        if (language is null) { SetStatus("Read aloud is not available for this language."); return; }
        var revision = ++playbackRevision;
        try
        {
            if (playback.UsesOpenAi) SetStatus("Preparing OpenAI voice … Tap the speaker again to stop.");
            var started = await playback.SpeakAsync(text, language.Code, Preferences.Default.Get("koe.voice." + language.Code, ""));
            if (revision == playbackRevision && pageVisible && !busy && !recording.IsRecording)
                SetStatus(started && playback.UsesOpenAi ? "OpenAI voice (AI-generated) · tap the speaker to stop." : null);
        }
        catch (Exception ex) { if (revision == playbackRevision && pageVisible && !busy && !recording.IsRecording) SetStatus(ex.Message); }
    }

    public async Task ShowVoicesAsync()
    {
        if (voiceSettings.Enabled) { await ShowOpenAiVoiceSettingsAsync(); return; }
        var language = await PickLanguageAsync("Voice language", ConversationLanguage.All);
        if (language is null) return;
        var voices = playback.GetVoices(language.Code);
        if (voices.Count == 0)
        {
            await DisplayAlertAsync("No iPhone voice available", "No iPhone voice is available for this language. You can enable OpenAI voice in Koe Settings instead.", "OK");
            return;
        }
        var choices = voices.Select((voice, index) => $"{index + 1}. {voice.DisplayName}").ToArray();
        var selected = await DisplayActionSheetAsync($"{language.Name} · {voices.Count} voices", "Cancel", null, choices);
        var selection = Array.IndexOf(choices, selected);
        if (selection < 0) return;
        Preferences.Default.Set("koe.voice." + language.Code, voices[selection].Id);
        try { await playback.SpeakAsync(language.Greeting, language.Code, voices[selection].Id); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    public async Task ShowOpenAiVoiceSettingsAsync()
    {
        ++playbackRevision;
        playback.Stop();
        await (settingsNavigation ?? Navigation).PushModalAsync(new OpenAiVoicePage(voiceSettings));
    }

    async Task CopyTextAsync(string text)
    {
        try { await Clipboard.Default.SetTextAsync(text); SetStatus("Text copied."); }
        catch { SetStatus("Could not copy text. Please try again."); }
    }

    void ScrollToLatest()
    {
        var revision = ++scrollRevision;
        if (showingConversations || turns.Count == 0) return;
        // Collection changes arrive before iOS has measured the new cell. Scroll after layout.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            if (revision != scrollRevision || !pageVisible || showingConversations || turns.Count == 0 || messages.Handler is null) return;
            messages.ScrollTo(turns.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); pageVisible = true;
        await LoadAccountAsync();
        if (!initializedConversations)
        {
            initializedConversations = true;
            await conversationList.RefreshAsync();
        }
        UpdateControls();
        await RefreshModelsAsync();
    }

    public async Task RefreshModelsAsync()
    {
        if (!auth.IsConnected)
        {
            availableModels = [];
            NotifyModelChoices();
            return;
        }
        if (loadingModels) return;
        loadingModels = true;
        NotifyModelChoices();
        var connection = auth.ConnectionKey;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var available = await translator.GetModelsAsync(timeout.Token);
            if (!auth.IsConnected || auth.ConnectionKey != connection) return;
            var selected = available.FirstOrDefault(x => x.Id == selectedModelId) ?? available[0];
            availableModels = available;
            selectedModelId = selected.Id;
            Preferences.Default.Set("koe.model", selectedModelId);
        }
        catch (Exception) { if (!busy && auth.IsConnected && pageVisible) SetStatus("Could not load model choices. Reconnect to refresh the list."); }
        finally
        {
            loadingModels = false; UpdateControls(); NotifyModelChoices();
            if (auth.IsConnected && auth.ConnectionKey != connection) await RefreshModelsAsync();
        }
    }

    void NotifyModelChoices()
    {
        updatingModels = true;
        try
        {
            OnPropertyChanged(nameof(AvailableModels));
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(ModelChoiceHint));
            OnPropertyChanged(nameof(CanSelectModel));
        }
        finally { updatingModels = false; }
    }

    protected override void OnDisappearing()
    {
        ++playbackRevision;
        playback.Stop();
        if (!pickingAttachment) { pageVisible = false; operation?.Cancel(); recording.Suspend(); }
        if (!pickingAttachment) _ = FlushConversationSafelyAsync();
        base.OnDisappearing();
    }

    async Task PickAttachmentAsync()
    {
        if (busy) return;
        draft.Unfocus();
        recognizingSpeech = mode.IsAi && recording.IsRecording;
        discardingInput = false; busy = true; pickingAttachment = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.IsRecording) await recording.PauseAsync(operation.Token);
            recognizingSpeech = false; UpdateControls();
            var choice = await DisplayActionSheetAsync("Add image", "Cancel", null, "Choose photo", "Camera", "Choose image file");
            FileResult? file = null;
            switch (choice)
            {
                case "Choose photo":
                    var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });
                    file = photos.FirstOrDefault(); break;
                case "Camera":
                    if (!MediaPicker.Default.IsCaptureSupported) { SetStatus("No camera is available on this device."); return; }
                    file = await MediaPicker.Default.CapturePhotoAsync(); break;
                case "Choose image file":
                    file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose image", FileTypes = FilePickerFileType.Images }); break;
                default: return;
            }
            if (file is null) return;
            SetAttachment(await AttachmentLoader.LoadAsync(file, operation.Token));
            SetStatus("Image ready. Add a spoken or typed message, or send the image with ↑.");
        }
        catch (OperationCanceledException) { }
        catch (PermissionException) { SetStatus("Please allow camera or photo access for Koe in iPhone Settings."); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { pickingAttachment = false; FinishOperation(); }
    }

    void SetAttachment(ImageAttachment? image)
    {
        pendingImage = image;
        attachmentPreview.Source = image is null ? null : ImageSource.FromStream(() => new MemoryStream(image.Bytes, writable: false));
        attachmentName.Text = image?.Name;
        attachmentCard.IsVisible = image is not null;
        if (image is not null && !restoringConversation) ShowConversationArea();
        UpdateControls();
        MarkConversationChanged();
    }

    public string FirstLanguageName => first.Name;
    public string SecondLanguageName => second.Name;
    public string CurrentTextSize => TextSizeLabel;
    public bool IsAccountConnected => auth.IsConnected;
    public bool IsAccountDisconnected => !auth.IsConnected;
    public string AccountName => auth.AccountLabel;
    public IReadOnlyList<TranslationModel> AvailableModels => availableModels;
    public bool CanSelectModel => auth.IsConnected && !busy && !recording.HasTurn && !loadingModels && availableModels.Length > 0;
    public string ModelChoiceHint => !auth.IsConnected ? "Connect ChatGPT to choose a model."
        : loadingModels ? "Loading your account's models …"
        : availableModels.Length == 0 ? "Could not load models. Close Settings and reopen to retry."
        : "Applies to translations and AI replies.";
    public TranslationModel? SelectedModel
    {
        get => availableModels.FirstOrDefault(model => model.Id == selectedModelId);
        set
        {
            if (updatingModels || !CanSelectModel || value is null || !availableModels.Any(model => model.Id == value.Id) || value.Id == selectedModelId) return;
            selectedModelId = value.Id;
            Preferences.Default.Set("koe.model", selectedModelId);
            OnPropertyChanged(nameof(SelectedModel));
        }
    }

    public Task ShowSettingsAsync(INavigation? navigation = null)
    {
        settingsNavigation = navigation ?? Navigation;
        return settingsNavigation.PushModalAsync(new SettingsPage(this, voiceSettings));
    }

    public async Task ChangeLanguageAsync(bool changeFirst)
    {
        var other = changeFirst ? second : first;
        var selected = await PickLanguageAsync(changeFirst ? "First speaker's language" : "Second speaker's language",
            ConversationLanguage.All.Where(x => x.Code != other.Code).ToArray());
        if (selected is null) return;
        if (changeFirst) first = selected; else second = selected;
        mode.Reset();
        Preferences.Default.Set("koe.language.first", first.Code);
        Preferences.Default.Set("koe.language.second", second.Code);
        UpdateControls();
        MarkConversationChanged();
    }

    public void SetTransliteration(bool value)
    {
        showTransliteration = value;
        Preferences.Default.Set("koe.show-transliteration", value);
        OnPropertyChanged(nameof(ShowTransliteration));
        ScrollToLatest();
    }

    async Task<ConversationLanguage?> PickLanguageAsync(string title, ConversationLanguage[] choices)
    {
        var navigation = settingsNavigation ?? Navigation;
        var completion = new TaskCompletionSource<ConversationLanguage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = false;
        var search = new SearchBar { Placeholder = "Search languages", BackgroundColor = Colors.White };
        search.SetAppThemeColor(SearchBar.TextColorProperty, Ink, Colors.White);
        search.SetAppThemeColor(SearchBar.PlaceholderColorProperty, Muted, Color.FromArgb("#B2B2B7"));
        search.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#333333"));
        var list = new CollectionView
        {
            ItemsSource = choices, SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var name = new Label { FontSize = 18, TextColor = Ink, VerticalTextAlignment = TextAlignment.Center };
                SettingsStyle.Ink(name);
                name.SetBinding(Label.TextProperty, nameof(ConversationLanguage.Name));
                return new Grid { Padding = new Thickness(18, 14), MinimumHeightRequest = 52, Children = { name } };
            })
        };
        var close = new Button { Text = "Cancel", TextColor = Blue, BackgroundColor = Colors.Transparent };
        var heading = new Grid { Padding = new Thickness(16, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var languageHeading = SettingsStyle.Label(title, 20); languageHeading.FontAttributes = FontAttributes.Bold;
        heading.Add(languageHeading, 0, 0);
        heading.Add(close, 1, 0);
        var content = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        content.Add(heading, 0, 0); content.Add(search, 0, 1);
        content.Add(new Label { Text = $"{choices.Length} languages · Some require an internet connection", TextColor = Muted, FontSize = 12, Margin = new Thickness(18, 4) }, 0, 2);
        content.Add(list, 0, 3);
        var page = new ContentPage { Title = title, SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All, BackgroundColor = Color.FromArgb("#F0F6FA"), Content = content };
        SettingsStyle.Background(page);
        async Task CloseAsync(ConversationLanguage? selected)
        {
            if (closing) return;
            closing = true;
            search.Unfocus();
            await navigation.PopModalAsync();
            completion.TrySetResult(selected);
        }
        close.Clicked += async (_, _) => await CloseAsync(null);
        list.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is ConversationLanguage selected) await CloseAsync(selected);
        };
        search.TextChanged += (_, args) =>
        {
            var query = args.NewTextValue?.Trim() ?? "";
            list.ItemsSource = choices.Where(x => x.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        page.Disappearing += (_, _) => { if (!closing) completion.TrySetResult(null); };
        await navigation.PushModalAsync(page);
        return await completion.Task;
    }

    public void SetTextSize(string selection)
    {
        if (selection is not ("Small" or "Medium" or "Large")) return;
        textSize = selection == "Small" ? "Klein" : selection == "Large" ? "Groß" : "Mittel";
        Preferences.Default.Set("koe.text-size", textSize);
        OnPropertyChanged(nameof(MessageFontSize));
        OnPropertyChanged(nameof(OriginalFontSize));
        OnPropertyChanged(nameof(ReadingFontSize));
        draft.FontSize = OriginalFontSize + 2;
        AlignComposerText();
        ScrollToLatest();
    }

    public async Task ConnectAsync()
    {
        if (busy || recording.HasTurn) return;
        signInExplanation.Text = SignInExplanation;
        discardingInput = false; busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (auth.IsConnected) { await auth.SignOutAsync(operation.Token); translator.ClearModels(); SetStatus("Signed out."); }
            else { await auth.SignInAsync(operation.Token); SetStatus(null); }
        }
        catch (OperationCanceledException) { SetStatus("Sign-in cancelled."); }
        catch (Exception ex) { signInExplanation.Text = ex.Message; SetStatus(ex.Message); }
        finally { FinishOperation(); await RefreshModelsAsync(); }
    }

    async Task ShowSignInRequiredAsync()
    {
        if (busy || promptingForSignIn || leavingConversation) return;
        promptingForSignIn = true;
        draft.Unfocus();
        UpdateControls();
        try
        {
            SetStatus("Connect ChatGPT before recording or sending a message. Your draft has been kept.");
            if (await DisplayAlertAsync("Connect ChatGPT", "Recording, translation and AI replies require a ChatGPT connection. Your draft will be kept. Connect first, then tap the microphone or send button again.", "Connect ChatGPT", "Not now") && !auth.IsConnected)
            {
                promptingForSignIn = false;
                await ConnectAsync();
            }
        }
        finally { promptingForSignIn = false; UpdateControls(); }
    }

    async Task TogglePauseAsync()
    {
        if (busy || promptingForSignIn || leavingConversation) return;
        if (!auth.IsConnected) { await ShowSignInRequiredAsync(); return; }
        ShowConversationArea();
        recognizingSpeech = mode.IsAi && recording.IsRecording;
        discardingInput = false; busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.IsRecording)
            {
                await recording.PauseAsync(operation.Token);
                SetStatus("Recording paused. You can edit the text.");
            }
            else
            {
                draft.Unfocus();
                ++playbackRevision;
                playback.Stop();
                await recording.StartAsync(CultureInfo.GetCultureInfo(SourceLanguage.Culture), operation.Token,
                    mode.IsAi ? [CultureInfo.GetCultureInfo(first.Culture), CultureInfo.GetCultureInfo(second.Culture)] : null);
                SetStatus(null);
            }
        }
        catch (OperationCanceledException) { recording.Suspend(); SetStatus("Recording paused."); }
        catch (Exception ex) { recording.Suspend(); SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    void SyncDraft()
    {
        syncingDraft = true;
        try { if (draft.Text != recording.Text) draft.Text = recording.Text; }
        finally { syncingDraft = false; }
        UpdateControls();
        MarkConversationChanged();
    }

    async Task SendTextAsync()
    {
        if (busy || promptingForSignIn || leavingConversation) return;
        if (!auth.IsConnected) { await ShowSignInRequiredAsync(); return; }
        if (!recording.HasTurn && string.IsNullOrWhiteSpace(recording.Text) && pendingImage is null) return;
        draft.Unfocus();
        ++playbackRevision; playback.Stop();
        recognizingSpeech = mode.IsAi && recording.IsRecording;
        discardingInput = false; busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            var text = (await recording.FinishAsync(operation.Token)).Trim();
            recognizingSpeech = false; UpdateControls();
            if (string.IsNullOrWhiteSpace(text) && pendingImage is null) { SetStatus("No speech detected. Tap the microphone to try again."); return; }
            if (mode.IsAi)
            {
                await AskAiDraftAsync(text, operation.Token);
                SetStatus("AI replied in both languages. Ask a follow-up or select a speaker to translate.");
            }
            else
            {
                await TranslateDraftAsync(text, operation.Token);
                SetStatus("Translated. The next speaker can start recording.");
            }
            await FlushConversationAsync();
        }
        catch (OperationCanceledException) { recording.Suspend(); SetStatus(mode.IsAi ? "AI request cancelled. Your draft has been kept." : "Translation cancelled. Your draft has been kept."); }
        catch (Exception ex) { recording.Suspend(); SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TranslateDraftAsync(string text, CancellationToken ct)
    {
        SetStatus("Translating …");
        var direction = mode.FromFirst;
        var image = pendingImage;
        var translated = await WithPreviewAsync(ct, "Translating · preview", preview =>
            translator.TranslateAsync(text, SourceLanguage, TargetLanguage, ct, image, preview, showTransliteration, selectedModelId));
        ct.ThrowIfCancellationRequested();
        if (image is not null)
        {
            if (translated.ImageText is { } imageText)
                turns.Add(new(ConversationLanguage.All.FirstOrDefault(x => x.Code == imageText.Source)?.Name ?? imageText.Source, ConversationLanguage.Get(imageText.Target).Name, imageText.Original, imageText.Translation, imageText.SourceTransliteration, imageText.TargetTransliteration, image, imageText.Source == first.Code));
            else turns.Add(new("Image", "", "", "No legible text found in the image.", "", "", image));
        }
        if (!string.IsNullOrWhiteSpace(text))
            turns.Add(new(SourceLanguage.Name, TargetLanguage.Name, translated.CorrectedOriginal, translated.Translation, translated.SourceTransliteration, translated.TargetTransliteration, null, direction));
        recording.Clear(); SetAttachment(null);
        if (!string.IsNullOrWhiteSpace(text)) mode.CompleteTurn(first, SourceLanguage.Code);
        else if (translated.ImageText is { } detected) mode.CompleteTurn(first, detected.Source);
        SetStatus(null);
    }

    async Task<T> WithPreviewAsync<T>(CancellationToken ct, string caption, Func<Action<TranslationPreview>, Task<T>> request)
    {
        liveCaption.Text = caption;
        var closed = false;
        var scheduled = false;
        var latest = new TranslationPreview("", "");
        void Preview(TranslationPreview preview) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (closed || ct.IsCancellationRequested || !pageVisible) return;
            latest = preview;
            if (scheduled) return;
            scheduled = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () =>
            {
                scheduled = false;
                if (closed || ct.IsCancellationRequested || !pageVisible) return;
                liveTranslation.Text = string.Join("\n\n", new[] { latest.Text, latest.ImageText }.Where(x => !string.IsNullOrWhiteSpace(x)));
                livePreview.IsVisible = !string.IsNullOrWhiteSpace(liveTranslation.Text);
            });
        });
        try { return await request(Preview); }
        finally { closed = true; livePreview.IsVisible = false; liveTranslation.Text = ""; }
    }

    IEnumerable<AiContextMessage> AiContext()
    {
        foreach (var turn in turns.TakeLast(AiChatRequest.MaximumHistoryMessages))
        {
            if (turn.Kind == ConversationTurnKind.AiAnswer)
                yield return new("assistant", $"{turn.Source}: {turn.Original}\n\n{turn.Target}: {turn.Translation}" +
                    (turn.Citations is { Count: > 0 } links ? "\n\nSources:\n" + string.Join("\n", links.Select((link, index) => $"[{index + 1}] {link.Title}: {link.Url}")) : ""));
            else if (turn.Kind == ConversationTurnKind.AiQuestion)
                yield return new("user", turn.Original, turn.Attachment);
            else
                yield return new("user", $"Conversation excerpt (background only):\n{turn.Source}: {turn.Original}\n{turn.Target}: {turn.Translation}", turn.Attachment);
        }
    }

    async Task AskAiDraftAsync(string text, CancellationToken ct)
    {
        SetStatus("AI is answering …");
        var image = pendingImage;
        var reply = await WithPreviewAsync(ct, "AI · automatic language detection", preview =>
            translator.AskAsync(text, first, second, AiContext(), ct, image, preview, showTransliteration, selectedModelId,
                () => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (pageVisible && busy && operation is not null && operation.Token == ct && !ct.IsCancellationRequested)
                        SetStatus("Searching the web …");
                })));
        ct.ThrowIfCancellationRequested();
        var primary = reply.FirstLanguageCode == first.Code ? first : second;
        var secondary = primary.Code == first.Code ? second : first;
        var inputLanguage = ConversationLanguage.All.FirstOrDefault(x => x.Code == reply.InputLanguageCode)?.Name ?? "Auto-detected";
        turns.Add(new(inputLanguage, "", text, "", "", "", image, Kind: ConversationTurnKind.AiQuestion));
        turns.Add(new(primary.Name, secondary.Name, reply.FirstAnswer, reply.SecondAnswer, reply.FirstTransliteration, reply.SecondTransliteration,
            Kind: ConversationTurnKind.AiAnswer, Citations: reply.Citations, WebSearchUsed: reply.WebSearchUsed));
        mode.CompleteTurn(first, primary.Code);
        recording.Clear(); SetAttachment(null);
    }

    void DiscardInput()
    {
        ++playbackRevision;
        playback.Stop();
        discardingInput = true;
        operation?.Cancel();
        recording.Clear();
        SyncDraft();
        SetAttachment(null);
        livePreview.IsVisible = false;
        liveTranslation.Text = "";
        SetStatus("Input discarded.");
        UpdateControls();
    }

    void FinishOperation()
    {
        operation?.Dispose(); operation = null; busy = false; recognizingSpeech = false;
        if (discardingInput)
        {
            recording.Clear(); SyncDraft(); SetAttachment(null);
            SetStatus("Input discarded.");
        }
        UpdateControls(); ScrollToLatest();
    }
    void SetStatus(string? text) { status.Text = text; status.IsVisible = !string.IsNullOrWhiteSpace(text); }

    void UpdateControls()
    {
        messages.IsVisible = !showingConversations;
        conversationList.IsVisible = showingConversations;
        conversationList.IsEnabled = !busy && !leavingConversation;
        chatBack.IsVisible = !showingConversations;
        newConversation.IsVisible = showingConversations;
        chatBack.IsEnabled = newConversation.IsEnabled = !busy && !leavingConversation;
        composer.IsEnabled = !leavingConversation;
        draft.Placeholder = mode.IsAi ? "Ask AI …" : "Type a message …";
        welcomeHint.Text = mode.IsAi ? "Ask AI in either selected language.\nLanguage is detected automatically.\nReplies appear in both languages." : "Microphone: record, pause and resume.\nSend with ↑ to translate and switch speakers.";
        attachmentHint.Text = mode.IsAi ? "Ask AI about this image" : "Translate image text + your message";
        livePreview.BackgroundColor = mode.IsAi ? Color.FromArgb("#DDF1DF") : Colors.White;
        firstGreeting.Text = first.Greeting;
        secondGreeting.Text = second.Greeting;
        draft.PreferredCulture = mode.IsAi ? "" : SourceLanguage.Culture;
        OnPropertyChanged(nameof(IsAccountConnected));
        OnPropertyChanged(nameof(IsAccountDisconnected));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(CanSelectModel));
        signInCard.IsVisible = !auth.IsConnected; connect.IsEnabled = !busy && !promptingForSignIn;
        draft.IsReadOnly = busy && (!pausingForEdit || discardingInput);
        send.IsEnabled = !busy && !promptingForSignIn && (!auth.IsConnected || recording.HasTurn || !string.IsNullOrWhiteSpace(draft.Text) || pendingImage is not null);
        attach.IsEnabled = !busy;
        removeAttachment.IsEnabled = !busy;
        shutter.IsEnabled = !busy && !promptingForSignIn;
        shutter.Text = busy ? "" : recording.IsRecording ? "Ⅱ" : recording.HasTurn ? "▶" : "";
        shutter.ImageSource = !busy && !recording.HasTurn ? ImageSource.FromFile("microphone.png") : null;
        var buttonColor = busy ? Color.FromArgb("#7E96AB") : recording.IsRecording ? Color.FromArgb("#B63E46") : recording.HasTurn ? Color.FromArgb("#E8BD70") : Color.FromArgb("#CF4F55");
        shutter.BackgroundColor = buttonColor;
        shutterRing.Stroke = buttonColor;
        shutter.TextColor = !busy && recording.State == RecordingState.Paused ? Ink : Colors.White;
        shutter.Opacity = 1;
        SemanticProperties.SetDescription(shutter, recording.IsRecording ? "Pause recording" : recording.HasTurn ? "Resume recording" : "Start recording");
        prompt.Text = busy ? "One moment …" : recording.IsRecording ? $"{SourceLanguage.Name} · speak now" : recording.HasTurn ? "Recording paused" : $"{SourceLanguage.Name} · ready";
        hint.Text = busy ? "Processing your message." : recording.IsRecording ? "Microphone: pause · ↑: send" : recording.HasTurn ? "Resume on the left · send on the right" : "Microphone: record · ↑: send";
        if (mode.IsAi)
        {
            prompt.Text = recognizingSpeech ? "AI · detecting language …" : busy ? "AI · one moment …" : recording.IsRecording ? "AI · auto language · speak now" : recording.HasTurn ? "AI · recording paused" : "AI · auto language · ready";
            if (recognizingSpeech) hint.Text = "Recognizing speech in the two selected languages.";
            else if (busy) hint.Text = "AI is answering in both languages.";
            else if (recording.IsRecording) hint.Text = "Live text · language is checked automatically.";
            else if (!recording.HasTurn) hint.Text = "Microphone: record · ↑: ask AI";
        }
        if (!auth.IsConnected && !busy)
        {
            prompt.Text = "Connect ChatGPT to begin";
            hint.Text = "Tap the microphone or send button to sign in.";
            SemanticProperties.SetDescription(shutter, "Connect ChatGPT to enable recording");
        }
        SemanticProperties.SetDescription(send, !auth.IsConnected ? "Connect ChatGPT to send messages" : mode.IsAi ? "Ask AI and receive an answer in both languages" : "Send message, translate and switch speakers");
        var speakerGreen = Color.FromArgb("#D1EEDC");
        var speakerInk = Color.FromArgb("#205C3B");
        firstSpeaker.BackgroundColor = !mode.IsAi && mode.FromFirst ? speakerGreen : Colors.Transparent;
        secondSpeaker.BackgroundColor = !mode.IsAi && !mode.FromFirst ? speakerGreen : Colors.Transparent;
        firstSpeaker.Text = (!mode.IsAi && mode.FromFirst ? "🎙 " : "") + first.SpeakerName;
        secondSpeaker.Text = (!mode.IsAi && !mode.FromFirst ? "🎙 " : "") + second.SpeakerName;
        firstSpeaker.TextColor = !mode.IsAi && mode.FromFirst ? speakerInk : Muted; secondSpeaker.TextColor = !mode.IsAi && !mode.FromFirst ? speakerInk : Muted;
        SemanticProperties.SetDescription(firstSpeaker, $"{first.Name}, " + (!mode.IsAi && mode.FromFirst ? "active speaker" : "select speaker"));
        SemanticProperties.SetDescription(secondSpeaker, $"{second.Name}, " + (!mode.IsAi && !mode.FromFirst ? "active speaker" : "select speaker"));
        aiSpeaker.BackgroundColor = mode.IsAi ? Color.FromArgb("#BFE5CA") : Colors.White;
        SemanticProperties.SetDescription(aiSpeaker, mode.IsAi ? "AI mode active, automatic language detection" : "Ask AI in both conversation languages");
        firstSpeaker.IsEnabled = secondSpeaker.IsEnabled = aiSpeaker.IsEnabled = !busy && !recording.HasTurn;
        var hasInput = recording.HasTurn || !string.IsNullOrWhiteSpace(draft.Text) || pendingImage is not null;
        cancel.Text = hasInput ? "×  Discard input" : "Cancel";
        cancel.IsVisible = hasInput || (busy && !discardingInput);
        cancel.IsEnabled = !discardingInput || !busy;
        SemanticProperties.SetDescription(cancel, hasInput ? "Stop and delete the current input and attached image" : "Cancel current operation");
        progress.IsRunning = busy; progress.IsVisible = busy;
    }
}
