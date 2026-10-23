using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed record ConversationTurn(string Source, string Target, string Original, string Translation)
{
    public string Direction => $"{Source}  →  {Target}";
    public bool FromGerman => Source == "Deutsch";
}

public sealed class MainPage : ContentPage
{
    static readonly Color Ink = Color.FromArgb("#19384E"), Muted = Color.FromArgb("#638295"), Blue = Color.FromArgb("#568FBE");
    readonly ChatGptAuth auth;
    readonly OpenAiTranslator translator;
    readonly ISpeechCapture speech;
    readonly ObservableCollection<ConversationTurn> turns = [];
    readonly Editor draft = new() { Placeholder = "Nachricht eingeben …", PlaceholderColor = Muted, TextColor = Ink, BackgroundColor = Colors.Transparent, FontSize = 16, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44, MaximumHeightRequest = 104 };
    readonly Button connect = new() { Text = "Continue with ChatGPT", BackgroundColor = Blue, TextColor = Colors.White, CornerRadius = 18, FontSize = 14 };
    readonly Button send = new() { Text = "↑", FontSize = 25, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0, BackgroundColor = Blue, TextColor = Colors.White, VerticalOptions = LayoutOptions.End };
    readonly Button shutter = new() { Text = "●", FontSize = 28, WidthRequest = 78, HeightRequest = 78, CornerRadius = 39, Padding = 0, BackgroundColor = Blue, TextColor = Colors.White };
    readonly Button pause = new() { Text = "Pause", FontSize = 13, TextColor = Muted, BackgroundColor = Colors.Transparent, Padding = new Thickness(8, 4), IsVisible = false };
    readonly Button cancel = new() { Text = "Abbrechen", FontSize = 13, BackgroundColor = Colors.Transparent, TextColor = Muted, IsVisible = false, Padding = new Thickness(6, 4) };
    readonly Button german = LanguageButton("Deutsch"), japanese = LanguageButton("日本語");
    readonly Label status = new() { FontSize = 12, TextColor = Muted, IsVisible = false };
    readonly Label account = new() { FontSize = 12, TextColor = Muted };
    readonly Label prompt = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ink };
    readonly Label hint = new() { FontSize = 12, TextColor = Muted };
    readonly ActivityIndicator progress = new() { Color = Blue, WidthRequest = 24, HeightRequest = 24, IsVisible = false };
    readonly Border signInCard;
    CancellationTokenSource? operation;
    bool busy, turnActive, pageVisible;
    bool fromGerman = true;

    public MainPage(ChatGptAuth auth, OpenAiTranslator translator, ISpeechCapture speech)
    {
        this.auth = auth; this.translator = translator; this.speech = speech;
        Title = "Koe"; BackgroundColor = Color.FromArgb("#F0F6FA");
        var avatar = new Border
        {
            WidthRequest = 44, HeightRequest = 44, BackgroundColor = Color.FromArgb("#CFE5F5"), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = new Label { Text = "K", TextColor = Blue, FontSize = 25, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        };
        var menu = new Button { Text = "•••", FontSize = 23, TextColor = Ink, BackgroundColor = Colors.Transparent, WidthRequest = 48, Padding = 0 };
        SemanticProperties.SetDescription(menu, "Gesprächsoptionen");
        menu.Clicked += async (_, _) => await ShowMenuAsync();
        var header = new Grid { Padding = new Thickness(18, 12), ColumnSpacing = 12, BackgroundColor = Colors.White, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(avatar, 0, 0);
        header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = "Koe", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Ink }, account } }, 1, 0);
        header.Add(menu, 2, 0);
        signInCard = new Border
        {
            Margin = new Thickness(16, 10), Padding = 14, StrokeThickness = 0, BackgroundColor = Color.FromArgb("#E1EEF8"), StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new VerticalStackLayout { Spacing = 9, Children = { new Label { Text = "Zwei Sprachen. Ein Gespräch.", TextColor = Ink, FontSize = 17, FontAttributes = FontAttributes.Bold }, new Label { Text = "Verbinde ChatGPT, um eure Nachrichten zu übersetzen.", TextColor = Muted, FontSize = 13 }, connect } }
        };
        var messages = new CollectionView
        {
            Margin = new Thickness(14, 0), ItemsSource = turns, SelectionMode = SelectionMode.None,
            EmptyView = new VerticalStackLayout { Padding = new Thickness(30, 42), Spacing = 12, Children =
            {
                new Label { Text = "Hallo.  こんにちは。", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Blue, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Tippe auf den großen Knopf und sprich.\nBeim nächsten Tippen ist die andere Person dran.", FontSize = 15, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.2 }
            } },
            ItemTemplate = new DataTemplate(CreateBubble)
        };
        var languages = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        languages.Add(german, 0, 0);
        languages.Add(new Label { Text = "⇄", FontSize = 21, TextColor = Blue, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        languages.Add(japanese, 2, 0);
        var input = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        input.Add(draft, 0, 0); input.Add(send, 1, 0);
        var inputBorder = new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE8F0"), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Padding = new Thickness(12, 4, 5, 4), Content = input };
        var shutterRing = new Border { WidthRequest = 94, HeightRequest = 94, Padding = 6, StrokeThickness = 2, Stroke = Blue, StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = shutter };
        prompt.HorizontalTextAlignment = TextAlignment.Center;
        hint.HorizontalTextAlignment = TextAlignment.Center;
        var turnCaption = new VerticalStackLayout { Spacing = 4, Children = { prompt, hint } };
        var turnControls = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        turnControls.Add(shutterRing, 1, 0);
        turnControls.Add(new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 2, Children = { progress, pause, cancel } }, 2, 0);
        var composer = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 14), Spacing = 10, BackgroundColor = Color.FromArgb("#E8F2F9"), Children = { languages, inputBorder, turnCaption, turnControls, status } };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(header, 0, 0); root.Add(signInCard, 0, 1); root.Add(messages, 0, 2); root.Add(composer, 0, 3);
        Content = root;
        SemanticProperties.SetDescription(send, "Geschriebenen Text übersetzen");
        connect.Clicked += async (_, _) => await ConnectAsync();
        shutter.Clicked += async (_, _) => await AdvanceConversationAsync(true);
        pause.Clicked += async (_, _) => await AdvanceConversationAsync(false);
        send.Clicked += async (_, _) => await SendTextAsync();
        cancel.Clicked += (_, _) => operation?.Cancel();
        german.Clicked += (_, _) => { if (!busy && !turnActive) { fromGerman = true; UpdateControls(); } };
        japanese.Clicked += (_, _) => { if (!busy && !turnActive) { fromGerman = false; UpdateControls(); } };
        draft.TextChanged += (_, _) => UpdateControls();
        speech.TranscriptChanged += text => MainThread.BeginInvokeOnMainThread(() => { if (pageVisible && turnActive) draft.Text = text; });
        speech.Stopped += error => MainThread.BeginInvokeOnMainThread(() => { if (error is not null && !busy && turnActive) SetStatus(error); UpdateControls(); });
        turns.CollectionChanged += (_, _) => { if (turns.Count > 0) messages.ScrollTo(turns[^1], position: ScrollToPosition.End); };
        UpdateControls();
    }

    static Button LanguageButton(string text) => new() { Text = text, FontSize = 14, CornerRadius = 16, HeightRequest = 36, Padding = new Thickness(12, 0), TextColor = Muted, BackgroundColor = Colors.Transparent };

    static View CreateBubble()
    {
        var direction = new Label { FontSize = 11, TextColor = Muted };
        direction.SetBinding(Label.TextProperty, nameof(ConversationTurn.Direction));
        var original = new Label { FontSize = 14, TextColor = Muted, LineHeight = 1.15 };
        original.SetBinding(Label.TextProperty, nameof(ConversationTurn.Original));
        var translation = new Label { FontSize = 18, TextColor = Ink, LineHeight = 1.15 };
        translation.SetBinding(Label.TextProperty, nameof(ConversationTurn.Translation));
        var bubble = new Border
        {
            Margin = new Thickness(4, 6, 34, 6), Padding = new Thickness(14, 11), StrokeThickness = 0, BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17, 17, 4, 17) }, HorizontalOptions = LayoutOptions.Start, MaximumWidthRequest = 330,
            Content = new VerticalStackLayout { Spacing = 7, Children = { direction, translation, original } }
        };
        bubble.BindingContextChanged += (_, _) =>
        {
            if (bubble.BindingContext is not ConversationTurn turn) return;
            bubble.HorizontalOptions = turn.FromGerman ? LayoutOptions.End : LayoutOptions.Start;
            bubble.Margin = turn.FromGerman ? new Thickness(34, 6, 4, 6) : new Thickness(4, 6, 34, 6);
            bubble.BackgroundColor = turn.FromGerman ? Color.FromArgb("#D3E8F8") : Colors.White;
            bubble.StrokeShape = new RoundRectangle { CornerRadius = turn.FromGerman ? new CornerRadius(17, 17, 17, 4) : new CornerRadius(17, 17, 4, 17) };
        };
        return new Grid { Children = { bubble } };
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
        pageVisible = false; operation?.Cancel(); turnActive = false; speech.Stop();
        base.OnDisappearing();
    }

    async Task ShowMenuAsync()
    {
        if (busy || turnActive) return;
        var selection = await DisplayActionSheetAsync(auth.IsConnected ? $"ChatGPT · {auth.AccountLabel}" : "Gespräch", "Schließen", null, "Verlauf löschen", "Nutzung verwalten", auth.IsConnected ? "Abmelden" : "Mit ChatGPT verbinden");
        switch (selection)
        {
            case "Verlauf löschen": turns.Clear(); break;
            case "Nutzung verwalten": await Browser.Default.OpenAsync("https://chatgpt.com/settings/usage"); break;
            case "Abmelden": case "Mit ChatGPT verbinden": await ConnectAsync(); break;
        }
    }

    async Task ConnectAsync()
    {
        if (busy || turnActive) return;
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

    async Task AdvanceConversationAsync(bool continueListening)
    {
        if (busy || !auth.IsConnected) return;
        draft.Unfocus();
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (turnActive)
            {
                var finalText = await speech.FinishAsync(operation.Token);
                turnActive = false;
                if (!string.IsNullOrWhiteSpace(finalText)) draft.Text = finalText;
                var text = draft.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) { SetStatus("Kein Text erkannt. Tippe zum erneuten Sprechen."); return; }
                await TranslateDraftAsync(text, operation.Token);
            }
            else if (!string.IsNullOrWhiteSpace(draft.Text)) await TranslateDraftAsync(draft.Text.Trim(), operation.Token);
            if (continueListening && pageVisible)
            {
                operation.Token.ThrowIfCancellationRequested(); turnActive = true;
                try { await speech.StartAsync(CultureInfo.GetCultureInfo(fromGerman ? "de-DE" : "ja-JP")); }
                catch { turnActive = false; throw; }
                operation.Token.ThrowIfCancellationRequested();
                SetStatus(null);
            }
        }
        catch (OperationCanceledException) { turnActive = false; speech.Stop(); SetStatus("Pausiert. Dein Text bleibt erhalten."); }
        catch (Exception ex) { turnActive = false; speech.Stop(); SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task SendTextAsync()
    {
        var text = draft.Text?.Trim();
        if (busy || turnActive || !auth.IsConnected || string.IsNullOrWhiteSpace(text)) return;
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try { await TranslateDraftAsync(text, operation.Token); }
        catch (OperationCanceledException) { SetStatus("Übersetzung abgebrochen. Dein Text bleibt erhalten."); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { FinishOperation(); }
    }

    async Task TranslateDraftAsync(string text, CancellationToken ct)
    {
        SetStatus("Übersetze …");
        var direction = fromGerman;
        var translated = await translator.TranslateAsync(text, direction, ct);
        ct.ThrowIfCancellationRequested();
        turns.Add(new(direction ? "Deutsch" : "日本語", direction ? "日本語" : "Deutsch", text, translated));
        draft.Text = ""; fromGerman = !direction; SetStatus(null);
    }

    void FinishOperation() { operation?.Dispose(); operation = null; busy = false; UpdateControls(); }
    void SetStatus(string? text) { status.Text = text; status.IsVisible = !string.IsNullOrWhiteSpace(text); }

    void UpdateControls()
    {
        account.Text = auth.IsConnected ? "Verbunden · Deutsch ↔ 日本語" : "Deutsch ↔ 日本語";
        signInCard.IsVisible = !auth.IsConnected; connect.IsEnabled = !busy;
        draft.IsReadOnly = busy || turnActive;
        send.IsEnabled = !busy && !turnActive && auth.IsConnected && !string.IsNullOrWhiteSpace(draft.Text);
        shutter.IsEnabled = !busy && auth.IsConnected;
        shutter.Text = turnActive ? "⇄" : "●";
        shutter.BackgroundColor = turnActive ? Color.FromArgb("#367BAB") : Blue;
        shutter.Opacity = busy ? 0.45 : 1;
        SemanticProperties.SetDescription(shutter, turnActive ? "Satz übersetzen und zur anderen Sprache wechseln" : "Gespräch starten");
        prompt.Text = busy ? "Einen Moment …" : turnActive ? (speech.IsRecording ? (fromGerman ? "Deutsch · sprich jetzt" : "日本語 · sprich jetzt") : "Satz bereit") : "Gespräch starten";
        hint.Text = busy ? "Dein Satz wird verarbeitet." : turnActive ? "Tippen: übersetzen & wechseln" : (fromGerman ? "Deutsch beginnt. Tippe zum Sprechen." : "日本語 beginnt. Tippe zum Sprechen.");
        german.BackgroundColor = fromGerman ? Color.FromArgb("#CBE2F3") : Colors.Transparent;
        japanese.BackgroundColor = fromGerman ? Colors.Transparent : Color.FromArgb("#CBE2F3");
        german.TextColor = fromGerman ? Ink : Muted; japanese.TextColor = fromGerman ? Muted : Ink;
        german.IsEnabled = japanese.IsEnabled = !busy && !turnActive;
        pause.IsVisible = turnActive && !busy; cancel.IsVisible = busy;
        progress.IsRunning = busy; progress.IsVisible = busy;
    }
}
