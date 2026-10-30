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
        // MAUI's own accessory helper is internal. Use public UIKit APIs instead.
        var toolbar = new UIToolbar();
        var weakView = new WeakReference<ConversationTextView>(view);
        var done = new UIBarButtonItem(UIBarButtonSystemItem.Done, (_, _) =>
        {
            if (weakView.TryGetTarget(out var editor)) editor.ResignFirstResponder();
        });
        toolbar.SetItems([new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace), done], false);
        toolbar.SizeToFit();
        view.InputAccessoryView = toolbar;
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

    public override UITextInputMode TextInputMode
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
