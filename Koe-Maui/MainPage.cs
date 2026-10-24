using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed record ConversationTurn(string Source, string Target, string Original, string Translation, string Romaji, ImageAttachment? Attachment = null)
{
    public string Direction => string.IsNullOrWhiteSpace(Target) ? Source : $"{Source}  →  {Target}";
    public bool FromGerman => Source == "Deutsch";
    public string RomajiCaption => "Rōmaji · " + Romaji;
    public bool TranslationHasRomaji => FromGerman && !string.IsNullOrWhiteSpace(Romaji);
    public bool OriginalHasRomaji => !FromGerman && !string.IsNullOrWhiteSpace(Romaji);
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
    readonly CollectionView messages;
    int scrollRevision;
    string textSize = Preferences.Default.Get("koe.text-size", "Mittel");
    bool showRomaji = Preferences.Default.Get("koe.show-romaji", true);
    public bool ShowRomaji => showRomaji;
    public double MessageFontSize => textSize == "Klein" ? 16 : textSize == "Groß" ? 23 : 18;
    public double OriginalFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 19 : 14;
    public double RomajiFontSize => textSize == "Klein" ? 13 : textSize == "Groß" ? 18 : 14;
    readonly Editor draft = new() { Placeholder = "Nachricht eingeben …", PlaceholderColor = Muted, TextColor = Ink, BackgroundColor = Colors.Transparent, FontSize = 16, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44, MaximumHeightRequest = 104 };
    readonly Button attach = new() { Text = "+", FontSize = 28, WidthRequest = 40, HeightRequest = 44, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Blue, VerticalOptions = LayoutOptions.End };
    readonly Image attachmentPreview = new() { WidthRequest = 70, HeightRequest = 70, Aspect = Aspect.AspectFill };
    readonly Label attachmentName = new() { FontSize = 12, TextColor = Ink, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    readonly Button removeAttachment = new() { Text = "×", FontSize = 24, WidthRequest = 40, HeightRequest = 40, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Muted };
    readonly Border attachmentCard;
    ImageAttachment? pendingImage;
    bool pickingAttachment;
    readonly Button connect = new() { Text = "Continue with ChatGPT", BackgroundColor = Blue, TextColor = Colors.White, CornerRadius = 18, FontSize = 14 };
    readonly Button send = new() { Text = "↑", FontSize = 25, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0, BackgroundColor = Blue, TextColor = Colors.White, VerticalOptions = LayoutOptions.End };
    readonly Button shutter = new() { ImageSource = "microphone.png", FontSize = 28, WidthRequest = 78, HeightRequest = 78, CornerRadius = 39, Padding = 0, BackgroundColor = Color.FromArgb("#CF4F55"), TextColor = Colors.White };
    readonly Button endConversation = new() { Text = "■", FontSize = 19, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#CE4949"), WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0 };
    readonly Button pauseRecording = new() { Text = "Ⅱ", FontSize = 21, TextColor = Ink, BackgroundColor = Color.FromArgb("#CBE2F3"), WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    readonly Button cancel = new() { Text = "Abbrechen", FontSize = 13, BackgroundColor = Colors.Transparent, TextColor = Muted, IsVisible = false, Padding = new Thickness(6, 4) };
    readonly Button german = LanguageButton("Deutsch"), japanese = LanguageButton("日本語");
    readonly Label status = new() { FontSize = 12, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
    readonly Label account = new() { FontSize = 12, TextColor = Muted };
    readonly Label prompt = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ink };
    readonly Label hint = new() { FontSize = 12, TextColor = Muted };
    readonly ActivityIndicator progress = new() { Color = Colors.White, WidthRequest = 42, HeightRequest = 42, IsVisible = false, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true, ZIndex = 1 };
    readonly Border signInCard;
    readonly Border shutterRing;
    CancellationTokenSource? operation;
    bool busy, pageVisible, syncingDraft, pausingForEdit;
    bool fromGerman = true;

    public MainPage(ChatGptAuth auth, OpenAiTranslator translator, ISpeechCapture speech)
    {
        this.auth = auth; this.translator = translator; recording = new SpeechDraft(speech);
        Title = "Koe"; BackgroundColor = Color.FromArgb("#F0F6FA");
        var menu = new Button { Text = "•••", FontSize = 23, TextColor = Ink, BackgroundColor = Colors.Transparent, WidthRequest = 48, Padding = 0 };
        SemanticProperties.SetDescription(menu, "Gesprächsoptionen");
        menu.Clicked += async (_, _) => await ShowMenuAsync();
        var header = new Grid { Padding = new Thickness(18, 12), ColumnSpacing = 12, BackgroundColor = Colors.White, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = "Koe", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Ink }, account } }, 0, 0);
        header.Add(menu, 1, 0);
        signInCard = new Border
        {
            Margin = new Thickness(16, 10), Padding = 14, StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E1EEF8"), StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new VerticalStackLayout { Spacing = 9, Children = { new Label { Text = "Zwei Sprachen. Ein Gespräch.", TextColor = Ink, FontSize = 17, FontAttributes = FontAttributes.Bold }, new Label { Text = "Verbinde ChatGPT, um eure Nachrichten zu übersetzen.", TextColor = Muted, FontSize = 13 }, connect } }
        };
        messages = new CollectionView
        {
            Margin = new Thickness(14, 0), ItemsSource = turns, SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            EmptyView = new VerticalStackLayout { Padding = new Thickness(30, 42), Spacing = 12, Children =
            {
                new Label { Text = "Hallo.  こんにちは。", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Blue, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Tippen: aufnehmen. Nochmals tippen: übersetzen.\nDie andere Person startet ihre Aufnahme selbst.", FontSize = 15, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.2 }
            } },
            ItemTemplate = new DataTemplate(CreateBubble)
        };
        var languages = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        languages.Add(german, 0, 0);
        languages.Add(new Label { Text = "⇄", FontSize = 21, TextColor = Blue, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        languages.Add(japanese, 2, 0);
        var input = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        input.Add(attach, 0, 0); input.Add(draft, 1, 0); input.Add(send, 2, 0);
        var attachmentRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        attachmentRow.Add(attachmentPreview, 0, 0);
        attachmentRow.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 4, Children = { attachmentName, new Label { Text = "Bildtext + deine Nachricht übersetzen", FontSize = 11, TextColor = Muted } } }, 1, 0);
        attachmentRow.Add(removeAttachment, 2, 0);
        attachmentCard = new Border { IsVisible = false, Padding = 8, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE8F0"), StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = attachmentRow };
        var inputBorder = new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE8F0"), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Padding = new Thickness(12, 4, 5, 4), Content = input };
        shutterRing = new Border { WidthRequest = 94, HeightRequest = 94, Padding = 6, StrokeThickness = 2, Stroke = Blue, StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = shutter };
        prompt.HorizontalTextAlignment = TextAlignment.Center;
        hint.HorizontalTextAlignment = TextAlignment.Center;
        var turnCaption = new VerticalStackLayout { Spacing = 4, Children = { prompt, hint } };
        var turnControls = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        turnControls.Add(pauseRecording, 0, 0);
        turnControls.Add(new Grid { WidthRequest = 94, HeightRequest = 94, HorizontalOptions = LayoutOptions.Center, Children = { shutterRing, progress } }, 1, 0);
        turnControls.Add(new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 2, Children = { endConversation, cancel } }, 2, 0);
        var composer = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 14), Spacing = 10, BackgroundColor = Color.FromArgb("#E8F2F9"), Children = { languages, attachmentCard, inputBorder, turnCaption, turnControls, status } };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(header, 0, 0); root.Add(signInCard, 0, 1); root.Add(messages, 0, 2); root.Add(composer, 0, 3);
        Content = root;
        SemanticProperties.SetDescription(endConversation, "Gespräch beenden");
        SemanticProperties.SetDescription(send, "Geschriebenen Text übersetzen");
        SemanticProperties.SetDescription(attach, "Foto, Kamera oder Bilddatei auswählen");
        SemanticProperties.SetDescription(removeAttachment, "Bild entfernen");
        attach.Clicked += async (_, _) => await PickAttachmentAsync();
        removeAttachment.Clicked += (_, _) => { if (!busy) SetAttachment(null); };
        connect.Clicked += async (_, _) => await ConnectAsync();
        shutter.Clicked += async (_, _) => await AdvanceConversationAsync(false);
        endConversation.Clicked += async (_, _) => await AdvanceConversationAsync(true);
        pauseRecording.Clicked += async (_, _) => await TogglePauseAsync();
        send.Clicked += async (_, _) => await SendTextAsync();
        cancel.Clicked += (_, _) => operation?.Cancel();
        german.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { fromGerman = true; UpdateControls(); } };
        japanese.Clicked += (_, _) => { if (!busy && !recording.HasTurn) { fromGerman = false; UpdateControls(); } };
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
        UpdateControls();
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
        var translatedRomaji = new Label { TextColor = Blue, LineHeight = 1.15 };
        translatedRomaji.SetBinding(Label.TextProperty, nameof(ConversationTurn.RomajiCaption));
        translatedRomaji.SetBinding(Label.IsVisibleProperty, nameof(ConversationTurn.TranslationHasRomaji));
        translatedRomaji.SetBinding(Label.FontSizeProperty, new Binding(nameof(RomajiFontSize), source: this));
        var originalRomaji = new Label { TextColor = Blue, LineHeight = 1.15 };
        originalRomaji.SetBinding(Label.TextProperty, nameof(ConversationTurn.RomajiCaption));
        originalRomaji.SetBinding(Label.IsVisibleProperty, nameof(ConversationTurn.OriginalHasRomaji));
        originalRomaji.SetBinding(Label.FontSizeProperty, new Binding(nameof(RomajiFontSize), source: this));
        var translatedReading = new VerticalStackLayout { Children = { translatedRomaji } };
        translatedReading.SetBinding(IsVisibleProperty, new Binding(nameof(ShowRomaji), source: this));
        var originalReading = new VerticalStackLayout { Children = { originalRomaji } };
        originalReading.SetBinding(IsVisibleProperty, new Binding(nameof(ShowRomaji), source: this));
        var copyTranslation = CopyButton("Übersetzung kopieren");
        copyTranslation.Clicked += async (_, _) =>
        {
            if (copyTranslation.BindingContext is ConversationTurn turn) await CopyTextAsync(turn.Translation);
        };
        var copyOriginal = CopyButton("Originaltext kopieren");
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
            bubble.HorizontalOptions = turn.FromGerman ? LayoutOptions.End : LayoutOptions.Start;
            bubble.Margin = turn.FromGerman ? new Thickness(34, 6, 4, 6) : new Thickness(4, 6, 34, 6);
            bubble.BackgroundColor = turn.FromGerman ? Color.FromArgb("#D3E8F8") : Colors.White;
            bubble.StrokeShape = new RoundRectangle { CornerRadius = turn.FromGerman ? new CornerRadius(17, 17, 17, 4) : new CornerRadius(17, 17, 4, 17) };
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
        try { await Clipboard.Default.SetTextAsync(text); SetStatus("Text kopiert."); }
        catch { SetStatus("Der Text konnte nicht kopiert werden. Bitte erneut versuchen."); }
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
        catch { SetStatus("Die Anmeldung konnte nicht geladen werden. Bitte erneut verbinden."); }
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
        busy = true; pickingAttachment = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.IsRecording) await recording.PauseAsync(operation.Token);
            var choice = await DisplayActionSheetAsync("Bild hinzufügen", "Abbrechen", null, "Foto auswählen", "Kamera", "Bilddatei auswählen");
            FileResult? file = null;
            switch (choice)
            {
                case "Foto auswählen":
                    var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });
                    file = photos.FirstOrDefault(); break;
                case "Kamera":
                    if (!MediaPicker.Default.IsCaptureSupported) { SetStatus("Auf diesem Gerät ist keine Kamera verfügbar."); return; }
                    file = await MediaPicker.Default.CapturePhotoAsync(); break;
                case "Bilddatei auswählen":
                    file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Bild auswählen", FileTypes = FilePickerFileType.Images }); break;
                default: return;
            }
            if (file is null) return;
            SetAttachment(await AttachmentLoader.LoadAsync(file, operation.Token));
            SetStatus("Bild bereit. Sprich oder schreibe dazu, oder sende das Bild direkt mit ↑.");
        }
        catch (OperationCanceledException) { }
        catch (PermissionException) { SetStatus("Bitte erlaube Koe den Kamera- bzw. Fotozugriff in den iPhone-Einstellungen."); }
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
        var selection = await DisplayActionSheetAsync(auth.IsConnected ? $"ChatGPT · {auth.AccountLabel}" : "Gespräch", "Schließen", null, "Einstellungen", "Verlauf löschen", "Nutzung verwalten", auth.IsConnected ? "Abmelden" : "Mit ChatGPT verbinden");
        switch (selection)
        {
            case "Verlauf löschen": turns.Clear(); break;
            case "Einstellungen": await ShowSettingsAsync(); break;
            case "Nutzung verwalten": await Browser.Default.OpenAsync("https://chatgpt.com/settings/usage"); break;
            case "Abmelden": case "Mit ChatGPT verbinden": await ConnectAsync(); break;
        }
    }

    async Task ShowSettingsAsync()
    {
        var romajiAction = showRomaji ? "Romaji ausschalten" : "Romaji einschalten";
        var choice = await DisplayActionSheetAsync("Einstellungen", "Schließen", null, $"Textgröße · {textSize}", romajiAction);
        if (choice == romajiAction)
        {
            showRomaji = !showRomaji;
            Preferences.Default.Set("koe.show-romaji", showRomaji);
            OnPropertyChanged(nameof(ShowRomaji));
            ScrollToLatest();
        }
        else if (choice == $"Textgröße · {textSize}") await ShowTextSizeAsync();
    }

    async Task ShowTextSizeAsync()
    {
        var selection = await DisplayActionSheetAsync($"Textgröße · {textSize}", "Abbrechen", null, "Klein", "Mittel", "Groß");
        if (selection is not ("Klein" or "Mittel" or "Groß")) return;
        textSize = selection;
        Preferences.Default.Set("koe.text-size", selection);
        OnPropertyChanged(nameof(MessageFontSize));
        OnPropertyChanged(nameof(OriginalFontSize));
        OnPropertyChanged(nameof(RomajiFontSize));
        draft.FontSize = OriginalFontSize + 2;
        ScrollToLatest();
    }

    async Task ConnectAsync()
    {
        if (busy || recording.HasTurn) return;
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (auth.IsConnected) { await auth.SignOutAsync(operation.Token); SetStatus("Abgemeldet."); }
            else { await auth.SignInAsync(operation.Token); SetStatus(null); }
        }
        catch (OperationCanceledException) { SetStatus("Anmeldung abgebrochen."); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task AdvanceConversationAsync(bool endingConversation)
    {
        if (busy || !auth.IsConnected) return;
        draft.Unfocus();
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.HasTurn || endingConversation)
            {
                var text = (await recording.FinishAsync(operation.Token)).Trim();
                if (string.IsNullOrWhiteSpace(text) && pendingImage is null) { SetStatus(endingConversation ? "Gespräch beendet." : "Kein Text erkannt. Tippe zum erneuten Sprechen."); return; }
                await TranslateDraftAsync(text, operation.Token);
                SetStatus(endingConversation ? "Gespräch beendet." : "Übersetzt. Die nächste Person kann die Aufnahme starten.");
            }
            else if (pageVisible)
            {
                await recording.StartAsync(CultureInfo.GetCultureInfo(fromGerman ? "de-DE" : "ja-JP"), operation.Token);
                SetStatus(null);
            }
        }
        catch (OperationCanceledException) { recording.Suspend(); SetStatus("Pausiert. Dein Text bleibt erhalten."); }
        catch (Exception ex) { recording.Suspend(); SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TogglePauseAsync()
    {
        if (busy || !recording.HasTurn) return;
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (recording.IsRecording)
            {
                await recording.PauseAsync(operation.Token);
                SetStatus("Aufnahme pausiert. Du kannst den Text bearbeiten.");
            }
            else
            {
                draft.Unfocus();
                await recording.StartAsync(CultureInfo.GetCultureInfo(fromGerman ? "de-DE" : "ja-JP"), operation.Token);
                SetStatus(null);
            }
        }
        catch (OperationCanceledException) { recording.Suspend(); SetStatus("Aufnahme pausiert."); }
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
        var text = draft.Text?.Trim() ?? "";
        if (busy || recording.IsRecording || !auth.IsConnected || (string.IsNullOrWhiteSpace(text) && pendingImage is null)) return;
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try { await recording.FinishAsync(operation.Token); await TranslateDraftAsync(text, operation.Token); }
        catch (OperationCanceledException) { SetStatus("Übersetzung abgebrochen. Dein Text bleibt erhalten."); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TranslateDraftAsync(string text, CancellationToken ct)
    {
        SetStatus("Übersetze …");
        var direction = fromGerman;
        var image = pendingImage;
        var translated = await translator.TranslateAsync(text, direction, ct, image);
        ct.ThrowIfCancellationRequested();
        if (image is not null)
        {
            if (translated.ImageText is { } imageText)
                turns.Add(new(imageText.FromGerman ? "Deutsch" : "日本語", imageText.FromGerman ? "日本語" : "Deutsch", imageText.Original, imageText.Translation, imageText.Romaji, image));
            else turns.Add(new("Bild", "", "", "Kein lesbarer Text im Bild erkannt.", "", image));
        }
        if (!string.IsNullOrWhiteSpace(text))
            turns.Add(new(direction ? "Deutsch" : "日本語", direction ? "日本語" : "Deutsch", text, translated.Translation, translated.Romaji));
        recording.Clear(); SetAttachment(null);
        fromGerman = !string.IsNullOrWhiteSpace(text) ? !direction : translated.ImageText is { } detected ? !detected.FromGerman : direction;
        SetStatus(null);
    }

    void FinishOperation() { operation?.Dispose(); operation = null; busy = false; UpdateControls(); ScrollToLatest(); }
    void SetStatus(string? text) { status.Text = text; status.IsVisible = !string.IsNullOrWhiteSpace(text); }

    void UpdateControls()
    {
        account.Text = auth.IsConnected ? "Verbunden · Deutsch ↔ 日本語" : "Deutsch ↔ 日本語";
        signInCard.IsVisible = !auth.IsConnected; connect.IsEnabled = !busy;
        draft.IsReadOnly = busy && !pausingForEdit;
        send.IsEnabled = !busy && !recording.IsRecording && auth.IsConnected && (!string.IsNullOrWhiteSpace(draft.Text) || pendingImage is not null);
        attach.IsEnabled = !busy;
        removeAttachment.IsEnabled = !busy;
        shutter.IsEnabled = !busy && auth.IsConnected;
        shutter.Text = !busy && recording.HasTurn ? "↑" : "";
        shutter.ImageSource = !busy && !recording.HasTurn ? ImageSource.FromFile("microphone.png") : null;
        var buttonColor = busy ? Color.FromArgb("#7E96AB") : recording.IsRecording ? Color.FromArgb("#B63E46") : recording.HasTurn ? Color.FromArgb("#E8BD70") : Color.FromArgb("#CF4F55");
        shutter.BackgroundColor = buttonColor;
        shutterRing.Stroke = buttonColor;
        shutter.TextColor = !busy && recording.State == RecordingState.Paused ? Ink : Colors.White;
        shutter.Opacity = 1;
        SemanticProperties.SetDescription(shutter, recording.HasTurn ? "Satz übersetzen und zur anderen Sprache wechseln" : "Aufnahme starten");
        prompt.Text = busy ? "Einen Moment …" : recording.IsRecording ? (fromGerman ? "Deutsch · sprich jetzt" : "日本語 · sprich jetzt") : recording.HasTurn ? "Aufnahme pausiert" : (fromGerman ? "Deutsch · bereit" : "日本語 · bereit");
        hint.Text = busy ? "Dein Satz wird verarbeitet." : recording.IsRecording ? "Nochmals tippen: übersetzen" : recording.HasTurn ? "Text bearbeiten · links fortsetzen · Mitte übersetzen" : "Tippen, um die Aufnahme zu starten";
        german.BackgroundColor = fromGerman ? Color.FromArgb("#CBE2F3") : Colors.Transparent;
        japanese.BackgroundColor = fromGerman ? Colors.Transparent : Color.FromArgb("#CBE2F3");
        german.TextColor = fromGerman ? Ink : Muted; japanese.TextColor = fromGerman ? Muted : Ink;
        german.IsEnabled = japanese.IsEnabled = !busy && !recording.HasTurn;
        endConversation.IsEnabled = recording.HasTurn && !busy; cancel.IsVisible = busy;
        pauseRecording.IsEnabled = recording.HasTurn && !busy;
        pauseRecording.Text = recording.State == RecordingState.Paused ? "▶" : "Ⅱ";
        SemanticProperties.SetDescription(pauseRecording, recording.State == RecordingState.Paused ? "Aufnahme fortsetzen" : "Aufnahme pausieren");
        progress.IsRunning = busy; progress.IsVisible = busy;
    }
}
