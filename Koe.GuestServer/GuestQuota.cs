using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koe.Services;

namespace Koe.GuestServer;

internal sealed record GuestReservation(GuestUsage Usage, GuestError? Error = null);

internal sealed class GuestQuota : IDisposable
{
    readonly object gate = new();
    readonly GuestOptions options;
    readonly TimeProvider time;
    readonly FileStream directoryLock;
    readonly string path;
    CounterState state;

    sealed record CounterState(DateOnly Day, byte[] Key, int DayCount, int MonthCount,
        Dictionary<string, int> Guests, Dictionary<string, int> Networks);

    public GuestQuota(GuestOptions options, TimeProvider time)
    {
        this.options = options;
        this.time = time;
        var directory = Path.GetFullPath(options.DataDirectory);
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "requests.json");
        directoryLock = File.Open(Path.Combine(directory, "requests.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                state = JsonSerializer.Deserialize<CounterState>(file) ?? throw new InvalidDataException("Guest request counters are missing.");
                if (state.Key?.Length != 32 || state.Guests is null || state.Networks is null ||
                    state.DayCount < 0 || state.MonthCount < state.DayCount ||
                    state.Guests.Values.Any(count => count < 0) || state.Networks.Values.Any(count => count < 0))
                    throw new InvalidDataException("Guest request counters are invalid.");
            }
            else state = new CounterState(Today(), RandomNumberGenerator.GetBytes(32), 0, 0, [], []);
        }
        catch
        {
            directoryLock.Dispose();
            throw;
        }
    }

    public GuestUsage GetUsage(Guid installation)
    {
        lock (gate)
        {
            AdvanceDay();
            return Usage(state.Guests.GetValueOrDefault(Hash("guest:" + installation.ToString("N"))));
        }
    }

    public GuestReservation Reserve(Guid installation, IPAddress? address)
    {
        lock (gate)
        {
            AdvanceDay();
            var guest = Hash("guest:" + installation.ToString("N"));
            var network = Hash("network:" + Network(address));
            var used = state.Guests.GetValueOrDefault(guest);
            var networkUsed = state.Networks.GetValueOrDefault(network);
            var usage = Usage(used);
            if (usage.Remaining == 0)
                return new GuestReservation(usage, new GuestError(GuestProtocol.LimitReached,
                    "Today's guest requests are used up. Connect ChatGPT to keep going."));
            if (networkUsed >= options.RequestsPerNetworkPerDay || state.DayCount >= options.RequestsPerDay ||
                state.MonthCount >= options.RequestsPerMonth)
                return new GuestReservation(usage, new GuestError(GuestProtocol.CapacityReached,
                    "The guest service has reached its usage limit. Connect ChatGPT to keep going."));

            var next = state with
            {
                DayCount = state.DayCount + 1,
                MonthCount = state.MonthCount + 1,
                Guests = new Dictionary<string, int>(state.Guests) { [guest] = used + 1 },
                Networks = new Dictionary<string, int>(state.Networks) { [network] = networkUsed + 1 }
            };
            // Reserve and persist the request before any potentially billable upstream call.
            using (var file = File.Create(path + ".next"))
            {
                JsonSerializer.Serialize(file, next);
                file.Flush(flushToDisk: true);
            }
            File.Move(path + ".next", path, overwrite: true);
            state = next;
            return new GuestReservation(Usage(used + 1));
        }
    }

    DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    void AdvanceDay()
    {
        var today = Today();
        if (today <= state.Day) return;
        var sameMonth = today.Year == state.Day.Year && today.Month == state.Day.Month;
        state = new CounterState(today, state.Key, 0, sameMonth ? state.MonthCount : 0, [], []);
    }

    GuestUsage Usage(int used) => new(options.RequestsPerGuestPerDay, Math.Max(0, options.RequestsPerGuestPerDay - used),
        new DateTimeOffset(state.Day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), options.Model);

    string Hash(string value) => Convert.ToHexString(HMACSHA256.HashData(state.Key, Encoding.UTF8.GetBytes(value)));

    static string Network(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return bytes.Length == 16 ? Convert.ToHexString(bytes.AsSpan(0, 8)) : address.ToString();
    }

    public void Dispose() => directoryLock.Dispose();
}
