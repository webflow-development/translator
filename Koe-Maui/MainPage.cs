using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed record ConversationTurn(string Source, string Target, string Original, string Translation);

public sealed class MainPage : ContentPage
{
    readonly ChatGptAuth auth;
    readonly OpenAiTranslator translator;
    readonly ISpeechCapture speech;
    readonly ObservableCollection<ConversationTurn> turns = [];
    readonly Editor draft = new() { Placeholder = "Satz eingeben …", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 72, MaximumHeightRequest = 150 };
    readonly Picker language = new() { Title = "Spricht gerade", ItemsSource = new[] { "Deutsch", "日本語" }, SelectedIndex = 0 };
    readonly Button connect = new() { Text = "Continue with ChatGPT" };
    readonly Button send = new() { Text = "Übersetzen" };
    readonly Button microphone = new() { Text = "Sprechen" };
    readonly Button cancel = new() { Text = "Abbrechen", IsVisible = false };
    readonly Label status = new() { FontSize = 13, TextColor = Colors.DimGray };
    readonly Label account = new() { FontSize = 13 };
    readonly ActivityIndicator progress = new() { Color = Color.FromArgb("#2458CC") };
    readonly Switch readAloud = new() { IsToggled = true };
    CancellationTokenSource? operation;
    CancellationTokenSource? playback;
    bool busy;

