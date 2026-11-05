using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

public sealed class ChatsPage : ContentPage
{
    readonly ConversationStore store;
    readonly MainPage conversation;
    readonly ObservableCollection<ConversationSummary> chats = [];
    readonly Label notice = SettingsStyle.Label("", 13, muted: true);
    readonly ActivityIndicator loading = new() { Color = Color.FromArgb("#568FBE") };
    bool acting, refreshing;

    public ChatsPage(ConversationStore store, MainPage conversation)
    {
        this.store = store; this.conversation = conversation;
        Title = "Chats"; SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        SettingsStyle.Background(this);
        var settings = new Button { Text = "⚙", FontSize = 25, WidthRequest = 44, HeightRequest = 44, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#568FBE") };
        var add = new Button { Text = "+", FontSize = 32, WidthRequest = 44, HeightRequest = 44, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#568FBE") };
        SemanticProperties.SetDescription(settings, "Settings"); SemanticProperties.SetDescription(add, "Start a new conversation");
        var title = SettingsStyle.Label("Chats", 30); title.FontAttributes = FontAttributes.Bold;
        var heading = new Grid { Padding = new Thickness(20, 8), ColumnSpacing = 6, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        heading.Add(title, 0, 0); heading.Add(settings, 1, 0); heading.Add(add, 2, 0);
        var list = new CollectionView
        {
            ItemsSource = chats, SelectionMode = SelectionMode.None, Margin = new Thickness(16, 4),
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            ItemTemplate = new DataTemplate(CreateRow),
            EmptyView = new VerticalStackLayout
            {
                Padding = new Thickness(28, 70), Spacing = 14, Children =
                {
                    SettingsStyle.Label("Your conversations, kept together.", 21),
                    SettingsStyle.Label("Tap + to start a new chat.\nYour messages and photos will stay here when you return.", 16, muted: true)
                }
            }
        };
        notice.Margin = new Thickness(20, 4, 20, 8);
        var content = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(heading, 0, 0); content.Add(list, 0, 1); content.Add(notice, 0, 2);
        Grid.SetRowSpan(loading, 3); content.Children.Add(loading);
        Content = content;
        add.Clicked += async (_, _) => await OpenAsync(null);
        settings.Clicked += async (_, _) =>
        {
            if (acting || refreshing) return;
            acting = true;
            try { await conversation.PrepareNewConversationAsync(); await conversation.ShowSettingsAsync(Navigation); }
            catch { await DisplayAlertAsync("Could not open settings", "Please try again.", "OK"); }
            finally { acting = false; }
        };
    }

    View CreateRow()
    {
        var title = SettingsStyle.Label("", 17); title.FontAttributes = FontAttributes.Bold;
        title.MaxLines = 1; title.LineBreakMode = LineBreakMode.TailTruncation;
        title.SetBinding(Label.TextProperty, nameof(ConversationSummary.Title));
        var preview = SettingsStyle.Label("", 14, muted: true);
        preview.MaxLines = 1; preview.LineBreakMode = LineBreakMode.TailTruncation;
        preview.SetBinding(Label.TextProperty, nameof(ConversationSummary.Preview));
        var languages = SettingsStyle.Label("", 12, muted: true);
        var date = SettingsStyle.Label("", 11, muted: true); date.VerticalOptions = LayoutOptions.Start;
        var labels = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center, Children = { title, preview, languages } };
        var content = new Grid { Padding = new Thickness(16, 14), ColumnSpacing = 12, MinimumHeightRequest = 100, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(labels, 0, 0); content.Add(date, 1, 0);
        var card = SettingsStyle.Group(content);
        var swipe = new SwipeView { Content = card, Threshold = 76 };
        var delete = new SwipeItem { Text = "Delete", BackgroundColor = Color.FromArgb("#D54D56") };
        delete.Invoked += async (_, _) =>
        {
            if (acting || refreshing || swipe.BindingContext is not ConversationSummary chat) return;
            acting = true;
            try
            {
                await store.DeleteAsync(chat.Id);
                var item = chats.FirstOrDefault(x => x.Id == chat.Id);
                if (item is not null) chats.Remove(item);
                notice.Text = "Conversation deleted.";
            }
            catch { await DisplayAlertAsync("Could not delete conversation", "The chat could not be removed. Please try again.", "OK"); }
            finally { acting = false; }
        };
        swipe.RightItems = new SwipeItems { delete };
        swipe.RightItems.Mode = SwipeMode.Reveal;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => { if (swipe.BindingContext is ConversationSummary chat) await OpenAsync(chat.Id); };
        content.GestureRecognizers.Add(tap);
        swipe.BindingContextChanged += (_, _) =>
        {
            swipe.Close();
            if (swipe.BindingContext is not ConversationSummary chat) return;
            languages.Text = LanguageName(chat.FirstLanguageCode) + " ↔ " + LanguageName(chat.SecondLanguageCode);
            var local = chat.UpdatedAt.ToLocalTime();
            date.Text = local.Date == DateTime.Today ? local.ToString("t") : local.ToString("dd MMM");
            SemanticProperties.SetDescription(content, $"{chat.Title}, {languages.Text}, {chat.Preview}");
        };
        return swipe;
    }

    static string LanguageName(string code)
    {
        var supported = ConversationLanguage.All.FirstOrDefault(x => x.Code == code);
        if (supported is not null) return supported.SpeakerName;
        try { return CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (CultureNotFoundException) { return code; }
    }

    async Task OpenAsync(Guid? id)
    {
        if (acting || refreshing) return;
        acting = true;
        try
        {
            if (id is { } existing) await conversation.OpenConversationAsync(existing);
            else await conversation.PrepareNewConversationAsync();
            await Navigation.PushAsync(conversation);
        }
        catch (Exception) { await DisplayAlertAsync("Could not open conversation", "Your saved files have been kept. Please try again.", "OK"); }
        finally { acting = false; }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (refreshing) return;
        refreshing = true;
        loading.IsRunning = loading.IsVisible = true;
        try
        {
            await conversation.FlushConversationAsync();
            await conversation.LoadAccountAsync();
            var saved = await store.ListAsync();
            chats.Clear();
            foreach (var chat in saved) chats.Add(chat);
            notice.Text = store.UnreadableChats > 0 ? "Some chats could not be loaded. Their saved files have been kept."
                : chats.Count == 0 ? "Saved on this device." : "Swipe left on a conversation to delete it.";
        }
        catch { notice.Text = "Could not load chats. Your saved files have been kept."; }
        finally { refreshing = false; loading.IsRunning = loading.IsVisible = false; }
    }
}
