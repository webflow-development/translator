using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed record ConversationTurn(string Source, string Target, string Original, string Translation, string SourceTransliteration, string TargetTransliteration, ImageAttachment? Attachment = null, bool FromFirst = true)
{
    public string Direction => string.IsNullOrWhiteSpace(Target) ? Source : $"{Source}  →  {Target}";
    public string TranslatedReadingCaption => "Transliteration · " + TargetTransliteration;
    public string OriginalReadingCaption => "Transliteration · " + SourceTransliteration;
    public bool TranslationHasReading => !string.IsNullOrWhiteSpace(TargetTransliteration);
    public bool OriginalHasReading => !string.IsNullOrWhiteSpace(SourceTransliteration);
    public ImageSource? Picture => Attachment is null ? null : ImageSource.FromStream(() => new MemoryStream(Attachment.Bytes, writable: false));
    public bool HasPicture => Attachment is not null;
    public bool HasOriginal => !string.IsNullOrWhiteSpace(Original);
}

public sealed class MainPage : ContentPage
{
    static readonly Color Ink = Color.FromArgb("#19384E"), Muted = Color.FromArgb("#638295"), Blue = Color.FromArgb("#568FBE");
    readonly ChatGptAuth auth;
    readonly OpenAiTranslator translator;
    readonly SpeechDraft recording;
    readonly ObservableCollection<ConversationTurn> turns = [];
    readonly Label liveTranslation = new() { TextColor = Ink, FontSize = 18, LineHeight = 1.15 };
    readonly Border livePreview;
    readonly CollectionView messages;
    int scrollRevision;
    string textSize = Preferences.Default.Get("koe.text-size", "Mittel");
    bool showTransliteration = Preferences.Default.Get("koe.show-transliteration", Preferences.Default.Get("koe.show-romaji", true));
    string TextSizeLabel => textSize == "Klein" ? "Small" : textSize == "Groß" ? "Large" : "Medium";
    public bool ShowTransliteration => showTransliteration;
    public double MessageFontSize => textSize == "Klein" ? 16 : textSize == "Groß" ? 23 : 18;
    public double OriginalFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 19 : 14;
    public double ReadingFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 18 : 14;
    readonly Editor draft = new() { Placeholder = "Type a message …", PlaceholderColor = Muted, TextColor = Ink, BackgroundColor = Colors.Transparent, FontSize = 16, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44, MaximumHeightRequest = 104 };
    readonly Button attach = new() { Text = "+", FontSize = 28, WidthRequest = 40, HeightRequest = 44, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Blue, VerticalOptions = LayoutOptions.Start };
    readonly Image attachmentPreview = new() { WidthRequest = 70, HeightRequest = 70, Aspect = Aspect.AspectFill };
    readonly Label attachmentName = new() { FontSize = 12, TextColor = Ink, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    readonly Button removeAttachment = new() { Text = "×", FontSize = 24, WidthRequest = 40, HeightRequest = 40, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Muted };
    readonly Border attachmentCard;
    ImageAttachment? pendingImage;
    bool pickingAttachment;
    readonly Button connect = new() { Text = "Continue with ChatGPT", BackgroundColor = Blue, TextColor = Colors.White, CornerRadius = 18, FontSize = 14 };
    readonly Button send = new() { Text = "↑", FontSize = 30, WidthRequest = 56, HeightRequest = 56, CornerRadius = 28, Padding = 0, BackgroundColor = Blue, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
    readonly Button shutter = new() { ImageSource = "microphone.png", FontSize = 28, WidthRequest = 78, HeightRequest = 78, CornerRadius = 39, Padding = 0, BackgroundColor = Color.FromArgb("#CF4F55"), TextColor = Colors.White };
    readonly Button cancel = new() { Text = "×  Discard input", FontSize = 14, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#B63E46"), IsVisible = false, MinimumHeightRequest = 44, Padding = new Thickness(12, 6) };
    readonly Button firstSpeaker = LanguageButton("English"), secondSpeaker = LanguageButton("Japanese");
    readonly Label status = new() { FontSize = 12, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
    readonly Label account = new() { FontSize = 12, TextColor = Muted };
    readonly Label prompt = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ink };
    readonly Label hint = new() { FontSize = 12, TextColor = Muted };
    readonly ActivityIndicator progress = new() { Color = Colors.White, WidthRequest = 42, HeightRequest = 42, IsVisible = false, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true, ZIndex = 1 };
    readonly Border signInCard;
    readonly Border shutterRing;
    CancellationTokenSource? operation;
    bool busy, pageVisible, syncingDraft, pausingForEdit;
    bool discardingInput;
    bool fromFirst = true;
    ConversationLanguage first = ConversationLanguage.Get(Preferences.Default.Get("koe.language.first", "en"));
    ConversationLanguage second = ConversationLanguage.Get(Preferences.Default.Get("koe.language.second", "ja"));
    ConversationLanguage SourceLanguage => fromFirst ? first : second;
    ConversationLanguage TargetLanguage => fromFirst ? second : first;

    public MainPage(ChatGptAuth auth, OpenAiTranslator translator, ISpeechCapture speech)
    {
        if (first.Code == second.Code) second = ConversationLanguage.All.First(x => x.Code != first.Code);
        this.auth = auth; this.translator = translator; recording = new SpeechDraft(speech);
        Title = "Koe"; BackgroundColor = Color.FromArgb("#F0F6FA");
        // Resize the whole chat above the keyboard, including the composer and send button.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        var menu = new Button { Text = "•••", FontSize = 23, TextColor = Ink, BackgroundColor = Colors.Transparent, WidthRequest = 48, Padding = 0 };
        SemanticProperties.SetDescription(menu, "Conversation options");
        menu.Clicked += async (_, _) => await ShowMenuAsync();
        var header = new Grid { Padding = new Thickness(18, 12), ColumnSpacing = 12, BackgroundColor = Colors.White, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = "Koe", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Ink }, account } }, 0, 0);
        header.Add(menu, 1, 0);
        signInCard = new Border
        {
            Margin = new Thickness(16, 10), Padding = 14, StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E1EEF8"), StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new VerticalStackLayout { Spacing = 9, Children = { new Label { Text = "Two languages. One conversation.", TextColor = Ink, FontSize = 17, FontAttributes = FontAttributes.Bold }, new Label { Text = "Connect ChatGPT to translate your messages.", TextColor = Muted, FontSize = 13 }, connect } }
        };
        messages = new CollectionView
        {
            Margin = new Thickness(14, 0), ItemsSource = turns, SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            EmptyView = new VerticalStackLayout { Padding = new Thickness(30, 42), Spacing = 12, Children =
            {
                new Label { Text = "Hello.  こんにちは。", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Blue, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Microphone: record, pause and resume.\nSend with ↑ to translate and switch speakers.", FontSize = 15, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.2 }
            } },
            ItemTemplate = new DataTemplate(CreateBubble)
        };
        var languages = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        languages.Add(firstSpeaker, 0, 0);
        languages.Add(new Label { Text = "⇄", FontSize = 21, TextColor = Blue, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        languages.Add(secondSpeaker, 2, 0);
        var input = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        input.Add(attach, 0, 0); input.Add(draft, 1, 0);
        var attachmentRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        attachmentRow.Add(attachmentPreview, 0, 0);
        attachmentRow.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 4, Children = { attachmentName, new Label { Text = "Translate image text + your message", FontSize = 11, TextColor = Muted } } }, 1, 0);
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
        var composer = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 14), Spacing = 10, BackgroundColor = Color.FromArgb("#E8F2F9"), Children = { languages, attachmentCard, inputBorder, turnCaption, turnControls, cancel, status } };
        liveTranslation.SetBinding(Label.FontSizeProperty, new Binding(nameof(MessageFontSize), source: this));
        livePreview = new Border
        {
            IsVisible = false, Margin = new Thickness(18, 4), Padding = 12, StrokeThickness = 0,
            BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout { Spacing = 4, Children =
            {
                new Label { Text = "Translating · preview", FontSize = 11, TextColor = Muted },
                new ScrollView { MaximumHeightRequest = 120, Content = liveTranslation }
            } }
        };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        root.Add(header, 0, 0); root.Add(signInCard, 0, 1); root.Add(messages, 0, 2); root.Add(livePreview, 0, 3); root.Add(composer, 0, 4);
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
        firstSpeaker.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { fromFirst = true; UpdateControls(); } };
        secondSpeaker.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { fromFirst = false; UpdateControls(); } };
        draft.TextChanged += (_, _) => { if (!syncingDraft) recording.Edit(draft.Text ?? ""); UpdateControls(); };
        draft.Focused += async (_, _) =>
        {
            if (busy || !recording.IsRecording) return;
            pausingForEdit = true;
            try { await TogglePauseAsync(); }
            finally { pausingForEdit = false; UpdateControls(); }
        };
        recording.Changed += () => MainThread.BeginInvokeOnMainThread(SyncDraft);
        recording.Error += error => MainThread.BeginInvokeOnMainThread(() => { if (!busy) SetStatus(error); });
        turns.CollectionChanged += (_, _) => ScrollToLatest();
        messages.SizeChanged += (_, _) => ScrollToLatest();
        messages.Loaded += (_, _) => ScrollToLatest();
        draft.FontSize = OriginalFontSize + 2;
        draft.HandlerChanged += (_, _) => AlignComposerText();
        draft.Loaded += (_, _) => AlignComposerText();
        UpdateControls();
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

    View CreateBubble()
    {
        var direction = new Label { FontSize = 11, TextColor = Muted };
        direction.SetBinding(Label.TextProperty, nameof(ConversationTurn.Direction));
        var original = new Label { FontSize = 14, TextColor = Muted, LineHeight = 1.15 };
        original.SetBinding(Label.TextProperty, nameof(ConversationTurn.Original));
        original.SetBinding(Label.FontSizeProperty, new Binding(nameof(OriginalFontSize), source: this));
        var translation = new Label { FontSize = 18, TextColor = Ink, LineHeight = 1.15 };
        translation.SetBinding(Label.TextProperty, nameof(ConversationTurn.Translation));
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
        var copyTranslation = CopyButton("Copy translation");
        copyTranslation.Clicked += async (_, _) =>
        {
            if (copyTranslation.BindingContext is ConversationTurn turn) await CopyTextAsync(turn.Translation);
        };
        var copyOriginal = CopyButton("Copy original");
        copyOriginal.Clicked += async (_, _) =>
        {
            if (copyOriginal.BindingContext is ConversationTurn turn) await CopyTextAsync(turn.Original);
        };
        var translatedRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        translatedRow.Add(translation, 0, 0); translatedRow.Add(copyTranslation, 1, 0);
        var originalRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        originalRow.SetBinding(IsVisibleProperty, nameof(ConversationTurn.HasOriginal));
        originalRow.Add(original, 0, 0); originalRow.Add(copyOriginal, 1, 0);
        var picture = new Image { HeightRequest = 150, Aspect = Aspect.AspectFit };
        picture.SetBinding(Image.SourceProperty, nameof(ConversationTurn.Picture));
        picture.SetBinding(IsVisibleProperty, nameof(ConversationTurn.HasPicture));
        var bubble = new Border
        {
            Margin = new Thickness(4, 6, 34, 6), Padding = new Thickness(14, 11), StrokeThickness = 0, BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17, 17, 4, 17) }, HorizontalOptions = LayoutOptions.Start, MaximumWidthRequest = 330,
            Content = new VerticalStackLayout { Spacing = 7, Children = { direction, picture, translatedRow, translatedReading, originalRow, originalReading } }
        };
        bubble.BindingContextChanged += (_, _) =>
        {
            if (bubble.BindingContext is not ConversationTurn turn) return;
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

    async Task CopyTextAsync(string text)
    {
        try { await Clipboard.Default.SetTextAsync(text); SetStatus("Text copied."); }
        catch { SetStatus("Could not copy text. Please try again."); }
    }

    void ScrollToLatest()
    {
        var revision = ++scrollRevision;
        if (turns.Count == 0) return;
        // Collection changes arrive before iOS has measured the new cell. Scroll after layout.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            if (revision != scrollRevision || !pageVisible || turns.Count == 0 || messages.Handler is null) return;
            messages.ScrollTo(turns.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); pageVisible = true;
        try { await auth.LoadAsync(); }
        catch { SetStatus("Could not load your sign-in. Please reconnect."); }
        UpdateControls();
    }

    protected override void OnDisappearing()
    {
        if (!pickingAttachment) { pageVisible = false; operation?.Cancel(); recording.Suspend(); }
        base.OnDisappearing();
    }

    async Task PickAttachmentAsync()
    {
        if (busy) return;
        draft.Unfocus();
        discardingInput = false; busy = true; pickingAttachment = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.IsRecording) await recording.PauseAsync(operation.Token);
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
        UpdateControls();
    }

    async Task ShowMenuAsync()
    {
        if (busy || recording.HasTurn) return;
        var selection = await DisplayActionSheetAsync(auth.IsConnected ? $"ChatGPT · {auth.AccountLabel}" : "Conversation", "Close", null, "Settings", "Clear history", "Manage usage", auth.IsConnected ? "Sign out" : "Connect ChatGPT");
        switch (selection)
        {
            case "Clear history": turns.Clear(); break;
            case "Settings": await ShowSettingsAsync(); break;
            case "Manage usage": await Browser.Default.OpenAsync("https://chatgpt.com/settings/usage"); break;
            case "Sign out": case "Connect ChatGPT": await ConnectAsync(); break;
        }
    }

    async Task ShowSettingsAsync()
    {
        var transliterationAction = showTransliteration ? "Hide transliteration" : "Show transliteration";
        var choice = await DisplayActionSheetAsync("Settings", "Close", null, "Conversation languages", $"Text size · {TextSizeLabel}", transliterationAction);
        if (choice == "Conversation languages") { await ShowLanguagesAsync(); return; }
        if (choice == transliterationAction)
        {
            showTransliteration = !showTransliteration;
            Preferences.Default.Set("koe.show-transliteration", showTransliteration);
            OnPropertyChanged(nameof(ShowTransliteration));
            ScrollToLatest();
        }
        else if (choice == $"Text size · {TextSizeLabel}") await ShowTextSizeAsync();
    }

    async Task ShowLanguagesAsync()
    {
        var selectedFirst = await PickLanguageAsync("First speaker's language", ConversationLanguage.All);
        if (selectedFirst is null) return;
        var selectedSecond = await PickLanguageAsync("Second speaker's language", ConversationLanguage.All.Where(x => x.Code != selectedFirst.Code).ToArray());
        if (selectedSecond is null) return;
        first = selectedFirst; second = selectedSecond; fromFirst = true;
        Preferences.Default.Set("koe.language.first", first.Code);
        Preferences.Default.Set("koe.language.second", second.Code);
        UpdateControls();
    }

    async Task<ConversationLanguage?> PickLanguageAsync(string title, ConversationLanguage[] choices)
    {
        var completion = new TaskCompletionSource<ConversationLanguage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = false;
        var search = new SearchBar { Placeholder = "Search languages", BackgroundColor = Colors.White };
        var list = new CollectionView
        {
            ItemsSource = choices, SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var name = new Label { FontSize = 18, TextColor = Ink, VerticalTextAlignment = TextAlignment.Center };
                name.SetBinding(Label.TextProperty, nameof(ConversationLanguage.Name));
                return new Grid { Padding = new Thickness(18, 14), MinimumHeightRequest = 52, Children = { name } };
            })
        };
        var close = new Button { Text = "Cancel", TextColor = Blue, BackgroundColor = Colors.Transparent };
        var heading = new Grid { Padding = new Thickness(16, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        heading.Add(new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ink, VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        heading.Add(close, 1, 0);
        var content = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        content.Add(heading, 0, 0); content.Add(search, 0, 1);
        content.Add(new Label { Text = $"{choices.Length} languages · Some require an internet connection", TextColor = Muted, FontSize = 12, Margin = new Thickness(18, 4) }, 0, 2);
        content.Add(list, 0, 3);
        var page = new ContentPage { Title = title, SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All, BackgroundColor = Color.FromArgb("#F0F6FA"), Content = content };
        async Task CloseAsync(ConversationLanguage? selected)
        {
            if (closing) return;
            closing = true;
            search.Unfocus();
            await Navigation.PopModalAsync();
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
            list.ItemsSource = choices.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(query, StringComparison.OrdinalIgnoreCase) || CultureInfo.GetCultureInfo(x.Culture).NativeName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        page.Disappearing += (_, _) => { if (!closing) completion.TrySetResult(null); };
        await Navigation.PushModalAsync(page);
        return await completion.Task;
    }

    async Task ShowTextSizeAsync()
    {
        var selection = await DisplayActionSheetAsync($"Text size · {TextSizeLabel}", "Cancel", null, "Small", "Medium", "Large");
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

    async Task ConnectAsync()
    {
        if (busy || recording.HasTurn) return;
        discardingInput = false; busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (auth.IsConnected) { await auth.SignOutAsync(operation.Token); SetStatus("Signed out."); }
            else { await auth.SignInAsync(operation.Token); SetStatus(null); }
        }
        catch (OperationCanceledException) { SetStatus("Sign-in cancelled."); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TogglePauseAsync()
    {
        if (busy || !auth.IsConnected) return;
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
                await recording.StartAsync(CultureInfo.GetCultureInfo(SourceLanguage.Culture), operation.Token);
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
    }

    async Task SendTextAsync()
    {
        if (busy || !auth.IsConnected || (!recording.HasTurn && string.IsNullOrWhiteSpace(recording.Text) && pendingImage is null)) return;
        draft.Unfocus();
        discardingInput = false; busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            var text = (await recording.FinishAsync(operation.Token)).Trim();
            if (string.IsNullOrWhiteSpace(text) && pendingImage is null) { SetStatus("No speech detected. Tap the microphone to try again."); return; }
            await TranslateDraftAsync(text, operation.Token);
            SetStatus("Translated. The next speaker can start recording.");
        }
        catch (OperationCanceledException) { recording.Suspend(); SetStatus("Translation cancelled. Your draft has been kept."); }
        catch (Exception ex) { recording.Suspend(); SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TranslateDraftAsync(string text, CancellationToken ct)
    {
        SetStatus("Translating …");
        var direction = fromFirst;
        var image = pendingImage;
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
        TranslationResult translated;
        try { translated = await translator.TranslateAsync(text, SourceLanguage, TargetLanguage, ct, image, Preview, showTransliteration); }
        finally { closed = true; livePreview.IsVisible = false; liveTranslation.Text = ""; }
        ct.ThrowIfCancellationRequested();
        if (image is not null)
        {
            if (translated.ImageText is { } imageText)
                turns.Add(new(ConversationLanguage.All.FirstOrDefault(x => x.Code == imageText.Source)?.Name ?? imageText.Source, ConversationLanguage.Get(imageText.Target).Name, imageText.Original, imageText.Translation, imageText.SourceTransliteration, imageText.TargetTransliteration, image, imageText.Source == first.Code));
            else turns.Add(new("Image", "", "", "No legible text found in the image.", "", "", image));
        }
        if (!string.IsNullOrWhiteSpace(text))
            turns.Add(new(SourceLanguage.Name, TargetLanguage.Name, text, translated.Translation, translated.SourceTransliteration, translated.TargetTransliteration, null, direction));
        recording.Clear(); SetAttachment(null);
        fromFirst = !string.IsNullOrWhiteSpace(text) ? !direction : translated.ImageText is { } detected ? detected.Source != first.Code : direction;
        SetStatus(null);
    }

    void DiscardInput()
    {
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
        operation?.Dispose(); operation = null; busy = false;
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
        account.Text = (auth.IsConnected ? "Connected · " : "") + $"{first.Name} ↔ {second.Name}";
        signInCard.IsVisible = !auth.IsConnected; connect.IsEnabled = !busy;
        draft.IsReadOnly = busy && (!pausingForEdit || discardingInput);
        send.IsEnabled = !busy && auth.IsConnected && (recording.HasTurn || !string.IsNullOrWhiteSpace(draft.Text) || pendingImage is not null);
        attach.IsEnabled = !busy;
        removeAttachment.IsEnabled = !busy;
        shutter.IsEnabled = !busy && auth.IsConnected;
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
        var speakerGreen = Color.FromArgb("#D1EEDC");
        var speakerInk = Color.FromArgb("#205C3B");
        firstSpeaker.BackgroundColor = fromFirst ? speakerGreen : Colors.Transparent;
        secondSpeaker.BackgroundColor = fromFirst ? Colors.Transparent : speakerGreen;
        firstSpeaker.Text = (fromFirst ? "🎙 " : "") + first.Name;
        secondSpeaker.Text = (fromFirst ? "" : "🎙 ") + second.Name;
        firstSpeaker.TextColor = fromFirst ? speakerInk : Muted; secondSpeaker.TextColor = fromFirst ? Muted : speakerInk;
        SemanticProperties.SetDescription(firstSpeaker, $"{first.Name}, " + (fromFirst ? "active speaker" : "select speaker"));
        SemanticProperties.SetDescription(secondSpeaker, $"{second.Name}, " + (fromFirst ? "select speaker" : "active speaker"));
        firstSpeaker.IsEnabled = secondSpeaker.IsEnabled = !busy && !recording.HasTurn;
        var hasInput = recording.HasTurn || !string.IsNullOrWhiteSpace(draft.Text) || pendingImage is not null;
        cancel.Text = hasInput ? "×  Discard input" : "Cancel";
        cancel.IsVisible = hasInput || (busy && !discardingInput);
        cancel.IsEnabled = !discardingInput || !busy;
        SemanticProperties.SetDescription(cancel, hasInput ? "Stop and delete the current input and attached image" : "Cancel current operation");
        progress.IsRunning = busy; progress.IsVisible = busy;
    }
}
