using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Koe.Platforms.iOS;

public sealed class ConversationEditorHandler : EditorHandler
{
    public static readonly PropertyMapper<ConversationEditor, ConversationEditorHandler> ConversationMapper = new(EditorHandler.Mapper)
    {
        [nameof(ConversationEditor.PreferredCulture)] = (handler, editor) =>
            ((ConversationTextView)handler.PlatformView).SetCulture(editor.PreferredCulture)
    };

    public ConversationEditorHandler() : base(ConversationMapper) { }

    protected override MauiTextView CreatePlatformView()
    {
        var view = new ConversationTextView();
        view.AddMauiDoneAccessoryView(this);
        return view;
    }
}

public sealed class ConversationTextView : MauiTextView
{
    string culture = "";

    public void SetCulture(string value)
    {
        if (culture == value) return;
        culture = value;
        // Refresh an already visible keyboard without changing the draft or selection.
        if (IsFirstResponder) ReloadInputViews();
    }

    public override UITextInputMode? TextInputMode
    {
        get
        {
            var modes = UITextInputMode.ActiveInputModes;
            var exact = modes.FirstOrDefault(mode => string.Equals(mode.PrimaryLanguage, culture, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var language = culture.Split('-')[0];
            // An enabled keyboard for another region of the same language is suitable too.
            return modes.FirstOrDefault(mode => !string.IsNullOrEmpty(language) &&
                string.Equals(mode.PrimaryLanguage?.Split('-')[0], language, StringComparison.OrdinalIgnoreCase))
                ?? base.TextInputMode;
        }
    }
}
