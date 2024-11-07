using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.DncEng.SecretManager.ServiceConnections.RestModels;

internal class GetServiceEndpointsResponse
{

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("value")]
    public IReadOnlyList<AdoServiceEndpoint> Value { get; set; }

    public GetServiceEndpointsResponse(int count, IReadOnlyList<AdoServiceEndpoint> value)
    {
        Count = count;
        Value = value;
    }
}
