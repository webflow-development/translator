using System.Text.Json.Serialization;

namespace Koe.Services;

[JsonSerializable(typeof(Credential))]
[JsonSerializable(typeof(Registration))]
internal partial class KoeJsonContext : JsonSerializerContext { }
