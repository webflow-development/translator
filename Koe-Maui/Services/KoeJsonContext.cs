using System.Text.Json.Serialization;

namespace Koe.Services;

[JsonSerializable(typeof(Credential))]
internal partial class KoeJsonContext : JsonSerializerContext { }
