using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

// Shared grouped appearance for settings and its voice setup sheet.
internal static class SettingsStyle
{
    public static void Background(VisualElement view) => view.SetAppThemeColor(VisualElement.BackgroundColorProperty,
        Color.FromArgb("#F0F6FA"), Color.FromArgb("#222222"));
    public static void Ink(Label label) => label.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty,
        Color.FromArgb("#19384E"), Color.FromArgb("#F5F5F7"));
    public static void Muted(Label label) => label.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty,
        Color.FromArgb("#638295"), Color.FromArgb("#B2B2B7"));
    public static void Accent(Label label) => label.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty,
        Color.FromArgb("#287CA8"), Color.FromArgb("#64D2FF"));
    public static Label Label(string text, double size = 17, bool muted = false)
    {
        var label = new Label { Text = text, FontSize = size, VerticalTextAlignment = TextAlignment.Center };
        if (muted) Muted(label); else Ink(label);
        return label;
    }
    public static Border Group(params View[] rows)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        for (var index = 0; index < rows.Length; index++)
        {
            if (index > 0)
            {
                var line = new BoxView { HeightRequest = 1, Margin = new Thickness(16, 0) };
                line.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#E1E8EF"), Color.FromArgb("#484848"));
                stack.Children.Add(line);
            }
            stack.Children.Add(rows[index]);
        }
        var card = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = stack };
        card.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#333333"));
        return card;
    }
    public static View Header(string title, Button done)
    {
        done.Text = "Done"; done.FontSize = 16; done.CornerRadius = 22;
        done.HeightRequest = 44; done.WidthRequest = 76; done.Padding = 0;
        done.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#19384E"), Colors.White);
        done.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#303030"));
        var heading = Label(title, 20); heading.FontAttributes = FontAttributes.Bold;
        heading.HorizontalTextAlignment = TextAlignment.Center;
        var row = new Grid
        {
            Padding = new Thickness(16, 12), ColumnSpacing = 6,
            ColumnDefinitions = { new(76), new(GridLength.Star), new(76) }
        };
        row.Add(heading, 1, 0); row.Add(done, 2, 0);
        return row;
    }
}

public sealed class SettingsPage : ContentPage
{
    readonly MainPage owner;
    readonly OpenAiVoiceSettings voiceSettings;
    readonly VerticalStackLayout body = new() { Padding = new Thickness(16, 8, 16, 30), Spacing = 12 };
    bool acting;

    public SettingsPage(MainPage owner, OpenAiVoiceSettings voiceSettings)
    {
        this.owner = owner; this.voiceSettings = voiceSettings;
        Title = "Settings"; SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        SettingsStyle.Background(this);
        var done = new Button();
        done.Clicked += async (_, _) => { if (!acting) await Navigation.PopModalAsync(); };
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        layout.Add(SettingsStyle.Header("Settings", done), 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        Content = layout;
        Refresh();
    }

    protected override void OnAppearing() { base.OnAppearing(); Refresh(); }

    void Section(string name)
    {
        var label = SettingsStyle.Label(name, 16, muted: true);
        label.FontAttributes = FontAttributes.Bold; label.Margin = new Thickness(16, 14, 0, 2);
        body.Children.Add(label);
    }

    void Note(string text)
    {
        var label = SettingsStyle.Label(text, 13, muted: true);
        label.Margin = new Thickness(16, 0); label.LineHeight = 1.15;
        body.Children.Add(label);
    }

    Grid Row(string icon, string title, View trailing, string? subtitle = null)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 10), ColumnSpacing = 12, MinimumHeightRequest = 62,
            ColumnDefinitions = { new(30), new(GridLength.Star), new(GridLength.Auto) }
        };
        var symbol = SettingsStyle.Label(icon, 22); SettingsStyle.Accent(symbol);
        symbol.HorizontalTextAlignment = TextAlignment.Center;
        row.Add(symbol, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        labels.Children.Add(SettingsStyle.Label(title));
        if (subtitle is not null) labels.Children.Add(SettingsStyle.Label(subtitle, 13, muted: true));
        row.Add(labels, 1, 0); row.Add(trailing, 2, 0);
        return row;
    }

    View Action(string icon, string title, Func<Task> action, string? subtitle = null)
    {
        var chevron = SettingsStyle.Label("›", 25, muted: true);
        var row = Row(icon, title, chevron, subtitle);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (acting) return;
            acting = true;
            try { await action(); }
            catch (Exception) { await DisplayAlertAsync("Could not update settings", "Please try again.", "OK"); }
            finally { acting = false; Refresh(); }
        };
        row.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(row, title + (subtitle is null ? "" : ", " + subtitle));
        return row;
    }

    void Refresh()
    {
        body.Children.Clear();
        Section("Conversation languages");
        body.Children.Add(SettingsStyle.Group(
            Action("1", "First speaker", () => owner.ChangeLanguageAsync(true), owner.FirstLanguageName),
            Action("2", "Second speaker", () => owner.ChangeLanguageAsync(false), owner.SecondLanguageName)));
        Note("Each conversation keeps its own language pair.");

        Section("Text size");
        var sizes = new List<View>();
        foreach (var size in new[] { "Small", "Medium", "Large" })
        {
            var check = SettingsStyle.Label(owner.CurrentTextSize == size ? "✓" : "", 24);
            SettingsStyle.Accent(check);
            var row = Row("Aa", size, check);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => { owner.SetTextSize(size); Refresh(); };
            row.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(row, size + (owner.CurrentTextSize == size ? ", selected" : " text"));
            sizes.Add(row);
        }
        body.Children.Add(SettingsStyle.Group(sizes.ToArray()));
        Note("Changes the size of messages and the input text.");

        Section("Reading and speech");
        var transliteration = new Switch { IsToggled = owner.ShowTransliteration, OnColor = Color.FromArgb("#34C759"), VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(transliteration, "Show transliteration");
        transliteration.Toggled += (_, e) => owner.SetTransliteration(e.Value);
        body.Children.Add(SettingsStyle.Group(
            Row("文", "Transliteration", transliteration),
            Action("♪", "Speech voices", owner.ShowVoicesAsync)));
        Note("Transliteration adds a Latin-script reading for languages such as Japanese and Mandarin.");

        Section("Account and billing");
        body.Children.Add(SettingsStyle.Group(
            Action("↗", "Manage usage", () => Browser.Default.OpenAsync("https://chatgpt.com/settings/usage")),
            Action("AI", "OpenAI voice", owner.ShowOpenAiVoiceSettingsAsync, voiceSettings.Enabled ? "On · additional API charges" : "Off · API key and billing setup"),
            Action("●", owner.IsAccountConnected ? "Sign out" : "Connect ChatGPT", async () =>
            {
                await Navigation.PopModalAsync();
                await owner.ConnectAsync();
            }, owner.IsAccountConnected ? owner.AccountName : "Connect to translate and ask AI")));
        Note("Conversations and their photos are saved on this device. Swipe left on a chat in Chats to delete it.");
    }
}
