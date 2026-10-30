namespace Koe;

// Only the conversation draft requests a keyboard matching the active speaker.
public sealed class ConversationEditor : Editor
{
    public static readonly BindableProperty PreferredCultureProperty = BindableProperty.Create(
        nameof(PreferredCulture), typeof(string), typeof(ConversationEditor), "");

    public string PreferredCulture
    {
        get => (string)GetValue(PreferredCultureProperty);
        set => SetValue(PreferredCultureProperty, value);
    }
}
