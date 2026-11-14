using System.Text.Json.Serialization;

namespace Koe.Services;

public sealed record GuestRequest(string Text, string First, string Second, bool Transliteration, byte[]? Image, GuestMessage[]? History = null);
public sealed record GuestMessage(string Role, string Text, byte[]? Image);
public sealed record GuestUsage(int DailyLimit, int Remaining, DateTimeOffset ResetsAt, string Model);
public sealed record GuestError(string Code, string Message);

internal static class GuestProtocol
{
    public const string InstallationHeader = "X-Koe-Guest";
    public const string LimitHeader = "X-Koe-Guest-Limit";
    public const string RemainingHeader = "X-Koe-Guest-Remaining";
    public const string ResetHeader = "X-Koe-Guest-Reset";
    public const string ModelHeader = "X-Koe-Guest-Model";
    public const string LimitReached = "guest_limit_reached";
    public const string CapacityReached = "guest_capacity_reached";
    public const int MaximumMessageCharacters = 12000;
    public const long MaximumRequestBytes = 24 * 1024 * 1024;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GuestRequest))]
[JsonSerializable(typeof(GuestUsage))]
[JsonSerializable(typeof(GuestError))]
internal partial class GuestJsonContext : JsonSerializerContext { }
