using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonSerializable(typeof(BrokerRequest))]
[JsonSerializable(typeof(BrokerResponse))]
public partial class BrokerJsonContext : JsonSerializerContext { }
