using System.Net;

namespace Koe.GuestServer;

internal sealed class GuestOptions
{
    public string Model { get; init; } = "gpt-6-luna";
    public int RequestsPerGuestPerDay { get; init; } = 20;
    public int RequestsPerNetworkPerDay { get; init; } = 80;
    public int RequestsPerDay { get; init; } = 200;
    public int RequestsPerMonth { get; init; } = 2000;
    public int ConcurrentRequests { get; init; } = 4;
    public string DataDirectory { get; init; } = "guest-data";
    public string? TrustedProxyIp { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100 ||
            Model.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new InvalidOperationException("Guest:Model must contain a model ID.");
        if (RequestsPerGuestPerDay <= 0 || RequestsPerNetworkPerDay <= 0 || RequestsPerDay <= 0 ||
            RequestsPerMonth <= 0 || ConcurrentRequests <= 0)
            throw new InvalidOperationException("Guest request limits must be positive.");
        if (string.IsNullOrWhiteSpace(DataDirectory))
            throw new InvalidOperationException("Set Guest:DataDirectory to a persistent writable directory.");
        if (TrustedProxyIp is not null && !IPAddress.TryParse(TrustedProxyIp, out _))
            throw new InvalidOperationException("Guest:TrustedProxyIp must be the address of the immediate trusted proxy.");
    }
}