    public MainPage(ChatGptAuth auth, OpenAiTranslator translator, ISpeechCapture speech)
    {
        this.auth = auth;
        this.translator = translator;
        this.speech = speech;
        Title = "Koe";
        BackgroundColor = Color.FromArgb("#F3F5FA");
        var messages = new CollectionView
        {
            ItemsSource = turns,
            SelectionMode = SelectionMode.None,
            EmptyView = new Label { Text = "Dein Gespräch beginnt hier.\nWähle die Sprache und sprich oder schreibe einen Satz.", Margin = 24, HorizontalTextAlignment = TextAlignment.Center },
            ItemTemplate = new DataTemplate(() =>
            {
                var source = new Label { FontSize = 12, TextColor = Colors.DimGray };
                source.SetBinding(Label.TextProperty, nameof(ConversationTurn.Source));
                var original = new Label { FontSize = 17, TextColor = Colors.Black };
                original.SetBinding(Label.TextProperty, nameof(ConversationTurn.Original));
                var target = new Label { FontSize = 12, TextColor = Colors.DimGray };
                target.SetBinding(Label.TextProperty, nameof(ConversationTurn.Target));
                var translated = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black };
                translated.SetBinding(Label.TextProperty, nameof(ConversationTurn.Translation));
                var replay = new Button { Text = "Vorlesen", HorizontalOptions = LayoutOptions.Start };
                replay.Clicked += async (_, _) =>
                {
                    if (replay.BindingContext is ConversationTurn turn)
                    {
                        try { await SpeakAsync(turn.Translation, turn.Target == "日本語" ? "ja-JP" : "de-DE"); }
                        catch { status.Text = "Vorlesen ist auf diesem Gerät nicht verfügbar."; }
                    }
                };
                return new Border
                {
                    Margin = new Thickness(0, 6), Padding = 16, BackgroundColor = Colors.White,
                    Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 18 },
                    Content = new VerticalStackLayout { Spacing = 8, Children = { source, original, new BoxView { HeightRequest = 1, Color = Colors.LightGray }, target, translated, replay } }
                };
            })
        };
        var clear = new Button { Text = "Verlauf löschen" };
        clear.Clicked += (_, _) => turns.Clear();
        var usage = new Button { Text = "Nutzung verwalten" };
        usage.Clicked += async (_, _) => await Browser.Default.OpenAsync("https://chatgpt.com/settings/usage");
        var top = new VerticalStackLayout
        {
            Spacing = 5, Children =
            {
                new Label { Text = "Koe", FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2458CC") },
                new Label { Text = "Deutsch ↔ 日本語", TextColor = Colors.DimGray }, account, connect,
                new HorizontalStackLayout { Spacing = 8, Children = { usage, clear } }
            }
        };
        var composer = new VerticalStackLayout
        {
            Spacing = 7, Children =
            {
                language,
                new Label { Text = "Diktat nutzt Apples Spracherkennung; je nach Gerät lokal oder über Apples Dienst.", FontSize = 11, TextColor = Colors.DimGray },
                draft,
                new HorizontalStackLayout { Spacing = 10, Children = { microphone, send, cancel, progress } },
                new HorizontalStackLayout { Spacing = 8, Children = { readAloud, new Label { Text = "Übersetzung vorlesen", VerticalOptions = LayoutOptions.Center, TextColor = Colors.DimGray } } },
                status
            }
        };
        var grid = new Grid { Padding = 16, RowSpacing = 10, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        grid.Add(top, 0, 0); grid.Add(messages, 0, 1); grid.Add(composer, 0, 2);
        Content = grid;
        connect.Clicked += async (_, _) => await ConnectAsync();
        send.Clicked += async (_, _) => await SendAsync();
        cancel.Clicked += (_, _) => { operation?.Cancel(); playback?.Cancel(); };
        draft.TextChanged += (_, _) => UpdateControls();
        microphone.Clicked += async (_, _) =>
        {
            try
            {
                playback?.Cancel();
                if (speech.IsRecording) speech.Stop();
                else await speech.StartAsync(CultureInfo.GetCultureInfo(language.SelectedIndex == 0 ? "de-DE" : "ja-JP"));
                UpdateControls();
            }
            catch (Exception ex) { status.Text = ex.Message; UpdateControls(); }
        };
        speech.TranscriptChanged += text => MainThread.BeginInvokeOnMainThread(() => draft.Text = text);
        speech.Stopped += error => MainThread.BeginInvokeOnMainThread(() => { if (error is not null) status.Text = error; UpdateControls(); });
        turns.CollectionChanged += (_, _) => { if (turns.Count > 0) messages.ScrollTo(turns[^1], position: ScrollToPosition.End); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await auth.LoadAsync(); }
        catch { status.Text = "Die gespeicherte Anmeldung konnte nicht geladen werden. Bitte erneut anmelden."; }
        UpdateControls();
    }

    protected override void OnDisappearing()
    {
        operation?.Cancel(); playback?.Cancel(); speech.Stop();
        base.OnDisappearing();
    }

    async Task ConnectAsync()
    {
        busy = true; operation = new CancellationTokenSource(); UpdateControls();
        try
        {
            if (auth.IsConnected) { await auth.SignOutAsync(operation.Token); status.Text = "Abgemeldet."; }
            else { await auth.SignInAsync(operation.Token); status.Text = "ChatGPT verbunden."; }
        }
        catch (OperationCanceledException) { status.Text = "Anmeldung abgebrochen."; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { operation.Dispose(); operation = null; busy = false; UpdateControls(); }
    }

    async Task SendAsync()
    {
        var text = draft.Text?.Trim();
        if (busy || !auth.IsConnected || string.IsNullOrEmpty(text)) return;
        busy = true; operation = new CancellationTokenSource();
        var german = language.SelectedIndex == 0;
        try
        {
            speech.Stop(); playback?.Cancel(); UpdateControls(); status.Text = "Übersetze …";
            var translation = await translator.TranslateAsync(text, german, operation.Token);
            turns.Add(new(german ? "Deutsch" : "日本語", german ? "日本語" : "Deutsch", text, translation));
            draft.Text = ""; language.SelectedIndex = german ? 1 : 0; status.Text = "";
            if (readAloud.IsToggled)
            {
                try { await SpeakAsync(translation, german ? "ja-JP" : "de-DE"); }
                catch (OperationCanceledException) { }
                catch { status.Text = "Übersetzt. Vorlesen ist gerade nicht verfügbar."; }
            }
        }
        catch (OperationCanceledException) { status.Text = "Übersetzung abgebrochen. Dein Text bleibt erhalten."; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { operation.Dispose(); operation = null; busy = false; UpdateControls(); }
    }

    async Task SpeakAsync(string text, string culture)
    {
        playback?.Cancel();
        playback = new CancellationTokenSource();
        var locales = await TextToSpeech.Default.GetLocalesAsync();
        var locale = locales.FirstOrDefault(x => x.Language.StartsWith(culture[..2], StringComparison.OrdinalIgnoreCase));
        if (locale is null) throw new NotSupportedException();
        await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Locale = locale }, playback.Token);
    }

    void UpdateControls()
    {
        account.Text = auth.IsConnected ? $"Verbunden: {auth.AccountLabel}" : "Verbinde deinen ChatGPT-Plan für Übersetzungen.";
        connect.Text = auth.IsConnected ? "Abmelden" : "Continue with ChatGPT";
        connect.IsEnabled = !busy;
        language.IsEnabled = !busy && !speech.IsRecording;
        draft.IsReadOnly = busy;
        send.IsEnabled = !busy && auth.IsConnected && !string.IsNullOrWhiteSpace(draft.Text);
        microphone.IsEnabled = !busy;
        microphone.Text = speech.IsRecording ? "Diktat stoppen" : "Sprechen";
        progress.IsRunning = busy; progress.IsVisible = busy; cancel.IsVisible = busy;
    }
}
