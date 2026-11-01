namespace Koe.Services;

public sealed class ConversationMode
{
    public bool IsAi { get; private set; }
    public bool FromFirst { get; private set; } = true;
    public string? LastSpeakerCode { get; private set; }

    public void SelectSpeaker(bool fromFirst, string code)
    {
        IsAi = false; FromFirst = fromFirst; LastSpeakerCode = code;
    }

    public void SelectAi() => IsAi = true;

    public void CompleteTurn(ConversationLanguage first, string speakerCode)
    {
        LastSpeakerCode = speakerCode;
        if (!IsAi) FromFirst = speakerCode != first.Code;
    }

    public void Reset()
    {
        IsAi = false; FromFirst = true; LastSpeakerCode = null;
    }
}
