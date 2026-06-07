using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonSerializable(typeof(BrokerRequest))]
[JsonSerializable(typeof(BrokerResponse))]
[JsonSerializable(typeof(DiskInfo))]
public partial class BrokerJsonContext : JsonSerializerContext { }
