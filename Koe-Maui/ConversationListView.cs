using System.Collections.ObjectModel;
using System.Globalization;
using Koe.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Koe;

// Replaces only the transcript area; the normal Koe header and composer stay visible.
public sealed class ConversationListView : ContentView
{
    readonly ConversationStore store;
    readonly Func<Guid, Task> openConversation;
    readonly ObservableCollection<ConversationSummary> chats = [];
    readonly Label notice = new() { FontSize = 12, TextColor = Color.FromArgb("#638295") };
    readonly ActivityIndicator loading = new() { Color = Color.FromArgb("#568FBE"), IsVisible = false };
    bool acting, refreshing;

    public ConversationListView(ConversationStore store, Func<Guid, Task> openConversation)
    {
        this.store = store; this.openConversation = openConversation;
        var list = new CollectionView
        {
            ItemsSource = chats, SelectionMode = SelectionMode.None, Margin = new Thickness(14, 6),
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            ItemTemplate = new DataTemplate(CreateRow),
            EmptyView = new VerticalStackLayout
            {
                Padding = new Thickness(20, 28), Spacing = 12, Children =
                {
                    new Label { Text = "Your conversations", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#19384E"), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Tap + above to start a new chat.\nYour saved conversations will appear here.", FontSize = 15, TextColor = Color.FromArgb("#638295"), HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
        notice.Margin = new Thickness(18, 2, 18, 6);
        var content = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(list, 0, 0); content.Add(notice, 0, 1);
        Grid.SetRowSpan(loading, 2); content.Children.Add(loading);
        Content = content;
    }

    View CreateRow()
    {
        var title = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#19384E"), MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation };
        title.SetBinding(Label.TextProperty, nameof(ConversationSummary.Title));
        var preview = new Label { FontSize = 14, TextColor = Color.FromArgb("#638295"), MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation };
        preview.SetBinding(Label.TextProperty, nameof(ConversationSummary.Preview));
        var languages = new Label { FontSize = 12, TextColor = Color.FromArgb("#638295") };
        var date = new Label { FontSize = 11, TextColor = Color.FromArgb("#638295"), VerticalOptions = LayoutOptions.Start };
        var labels = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center, Children = { title, preview, languages } };
        var content = new Grid { Padding = new Thickness(16, 14), ColumnSpacing = 12, MinimumHeightRequest = 100, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(labels, 0, 0); content.Add(date, 1, 0);
        var card = new Border { BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = content };
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
            catch { notice.Text = "Could not delete this conversation. Please try again."; }
            finally { acting = false; }
        };
        swipe.RightItems = new SwipeItems { delete };
        swipe.RightItems.Mode = SwipeMode.Reveal;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (acting || refreshing || swipe.BindingContext is not ConversationSummary chat) return;
            acting = true;
            try { await openConversation(chat.Id); }
            catch { notice.Text = "Could not open this conversation. Your saved files have been kept."; }
            finally { acting = false; }
        };
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

    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        loading.IsRunning = loading.IsVisible = true;
        try
        {
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
