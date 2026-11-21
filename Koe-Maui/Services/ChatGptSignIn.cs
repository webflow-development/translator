namespace Koe.Services;

internal static class ChatGptSignIn
{
    public static Dictionary<string, string> AuthorizationFields() => new()
    {
        ["client_id"] = "dynamic_agent_client",
        ["agent_name_hint"] = "Koe"
    };

    public static string ResolveClientId(Dictionary<string, string> callback)
    {
        var clientId = callback.GetValueOrDefault("client_id");
        if (string.IsNullOrWhiteSpace(clientId) || clientId == "dynamic_agent_client")
            throw new InvalidOperationException("OpenAI returned an incomplete registration.");
        return clientId;
    }
}
