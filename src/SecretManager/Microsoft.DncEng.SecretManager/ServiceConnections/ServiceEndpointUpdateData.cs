using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.ServiceConnections;

#nullable enable

public class ServiceEndpointUpdateData
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AccessToken { get; set; }
}
