using Koe.Services;

namespace Koe;

public sealed class OpenAiVoicePage : ContentPage
{
    static readonly Color Ink = Color.FromArgb("#19384E"), Muted = Color.FromArgb("#4B6575");
    readonly OpenAiVoiceSettings settings;
    readonly Switch enabled;
    readonly Entry key = new()
    {
        TextColor = Ink, PlaceholderColor = Muted, BackgroundColor = Colors.White,
        IsPassword = true, Placeholder = "Paste your OpenAI API key", Keyboard = Keyboard.Text,
        IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, ReturnType = ReturnType.Done
    };
    readonly Picker voice = new() { Title = "OpenAI voice", TextColor = Ink, TitleColor = Muted, BackgroundColor = Colors.White, ItemsSource = OpenAiSpeechClient.Voices };
    readonly Label keyStatus = new() { FontSize = 13, TextColor = Muted };
    readonly Button save = new() { Text = "Save", BackgroundColor = Color.FromArgb("#2D648E"), TextColor = Colors.White };
    bool confirming, saving, closing;

    public OpenAiVoicePage(OpenAiVoiceSettings settings)
    {
        this.settings = settings;
        Title = "OpenAI voice";
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        BackgroundColor = Color.FromArgb("#F0F6FA");
        enabled = new Switch { IsToggled = settings.Enabled };
        voice.SelectedItem = OpenAiSpeechClient.Voices.Contains(settings.Voice) ? settings.Voice : "marin";
        var close = new Button { Text = "Close", BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#19384E") };
        close.Clicked += async (_, _) => { if (!saving) await CloseAsync(); };
        var heading = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        heading.Add(new Label { Text = "OpenAI voice", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#19384E"), VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        heading.Add(close, 1, 0);
        var toggleRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        toggleRow.Add(new Label { TextColor = Ink, Text = "Use OpenAI voice", FontSize = 17, VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        toggleRow.Add(enabled, 1, 0);
        SemanticProperties.SetDescription(enabled, "Use paid OpenAI speech instead of iPhone speech");
        enabled.Toggled += async (_, e) =>
        {
            if (!e.Value || confirming) return;
            confirming = true; enabled.IsEnabled = false; save.IsEnabled = false;
            try
            {
                var accepted = await DisplayAlertAsync("Additional API costs",
                    "OpenAI voice is charged to the OpenAI API account associated with your key. Your ChatGPT sign-in does not pay for these voice requests.\n\n" +
                    "When you tap a speaker, the selected message text is sent to OpenAI to generate an AI voice. Internet access is required. Audio already generated may be billed even if you stop playback.\n\nEnable OpenAI voice?",
                    "Enable", "Cancel");
                if (!accepted) enabled.IsToggled = false;
            }
            finally { confirming = false; enabled.IsEnabled = true; save.IsEnabled = true; }
        };
        var billing = Link("1. Open API billing", "https://platform.openai.com/settings/organization/billing/overview");
        var keys = Link("2. Create an API key", "https://platform.openai.com/api-keys");
        var pricing = Link("View current OpenAI API prices", "https://developers.openai.com/api/docs/pricing");
        var remove = new Button { Text = "Remove saved key", TextColor = Color.FromArgb("#B63E46"), BackgroundColor = Colors.Transparent };
        remove.Clicked += async (_, _) =>
        {
            if (saving || confirming) return;
            if (!await DisplayAlertAsync("Remove API key?", "This also disables OpenAI voice. Koe will use iPhone voices again.", "Remove", "Cancel")) return;
            try
            {
                settings.RemoveKey(); enabled.IsToggled = false; key.Text = "";
                keyStatus.Text = "No API key saved.";
            }
            catch { await DisplayAlertAsync("Could not remove key", "Please try again.", "OK"); }
        };
        save.Clicked += async (_, _) =>
        {
            if (confirming || saving) return;
            saving = true; save.IsEnabled = enabled.IsEnabled = false;
            try
            {
                await settings.SaveAsync(enabled.IsToggled, (string?)voice.SelectedItem ?? "marin", key.Text);
                await CloseAsync();
            }
            catch (Exception) { await DisplayAlertAsync("Could not save", "Check that your API key contains no spaces and try again. Your key is required when OpenAI voice is enabled.", "OK"); }
            finally { saving = false; save.IsEnabled = enabled.IsEnabled = true; }
        };
        var body = new VerticalStackLayout
        {
            Padding = 20, Spacing = 14, Children =
            {
                heading, toggleRow,
                new Label { TextColor = Ink, Text = "Off uses the iPhone voices. On uses an AI-generated OpenAI voice with additional API charges.", FontSize = 15, LineHeight = 1.15 },
                new Label { TextColor = Ink, Text = "Set up step by step", FontSize = 18, FontAttributes = FontAttributes.Bold },
                new Label { TextColor = Ink, Text = "1. Sign in to the OpenAI API platform. Add a payment method or API credits if needed. Check your usage budget and billing settings.", FontSize = 15, LineHeight = 1.15 }, billing,
                new Label { TextColor = Ink, Text = "2. Open API keys, select your project and choose Create new secret key. Give the key a name such as Koe voice, allow Realtime requests, and copy it. The full secret is normally shown only once.", FontSize = 15, LineHeight = 1.15 }, keys,
                new Label { TextColor = Ink, Text = "3. Return to Koe and paste the key below. Choose a voice, turn on Use OpenAI voice, accept the cost notice, then tap Save.", FontSize = 15, LineHeight = 1.15 },
                key, keyStatus, voice,
                new Label { TextColor = Ink, Text = "4. Tap a speaker beside a chat message to listen. Tap it again to stop. Turn this setting off at any time to return to iPhone voices.", FontSize = 15, LineHeight = 1.15 },
                new Label { Text = "Your personal key is stored in the iPhone Keychain. Leave the field empty to keep a saved key. Never share it in chat or screenshots. No audio is generated just by saving these settings.", FontSize = 13, TextColor = Muted },
                pricing, save, remove
            }
        };
        Content = new ScrollView { Content = body };
    }

    Button Link(string title, string url)
    {
        var button = new Button { Text = title, FontSize = 14, TextColor = Color.FromArgb("#3679AC"), BackgroundColor = Colors.White };
        button.Clicked += async (_, _) =>
        {
            try { await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred); }
            catch { await DisplayAlertAsync("Open in Safari", url, "OK"); }
        };
        return button;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { keyStatus.Text = string.IsNullOrWhiteSpace(await settings.GetKeyAsync()) ? "No API key saved." : "API key saved securely. Paste a new key only to replace it."; }
        catch { keyStatus.Text = "Could not read saved key. Please try again."; }
    }

    async Task CloseAsync()
    {
        if (closing) return;
        closing = true;
        key.Text = "";
        try { await Navigation.PopModalAsync(); }
        finally { closing = false; }
    }
}
