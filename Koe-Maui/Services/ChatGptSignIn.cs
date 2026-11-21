namespace Koe.Services;

internal sealed record Registration(string ClientId, string Subject, string? Email = null);

internal sealed class ChatGptSignIn(Registration? previous, bool useDifferentAccount)
{
    readonly Registration? selected = useDifferentAccount ? null : previous;

    public Dictionary<string, string> AuthorizationFields(string? idTokenHint)
    {
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = selected?.ClientId ?? "dynamic_agent_client"
        };
        if (selected is null) fields["agent_name_hint"] = "Koe";
        else
        {
            if (!string.IsNullOrEmpty(selected.Email)) fields["login_hint"] = selected.Email;
            if (!string.IsNullOrEmpty(idTokenHint)) fields["id_token_hint"] = idTokenHint;
        }
        return fields;
    }

    public string ResolveClientId(Dictionary<string, string> callback)
    {
        var clientId = callback.GetValueOrDefault("client_id") ?? selected?.ClientId;
        if (string.IsNullOrEmpty(clientId) || clientId == "dynamic_agent_client")
            throw new InvalidOperationException("OpenAI returned an incomplete registration.");
        if (selected is not null && selected.ClientId != clientId)
            throw new InvalidOperationException("The sign-in belongs to a different registration.");
        return clientId;
    }

    public void ValidateSubject(string subject)
    {
        if (selected is not null && selected.Subject != subject)
            throw new InvalidOperationException("The sign-in belongs to a different account. Choose Use another ChatGPT account to connect it.");
    }
}
