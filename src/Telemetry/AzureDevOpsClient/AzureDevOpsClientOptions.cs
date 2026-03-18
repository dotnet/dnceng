// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
namespace Microsoft.DotNet.Internal.AzureDevOps;

public class AzureDevOpsClientOptions
{
    public string Organization { get; set; }
    public int MaxParallelRequests { get; set; } = 4;
    public string AccessToken { get; set; }

    /// <summary>
    /// The client ID of the Managed Identity to use for Entra-based authentication.
    /// When set (and <see cref="AccessToken"/> is not provided), the client will use
    /// a Managed Identity to obtain a bearer token for Azure DevOps.
    /// </summary>
    public string ManagedIdentityClientId { get; set; }
}
